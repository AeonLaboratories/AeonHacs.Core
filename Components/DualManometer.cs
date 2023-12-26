using AeonHacs;
using Newtonsoft.Json;
using System;
using System.ComponentModel;
using AeonHacs.Utilities;

namespace AeonHacs.Components
{
    
    // vacuum system pressure monitor
    public class DualManometer : SwitchedManometer, IDualManometer, DualManometer.IDevice, DualManometer.IConfig
    {
        #region static

        public static implicit operator double(DualManometer x)
        { return x?.Pressure ?? 0; }

        #endregion static

        #region HacsComponent

        [HacsConnect]
        protected virtual void Connect()
        {
            HighPressureManometer = Find<IManometer>(highPressureManometerName);
            LowPressureManometer = Find<IManometer>(lowPressureManometerName);
            Switch = LowPressureManometer as ISwitch;
        }

        #endregion HacsComponent

        #region Device interfaces

        public new interface IDevice : SwitchedManometer.IDevice { }
        public new interface IConfig : SwitchedManometer.IConfig { }
        public new IDevice Device => this;
        public new IConfig Config => this;

        #endregion Device interfaces

        // for pressures > high vacuum
        [JsonProperty("HighPressureManometer")]
        string HighPressureManometerName { get => HighPressureManometer?.Name; set => highPressureManometerName = value; }
        string highPressureManometerName;
        public IManometer HighPressureManometer
        {
            get => highPressureManometer;
            set => Ensure(ref highPressureManometer, value, OnPropertyChanged);
        }
        IManometer highPressureManometer;

        // for pressures <= "high vacuum"
        [JsonProperty("LowPressureManometer")]
        string LowPressureManometerName { get => LowPressureManometer?.Name; set => lowPressureManometerName = value; }
        string lowPressureManometerName;
        public IManometer LowPressureManometer
        {
            get => lowPressureManometer;
            set
            {
                if (Ensure(ref lowPressureManometer, value, OnPropertyChanged))
                {
                    if (lowPressureManometer is ISwitchedManometer switchedManometer)
                    {
                        base.StopAction = switchedManometer.StopAction;
                        base.MillisecondsToValid = switchedManometer.MillisecondsToValid;
                        base.MinimumMillisecondsOff = switchedManometer.MinimumMillisecondsOff;
                    }
                }
            }
        }
        IManometer lowPressureManometer;

        [JsonProperty]
        public double MaximumLowPressure { get; set; }
        [JsonProperty]
        public double MinimumHighPressure { get; set; }
        [JsonProperty]
        public double SwitchpointPressure { get; set; }

        public override StopAction StopAction
        { 
            get => base.StopAction;
            set
            {
                if (Switch != null)
                    Switch.StopAction = value;
                base.StopAction = value;
            }
        }

        public override int MillisecondsToValid
        { 
            get => base.MillisecondsToValid;
            set
            {
                if (LowPressureManometer is ISwitchedManometer switchedManometer)
                    switchedManometer.MillisecondsToValid = value;
                base.MillisecondsToValid = value;
            }
        }

        public override int MinimumMillisecondsOff
        { 
            get => base.MinimumMillisecondsOff;
            set
            {
                if (LowPressureManometer is ISwitchedManometer switchedManometer)
                    switchedManometer.MinimumMillisecondsOff = value;
                base.MinimumMillisecondsOff = value;
            }
        }


        // not a HacsComponent Update operation
        // triggered by change in either component manometer;
        // might be called twice for a single DAQ read...
        public void UpdatePressure()
        {
            if (!Initialized) return;

            double pressure;
            double pHP = Math.Max(HighPressureManometer.Pressure, HighPressureManometer.Sensitivity);
            double pLP = Math.Max(LowPressureManometer.Pressure, LowPressureManometer.Sensitivity);

            if (pHP >= MinimumHighPressure || (LowPressureManometer is ISwitchedManometer s && !s.Valid) || (LowPressureManometer is EdwardsAimX x && x.Error != 0))
                pressure = pHP;
            else if (pLP <= MaximumLowPressure || pHP <= pLP)
                pressure = pLP;
            else    // MaximumLowPressure < pLP < pHP < MinimumHighPressure
            {
                // low pressure reading weight coefficient
                double wlp = (MinimumHighPressure - pLP) / (MinimumHighPressure - MaximumLowPressure);
                pressure = (1 - wlp) * pHP + wlp * pLP;
            }

            if (pressure < 0) pressure = 0;         // this should never happen
            Device.Pressure = pressure;
            ManageSwitch();
        }

        /// <summary>
        /// Automatically manage the LowPressureManometer's on/off state 
        /// based on the pressure and settings.
        /// </summary>
        public bool ManualMode
        {
            get => manualMode;
            set => Ensure(ref manualMode, value);
        }
        bool manualMode = false;

        public override bool TurnOn()
        {
            ManualMode = true;
            return Switch.TurnOn();
        }
        public override bool TurnOff()
        {
            ManualMode = true;
            return Switch.TurnOff();
        }

        void ManageSwitch()
        {
            if (ManualMode || Switch == null) return;

            bool pressureHigh = HighPressureManometer.Pressure > SwitchpointPressure;
            if (pressureHigh && !Switch.IsOff)
                Switch.TurnOff();
            else if (!pressureHigh && !Switch.IsOn)
                Switch.TurnOn();
        }

        bool discard = true;
        public override void OnPropertyChanged(object sender = null, PropertyChangedEventArgs e = null)
        {
            // Both manometers trigger this method on every DAQ scan, but the 
            // pressure should only be updated once per scan, in case there is a 
            // filter, which potentially depends on the DAQ scan frequency.
            // Ideally, UpdatePressure should be called when the second of the two
            // events occurs, but there currently is no way to be sure.
            var propertyName = e?.PropertyName;
            if (sender == LowPressureManometer || sender == HighPressureManometer)
            {
                if (propertyName == nameof(IValue.Value))
                {
                    if (!discard)
                        UpdatePressure();
                    discard = !discard;
                }
                else
                    NotifyPropertyChanged(propertyName);
            }
        }

        public DualManometer(IHacsDevice d = null) : base(d) { }

        public override string ToString()
        {
            return ManometerString() +
                Utility.IndentLines(
                    $"\r\n{LowPressureManometer}" +
                    $"\r\n{HighPressureManometer}");
        }
    }
}
