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
using Datastructure;
using System;
using System.Collections.Generic;
using System.IO;
using TMG.Input;
using XTMF;
namespace TMG.Frameworks.Data.Loading
{
    [ModuleInformation(Description =
@"This module will stream ODData<float> from a CSV file.  If there are two columns of data it will store it as Origin,Data.  If there three or more it
 will be stored as Origin,Destination,Data.")]
    // ReSharper disable once InconsistentNaming
    public class LoadODDataFromCSV : IReadODData<float>
    {
        [SubModelInformation(Required = true, Description = "The location to read the data from.")]
        public FileLocation LoadFrom;

        [RunParameter("Contains Header", true, "Set this to true if there is a header line in the CSV File (Only for ThirdNormalized).")]
        public bool ContainsHeader;

        [RunParameter("Format", FileType.ThirdNormalized, "The format to read the data from.")]
        public FileType CSVFormat;

        [RunParameter("Read Type", ReadType.AutoDetect, "Either auto detect if we are reading a vector or a matrix or specify this manually.  This only applies to ThirdNormalized.")]
        public ReadType ThirdNormalizedType;

        public enum FileType
        {
            ThirdNormalized,
            SquareMatrix
        }

        public enum ReadType
        {
            AutoDetect,
            Vector,
            Matrix
        }

        public string Name { get; set; }

        public float Progress { get; set; }

        public Tuple<byte, byte, byte> ProgressColour { get { return new Tuple<byte, byte, byte>(50, 150, 50); } }

        public IEnumerable<ODData<float>> Read()
        {
            switch (CSVFormat)
            {
                case FileType.ThirdNormalized:
                    return ReadThirdNormalized();
                case FileType.SquareMatrix:
                    return ReadSquareMatrix();
                default:
                    throw new XTMFRuntimeException(this, $"Unknown file format option {Enum.GetName(typeof(FileType), CSVFormat)}!");
            }

        }

        private IEnumerable<ODData<float>> ReadSquareMatrix()
        {
            var anyLinesRead = false;
            // Spaces are not delimiters
            if (!File.Exists(LoadFrom))
            {
                throw new XTMFRuntimeException(this, $"In {Name} the file '{LoadFrom.GetFilePath()}' does not exist!");
            }
            using (var reader = new CsvReader(LoadFrom, false))
            {
                // read in the destinations
                reader.LoadLine(out int columns);
                int[] destinations = new int[columns - 1];
                for (int i = 0; i < destinations.Length; i++)
                {
                    reader.Get(out destinations[i], i + 1);
                }
                while (reader.LoadLine(out columns))
                {
                    if (columns >= destinations.Length + 1)
                    {
                        anyLinesRead = true;
                        ODData<float> data = new ODData<float>();
                        reader.Get(out data.O, 0);
                        for (int i = 0; i < destinations.Length; i++)
                        {
                            data.D = destinations[i];
                            reader.Get(out data.Data, i + 1);
                            yield return data;
                        }
                    }
                }
            }
            if (!anyLinesRead)
            {
                throw new XTMFRuntimeException(this, $"In {Name} when reading the file '{LoadFrom}' we did not load any information!");
            }
        }

        private IEnumerable<ODData<float>> ReadThirdNormalized()
        {
            if (!File.Exists(LoadFrom))
            {
                throw new XTMFRuntimeException(this, $"In {Name} the file '{LoadFrom.GetFilePath()}' does not exist!");
            }
            using (var reader = new CsvReader(LoadFrom, true))
            {
                if (ContainsHeader)
                {
                    reader.LoadLine();
                }
                switch (ThirdNormalizedType)
                {
                    case ReadType.AutoDetect:
                        while (reader.LoadLine(out int columns))
                        {
                            if (columns >= 2)
                            {
                                ODData<float> data = new ODData<float>();
                                reader.Get(out data.O, 0);
                                if (columns >= 3)
                                {
                                    reader.Get(out data.D, 1);
                                    reader.Get(out data.Data, 2);
                                }
                                else
                                {
                                    reader.Get(out data.Data, 1);
                                }
                                yield return data;
                            }
                        }
                        break;
                    case ReadType.Matrix:
                        while (reader.LoadLine(out int columns))
                        {
                            if (columns >= 3)
                            {
                                ODData<float> data = new ODData<float>();
                                reader.Get(out data.O, 0);
                                reader.Get(out data.D, 1);
                                reader.Get(out data.Data, 2);
                                yield return data;
                            }
                        }
                        break;
                    case ReadType.Vector:
                        while (reader.LoadLine(out int columns))
                        {
                            if (columns >= 2)
                            {
                                ODData<float> data = new ODData<float>();
                                reader.Get(out data.O, 0);
                                reader.Get(out data.Data, 1);
                                yield return data;
                            }
                        }
                        break;
                }

            }
        }

        public bool RuntimeValidation(ref string error)
        {
            return true;
        }
    }

}
