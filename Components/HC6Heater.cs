using AeonHacs;
using Newtonsoft.Json;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using AeonHacs.Utilities;

namespace AeonHacs.Components
{
    public class HC6Heater : ManagedHeater, IHC6Heater,
        HC6Heater.IConfig, HC6Heater.IDevice
    {
        #region static

        public static implicit operator double(HC6Heater x)
        { return x?.Temperature ?? 0; }

        #endregion static

        #region HacsComponent

        [HacsConnect]
        protected virtual void Connect()
        {
            Pid = Find<IPidSetup>(pidName);
        }

        #endregion HacsComponent

        #region Device constants

        /// <summary>
        /// Heater operating modes
        /// </summary>
        public enum Modes
        {
            /// <summary>
            /// Device is powered off.
            /// </summary>
            Off = '0',
            /// <summary>
            /// Device power is fixed at a Setpoint power level in the range of [0..100%].
            /// </summary>
            Manual = 'm',
            /// <summary>
            /// Device power is automatically controlled to reach and maintain a
            /// Setpoint temperature provided in °C.
            /// </summary>
            Auto = 'a'
        }

        #endregion Device constants

        #region Class interface properties and methods

        #region Device interfaces

        public new interface IDevice : ManagedHeater.IDevice
        {
            Modes Mode { get; set; }
            int ThermocoupleChannel { get; set; }
            int PidGain { get; set; }
            int PidIntegral { get; set; }
            int PidDerivative { get; set; }
            int PidPreset { get; set; }
            HC6ErrorCodes Errors { get; set; }
        }

        public new interface IConfig : ManagedHeater.IConfig
        {
            Modes Mode { get; }
            int ThermocoupleChannel { get; }
            int PidGain { get; }
            int PidIntegral { get; }
            int PidDerivative { get; }
            int PidPreset { get; }
        }
        public new IDevice Device => this;
        public new IConfig Config => this;

        #endregion Device interfaces

        /// <summary>
        /// The Heater's operating Mode {Off, Manual, or Auto}.
        /// </summary>
        public Modes Mode { get; protected set; }
        Modes IConfig.Mode => !Config.State.IsOn() ? Modes.Off : Config.ManualMode ?  Modes.Manual : Modes.Auto;
        Modes IDevice.Mode
        {
            get => Mode;
            set
            {
                if (Mode != value)
                {
                    Mode = value;
                    if (Mode == Modes.Auto)
                    {
                        Device.ManualMode = false;
                        Device.OnOffState = OnOffState.On;
                    }
                    else if (Mode == Modes.Manual)
                    {
                        Device.ManualMode = true;
                        Device.OnOffState = OnOffState.On;
                    }
                    else
                    {
                        Device.OnOffState = OnOffState.Off;
                    }
                    NotifyConfigChanged(nameof(IsOn));
                    NotifyPropertyChanged(nameof(Mode));
                    NotifyPropertyChanged(nameof(OnOffState));
                    NotifyPropertyChanged(nameof(IsOn));
                }
            }
        }


        /// <summary>
        /// The controller thermocouple channel that monitors this 
        /// device's temperature.
        /// </summary>
        public int ThermocoupleChannel
        {
            get => tcChannel;
            set => Ensure(ref TargetThermocoupleChannel, value, NotifyConfigChanged, nameof(TargetThermocoupleChannel));
        }
        [JsonProperty("ThermocoupleChannel"), DefaultValue(-1)]
        int TargetThermocoupleChannel;
        int IConfig.ThermocoupleChannel => TargetThermocoupleChannel;

        int IDevice.ThermocoupleChannel
        {
            get => tcChannel;
            set
            {
                if (tcChannel != value)
                {
                    tcChannel = value;
                    var tcKey = $"t{tcChannel}";
                    Thermocouple =
                        Manager is HC6Controller c &&
                        c.Devices.ContainsKey(tcKey) &&
                        c.Devices[tcKey] is HC6Thermocouple tc ?
                        tc : null;
                    NotifyPropertyChanged(nameof(ThermocoupleChannel));
                }
            }
        }
        int tcChannel = -1;


        /// <summary>
        /// The thermocouple attached to the Heater's ThermocoupleChannel
        /// </summary>
        public HC6Thermocouple Thermocouple
        {
            get => thermocouple;
            private set => Ensure(ref thermocouple, value, OnPropertyChanged);
        }
        HC6Thermocouple thermocouple;


        #region PID settings

        /// <summary>
        /// The PidSetup to use when operating the device in Auto mode.
        /// </summary>
        public IPidSetup Pid
        {
            get => pid;
            set => Ensure(ref pid, value, OnPropertyChanged);
        }
        IPidSetup pid;

        [JsonProperty("PidSetup")]
        string PidName { get => Pid?.Name; set => pidName = value; }
        string pidName;

        /// <summary>
        /// The encoded PidGain value used when communicating with the controller.
        /// </summary>
        public int PidGain
        {
            get => pidGain;
            private set => Ensure(ref TargetPidGain, value, NotifyConfigChanged, nameof(TargetPidGain));
        }
        int TargetPidGain;
        int IConfig.PidGain => TargetPidGain;
        int IDevice.PidGain
        {
            get => pidGain;
            set => Ensure(ref pidGain, value);
        }
        int pidGain;

        /// <summary>
        /// The encoded PidIntegral value used when communicating with the controller.
        /// </summary>
        public int PidIntegral
        {
            get => pidIntegral;
            private set => Ensure(ref TargetPidIntegral, value, NotifyConfigChanged, nameof(TargetPidIntegral));
        }
        int TargetPidIntegral;
        int IConfig.PidIntegral => TargetPidIntegral;
        int IDevice.PidIntegral
        {
            get => pidIntegral;
            set => Ensure(ref pidIntegral, value, NotifyConfigChanged, nameof(TargetPidGain));
        }
        int pidIntegral;

        /// <summary>
        /// The encoded PidDerivative value used when communicating with the controller.
        /// </summary>
        public int PidDerivative
        {
            get => pidDerivative;
            private set => Ensure(ref TargetPidDerivative, value, NotifyConfigChanged, nameof(TargetPidDerivative));
        }
        int TargetPidDerivative;
        int IConfig.PidDerivative => TargetPidDerivative;
        int IDevice.PidDerivative
        {
            get => pidDerivative;
            set => Ensure(ref pidDerivative, value);
        }
        int pidDerivative;

        /// <summary>
        /// The encoded PidPreset value used when communicating with the controller.
        /// </summary>
        public int PidPreset
        {
            get => pidPreset;
            private set => Ensure(ref TargetPidPreset, value, NotifyConfigChanged, nameof(TargetPidPreset));
        }
        int TargetPidPreset;
        int IConfig.PidPreset => TargetPidPreset;
        int IDevice.PidPreset
        {
            get => pidPreset;
            set => Ensure(ref pidPreset, value);
        }
        int pidPreset;

        #endregion PID settings


        /// <summary>
        /// Error codes reported by the controller.
        /// </summary>
        public HC6ErrorCodes Errors => errors;
        HC6ErrorCodes IDevice.Errors
        {
            get => errors;
            set => Ensure(ref errors, value);
        }
        HC6ErrorCodes errors;

        /// <summary>
        /// The controller's Pid configuration for this device 
        /// matches the desired setup.
        /// </summary>
        public bool PidConfigured()
        {
            return
                pidGain == TargetPidGain &&
                pidIntegral == TargetPidIntegral &&
                pidDerivative == TargetPidDerivative &&
                pidPreset == TargetPidPreset;
        }

        #endregion Class interface properties and methods


        void SetPidConfig()
        {
            if (Pid == null)
                SetPidConfig(0, 0, 0, 0);
            else
                SetPidConfig(Pid.EncodedGain, Pid.EncodedIntegral, Pid.EncodedDerivative, Pid.EncodedPreset);
        }
        void SetPidConfig(int gain, int integral, int derivative, int preset)
        {
            PidGain = gain;
            PidIntegral = integral;
            PidDerivative = derivative;
            PidPreset = preset;
        }

        public override void OnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            var propertyName = e?.PropertyName;
            if (sender == Pid)
                SetPidConfig();
            else if (sender == Thermocouple)
            {
                if (propertyName == nameof(IThermocouple.Temperature))
                    Device.Temperature = Thermocouple.Temperature;
                else
                    NotifyPropertyChanged(nameof(Thermocouple));
            }
            else
                base.OnPropertyChanged(sender, e);
        }


