﻿using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Data;
using System.Xml.Serialization;
using Models.Core;
using System.Reflection;

namespace Models
{

    [ViewName("UserInterface.Views.ReportView")]
    [PresenterName("UserInterface.Presenters.ReportPresenter")]
    public class Report : Model
    {
        /// <summary>
        /// A variable class for looking after multiple values for a single variable. 
        /// The variable might be a scalar, an array or a class or structure. This class 
        /// has members for returning names, types and values ready for DataStore. It 
        /// can handle array sizes changing through a simulation. It "flattens" arrays and structures
        /// e.g. if the variable is sw_dep and has 3 elements then
        ///      Names -> sw_dep(1), sw_dep(2), sw_dep(3)
        ///      Types ->    double,    double,    double
        ///      
        /// e.g. if the variable is a struct {double A; double B; double C;}
        ///      Names -> struct.A, struct.B, struct.C
        /// </summary>
        private class VariableMember
        {
            private MemberInfo Info;
            private object Model;
            private string FullName;
            private List<string> _Names = new List<string>();
            private List<Type> _Types = new List<Type>();
            private List<object> _Values = new List<object>();
            private int MaxNumArrayElements;

            /// <summary>
            /// Constructor
            /// </summary>
            public VariableMember(string FullName, MemberInfo Info, object Model)
            {
                this.FullName = FullName;
                this.Info = Info;
                this.Model = Model;
            }

            /// <summary>
            /// Analyse the variable to "flatten" arrays and classes to a list of names and types.
            /// </summary>
            public void Analyse()
            {
                // Work out the type of data we're dealing with.
                Type T = null;
                if (Info is PropertyInfo)
                    T = (Info as PropertyInfo).PropertyType;
                else if (Info is FieldInfo)
                    T = (Info as FieldInfo).FieldType;

                if (T != null && _Values.Count > 0)
                {
                    if (T.IsArray)
                    {
                        // Array - calculate the maximum number of array elements and analyse each array element
                        // on row 0 for a name and type.
                        MaxNumArrayElements = CalcMaxNumArrayElements();

                        Array Arr = _Values[0] as Array;
                        for (int Col = 0; Col < MaxNumArrayElements; Col++)
                        {
                            string Heading = FullName + "(" + (Col + 1).ToString() + ")";
                            if (Col < Arr.Length)
                                AnalyseValue(Heading, Arr.GetValue(Col));
                        }
                    }
                    else
                    {
                        AnalyseValue(FullName, _Values[0]);
                    }
                }
            }

            /// <summary>
            /// Return a list of names for this variable after flattening out arrays and 
            /// structures.
            /// </summary>
            public string[] Names { get { return _Names.ToArray(); } }

            /// <summary>
            /// Return a list of types for this variable.
            /// </summary>
            public Type[] Types { get { return _Types.ToArray(); } }

            /// <summary>
            /// Return an array of values for the specified row.
            /// </summary>
            public object[] Values(int Row)
            {
                List<object> AllValues = new List<object>();

                // Work out the type of data we're dealing with.
                Type T = null;
                if (Info is PropertyInfo)
                    T = (Info as PropertyInfo).PropertyType;
                else if (Info is FieldInfo)
                    T = (Info as FieldInfo).FieldType;

                if (T != null)
                {
                    if (T.IsArray)
                    {
                        // Add required columns
                        Array Arr = _Values[Row] as Array;
                        for (int Col = 0; Col < MaxNumArrayElements; Col++)
                        {
                            string Heading = FullName + "(" + (Col + 1).ToString() + ")";
                            if (Col < Arr.Length)
                                AddValueToList(AllValues, Row, Heading, Arr.GetValue(Col));
                        }
                    }
                    else
                    {
                        AddValueToList(AllValues, Row, FullName, _Values[Row]);
                    }
                }
                return AllValues.ToArray();
            }

            /// <summary>
            /// Return the number of values stored in this variable.
            /// </summary>
            public int NumValues { get { return _Values.Count; } }

            /// <summary>
            /// Store the current value in our array of values.
            /// </summary>
            public void StoreValue()
            {
                object Value = null;
                if (Info != null && Model != null)
                {
                    if (Info is FieldInfo)
                        Value = (Info as FieldInfo).GetValue(Model);
                    else if (Info is PropertyInfo)
                        Value = (Info as PropertyInfo).GetValue(Model, null);
                    if (Value.GetType().IsArray)
                    {
                        Array A = Value as Array;
                        Value = A.Clone();
                    }
                }
                _Values.Add(Value);
            }

            /// <summary>
            /// Analyse the value passed in and store it's name and type in the
            /// Names and Types List.
            /// </summary>
            private void AnalyseValue(string Name, object Value)
            {
                Type T = Value.GetType();

                // Scalar
                if (T == typeof(DateTime) || T == typeof(string) || !T.IsClass)
                {
                    // Built in type.
                    _Names.Add(Name);
                    _Types.Add(T);
                }
                else
                {
                    // class or struct.
                    foreach (PropertyInfo Property in Model.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
                    {
                        _Names.Add(Name + "." + Property.Name);
                        _Types.Add(Property.PropertyType);
                    }
                    foreach (FieldInfo Field in Model.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public))
                    {
                        _Names.Add(Name + "." + Field.Name);
                        _Types.Add(Field.FieldType);
                    }
                }
            }

