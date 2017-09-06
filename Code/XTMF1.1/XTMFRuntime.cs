﻿/*
    Copyright 2014 Travel Modelling Group, Department of Civil Engineering, University of Toronto

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
using System.ComponentModel;
using System.IO;
using System.Reflection;
using XTMF.Networking;

namespace XTMF
{
    public class XTMFRuntime
    {
        /// <summary>
        /// The configuration used for all of the settings
        /// and holding the data for the XTMF installation
        /// </summary>
        public Configuration Configuration { get; private set; }

        public ModelSystemController ModelSystemController { get; private set; }

        public ProjectController ProjectController { get; private set; }

        public XTMFRuntime(Configuration configuration = null)
        {
            CopyBuffer = new CopyBuffer();
            Configuration = configuration == null ? BuildConfiguration() : configuration;
            ModelSystemController = new ModelSystemController(this);
            ProjectController = new ProjectController(this);

         
        }

        private Configuration BuildConfiguration()
        {
 
            var localInstanceDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            var localConfigurationFileName = Path.Combine(localInstanceDirectory, "LocalXTMFConfiguration.xml");
            if (File.Exists(localConfigurationFileName))
            {
                return new Configuration(localConfigurationFileName);
            }
           
            return new Configuration();
        }

        /// <summary>
        /// Creates a new instance of XTMF allowing for you to
        /// run all of the systems contained within
        /// </summary>

        public IHost ActiveHost
        {
            get
            {
                return Configuration.GetActiveHost();
            }
        }

        /// <summary>
        /// Get the copy buffer
        /// </summary>
        public CopyBuffer CopyBuffer { get; private set; }


        public IClient InitializeRemoteClient(string address, int port)
        {
            string error = null;
            Configuration.RemoteServerAddress = address;
            Configuration.RemoteServerPort = port;
            if (Configuration.StartupNetworkingClient(out IClient client, ref error))
            {
                return client;
            }
            return null;
        }

        /// <summary>
        /// Terminate the runtime
        /// </summary>
        public void ShutDown()
        {

        }


        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool all)
        {
            if (Configuration != null)
            {
                Configuration.Dispose();
                Configuration = null;
            }
        }
    }
}
