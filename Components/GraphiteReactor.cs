using AeonHacs.Utilities;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

namespace AeonHacs.Components
{
    // TODO Get magic numbers into settings file
    // TODO consider deriving from a new class that wraps Chamber or Section and IStateManager (and get rid of Update())
    // TODO incorporate post-Update() logic from CEGS.cs into this class
    public class GraphiteReactor : Port, IGraphiteReactor
    {
        #region HacsComponent
        [HacsConnect]
        protected override void Connect()
        {
            base.Connect();
            Sample = Find<ISample>(sampleName);
        }

        #endregion HacsComponent

        public enum States { InProcess, Start, WaitTemp, WaitFalling, WaitFinish, Stop, WaitService, WaitPrep, Prepared, Disabled }

        public enum Sizes { Standard, Small }

        [JsonProperty]
        public States State
        {
            get => state;
            set
            {
                Ensure(ref state, value, OnPropertyChanged);
                if (Initialized) StateStopwatch.Reset();
            }
        }
        States state = States.WaitService;

        [JsonProperty]
        public Sizes Size
        {
            get => size;
            set => Ensure(ref size, value);
        }
        Sizes size = Sizes.Standard;

        [JsonProperty("Sample")]
        string SampleName { get => Sample?.Name; set => sampleName = value; }
        string sampleName;
        public ISample Sample
        {
            get => sample;
            set => Ensure(ref sample, value, OnPropertyChanged);
        }
        ISample sample;

        [JsonProperty("Aliquot"), DefaultValue(0)]
        int AliquotIndex
        {
            get => aliquotIndex;
            set => Ensure(ref aliquotIndex, value, OnPropertyChanged);
        }
        int aliquotIndex = 0;

        public IAliquot Aliquot
        {
            get
            {
                if (Sample?.Aliquots != null && Sample.Aliquots.Count > AliquotIndex)
                    return Sample.Aliquots[AliquotIndex];
                else
                    return null;
            }
            set
            {
                Sample = value?.Sample;
                AliquotIndex = Sample?.AliquotIndex(value) ?? 0;
                NotifyPropertyChanged(nameof(Contents));
            }
        }

        public string Contents => Aliquot?.Name ?? "";

        [JsonProperty, DefaultValue(580)]
        public int GraphitizingTemperature
        {
            get => graphitizingTemperature;
            set => Ensure(ref graphitizingTemperature, value);
        }
        int graphitizingTemperature = 580;

        /// <summary>
        /// The difference between the true sample temperature and the 
        /// HeaterTemperature when the true sample temperature is at
        /// the Graphitizing temperature.
        /// </summary>
        [JsonProperty, DefaultValue(0)]
        public int SampleTemperatureOffset
        {
            get => sampleTemperatureOffset;
            set
            {
                if (Ensure(ref sampleTemperatureOffset, value))
                    SampleSetpoint = sampleSetpoint;    //    update Heater.Setpoint                    
            }
        }
        int sampleTemperatureOffset = 0;

        /// <summary>
        /// Controls Heater.Setpoint according to the SampleTemperatureOffset
        /// </summary>
        [JsonProperty, DefaultValue(580)]
        public double SampleSetpoint
        {
            get => sampleSetpoint;
            set
            {
                Ensure(ref sampleSetpoint, value);
                if (Heater is ISetpoint h)
                    h.Setpoint = sampleSetpoint - SampleTemperatureOffset;
            }
        }
        double sampleSetpoint;


        [JsonProperty]
        public Stopwatch StateStopwatch { get; protected set; } = new Stopwatch();
        [JsonProperty]
        public Stopwatch ProgressStopwatch { get; protected set; } = new Stopwatch();
        [JsonProperty]
        public Stopwatch GraphitizationStopwatch { get; protected set; } = new Stopwatch();

        [JsonProperty, DefaultValue(2000)]
        public double PriorPressure
        {
            get => pressureMinimum;
            set => Ensure(ref pressureMinimum, value);
        }
        double pressureMinimum = 2000;

