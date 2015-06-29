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
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Windows;

namespace XTMF.Gui.Models
{
    sealed class LinkedParameterDisplayModel : INotifyPropertyChanged, IDisposable
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private LinkedParameterModel RealLinkedParameter;

        public string Name
        {
            get
            {
                return RealLinkedParameter.Name;
            }
            set
            {
                string error = null;
                RealLinkedParameter.SetName(value, ref error);
                ModelHelper.PropertyChanged(PropertyChanged, this, "Name");
            }
        }

        public LinkedParameterDisplayModel(LinkedParameterModel realLinkedParameter)
        {
            RealLinkedParameter = realLinkedParameter;
            RealLinkedParameter.PropertyChanged += RealLinkedParameter_PropertyChanged;
        }

        private void RealLinkedParameter_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            ModelHelper.PropertyChanged(PropertyChanged, this, e.PropertyName);
        }

        ~LinkedParameterDisplayModel()
        {
            Dispose();
        }

        public void Dispose()
        {
            PropertyChanged = null;
            RealLinkedParameter.PropertyChanged -= RealLinkedParameter_PropertyChanged;
        }

        internal static ObservableCollection<LinkedParameterDisplayModel> CreateDisplayModel(ObservableCollection<LinkedParameterModel> observableCollection)
        {
            return new ObservableCollection<LinkedParameterDisplayModel>(observableCollection.Select(lp => new LinkedParameterDisplayModel(lp)));
        }
    }
}