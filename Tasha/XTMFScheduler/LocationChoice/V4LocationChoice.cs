﻿/*
    Copyright 2014-2015 Travel Modelling Group, Department of Civil Engineering, University of Toronto

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
using Tasha.Common;
using Tasha.Scheduler;
using TMG;
using XTMF;
using TMG.Functions;
using System.Numerics;


namespace Tasha.XTMFScheduler.LocationChoice
{

    public sealed class V4LocationChoice : ILocationChoiceModel
    {
        [RunParameter("Valid Destination Zones", "1-6999", typeof(RangeSet), "The valid zones to use.")]
        public RangeSet ValidDestinationZones;

        private bool[] ValidDestinations;

        [RootModule]
        public ITravelDemandModel Root;

        public string Name { get; set; }

        public float Progress { get; set; }

        public Tuple<byte, byte, byte> ProgressColour { get { return new Tuple<byte, byte, byte>(50, 150, 50); } }

        [SubModelInformation(Required = true, Description = "")]
        public IResource ProfessionalFullTime;
        [SubModelInformation(Required = true, Description = "")]
        public IResource ProfessionalPartTime;

        [SubModelInformation(Required = true, Description = "")]
        public IResource GeneralFullTime;
        [SubModelInformation(Required = true, Description = "")]
        public IResource GeneralPartTime;

        [SubModelInformation(Required = true, Description = "")]
        public IResource RetailFullTime;
        [SubModelInformation(Required = true, Description = "")]
        public IResource RetailPartTime;

        [SubModelInformation(Required = true, Description = "")]
        public IResource ManufacturingFullTime;
        [SubModelInformation(Required = true, Description = "")]
        public IResource ManufacturingPartTime;

        [RunParameter("Auto Network Name", "Auto", "The name of the network to use for computing auto times.")]
        public string AutoNetworkName;

        [RunParameter("Transit Network Name", "Transit", "The name of the network to use for computing transit times.")]
        public string TransitNetworkName;

        [RunParameter("Estimation Mode", false, "Enable this to improve performance when estimating a model.")]
        public bool EstimationMode;

        public IZone GetLocation(IEpisode ep, Random random)
        {
            var episodes = ep.ContainingSchedule.Episodes;
            var startTime = ep.StartTime;
            int i = 0;
            for (; i < episodes.Length; i++)
            {
                if (episodes[i] == null) break;
                if (startTime < episodes[i].StartTime)
                {
                    return GetLocation(ep, random, (i == 0 ? null : episodes[i - 1]), episodes[i], startTime);
                }
            }
            return GetLocation(ep, random, (i > 0 ? episodes[i - 1] : null), null, startTime);
        }

        public float[] GetLocationProbabilities(IEpisode ep)
        {
            var episodes = ep.ContainingSchedule.Episodes;
            var startTime = ep.StartTime;
            int i = 0;
            for (; i < episodes.Length; i++)
            {
                if (episodes[i] == null) break;
                if (startTime < episodes[i].StartTime)
                {
                    return GetLocationProbabilities(ep, (i == 0 ? null : episodes[i - 1]), episodes[i], startTime);
                }
            }
            return GetLocationProbabilities(ep, (i > 0 ? episodes[i - 1] : null), null, startTime);
        }

        [ThreadStatic]
        private static float[] CalculationSpace;

        private System.Collections.Concurrent.ConcurrentStack<float[]> CalculationPool = new System.Collections.Concurrent.ConcurrentStack<float[]>();

        private IZone GetLocation(IEpisode ep, Random random, IEpisode previous, IEpisode next, Time startTime)
        {
            var previousZone = GetZone(previous, ep);
            var nextZone = GetZone(next, ep);
            var calculationSpace = CalculationSpace;
            if (calculationSpace == null)
            {
                CalculationSpace = calculationSpace = new float[Root.ZoneSystem.ZoneArray.Count];
            }
            Time availableTime = ComputeAvailableTime(previous, next);
            switch (ep.ActivityType)
            {
                case Activity.Market:
                case Activity.JointMarket:
                    return MarketModel.GetLocation(previousZone, ep, nextZone, startTime, availableTime, calculationSpace, random);
                case Activity.JointOther:
                case Activity.IndividualOther:
                    return OtherModel.GetLocation(previousZone, ep, nextZone, startTime, availableTime, calculationSpace, random);
                case Activity.WorkBasedBusiness:
                case Activity.SecondaryWork:
                    return WorkBasedBusinessModel.GetLocation(previousZone, ep, nextZone, startTime, availableTime, calculationSpace, random);
            }
            // if it isn't something that we understand just accept its previous zone
            return ep.Zone;
        }

        private float[] GetLocationProbabilities(IEpisode ep, IEpisode previous, IEpisode next, Time startTime)
        {
            var previousZone = GetZone(previous, ep);
            var nextZone = GetZone(next, ep);
            var calculationSpace = CalculationSpace;
            if (calculationSpace == null)
            {
                CalculationSpace = calculationSpace = new float[Root.ZoneSystem.ZoneArray.Count];
            }
            Time availableTime = ComputeAvailableTime(previous, next);
            switch (ep.ActivityType)
            {
                case Activity.Market:
                case Activity.JointMarket:
                    return MarketModel.GetLocationProbabilities(previousZone, ep, nextZone, startTime, availableTime, calculationSpace);
                case Activity.JointOther:
                case Activity.IndividualOther:
                    return OtherModel.GetLocationProbabilities(previousZone, ep, nextZone, startTime, availableTime, calculationSpace);
                case Activity.WorkBasedBusiness:
                case Activity.SecondaryWork:
                    return WorkBasedBusinessModel.GetLocationProbabilities(previousZone, ep, nextZone, startTime, availableTime, calculationSpace);
            }
            // if it isn't something that we understand just accept its previous zone
            return calculationSpace;
        }

        [RunParameter("Maximum Episode Duration Compression", 0.5f, "The amount that the duration is allowed to be compressed from the original duration time (0 to 1 default is 0.5).")]
        public float MaximumEpisodeDurationCompression;

        private Time ComputeAvailableTime(IEpisode previous, IEpisode next)
        {
            return (next == null ? Time.EndOfDay : (next.StartTime + next.Duration - (MaximumEpisodeDurationCompression * next.OriginalDuration)))
                - (previous == null ? Time.StartOfDay : previous.EndTime - previous.Duration - (MaximumEpisodeDurationCompression * previous.OriginalDuration));
        }

        public sealed class SpatialRegion : IModule
        {
            [RunParameter("PDRange", "1", typeof(RangeSet), "The planning districts that constitute this spatial segment.")]
            public RangeSet Range;

            [RunParameter("Constant", 0.0f, "The constant applied if the spacial category is met.")]
            public float Constant;

            public string Name { get; set; }

            public float Progress { get; set; }

            public Tuple<byte, byte, byte> ProgressColour { get { return new Tuple<byte, byte, byte>(50, 150, 50); } }

            public bool RuntimeValidation(ref string error)
            {
                return true;
            }
        }

        public sealed class ODConstant : IModule
        {
            [RunParameter("Previous PD Range", "1", typeof(RangeSet), "The planning districts for the previous zone.")]
            public RangeSet Previous;

            [RunParameter("Next PD Range", "1", typeof(RangeSet), "The planning districts for the next zone.")]
            public RangeSet Next;

            [RunParameter("Interest PD Range", "1", typeof(RangeSet), "The planning districts the zone we are interested in.")]
            public RangeSet Interest;

            [RunParameter("Constant", 0.0f, "The constant applied if the spacial category is met.")]
            public float Constant;
            internal float ExpConstant;

            public string Name { get; set; }

            public float Progress { get; set; }

            public Tuple<byte, byte, byte> ProgressColour { get { return new Tuple<byte, byte, byte>(50, 150, 50); } }

            public bool RuntimeValidation(ref string error)
            {
                return true;
            }
        }

        public sealed class TimePeriod : IModule
        {
            [RootModule]
            public ITravelDemandModel Root;

            [ParentModel]
            public V4LocationChoice Parent;

            [RunParameter("Start Time", "6:00AM", typeof(Time), "The time this period starts at.")]
            public Time StartTime;

            [RunParameter("End Time", "9:00AM", typeof(Time), "The time this period ends at (exclusive).")]
            public Time EndTime;

            public string Name { get; set; }

            public float Progress { get; set; }

            public Tuple<byte, byte, byte> ProgressColour { get { return new Tuple<byte, byte, byte>(50, 150, 50); } }



            public float[] RowTravelTimes;
            public float[] ColumnTravelTimes;
            internal float[] EstimationAIVTT;
            internal float[] EstimationACOST;
            internal float[] EstimationTIVTT;
            internal float[] EstimationTWALK;
            internal float[] EstimationTWAIT;
            internal float[] EstimationTBOARDING;
            internal float[] EstimationTFARE;

            internal float[] EstimationTempSpace;
            internal float[] EstimationTempSpace2;

            public void Load()
            {
                var size = Root.ZoneSystem.ZoneArray.Count;
                var autoNetwork = Parent.AutoNetwork;
                var transitNetwork = Parent.TransitNetwork;
                // we only need to load in this data if we are
                if (Parent.EstimationMode)
                {
                    if (EstimationAIVTT == null)
                    {
                        var odPairs = size * size;
                        EstimationAIVTT = new float[odPairs];
                        EstimationACOST = new float[odPairs];
                        EstimationTIVTT = new float[odPairs];
                        EstimationTWALK = new float[odPairs];
                        EstimationTWAIT = new float[odPairs];
                        EstimationTBOARDING = new float[odPairs];
                        EstimationTFARE = new float[odPairs];
                        Parallel.For(0, size, (int i) =>
                        {
                            var time = StartTime;
                            int baseIndex = i * size;
                            for (int j = 0; j < size; j++)
                            {
                                autoNetwork.GetAllData(i, j, time, out EstimationAIVTT[baseIndex + j], out EstimationACOST[baseIndex + j]);
                                transitNetwork.GetAllData(i, j, time,
                                    out EstimationTIVTT[baseIndex + j],
                                    out EstimationTWALK[baseIndex + j],
                                    out EstimationTWAIT[baseIndex + j],
                                    out EstimationTBOARDING[baseIndex + j],
                                    out EstimationTFARE[baseIndex + j]
                                    );
                            }
                        });
                    }
                    if (RowTravelTimes != null)
                    {
                        return;
                    }
                }
                var rowData = (RowTravelTimes == null || RowTravelTimes.Length != size * size) ? new float[size * size] : RowTravelTimes;
                var columnData = (ColumnTravelTimes == null || ColumnTravelTimes.Length != size * size) ? new float[size * size] : ColumnTravelTimes;
                Parallel.For(0, size, (int i) =>
                {
                    var time = StartTime;
                    int startingIndex = i * size;
                    for (int j = 0; j < size; j++)
                    {
                        var ijTime = autoNetwork.TravelTime(i, j, time).ToMinutes();
                        rowData[startingIndex + j] = ijTime;
                        columnData[j * size + i] = ijTime;
                    }
                });
                RowTravelTimes = rowData;
                ColumnTravelTimes = columnData;
            }

            public bool RuntimeValidation(ref string error)
            {
                return true;
            }
        }


        public abstract class LocationChoiceActivity : IModule
        {
            [RootModule]
            public ITravelDemandModel Root;

            [ParentModel]
            public V4LocationChoice Parent;

            public string Name { get; set; }

            public float Progress { get; set; }

            public Tuple<byte, byte, byte> ProgressColour { get { return new Tuple<byte, byte, byte>(50, 150, 50); } }


            public class TimePeriodParameters : XTMF.IModule
            {
                [SubModelInformation(Description = "The PD constants for this time period.")]
                public SpatialRegion[] PDConstant;

                [SubModelInformation(Description = "The constants to apply when traveling between given places")]
                public ODConstant[] ODConstants;

                [RunParameter("Same PD", 0.0f, "The constant applied if the zone of interest is the same as both the previous and next planning districts.")]
                public float SamePD;
                internal float expSamePD;

                public string Name { get; set; }

                public float Progress { get; set; }

                public Tuple<byte, byte, byte> ProgressColour { get { return new Tuple<byte, byte, byte>(50, 150, 50); } }

                public bool RuntimeValidation(ref string error)
                {
                    return true;
                }
            }

            [SubModelInformation(Description = "The parameters for this model by time period. There must be the same number of time periods as in the location choice model.")]
            public TimePeriodParameters[] TimePeriod;

            /// <summary>
            /// To[timePeriod][o * #zones + d]
            /// </summary>
            protected float[][] To;
            /// <summary>
            /// From[timePeriod][o * #zones + d]
            /// </summary>
            protected float[][] From;

            [RunParameter("Professional FullTime", "0.0", typeof(float), "The weight applied for the worker category.")]
            public float ProfessionalFullTime;
            [RunParameter("Professional PartTime", "0.0", typeof(float), "The weight applied for the worker category.")]
            public float ProfessionalPartTime;
            [RunParameter("General FullTime", "0.0", typeof(float), "The weight applied for the worker category.")]
            public float GeneralFullTime;
            [RunParameter("General PartTime", "0.0", typeof(float), "The weight applied for the worker category.")]
            public float GeneralPartTime;
            [RunParameter("Sales FullTime", "0.0", typeof(float), "The weight applied for the worker category.")]
            public float RetailFullTime;
            [RunParameter("Sales PartTime", "0.0", typeof(float), "The weight applied for the worker category.")]
            public float RetailPartTime;
            [RunParameter("Manufacturing FullTime", "0.0", typeof(float), "The weight applied for the worker category.")]
            public float ManufacturingFullTime;
            [RunParameter("Manufacturing PartTime", "0.0", typeof(float), "The weight applied for the worker category.")]
            public float ManufacturingPartTime;
            [RunParameter("Population", "0.0", typeof(float), "The weight applied for the log of the population in the zone.")]
            public float Population;
            [RunParameter("Auto TravelTime", "0.0", typeof(float), "The weight applied for the travel time from origin to zone to final destination.")]
            public float AutoTime;
            [RunParameter("Transit Constant", "0.0", typeof(float), "The alternative specific constant for transit.")]
            public float TransitConstant;
            [RunParameter("Transit IVTT", "0.0", typeof(float), "The weight applied for the in vehicle travel time travel time from origin to zone to final destination.")]
            public float TransitTime;
            [RunParameter("Transit Walk", "0.0", typeof(float), "The weight applied for the walk time travel time from origin to zone to final destination.")]
            public float TransitWalk;
            [RunParameter("Transit Wait", "0.0", typeof(float), "The weight applied for the wait travel time travel time from origin to zone to final destination.")]
            public float TransitWait;
            [RunParameter("Transit Boarding", "0.0", typeof(float), "The weight applied for the boarding penalties from origin to zone to final destination.")]
            public float TransitBoarding;
            [RunParameter("Cost", "0.0", typeof(float), "The weight applied for the cost from origin to zone to final destination.")]
            public float Cost;
            [RunParameter("Intra Zonal", 0.0f, "The constant to apply if the trip is within the same zone.")]
            public float IntraZonal
            {
                get
                {
                    return _IntraZonal;
                }
                set
                {
                    _IntraZonal = value;
                    ExpIntraZonal = (float)Math.Exp(value);
                }
            }
            private float _IntraZonal;

            private float ExpIntraZonal;

            private int[][][][] PDCube;

            private double GetTransitUtility(ITripComponentData network, int i, int j, Time time)
            {
                float ivtt, walk, wait, cost, boarding;
                if (!network.GetAllData(i, j, time, out ivtt, out walk, out wait, out boarding, out cost))
                {
                    return 0f;
                }
                return Math.Exp(
                      TransitConstant
                    + TransitTime * ivtt
                    + TransitWalk * walk
                    + TransitWait * wait
                    + TransitBoarding * boarding
                    + Cost * cost);
            }

            protected float GetTravelLogsum(INetworkData autoNetwork, ITripComponentData transitNetwork, int i, int j, Time time)
            {
                float ivtt, cost;
                if (!autoNetwork.GetAllData(i, j, time, out ivtt, out cost))
                {
                    return 0.0f;
                }
                return (float)(GetTransitUtility(transitNetwork, i, j, time)
                    + Math.Exp(ivtt * AutoTime + cost * Cost));
            }

            internal float[] GenerateEstimationLogsums(TimePeriod timePeriod, IZone[] zones)
            {
                var zones2 = zones.Length * zones.Length;
                float[] autoSpace = timePeriod.EstimationTempSpace;
                float[] transitSpace = timePeriod.EstimationTempSpace2;

                if (autoSpace == null)
                {
                    timePeriod.EstimationTempSpace = autoSpace = new float[zones2];
                    timePeriod.EstimationTempSpace2 = transitSpace = new float[zones2];
                }
                Parallel.For(0, zones.Length, (int i) =>
                {
                    var start = i * zones.Length;
                    var end = start + zones.Length;
                    Vector<float> VCost = new Vector<float>(Cost);
                    Vector<float> VAutoTime = new Vector<float>(AutoTime);
                    Vector<float> VTransitConstant = new Vector<float>(TransitConstant);
                    Vector<float> VTransitTime = new Vector<float>(TransitTime);
                    Vector<float> VTransitWalk = new Vector<float>(TransitWalk);
                    Vector<float> VTransitWait = new Vector<float>(TransitWait);
                    Vector<float> VTransitBoarding = new Vector<float>(TransitBoarding);
                    Vector<float> VNegativeInfinity = new Vector<float>(float.NegativeInfinity);
                    int index = start;
                    // copy everything we can do inside of a vector
                    for (; index <= end - Vector<float>.Count; index += Vector<float>.Count)
                    {
                        // compute auto utility
                        var aivtt = new Vector<float>(timePeriod.EstimationAIVTT, index);
                        var acost = new Vector<float>(timePeriod.EstimationACOST, index);
                        (
                              aivtt * VAutoTime
                            + acost * VCost
                        ).CopyTo(autoSpace, index);
                        // compute transit utility
                        var tivtt = new Vector<float>(timePeriod.EstimationTIVTT, index);
                        var twalk = new Vector<float>(timePeriod.EstimationTWALK, index);
                        var twait = new Vector<float>(timePeriod.EstimationTWAIT, index);
                        var tboarding = new Vector<float>(timePeriod.EstimationTBOARDING, index);
                        var tFare = new Vector<float>(timePeriod.EstimationTFARE, index);
                        Vector.ConditionalSelect(Vector.GreaterThan(twalk, Vector<float>.Zero), (
                             VTransitConstant
                            + tivtt * VTransitTime
                            + twalk * VTransitWalk
                            + twait * VTransitWait
                            + tboarding * VTransitBoarding
                            + tFare * VCost), VNegativeInfinity).CopyTo(transitSpace, index);
                    }
                    // copy the remainder
                    for (; index < end; index++)
                    {
                        autoSpace[index] =
                              timePeriod.EstimationAIVTT[index] * AutoTime
                            + timePeriod.EstimationACOST[index] * Cost;
                        if (timePeriod.EstimationTWALK[index] > 0)
                        {
                            transitSpace[index] =
                                      TransitConstant
                                    + timePeriod.EstimationTIVTT[index] * TransitTime
                                    + timePeriod.EstimationTWALK[index] * TransitWalk
                                    + timePeriod.EstimationTWAIT[index] * TransitWait
                                    + timePeriod.EstimationTBOARDING[index] * TransitBoarding
                                    + timePeriod.EstimationTFARE[index] * Cost;
                        }
                        else
                        {
                            transitSpace[index] = float.NegativeInfinity;
                        }
                    }
                });
                Parallel.For(0, zones2, (int index) =>
                {
                    autoSpace[index] = (float)(Math.Exp(autoSpace[index]) + Math.Exp(transitSpace[index]));
                });
                return autoSpace;
            }

            public bool RuntimeValidation(ref string error)
            {
                var parentTimePeriods = Parent.TimePeriods;
                var ourTimePeriods = TimePeriod;
                if (parentTimePeriods.Length != ourTimePeriods.Length)
                {
                    error = "In '" + Name + "' the number of time periods contained in the module is '" + TimePeriod.Length
                        + "', the parent has '" + ourTimePeriods.Length + "'.  These must be the same to continue.";
                    return false;
                }
                return true;
            }

            private SparseArray<IZone> zoneSystem;
            private IZone[] zones;
            private int[] FlatZoneToPDCubeLookup;


            internal void Load()
            {
                zoneSystem = Root.ZoneSystem.ZoneArray;
                zones = zoneSystem.GetFlatData();
                if (To == null)
                {
                    To = new float[TimePeriod.Length][];
                    From = new float[TimePeriod.Length][];
                    for (int i = 0; i < TimePeriod.Length; i++)
                    {
                        To[i] = new float[zones.Length * zones.Length];
                        From[i] = new float[zones.Length * zones.Length];
                    }
                }
                foreach (var timePeriod in TimePeriod)
                {
                    timePeriod.expSamePD = (float)Math.Exp(timePeriod.SamePD);
                }
                // raise the constants to e^constant to save CPU time during the main phase
                foreach (var timePeriod in TimePeriod)
                {
                    for (int i = 0; i < timePeriod.ODConstants.Length; i++)
                    {
                        timePeriod.ODConstants[i].ExpConstant = (float)Math.Exp(timePeriod.ODConstants[i].Constant);
                    }
                }
                if (!Parent.EstimationMode || PDCube == null)
                {
                    var pds = TMG.Functions.ZoneSystemHelper.CreatePDArray<float>(Root.ZoneSystem.ZoneArray);
                    BuildPDCube(pds);
                    if (FlatZoneToPDCubeLookup == null)
                    {
                        FlatZoneToPDCubeLookup = zones.Select(zone => pds.GetFlatIndex(zone.PlanningDistrict)).ToArray();
                    }
                }
                // now that we are done we can calculate our utilities
                CalculateUtilities();
            }

            private static float[][] CreateSquare(int length)
            {
                var ret = new float[length][];
                for (int i = 0; i < ret.Length; i++)
                {
                    ret[i] = new float[length];
                }
                return ret;
            }

            private void BuildPDCube(SparseArray<float> pds)
            {
                var numberOfPds = pds.Count;
                var pdIndex = pds.ValidIndexArray();
                PDCube = new int[TimePeriod.Length][][][];
                for (int timePeriod = 0; timePeriod < PDCube.Length; timePeriod++)
                {
                    PDCube[timePeriod] = new int[numberOfPds][][];
                    for (int i = 0; i < PDCube[timePeriod].Length; i++)
                    {
                        PDCube[timePeriod][i] = new int[numberOfPds][];
                        for (int j = 0; j < PDCube[timePeriod][i].Length; j++)
                        {
                            PDCube[timePeriod][i][j] = new int[numberOfPds];
                            for (int k = 0; k < PDCube[timePeriod][i][j].Length; k++)
                            {
                                PDCube[timePeriod][i][j][k] = GetODIndex(timePeriod, pdIndex[i], pdIndex[k], pdIndex[j]);
                            }
                        }
                    }
                }
            }

            protected void CalculateUtilities()
            {
                var zones = Root.ZoneSystem.ZoneArray.GetFlatData();
                var pf = Parent.ProfessionalFullTime.AquireResource<SparseArray<float>>().GetFlatData();
                var pp = Parent.ProfessionalPartTime.AquireResource<SparseArray<float>>().GetFlatData();
                var gf = Parent.GeneralFullTime.AquireResource<SparseArray<float>>().GetFlatData();
                var gp = Parent.GeneralPartTime.AquireResource<SparseArray<float>>().GetFlatData();
                var sf = Parent.RetailFullTime.AquireResource<SparseArray<float>>().GetFlatData();
                var sp = Parent.RetailPartTime.AquireResource<SparseArray<float>>().GetFlatData();
                var mf = Parent.ManufacturingFullTime.AquireResource<SparseArray<float>>().GetFlatData();
                var mp = Parent.ManufacturingPartTime.AquireResource<SparseArray<float>>().GetFlatData();
                if (pf.Length != zones.Length)
                {
                    throw new XTMFRuntimeException("The professional full-time employment data is not of the same size as the number of zones!");
                }
                float[][] jSum = new float[TimePeriod.Length][];
                for (int i = 0; i < TimePeriod.Length; i++)
                {
                    jSum[i] = new float[zones.Length];
                    Parallel.For(0, jSum[i].Length, (int j) =>
                    {
                        var jPD = zones[j].PlanningDistrict;

                        if (Parent.ValidDestinations[j])
                        {
                            var jUtil = (float)
                        Math.Exp(
                             (Math.Log(1 + pf[j]) * ProfessionalFullTime
                            + Math.Log(1 + pp[j]) * ProfessionalPartTime
                            + Math.Log(1 + gf[j]) * GeneralFullTime
                            + Math.Log(1 + gp[j]) * GeneralPartTime
                            + Math.Log(1 + sf[j]) * RetailFullTime
                            + Math.Log(1 + sp[j]) * RetailPartTime
                            + Math.Log(1 + mf[j]) * ManufacturingFullTime
                            + Math.Log(1 + mp[j]) * ManufacturingPartTime
                            + Math.Log(1 + zones[j].Population) * Population));


                            var nonExpPDConstant = 0.0f;
                            for (int seg = 0; seg < TimePeriod[i].PDConstant.Length; seg++)
                            {
                                if (TimePeriod[i].PDConstant[seg].Range.Contains(jPD))
                                {
                                    nonExpPDConstant += TimePeriod[i].PDConstant[seg].Constant;
                                    break;
                                }
                            }
                            jSum[i][j] = jUtil * (float)Math.Exp(nonExpPDConstant);
                        }
                        else
                        {
                            jSum[i][j] = 0.0f;
                        }
                    });
                }
                if (Parent.EstimationMode)
                {
                    for (int i = 0; i < Parent.TimePeriods.Length; i++)
                    {
                        GenerateEstimationLogsums(Parent.TimePeriods[i], zones);
                    }
                }
                var itterRoot = (Root as IIterativeModel);
                int currentIteration = itterRoot != null ? itterRoot.CurrentIteration : 0;
                Parallel.For(0, zones.Length, new ParallelOptions() { MaxDegreeOfParallelism = Environment.ProcessorCount }, (int i) =>
                {
                    var numberOfZones = zones.Length;
                    var network = Parent.AutoNetwork;
                    var transitNetwork = Parent.TransitNetwork;
                    var times = Parent.TimePeriods;
                    for (int time = 0; time < times.Length; time++)
                    {
                        Time timeOfDay = times[time].StartTime;
                        if (Parent.EstimationMode)
                        {
                            unsafe
                            {
                                fixed (float* to = To[time])
                                fixed (float* from = From[time])
                                fixed (float* logsumSpace = times[time].EstimationTempSpace)
                                {
                                    for (int j = 0; j < zones.Length; j++)
                                    {
                                        var nonExpPDConstant = jSum[time][j] * (i == j ? ExpIntraZonal : 1.0f);
                                        var travelUtility = logsumSpace[i * zones.Length + j];
                                        // compute to
                                        to[i * zones.Length + j] = nonExpPDConstant * travelUtility;
                                        // compute from
                                        from[j * zones.Length + i] = travelUtility;
                                    }
                                }
                            }
                        }

                        else
                        {
                            // if we are on anything besides the first iteration do a blended assignment for the utility to reduce saw toothing.
                            if (currentIteration == 0)
                            {
                                for (int j = 0; j < zones.Length; j++)
                                {
                                    var nonExpPDConstant = jSum[time][j] * (i == j ? ExpIntraZonal : 1.0f);
                                    var travelUtility = GetTravelLogsum(network, transitNetwork, i, j, timeOfDay);
                                    // compute to
                                    To[time][i * zones.Length + j] = nonExpPDConstant * travelUtility;
                                    // compute from
                                    From[time][j * zones.Length + i] = travelUtility;
                                }
                            }
                            else
                            {
                                for (int j = 0; j < zones.Length; j++)
                                {
                                    var nonExpPDConstant = jSum[time][j] * (i == j ? ExpIntraZonal : 1.0f);
                                    var travelUtility = GetTravelLogsum(network, transitNetwork, i, j, timeOfDay);
                                    // compute to
                                    To[time][i * zones.Length + j] = ((nonExpPDConstant * travelUtility) + To[time][i * zones.Length + j]) * 0.5f;
                                    // compute from
                                    From[time][j * zones.Length + i] = (travelUtility + From[time][j * zones.Length + i]) * 0.5f;
                                }
                            }
                        }
                    }
                });
            }


            internal float[] GetLocationProbabilities(IZone previousZone, IEpisode ep, IZone nextZone, Time startTime, Time availableTime, float[] calculationSpace)
            {
                var total = CalculateLocationProbabilities(previousZone, ep, nextZone, startTime, availableTime, calculationSpace);
                if (total <= 0.0f)
                {
                    return calculationSpace;
                }
                VectorHelper.Multiply(calculationSpace, 0, calculationSpace, 0, 1.0f / total, calculationSpace.Length);
                return calculationSpace;
            }

            /// <summary>
            /// 
            /// </summary>
            /// <param name="previousZone"></param>
            /// <param name="ep"></param>
            /// <param name="nextZone"></param>
            /// <param name="startTime"></param>
            /// <param name="availableTime"></param>
            /// <param name="calculationSpace"></param>
            /// <returns>The sum of the calculation space</returns>
            private float CalculateLocationProbabilities(IZone previousZone, IEpisode ep, IZone nextZone, Time startTime, Time availableTime, float[] calculationSpace)
            {
                var p = zoneSystem.GetFlatIndex(previousZone.ZoneNumber);
                var n = zoneSystem.GetFlatIndex(nextZone.ZoneNumber);
                var size = zones.Length;
                int index = GetTimePeriod(startTime);
                var rowTimes = Parent.TimePeriods[index].RowTravelTimes;
                var columnTimes = Parent.TimePeriods[index].ColumnTravelTimes;
                var from = From[index];
                var available = availableTime.ToMinutes();
                var to = To[index];
                var pIndex = FlatZoneToPDCubeLookup[p];
                var nIndex = FlatZoneToPDCubeLookup[n];
                var data = PDCube[index][pIndex][nIndex];
                int previousIndexOffset = p * size;
                int nextIndexOffset = n * size;
                float total = 0.0f;
                if (Vector.IsHardwareAccelerated)
                {
                    Vector<float> availableTimeV = new Vector<float>(available);
                    Vector<float> totalV = Vector<float>.Zero;
                    int i = 0;
                    if (nIndex == pIndex)
                    {
                        for (i = 0; i < calculationSpace.Length; i++)
                        {
                            var odUtility = 1.0f;
                            var pdindex = data[FlatZoneToPDCubeLookup[i]];
                            if (pdindex >= 0)
                            {
                                odUtility = (pIndex == FlatZoneToPDCubeLookup[i]) ? TimePeriod[index].ODConstants[pdindex].ExpConstant * TimePeriod[index].expSamePD
                                    : TimePeriod[index].ODConstants[pdindex].ExpConstant;
                            }
                            else
                            {
                                odUtility = (pIndex == FlatZoneToPDCubeLookup[i]) ? TimePeriod[index].expSamePD : 1.0f;
                            }
                            calculationSpace[i] = odUtility;
                        }
                    }
                    else
                    {
                        for (i = 0; i < calculationSpace.Length; i++)
                        {
                            var pdindex = data[FlatZoneToPDCubeLookup[i]];
                            calculationSpace[i] = pdindex >= 0 ? TimePeriod[index].ODConstants[pdindex].ExpConstant : 1f;
                        }
                    }

                    for (i = 0; i <= calculationSpace.Length - Vector<float>.Count; i += Vector<float>.Count)
                    {
                        var timeTo = new Vector<float>(rowTimes, previousIndexOffset + i);
                        var timeFrom = new Vector<float>(columnTimes, nextIndexOffset + i);
                        var utilityTo = new Vector<float>(to, previousIndexOffset + i);
                        var utilityFrom = new Vector<float>(from, nextIndexOffset + i);
                        Vector<float> calcV = new Vector<float>(calculationSpace, i);
                        Vector<int> zeroMask = Vector.LessThanOrEqual(timeTo + timeFrom, availableTimeV);
                        calcV = Vector.AsVectorSingle(Vector.BitwiseAnd(Vector.AsVectorInt32(calcV), zeroMask))
                            * utilityTo * utilityFrom;
                        calcV.CopyTo(calculationSpace, i);
                        totalV += calcV;
                    }
                    float remainderTotal = 0.0f;
                    for (; i < calculationSpace.Length; i++)
                    {
                        if (rowTimes[previousIndexOffset + i] + columnTimes[nextIndexOffset + i] <= available)
                        {
                            remainderTotal += (calculationSpace[i] = to[previousIndexOffset + i] * from[nextIndexOffset + i] * calculationSpace[i]);
                        }
                        else
                        {
                            calculationSpace[i] = 0;
                        }
                    }
                    total += remainderTotal + Vector.Dot(totalV, Vector<float>.One);
                }
                else
                {
                    unsafe
                    {
                        fixed (float* pRowTimes = &rowTimes[0])
                        fixed (float* pColumnTimes = &columnTimes[0])
                        fixed (float* pTo = &to[0])
                        fixed (float* pFrom = &from[0])
                        fixed (int* pData = &data[0])
                        {
                            if (nIndex == pIndex)
                            {
                                for (int i = 0; i < calculationSpace.Length; i++)
                                {
                                    if (pRowTimes[previousIndexOffset + i] + pColumnTimes[nextIndexOffset + i] <= available)
                                    {
                                        var odUtility = 1.0f;
                                        var pdindex = pData[FlatZoneToPDCubeLookup[i]];
                                        if (pdindex >= 0)
                                        {
                                            odUtility = (pIndex == FlatZoneToPDCubeLookup[i]) ?
                                                TimePeriod[index].ODConstants[pdindex].ExpConstant * TimePeriod[index].expSamePD
                                                : TimePeriod[index].ODConstants[pdindex].ExpConstant;
                                        }
                                        else
                                        {
                                            odUtility = (pIndex == FlatZoneToPDCubeLookup[i]) ? TimePeriod[index].expSamePD : 1.0f;
                                        }
                                        total += calculationSpace[i] = pTo[previousIndexOffset + i] * pFrom[nextIndexOffset + i] * odUtility;
                                    }
                                    else
                                    {
                                        calculationSpace[i] = 0;
                                    }
                                }
                            }
                            else
                            {
                                for (int i = 0; i < calculationSpace.Length; i++)
                                {
                                    if (pRowTimes[previousIndexOffset + i] + pColumnTimes[nextIndexOffset + i] <= available)
                                    {
                                        var odUtility = 1.0f;
                                        var pdindex = pData[FlatZoneToPDCubeLookup[i]];
                                        if (pdindex >= 0)
                                        {
                                            odUtility = TimePeriod[index].ODConstants[pdindex].ExpConstant;
                                        }
                                        total += calculationSpace[i] = pTo[previousIndexOffset + i] * pFrom[nextIndexOffset + i] * odUtility;
                                    }
                                    else
                                    {
                                        calculationSpace[i] = 0;
                                    }
                                }
                            }
                        }
                    }
                }
                return total;
            }

            internal IZone GetLocation(IZone previousZone, IEpisode ep, IZone nextZone, Time startTime, Time availableTime, float[] calculationSpace, Random random)
            {
                var total = CalculateLocationProbabilities(previousZone, ep, nextZone, startTime, availableTime, calculationSpace);
                if (total <= 0)
                {
                    return null;
                }
                var pop = (float)random.NextDouble() * total;
                float current = 0.0f;
                for (int i = 0; i < calculationSpace.Length; i++)
                {
                    current += calculationSpace[i];
                    if (pop <= current)
                    {
                        return zones[i];
                    }
                }
                for (int i = 0; i < calculationSpace.Length; i++)
                {
                    if (calculationSpace[i] > 0)
                    {
                        return zones[i];
                    }
                }
                return null;
            }

            private int GetODIndex(int timePeriod, int pPD, int iPD, int nPD)
            {
                for (int i = 0; i < TimePeriod[timePeriod].ODConstants.Length; i++)
                {
                    if (TimePeriod[timePeriod].ODConstants[i].Previous.Contains(pPD)
                        && TimePeriod[timePeriod].ODConstants[i].Interest.Contains(iPD)
                        && TimePeriod[timePeriod].ODConstants[i].Next.Contains(nPD))
                    {
                        return i;
                    }
                }
                return -1;
            }

            private int GetTimePeriod(Time startTime)
            {
                var periods = Parent.TimePeriods;
                int i;
                for (i = 0; i < periods.Length; i++)
                {
                    if (periods[i].StartTime <= startTime & periods[i].EndTime > startTime)
                    {
                        return i;
                    }
                }
                return (i - 1);
            }
        }

        public sealed class MarketLocationChoice : LocationChoiceActivity
        {

        }

        public sealed class OtherLocationChoice : LocationChoiceActivity
        {

        }

        public sealed class WorkBasedBusinessocationChoice : LocationChoiceActivity
        {

        }

        [SubModelInformation(Required = true)]
        public MarketLocationChoice MarketModel;

        [SubModelInformation(Required = true)]
        public MarketLocationChoice OtherModel;

        [SubModelInformation(Required = true)]
        public MarketLocationChoice WorkBasedBusinessModel;

        [SubModelInformation(Description = "The different time periods supported")]
        public TimePeriod[] TimePeriods;

        private INetworkData AutoNetwork;
        private ITripComponentData TransitNetwork;

        private static IZone GetZone(IEpisode otherEpisode, IEpisode inserting)
        {
            return otherEpisode == null ? inserting.Owner.Household.HomeZone : otherEpisode.Zone;
        }

        public IZone GetLocationHomeBased(Activity activity, IZone zone, Random random)
        {
            throw new NotImplementedException("This method is no longer supported for V4.0+");
        }

        public IZone GetLocationHomeBased(IEpisode episode, ITashaPerson person, Random random)
        {
            throw new NotImplementedException("This method is no longer supported for V4.0+");
        }

        public IZone GetLocationWorkBased(IZone primaryWorkZone, ITashaPerson person, Random random)
        {
            throw new NotImplementedException("This method is no longer supported for V4.0+");
        }

        public void LoadLocationChoiceCache()
        {
            for (int i = 0; i < TimePeriods.Length; i++)
            {
                TimePeriods[i].Load();
            }
            if (!EstimationMode || ValidDestinations == null)
            {
                ValidDestinations = Root.ZoneSystem.ZoneArray.GetFlatData().Select(zone => ValidDestinationZones.Contains(zone.ZoneNumber)).ToArray();
            }
            MarketModel.Load();
            OtherModel.Load();
            WorkBasedBusinessModel.Load();
        }

        public bool RuntimeValidation(ref string error)
        {
            if (!ProfessionalFullTime.CheckResourceType<SparseArray<float>>())
            {
                error = "In '" + Name + "' the sub module Professional Full Time was not of type SparseArray<float>!";
                return false;
            }
            if (!ProfessionalPartTime.CheckResourceType<SparseArray<float>>())
            {
                error = "In '" + Name + "' the sub module Professional Part Time was not of type SparseArray<float>!";
                return false;
            }
            if (!ManufacturingFullTime.CheckResourceType<SparseArray<float>>())
            {
                error = "In '" + Name + "' the sub module Manufacturing Full Time was not of type SparseArray<float>!";
                return false;
            }
            if (!ManufacturingPartTime.CheckResourceType<SparseArray<float>>())
            {
                error = "In '" + Name + "' the sub module Manufacturing Part Time was not of type SparseArray<float>!";
                return false;
            }
            if (!GeneralFullTime.CheckResourceType<SparseArray<float>>())
            {
                error = "In '" + Name + "' the sub module General Full Time was not of type SparseArray<float>!";
                return false;
            }
            if (!GeneralPartTime.CheckResourceType<SparseArray<float>>())
            {
                error = "In '" + Name + "' the sub module General Part Time was not of type SparseArray<float>!";
                return false;
            }
            if (!RetailFullTime.CheckResourceType<SparseArray<float>>())
            {
                error = "In '" + Name + "' the sub module Retail Full Time was not of type SparseArray<float>!";
                return false;
            }
            if (!RetailPartTime.CheckResourceType<SparseArray<float>>())
            {
                error = "In '" + Name + "' the sub module Retail Part Time was not of type SparseArray<float>!";
                return false;
            }
            foreach (var network in Root.NetworkData)
            {
                if (network.NetworkType == AutoNetworkName)
                {
                    AutoNetwork = network;
                    break;
                }
            }
            if (AutoNetwork == null)
            {
                error = "In '" + Name + "' we were unable to find a network called '" + AutoNetworkName + "'";
            }

            foreach (var network in Root.NetworkData)
            {
                if (network.NetworkType == TransitNetworkName)
                {
                    TransitNetwork = network as ITripComponentData;
                    break;
                }
            }
            if (TransitNetwork == null)
            {
                error = "In '" + Name + "' we were unable to find a network called '" + AutoNetworkName + "'";
            }
            return true;
        }
    }

}