            /// <summary>
            /// Add the specified Value to the AllValues list. If Value is a class then it will add 
            /// class fields and property values as well.
            /// </summary>
            private void AddValueToList(List<object> AllValues, int Row, string Name, object Value)
            {
                Type T = Value.GetType();

                // Scalar
                if (T == typeof(DateTime) || T == typeof(string) || !T.IsClass)
                {
                    // Built in type.
                    AllValues.Add(Value);
                }
                else
                {
                    // class or struct.
                    foreach (PropertyInfo Property in Model.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
                    {
                        AllValues.Add(Property.GetValue(Value, null));
                    }
                    foreach (FieldInfo Field in Model.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public))
                    {
                        AllValues.Add(Field.GetValue(Value));
                    }
                }
            }

            /// <summary>
            /// Calculate the maximum number of array elements.
            /// </summary>
            private int CalcMaxNumArrayElements()
            {
                int MaxNumValues = 0;
                foreach (object Value in _Values)
                {
                    if (Value != null)
                        MaxNumValues = Math.Max(MaxNumValues, (Value as Array).Length);
                }
                return MaxNumValues;
            }
        }

        
        // Links.
        [Link] private DataStore DataStore = null;
        [Link] private Zone Paddock = null;
        [Link] private Simulation Simulation = null;

        // privates
        private List<VariableMember> Members = null;

        // Properties read in.
        public string[] Variables {get; set;}
        public string[] Events { get; set; }

        // The user interface would like to know our paddock
        public Zone ParentZone { get { return Paddock; } }

        /// <summary>
        /// An event handler to allow us to initialise ourselves.
        /// </summary>
        public override void OnInitialised()
        {
            foreach (string Event in Events)
            {
                string ComponentName = Utility.String.ParentName(Event, '.');
                string EventName = Utility.String.ChildName(Event, '.');

                if (ComponentName == null)
                    throw new Exception("Invalid syntax for reporting event: " + Event);
                object Component = Paddock.Find(ComponentName);
                if (Component == null)
                    throw new Exception(Name + " can not find the component: " + ComponentName);
                EventInfo ComponentEvent = Component.GetType().GetEvent(EventName);
                if (ComponentEvent == null)
                    throw new Exception("Cannot find event: " + EventName + " in model: " + ComponentName);

                ComponentEvent.AddEventHandler(Component, new EventHandler(OnReport));
            }
        }

        /// <summary>
        /// Event handler for the report event.
        /// </summary>
        public void OnReport(object sender, EventArgs e)
        {
            if (Members == null)
                FindVariableMembers();
            foreach (VariableMember Variable in Members)
                Variable.StoreValue();
        }

        /// <summary>
        /// Fill the Members list with VariableMember objects for each variable.
        /// </summary>
        private void FindVariableMembers()
        {
            Members = new List<VariableMember>();

            List<string> Names = new List<string>();
            List<Type> Types = new List<Type>();
            foreach (string FullVariableName in Variables)
            {
                string ParentName = Utility.String.ParentName(FullVariableName);
                string VariableName = Utility.String.ChildName(FullVariableName);
                object Parent = Paddock.Get(ParentName);

                MemberInfo FoundMember = null;
                if (Parent != null)
                {
                    MemberInfo[] Info = Parent.GetType().GetMember(VariableName);
                    if (Info.Length > 0)
                        FoundMember = Info[0];
                }
                Members.Add(new VariableMember(FullVariableName, FoundMember, Parent));
            }
        }

        /// <summary>
        /// Simulation has completed - write the report table.
        /// </summary>
        public override void OnCompleted()
        {
            // Write and store a table in the DataStore
            if (Members.Count > 0)
            {
                List<string> AllNames = new List<string>();
                List<Type> AllTypes = new List<Type>();
                foreach (VariableMember Variable in Members)
                {
                    Variable.Analyse();
                    AllNames.AddRange(Variable.Names);
                    AllTypes.AddRange(Variable.Types);
                }
                DataStore.CreateTable(Name, AllNames.ToArray(), AllTypes.ToArray());

                List<object> AllValues = new List<object>();
                for (int Row = 0; Row < Members[0].NumValues; Row++)
                {
                    AllValues.Clear();
                    foreach (VariableMember Variable in Members)
                    {
                        AllValues.AddRange(Variable.Values(Row));
                    }
                    DataStore.WriteToTable(Simulation.Name, Name, AllValues.ToArray());
                }
            }

            // Unsubscribe to all events.
            foreach (string Event in Events)
            {
                string ComponentName = Utility.String.ParentName(Event, '.');
                string EventName = Utility.String.ChildName(Event, '.');

                object Component = Paddock.Find(ComponentName);
                EventInfo ComponentEvent = Component.GetType().GetEvent(EventName);

                ComponentEvent.RemoveEventHandler(Component, new EventHandler(OnReport));
            }
            Members.Clear();
            Members = null;
        }

    }
}