﻿/*
    Copyright 2016 Travel Modelling Group, Department of Civil Engineering, University of Toronto

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
using XTMF;

namespace TMG.Frameworks.Data
{
    [ModuleInformation(Description = "This module provides that ability to load a data source from a resource.")]
    public class RemoteSetableDataSource<T> : ISetableDataSource<T>
    {
        [RootModule]
        public IResourceSource Root;

        [RunParameter("Resource Name", "", "The name of the resource to bind to.")]
        public string ResourceName;

        [RunParameter("Unload Resource", false, "If true unload requests will be passed to the resource.")]
        public bool UnloadResource;

        private IResource Linked;

        private IConfiguration Config;

        public RemoteSetableDataSource(IConfiguration config)
        {
            Config = config;
        }

        public bool Loaded
        {
            get
            {
                return Linked.GetDataSource().Loaded;
            }
        }

        public string Name { get; set; }

        public float Progress { get; set; }

        public Tuple<byte, byte, byte> ProgressColour { get { return new Tuple<byte, byte, byte>(50, 150, 50); } }

        public T GiveData()
        {
            return Linked.AcquireResource<T>();
        }

        public void SetData(T newValue)
        {
            (Linked.GetDataSource() as ISetableDataSource<T>)?.SetData(newValue);
        }


        public void LoadData()
        {

        }

        private IResource Link(string resourceName)
        {
            var ancestry = Functions.ModelSystemReflection.BuildModelStructureChain(Config, this);
            for (int i = ancestry.Count - 1; i >= 0; i--)
            {
                if (ancestry[i]?.Module is IResourceSource source)
                {
                    foreach (var resource in source.Resources)
                    {
                        if (resource.ResourceName == resourceName)
                        {
                            return resource;
                        }
                    }
                }
            }
            return null;
        }

        public bool RuntimeValidation(ref string error)
        {
            IResource linked;
            if ((linked = Link(ResourceName)) == null)
            {
                return false;
            }
            if (!linked.CheckResourceType<T>())
            {
                error = "In '" + Name + "' the resource was not of type '" + typeof(T).Name
                    + "' instead of was of type '" + linked.GetResourceType().Name + "'!";
                return false;
            }
            var isSetable = (linked.GetDataSource() as ISetableDataSource<T>) != null;
            if(!isSetable)
            {
                error = "In '" + Name + "' the resource was not an ISetableDataSource of type!";
                return false;
            }
            Linked = linked;
            return true;
        }

        public void UnloadData()
        {
            if (UnloadResource)
            {
                Linked.ReleaseResource();
            }
        }
    }
}