        public HC6Heater(IHacsDevice d = null) : base(d) { }


        public override string ToString()
        {
            StringBuilder sb = new StringBuilder($"{Name}:");
            if (Thermocouple != null)
                sb.Append($" {Temperature:0.0} {UnitSymbol}");
            sb.Append($" ({IsOn.OnOff()})");

            StringBuilder sb2 = new StringBuilder();
            sb2.Append($"\r\nControl Mode: {(Config.ManualMode ? "Manual" : "Auto")}");
            if (!Config.ManualMode)
                sb2.Append($"\r\nSetpoint: {Setpoint:0} {UnitSymbol}");
            sb2.Append($"\r\nPower Level: {PowerLevel:0.00}%");
            sb2.Append($"\r\nPower Max: {MaximumPowerLevel:0.00}%");

            if (Pid != null)
            {
                sb2.Append($"\r\nPid Setup: {Pid}");
                if (!PidConfigured())
                    sb2.Append("   (not Configured)");
            }

            sb2.Append(ManagedDevice.ManagerString(this));

            if (Errors != 0)
                sb2.Append($"\r\nError = {Errors}");
            if (Thermocouple is HC6Thermocouple tc)
                sb2.Append($"\r\n{tc}");
            sb.Append(Utility.IndentLines(sb2.ToString()));
            return sb.ToString();
        }
    }
}