        [JsonProperty, DefaultValue(0)]
        public int PressurePeak
        {
            get => pressurePeak;
            set => Ensure(ref pressurePeak, value);
        }
        int pressurePeak = 0; // clips (double) Pressure to detect only significant (1 Torr) change


        public bool Busy => state < States.WaitService;
        public bool Prepared => state == States.Prepared;

        public double HeaterTemperature => Heater.Temperature;
        public double ColdfingerTemperature => Coldfinger.Temperature;

        // Error conditions (note magic numbers)
        // Use AND'd error coding system? List<>? Throw exceptions?
        public bool FurnaceUnresponsive =>
            state == States.WaitTemp && StateStopwatch.Elapsed.TotalMinutes > 15;

        public bool ReactionNotStarting =>
            state == States.WaitFalling && StateStopwatch.Elapsed.TotalMinutes > 30;

        public bool ReactionNotFinishing =>
            state == States.WaitFinish && StateStopwatch.Elapsed.TotalMinutes > 4 * 60;

        public void Start() { if (Aliquot != null) Aliquot.Tries++; State = States.Start; }
        public void Stop() => State = States.Stop;
        public void Reserve(IAliquot aliquot) 
        { 
            Aliquot = aliquot;
            if (Aliquot != null) Aliquot.GraphiteReactor = Name;
            State = States.InProcess;
        }
        public void Reserve(string contents)
        {
            var s = new Sample() { AliquotIds = new List<string>() { contents } };
            Reserve(s.Aliquots[0]);    
        }
        public void ServiceComplete() { Aliquot = null; State = States.WaitPrep; }
        public void PreparationComplete() => State = States.Prepared;

        public void TurnOn(double sampleSetpoint)
        {
            SampleSetpoint = sampleSetpoint;        // sets HeaterSetpoint accordingly
            Heater.TurnOn();
        }
        public void TurnOff() => Heater.TurnOff();

        /// <summary>
        /// Estimated sample temperature.
        /// </summary>
        public double SampleTemperature
        {
            get
            {
                var temperature = Heater.Temperature;
                if (temperature > 100)
                    temperature += SampleTemperatureOffset;
                return temperature;
            }
        }

        // TODO: this creates a minor inconsistency--Temperature
        // is no longer == Thermometer.Temperature. Either create
        // a SampleThermometer or make GraphiteReactor itself implement
        // IThermometer and set GRx.Thermometer = GRx
        public override double Temperature => SampleTemperature;

