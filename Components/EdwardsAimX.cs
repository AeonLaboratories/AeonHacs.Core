using AeonHacs.Utilities;
using Newtonsoft.Json;
using System.ComponentModel;
using System.Text;

namespace AeonHacs.Components
{
    public class EdwardsAimX : SwitchedManometer, IEdwardsAimX, EdwardsAimX.IDevice, EdwardsAimX.IConfig
    {
        #region HacsComponent

        [HacsConnect]
        protected virtual void Connect()
        {
            Switch = Find<IDigitalOutput>(digitalOutputName);
            AnalogInput.Name = this.Name;
        }

        #endregion HacsComponent

        #region Device interfaces

        public new interface IDevice : SwitchedManometer.IDevice, AnalogInput.IDevice { }
        public new interface IConfig : SwitchedManometer.IConfig, AnalogInput.IConfig { }
        public new IDevice Device => this;
        public new IConfig Config => this;
        AnalogInput.IDevice IAnalogInput.Device => this;
        AnalogInput.IConfig IAnalogInput.Config => this;
        ManagedDevice.IDevice IManagedDevice.Device => this;
        ManagedDevice.IConfig IManagedDevice.Config => this;

        #endregion Device interfaces

        public IDeviceManager Manager { get => AnalogInput.Device.Manager; set => AnalogInput.Device.Manager = value; }

        #region AnalogInput

        [JsonProperty]
        public AnalogInputMode AnalogInputMode { get => AnalogInput.AnalogInputMode; set => AnalogInput.AnalogInputMode = value; }

        IDeviceManager ManagedDevice.IDevice.Manager { get => Device.Manager; set => Device.Manager = value; }
        IDeviceManager AnalogInput.IDevice.Manager { get => AnalogInput.Device.Manager; set => AnalogInput.Device.Manager = value; }

        public double Voltage => AnalogInput.Voltage;
        double AnalogInput.IDevice.Voltage
        {
            get => AnalogInput.Voltage;
            set
            {
                Update(value);
                AnalogInput.Device.Voltage = FilteredValue;
                if (Valid)
                    Error = Voltage < errorSignalVoltage ? 1 : 0;
            }
        }

        public double MaximumVoltage
        {
            get => AnalogInput.MaximumVoltage;
            set => AnalogInput.MaximumVoltage = value;
        }

        public double MinimumVoltage
        {
            get => AnalogInput.MinimumVoltage;
            set => AnalogInput.MinimumVoltage = value;
        }

        public override bool OverRange => base.OverRange || AnalogInput.OverRange;
        public override bool UnderRange => base.UnderRange || AnalogInput.UnderRange;

        #endregion AnalogInput

        [JsonProperty("ErrorSignalVoltage"), DefaultValue(2.0)]
        double errorSignalVoltage = 2.0;

        public virtual int Error
        {
            get => error;
            set => Ensure(ref error, value);
        }
        int error = 0;


        public override void OnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (sender == AnalogInput)
            {
                NotifyPropertyChanged(e?.PropertyName);
            }
            else
                base.OnPropertyChanged(sender, e);
        }

        public override void OnConfigChanged(object sender, PropertyChangedEventArgs e)
        {
            if (sender == AnalogInput)
                NotifyConfigChanged(e?.PropertyName);
            else
                base.OnConfigChanged(sender, e);
        }

        AnalogInput AnalogInput { get; set; }

        [JsonProperty("DigitalOutput")]
        string DigitalOutputName { get => Switch?.Name; set => digitalOutputName = value; }
        string digitalOutputName;

        public override string Name
        {
            get => base.Name;
            set { base.Name = value; AnalogInput.Name = $"({value})"; }
        }

        public EdwardsAimX(IHacsDevice d = null) : base(d)
        {
            AnalogInput = new AnalogInput(this);
        }

        public override string ToString()
        {
            var sb = new StringBuilder(base.ToString().Replace(UnitSymbol, $"{UnitSymbol}, {IsOn.OnOff()}"));
            if (IsOn)
                sb.Append(Utility.IndentLines($"\r\n({Voltage:0.0000} V)"));

            if (Error != 0)
                sb.Append("\r\nError Detected: Service Required?");
            sb.Append(Utility.IndentLines(ManagedDevice.ManagerString(this)));
            return sb.ToString();
        }
    }
}
