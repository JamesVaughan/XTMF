﻿/*
    Copyright 2017 Travel Modelling Group, Department of Civil Engineering, University of Toronto

    This file is part of XTMF.

    XTMF is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    XTMF is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with XTMF.  If not, see <http://www.gnu.org/licenses/>.
*/
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace XTMF.Run
{
    sealed class XTMFRunRemoteHost : XTMFRun
    {
        private float _RemoteProgress = 0.0f;

        private string _RemoteStatus = String.Empty;

        private NamedPipeServerStream _Pipe;
        /// <summary>
        /// Bound to in order to do a wait.
        /// This is triggered when the client has exited.
        /// </summary>
        private SemaphoreSlim ClientExiting = new SemaphoreSlim(0);

        public override bool RunsRemotely => true;

        public XTMFRunRemoteHost(IConfiguration configuration, ModelSystemStructureModel root, string runName, string runDirectory)
            : base(runName, runDirectory, configuration)
        {
            ModelSystemStructureModelRoot = root;
        }

        private string GetXTMFRunFileName() => Path.Combine(Path.GetDirectoryName(
                Assembly.GetEntryAssembly().Location), "XTMF.Run.exe");

        private void StartupHost()
        {
            var debugMode = !((Configuration)Configuration).RunInSeperateProcess; // Debugger.IsAttached;
            var pipeName = debugMode ? "DEBUG_MODEL_SYSTEM" : Guid.NewGuid().ToString();
            _Pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            if (!debugMode)
            {
                var info = new ProcessStartInfo(GetXTMFRunFileName(), "-pipe " + pipeName)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                DataReceivedEventHandler messageHandler = (sender, args) =>
                {
                    if (args.Data != null)
                    {
                        SendRunMessage(args.Data);
                    }
                };
                var runProcess = new Process
                {
                    StartInfo = info,
                    EnableRaisingEvents = true
                };
                runProcess.OutputDataReceived += messageHandler;
                runProcess.ErrorDataReceived += messageHandler;
                runProcess.Exited += RunProcess_Exited;
                runProcess.Start();
                runProcess.BeginOutputReadLine();
                runProcess.BeginErrorReadLine();
            }
            _Pipe.WaitForConnection();
        }

        private void RunProcess_Exited(object sender, EventArgs e)
        {
            // make sure the pipe is destroyed if the other process has
            // terminated
            _Pipe?.Dispose();
            ClientExiting.Release();
            InvokeRunCompleted();
        }

        private void RequestSignal(ToClient signal)
        {
            lock (this)
            {
                BinaryWriter writer = new BinaryWriter(_Pipe, System.Text.Encoding.Unicode, true);
                writer.Write((Int32)signal);
            }
        }

        private void RequestRemoteProgress()
        {
            RequestSignal(ToClient.RequestProgress);
        }

        private void RequestRemoteStatus()
        {
            RequestSignal(ToClient.RequestStatus);
        }

        private void InitializeClientAndSendModelSystem()
        {
            lock (this)
            {
                BinaryWriter writer = new BinaryWriter(_Pipe, System.Text.Encoding.Unicode, true);
                writer.Write((Configuration as Configuration)?.ConfigurationFileName ?? "");
            }
            WriteModelSystemToStream();
        }

        private void StartClientListener()
        {
            Task.Factory.StartNew(() =>
            {
                try
                {
                    BinaryReader reader = new BinaryReader(_Pipe, System.Text.Encoding.Unicode, true);
                    while (true)
                    {
                        switch ((ToHost)reader.ReadInt32())
                        {
                            case ToHost.Heartbeat:
                                break;
                            case ToHost.ClientReportedProgress:
                                _RemoteProgress = reader.ReadSingle();
                                break;
                            case ToHost.ClientReportedStatus:
                                _RemoteStatus = reader.ReadString();
                                break;
                            case ToHost.ClientErrorValidatingModelSystem:
                                InvokeValidationError(ReadErrors(reader));
                                return;
                            case ToHost.ClientErrorWhenRunningModelSystem:
                                InvokeRuntimeError(ReadError(reader));
                                return;
                            case ToHost.ClientFinishedModelSystem:
                            case ToHost.ClientExiting:
                                return;
                            case ToHost.ProjectSaved:
                                LoadAndSignalModelSystem(reader);
                                break;
                        }
                    }
                }
                finally
                {
                    ClientExiting.Release();
                }
            }, TaskCreationOptions.LongRunning);
        }

        private void LoadAndSignalModelSystem(BinaryReader reader)
        {
            try
            {
                var length = (int)reader.ReadInt64();
                byte[] msText = new byte[length];
                var soFar = 0;
                while(soFar < length)
                {
                    soFar += reader.Read(msText, soFar, length - soFar);
                }
                using (var stream = new MemoryStream(msText))
                {
                    var mss = ModelSystemStructure.Load(stream, Configuration);
                    SendProjectSaved(mss as ModelSystemStructure);
                }
            }
            catch(Exception e)
            {
                SendRunMessage(e.Message + "\r\n" + e.StackTrace);
            }
        }

        private static List<ErrorWithPath> ReadErrors(BinaryReader reader)
        {
            int numberOfErrors = reader.ReadInt32();
            List<ErrorWithPath> errors = new List<ErrorWithPath>(numberOfErrors);
            for (int i = 0; i < numberOfErrors; i++)
            {
                errors.Add(ReadError(reader));
            }
            return errors;
        }

        private static ErrorWithPath ReadError(BinaryReader reader)
        {
            int pathSize = reader.ReadInt32();
            List<int> path = null;
            if (pathSize > 0)
            {
                path = new List<int>(pathSize);
                for (int j = 0; j < pathSize; j++)
                {
                    path.Add(reader.ReadInt32());
                }
            }
            var message = reader.ReadString();
            var stackTrace = reader.ReadString();
            if (String.IsNullOrWhiteSpace(stackTrace))
            {
                stackTrace = null;
            }
            return new ErrorWithPath(path, message, stackTrace);
        }

        private void WriteModelSystemToStream()
        {
            lock (this)
            {
                using (var memStream = new MemoryStream())
                {
                    BinaryWriter writer = new BinaryWriter(_Pipe, System.Text.Encoding.Unicode, true);
                    ModelSystemStructureModelRoot.RealModelSystemStructure.Save(memStream);
                    writer.Write((UInt32)ToClient.RunModelSystem);
                    writer.Write(RunName);
                    writer.Write(RunDirectory);
                    writer.Write(Encoding.Unicode.GetString(memStream.ToArray()));
                }
            }
        }

        public override bool ExitRequest()
        {
            RequestSignal(ToClient.KillModelRun);
            return true;
        }

        public override Tuple<byte, byte, byte> PollColour() => new Tuple<byte, byte, byte>(50, 150, 50);

        public override float PollProgress()
        {
            RequestSignal(ToClient.RequestProgress);
            return _RemoteProgress;
        }

        public override string PollStatusMessage()
        {
            RequestSignal(ToClient.RequestStatus);
            return _RemoteStatus;
        }

        public override bool DeepExitRequest()
        {
            RequestSignal(ToClient.KillModelRun);
            return true;
        }

        public override void Start()
        {
            Task.Run(() =>
            {
                StartupHost();
                StartClientListener();
                // Send the instructions to run the model system
                InitializeClientAndSendModelSystem();
                SetStatusToRunning();
            });
        }

        public override void Wait() => ClientExiting.Wait();

        public override void TerminateRun() => RequestSignal(ToClient.KillModelRun);

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            _Pipe?.Dispose();
            ClientExiting?.Dispose();
        }
    }
}
