using AeonHacs;
using Newtonsoft.Json;
using System;
using System.ComponentModel;

namespace AeonHacs.Components
{
    /// <summary>
    /// A device capable of automatic temperature control.
    /// </summary>
    public class Oven : Thermometer, IAuto, IOven, Oven.IDevice, Oven.IConfig
    {
        #region Device interfaces

        public new interface IDevice : Thermometer.IDevice, Auto.IDevice { }
        public new interface IConfig : Thermometer.IConfig, Auto.IConfig { }

        public new IDevice Device => this;
        public new IConfig Config => this;

        Auto.IDevice IAuto.Device => this;
        Auto.IConfig IAuto.Config => this;
        Switch.IDevice ISwitch.Device => this;
        Switch.IConfig ISwitch.Config => this;
        OnOff.IDevice IOnOff.Device => this;
        OnOff.IConfig IOnOff.Config => this;

        #endregion Device interfaces

        #region Auto
        public virtual double Setpoint { get => Auto.Setpoint; set => TargetSetpoint = value; }
        [JsonProperty("Setpoint")]
        protected double TargetSetpoint { get => Auto.Config.Setpoint; set => Auto.Setpoint = value; }
        double Auto.IConfig.Setpoint => TargetSetpoint;
        double Auto.IDevice.Setpoint { get => Auto.Device.Setpoint; set => Auto.Device.Setpoint = value; }

        [JsonProperty]
        public virtual double MinimumSetpoint { get => Auto.MinimumSetpoint; set => Auto.MinimumSetpoint = value; }
        [JsonProperty]
        public virtual double MaximumSetpoint { get => Auto.MaximumSetpoint; set => Auto.MaximumSetpoint = value; }

        public virtual bool IsOn => Auto.IsOn;
        public virtual bool IsOff => Auto.IsOff;
        [JsonProperty]
        public virtual StopAction StopAction { get => Auto.StopAction; set => Auto.StopAction = value; }
        public virtual long MillisecondsOn => Auto.MillisecondsOn;
        public virtual long MillisecondsOff => Auto.MillisecondsOff;
        public virtual long MillisecondsInState => Auto.MillisecondsInState;

        [JsonProperty("OnOffState"), DisplayName("Target State")]
        public SwitchState State { get => Auto.State; set => Auto.State = value; }
        OnOffState OnOff.IDevice.OnOffState { get => Auto.Device.OnOffState; set => Auto.Device.OnOffState = value; }
        [DisplayName("Device State")]
        public virtual OnOffState OnOffState { get => Auto.OnOffState; }

        public virtual void TurnOn(double setpoint) => Auto.TurnOn(setpoint);
        public virtual bool TurnOn() => Auto.TurnOn();
        public virtual bool TurnOff() => Auto.TurnOff();
        public virtual bool TurnOnOff(bool on) => Auto.TurnOnOff(on);

        #endregion Auto

        public override void OnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (sender == Auto)
                NotifyPropertyChanged(e?.PropertyName);
            else
                base.OnPropertyChanged(sender, e);

            var a = Config.Setpoint;
        }

        public override void OnConfigChanged(object sender, PropertyChangedEventArgs e)
        {
            if (sender == Auto)
                NotifyConfigChanged(e?.PropertyName);
            else
                base.OnConfigChanged(sender, e);
        }

        Auto Auto { get; set; }
        public Oven(IHacsDevice d = null) : base(d)
        {
            Auto = new Auto(this);
        }
    }
}
