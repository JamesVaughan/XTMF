﻿/*
    Copyright 2015 Travel Modelling Group, Department of Civil Engineering, University of Toronto

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
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Datastructure;
using TMG;
using XTMF;
using TMG.Functions;

namespace Tasha.Data
{
    [ModuleInformation(
        Description = ""
        
        )]
    public class ZoneODMask : IDataSource<SparseTwinIndex<float>>
    {

        [RootModule]
        public ITravelDemandModel Root;

        [RunParameter("Origin Mask Range", "0-10", typeof(RangeSet), "The zones to set to")]
        public RangeSet OriginMaskRange;

        [RunParameter("Destination Mask Range", "0-10", typeof(RangeSet), "The zones to set to")]
        public RangeSet DestinationMaskRange;

        [RunParameter("Masked Value", 0.0f, "The value to set the elements that are masked to.")]
        public float MaskedValue;

        [SubModelInformation(Required = false, Description = "The zone OD resource to mask, only one of this or the data source should be selected.")]
        public IResource BaseDataResource;

        [SubModelInformation(Required = false, Description = "The zone OD data to mask, only one of this or the resource should be selected.")]
        public IDataSource<SparseTwinIndex<float>> BaseDataDataSource;

        public bool Loaded
        {
            get;
            set;
        }

        public string Name { get; set; }

        public float Progress { get; set; }

        public Tuple<byte, byte, byte> ProgressColour { get { return new Tuple<byte, byte, byte>(50, 150, 50); } }

        private SparseTwinIndex<float> Data;

        public SparseTwinIndex<float> GiveData()
        {
            return Data;
        }

        public void LoadData()
        {
            var matrix = Root.ZoneSystem.ZoneArray.CreateSquareTwinArray<float>();
            var data = matrix.GetFlatData();
            float[][] toMask = GetToMaskData().GetFlatData();
            bool[] originMasks = CreateMask(OriginMaskRange);
            bool[] destinationMasks = CreateMask(DestinationMaskRange);
            Parallel.For(0, originMasks.Length, (int i) =>
            {
                if (!originMasks[i])
                {
                    for (int j = 0; j < data[i].Length; j++)
                    {
                        data[i][j] = MaskedValue;
                    }
                }
                else
                {
                    var toMaskRow = toMask[i];
                    for (int j = 0; j < data[i].Length; j++)
                    {
                        data[i][j] = destinationMasks[j] ? toMaskRow[j] : MaskedValue;
                    }
                }
            });
            Data = matrix;
            Loaded = true;
        }

        private bool[] CreateMask(RangeSet range)
        {
            var zones = Root.ZoneSystem.ZoneArray.GetFlatData();
            bool[] ret = new bool[zones.Length];
            for (int i = 0; i < zones.Length; i++)
            {
                ret[i] = range.Contains(zones[i].ZoneNumber);
            }
            return ret;
        }

        private SparseTwinIndex<float> GetToMaskData()
        {
            SparseTwinIndex<float> toMask;
            if (BaseDataDataSource != null)
            {
                var needToLoad = !BaseDataDataSource.Loaded;
                if (needToLoad)
                {
                    BaseDataDataSource.LoadData();
                }
                toMask = BaseDataDataSource.GiveData();
                if (needToLoad)
                {
                    BaseDataDataSource.UnloadData();
                }
            }
            else
            {
                toMask = BaseDataResource.AquireResource<SparseTwinIndex<float>>();
            }

            return toMask;
        }

        public bool RuntimeValidation(ref string error)
        {
            if (BaseDataDataSource != null && BaseDataResource != null)
            {
                error = "In '" + Name + "' only one of Base Data DataSource or Base Data Resource should be selected for!";
                return false;
            }
            if(BaseDataDataSource == null && BaseDataResource == null)
            {
                error = "In '" + Name + "' one of Base Data DataSource or Base Data Resource should be selected for!";
                return false;
            }
            return true;
        }

        public void UnloadData()
        {
            Loaded = false;
            Data = null;
        }
    }

}