        public void Update()
        {
            switch (state)
            {
                case States.Prepared:
                case States.InProcess:
                    break;
                case States.Start:
                    if (Aliquot.GRStartPressure == 0)
                        Aliquot.GRStartPressure = Pressure;
                    TurnOn(GraphitizingTemperature);
                    State = States.WaitTemp;
                    break;
                case States.WaitTemp:
                    if (!Heater.IsOn) state = States.Start;
                    if (!StateStopwatch.IsRunning) StateStopwatch.Restart();
                    if (Math.Abs(GraphitizingTemperature - SampleTemperature) < 10)
                        State = States.WaitFalling;
                    break;
                case States.WaitFalling:    // wait for 15 minutes past the end of the peak pressure
                    if (!StateStopwatch.IsRunning)
                    {
                        StateStopwatch.Restart();   // mark start of WaitFalling
                        GraphitizationStopwatch.Restart();        // mark start of 'graphitization'
                        PressurePeak = (int)Pressure;
                        ProgressStopwatch.Restart();    // mark pPeak updated
                    }
                    else if (Pressure >= PressurePeak)
                    {
                        PressurePeak = (int)Pressure;
                        ProgressStopwatch.Restart();    // mark pPeak updated
                    }
                    else if (ProgressStopwatch.Elapsed.TotalMinutes > 10)  // 10 min since pPeak
                        State = States.WaitFinish;
                    break;
                case States.WaitFinish:
                    // Considers graphitization complete when the pressure
                    // decline slows to GRCompleteTorrPerMinute.
                    var elapsed = ProgressStopwatch.Elapsed.TotalMinutes;
                    if (!StateStopwatch.IsRunning)
                    {
                        StateStopwatch.Restart();    // mark start of WaitFinish
                        PriorPressure = Pressure;
                        ProgressStopwatch.Restart();    // mark pMin updated
                    }
                    else if (elapsed >= 3.0 && GraphitizationStopwatch.Elapsed.TotalMinutes >= Sample.Parameter("MinimumGRMinutes"))        // if 3 minutes have passed without a pressure decline
                        State = States.Stop;
                    else if (PriorPressure - Pressure > Sample.Parameter("GRCompleteTorrPerMinute") * elapsed)
                    {
                        PriorPressure = Pressure;
                        ProgressStopwatch.Restart();
                    }
                    break;
                case States.Stop:
                    Heater.TurnOff();
                    State = States.WaitService;
                    break;
                case States.WaitService:
                    // state changed by ServiceComplete();
                    break;
                case States.WaitPrep:
                // changed by PreparationComplete();
                default:
                    break;
            }

            if (Busy)
            {
                // graphitization is in progress
                if (Coldfinger.IsActivelyCooling && state >= States.Start)     // don't wait for graphitizing temp
                    Coldfinger.Thaw();

                if (FurnaceUnresponsive)
                {
                    Alert.Send("GraphiteReactor warning!",
                        $"{Name} furnace is unresponsive.");
                    State = State;  // reset the timer
                }

                if (ReactionNotStarting)
                {
                    if (Aliquot.Tries > 1)
                        Stop();
                    else
                    {
                        Alert.Send("Graphite reaction warning!",
                            $"{Name} reaction hasn't started.\r\n" +
                                "Is the furnace in place?");
                        State = State;  // reset the timer
                    }
                }

                if (ReactionNotFinishing)
                {
                    Alert.Send("Graphite reaction warning!",
                        $"{Name} reaction hasn't finished.");
                    State = State;  // reset the timer
                }
            }
        }

        protected override void OnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            var propertyName = e?.PropertyName;
            if (propertyName == nameof(Sample) || propertyName == nameof(AliquotIndex))
                NotifyPropertyChanged();
            else if (propertyName == nameof(State))
            {
                NotifyPropertyChanged(nameof(Busy));
                NotifyPropertyChanged(nameof(Prepared));
            }
            else
                base.OnPropertyChanged(sender, e);

            if (sender == Heater && e?.PropertyName == nameof(Heater.Temperature))
            {
                NotifyPropertyChanged(nameof(HeaterTemperature));
                NotifyPropertyChanged(nameof(SampleTemperature));
                NotifyPropertyChanged(nameof(Temperature));
            }
            if (sender == Coldfinger && e?.PropertyName == nameof(Coldfinger.Temperature))
            {
                NotifyPropertyChanged(nameof(ColdfingerTemperature));
            }

        }

        public override string ToString()
        {
            var sb = new StringBuilder($"{Name}: {Contents} ({State}), {Pressure:0} {Manometer?.UnitSymbol}, {SampleTemperature:0} {Heater?.UnitSymbol}");
            if (StateStopwatch.IsRunning)
            {
                sb.Append($" ({StateStopwatch.Elapsed:h':'mm':'ss}");
                if (ProgressStopwatch.IsRunning)
                    sb.Append($", {ProgressStopwatch.Elapsed:h':'mm':'ss}");
                sb.Append(")");
            }
            var sb2 = new StringBuilder();
            sb2.Append($"\r\n{Manometer}");
            sb2.Append($"\r\n{Heater}");
            sb2.Append($"\r\n{Coldfinger}");
            sb.Append(Utility.IndentLines(sb2.ToString()));
            return sb.ToString();
        }
    }
}