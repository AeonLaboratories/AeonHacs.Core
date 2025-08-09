using Newtonsoft.Json;
using System.ComponentModel;

namespace AeonHacs.Components
{
    /// <summary>
    /// A device that autonomously controls an output to maintain a sensed value at a given setpoint.
    /// </summary>
    public class Auto : Switch, IAuto, Auto.IDevice, Auto.IConfig
    {
        #region Device interfaces

        public new interface IDevice : Switch.IDevice
        {
            double Setpoint { get; set; }
        }

        public new interface IConfig : Switch.IConfig
        {
            double Setpoint { get; }
        }

        public new IDevice Device => this;
        public new IConfig Config => this;

        #endregion Device interfaces

        /// <summary>
        /// The Setpoint value. Default -999.
        /// </summary>
        public virtual double Setpoint
        {
            get => setpoint;
            set
            {
                if (value > MaximumSetpoint)
                    value = MaximumSetpoint;
                if (value < MinimumSetpoint)
                    value = MinimumSetpoint;
                if (Ensure(ref TargetSetpoint, value, NotifyConfigChanged, nameof(TargetSetpoint)) && Initialized)
                    Hacs.SystemLog.Record($"{Name}.Setpoint = {value:0.00}");
            }
        }
        [JsonProperty("Setpoint")]
        double TargetSetpoint;
        double IConfig.Setpoint => TargetSetpoint;

        double IDevice.Setpoint
        {
            get => setpoint;
            set => Ensure(ref setpoint, value);
        }
        double setpoint = -999;


        /// <summary>
        /// Trying to set the Setpoint below this
        /// causes the Setpoint to be this value instead.
        /// </summary>
        [JsonProperty, DefaultValue(-999.0)]
        public double MinimumSetpoint
        {
            get => minimumSetpoint;
            set => Ensure(ref minimumSetpoint, value);
        }
        double minimumSetpoint = -999.0;


        /// <summary>
        /// Trying to set the Setpoint above this
        /// causes the Setpoint to be this value instead.
        /// </summary>
        [JsonProperty, DefaultValue(1200.0)]
        public double MaximumSetpoint
        {
            get => maximumSetpoint;
            set => Ensure(ref maximumSetpoint, value);
        }
        double maximumSetpoint = 1200.0;


        public virtual void TurnOn(double setpoint)
        {
            Setpoint = setpoint;
            TurnOn();
        }

        public Auto(IHacsDevice d = null) : base(d) { }
    }
}
