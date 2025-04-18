﻿using AeonHacs;
using System.Collections.Generic;
using static AeonHacs.Utilities.Utility;

namespace AeonHacs.Components
{

    public class Valve : HacsDevice, IValve, Valve.IDevice, Valve.IConfig
    {

        #region Device interfaces
        public new interface IDevice : HacsDevice.IDevice
        {
            ValveState ValveState { get; set; }
            int Position { get; set; }
        }
        public new interface IConfig : HacsDevice.IConfig { }
        public new IDevice Device => this;
        public new IConfig Config => this;

        #endregion Device interfaces

        public virtual ValveState ValveState
        {
            get => valveState;
            protected set => Ensure(ref valveState, value);
        }
        ValveState valveState = ValveState.Unknown;
        ValveState IDevice.ValveState
        {
            get => ValveState;
            set => ValveState = value;
        }

        /// <summary>
        /// Absolute position "Value"
        /// </summary>
        public virtual int Position
        {
            get => position;
            protected set => Ensure(ref position, value);
        }
        int position;

        int IDevice.Position
        {
            get => Position;
            set => Position = value;
        }

        public virtual double OpenedVolumeDelta
        {
            get => openedVolumeDelta;
            set => Ensure(ref openedVolumeDelta, value);
        }
        double openedVolumeDelta = 0.0;

        public virtual List<string> Operations { get; protected set; } = new List<string>();
        public virtual void DoOperation(string operationName) { }
        public virtual bool Ready => false;
        public virtual bool Idle => true;
        public virtual bool IsOpened => ValveState == ValveState.Opened;
        public virtual bool IsClosed => ValveState == ValveState.Closed;
        public virtual void Open() => DoOperation("Open");
        public virtual void Close() => DoOperation("Close");
        public virtual void Stop() => DoOperation("Stop");
        public void OpenWait() { Open(); WaitForIdle(); }
        public void CloseWait() { Close(); WaitForIdle(); }
        public virtual void DoWait(string operation) { DoOperation(operation); WaitForIdle(); }
        public virtual void WaitForIdle() => WaitFor(() => Idle, -1, 35);
        public virtual void Exercise() { }
        public Valve(IHacsDevice d = null) : base(d) { }

    }
}
