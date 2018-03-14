﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using XTMF;

namespace TMG.Frameworks.Testing
{
    [ModuleInformation(Description =
        @"A dummy module that can be used as a root module in model systems used for testing the GUI.")]
    public class TestRuntimeErrorModule : ISelfContainedModule
    {
        private float _progress = 0;
        public string Name { get; set; }
        public float Progress { get => _progress; }
        public Tuple<byte, byte, byte> ProgressColour { get; }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="error"></param>
        /// <returns></returns>
        public bool RuntimeValidation(ref string error)
        {
            //nothing to validate
            return true;
        }

        /// <summary>
        /// 
        /// </summary>
        public void Start()
        {
            //throw new dummy exception
            throw new GenericRuntimeError("Test Runtime Error!");
        }
    }

    public class GenericRuntimeError : Exception
    {
        /// <summary>
        /// Creates a generic runtime error with message s.
        /// </summary>
        /// <param name="s"></param>
        public GenericRuntimeError(string s) : base(s)
        {

        }
    }
}
