using AeonHacs;
using Newtonsoft.Json;
using System.ComponentModel;
using System.Threading;
using AeonHacs.Utilities;

namespace AeonHacs.Components
{
    public class MassFlowController : AnalogOutput, IMassFlowController,
        MassFlowController.IDevice, MassFlowController.IConfig
    {
        #region HacsComponent

        #region Device interfaces

        public new interface IDevice : AnalogOutput.IDevice { }
        public new interface IConfig : AnalogOutput.IConfig { }

        public new IDevice Device => this;
        public new IConfig Config => this;

        #endregion Device interfaces

        [HacsConnect]
        protected virtual void Connect()
        {
            FlowMeter = Find<IMeter>(flowMeterName);
            if (FlowMeter != null)
                FlowMeter.PropertyChanged += OnPropertyChanged;
        }

        [HacsInitialize]
        protected virtual void Initialize()
        {
            flowTrackingThread = new Thread(TrackFlow)
            {
                Name = $"{Name} TrackFlow",
                IsBackground = true
            };
            flowTrackingThread.Start();
        }

        [HacsStart]
        protected virtual void Start() =>
            TurnOn(Setpoint);

        #endregion HacsComponent

        [JsonProperty]
        public OperationSet OutputConverter
        {
            get => outputConverter;
            set => Ensure(ref outputConverter, value);
        }
        OperationSet outputConverter;

        [JsonProperty]
        public double Setpoint
        {
            get { return setpoint; }
            set
            {
                if (!Initialized)
                {
                    setpoint = value;
                    return;
                }
                if (value < MinimumSetpoint)
                    value = MinimumSetpoint;
                else if (value > MaximumSetpoint)
                    value = MaximumSetpoint;
                if (setpoint != value)
                {
                    setpoint = value;
                    if (Initialized) Hacs.SystemLog.Record($"{Name}.Setpoint = {value}");
                }
                TurnOn(setpoint);
                NotifyPropertyChanged();
            }
        }
        double setpoint;

        [JsonProperty("FlowMeter")]
        string FlowMeterName { get => FlowMeter?.Name; set => flowMeterName = value; }
        string flowMeterName;
        IMeter FlowMeter
        {
            get => flowMeter;
            set => Ensure(ref flowMeter, value, NotifyPropertyChanged);
        }
        IMeter flowMeter;

        public void ZeroNow() => FlowMeter.ZeroNow();

        public double FlowRate => FlowMeter?.Value ?? 0;

        object flowTrackingLock = new object();
        public double TrackedFlow
        {
            get => trackedFlow;
            set => Ensure(ref trackedFlow, value);
        }
        double trackedFlow;

        Stopwatch flowTrackingStopwatch = new Stopwatch();
        Thread flowTrackingThread;
        AutoResetEvent flowTrackingSignal = new AutoResetEvent(false);

        [JsonProperty]
        public double MinimumSetpoint
        {
            get => minimumSetpoint;
            set => Ensure(ref minimumSetpoint, value);
        }
        double minimumSetpoint;
        [JsonProperty]
        public double MaximumSetpoint
        {
            get => maximumSetpoint;
            set => Ensure(ref maximumSetpoint, value);
        }
        double maximumSetpoint;

        void TrackFlow()
        {
            flowTrackingStopwatch.Restart();
            while (true)
            {
                lock (flowTrackingLock)
                {
                    TrackedFlow += FlowRate * flowTrackingStopwatch.ElapsedMilliseconds / 60000;
                    flowTrackingStopwatch.Restart();
                }

                flowTrackingSignal.WaitOne(500);
            }
        }

        public void ResetTrackedFlow()
        {
            lock (flowTrackingLock)
            {
                TrackedFlow = 0;
                flowTrackingStopwatch.Restart();
            }
        }


        /// <summary>
        /// Set the flow rate to the given value in standard cubic centimeters per minute.
        /// </summary>
        /// <param name="setpoint">sccm</param>
        public void TurnOn(double setpoint)
        {
            if (!Initialized) return;
            Voltage = OutputConverter?.Execute(setpoint) ?? setpoint;
        }

        public void TurnOff() => TurnOn(0);

        public MassFlowController(IHacsDevice d = null) : base(d) { }


        public override void OnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            var propertyName = e?.PropertyName;
            if (sender == FlowMeter)
            {
                if (propertyName == nameof(Meter.Value))
                    NotifyPropertyChanged(nameof(FlowRate));
            }
            else
                base.OnPropertyChanged(sender, e);
        }

        public override string ToString()
        {
            var tracked = TrackedFlow > 0 ? $"\r\nTracked Flow: {TrackedFlow:0.000}" : "";

            return $"{Name}:" +
                Utility.IndentLines(
                    $"\r\n{FlowMeter}" +
                    tracked +
                    $"\r\nSP: {Setpoint} sccm => {Config.Voltage:0.000} V; DAC = {Device.Voltage:0.000} V"
                );
        }

        //public void Setpoint(int v)
        //{
        //    throw new System.NotImplementedException();
        //}
    }
}
