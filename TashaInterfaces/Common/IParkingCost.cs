﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TMG;
using XTMF;

namespace Tasha.Common
{
    /// <summary>
    /// This interface is used to build models that can calculate parking costs
    /// </summary>
    public interface IParkingCost : IDataSource<IParkingCost>
    {
        float ComputeParkingCost(Time parkingStart, Time parkingEnd, IZone zone);
        float ComputeParkingCost(Time parkingStart, Time parkingEnd, int flatZone);
    }
}
