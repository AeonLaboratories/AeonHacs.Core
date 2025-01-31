using AeonHacs.Utilities;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using static AeonHacs.Notify;
using static AeonHacs.Utilities.Utility;

namespace AeonHacs.Components
{

    // TODO: make this a StateManager : IVacuumSystem and IVacuumSystem : IManometer
    /// <summary>
    /// A high-vacuum system with a turbomolecular pump and a low-vacuum
    /// roughing pump that is also used as the backing pump for the turbo.
    /// </summary>
    public class VacuumSystem : HacsComponent, IVacuumSystem
    {
        static List<VacuumSystem> systems { get; } = new();

        public VacuumSystem()
        {
            systems.Add(this);
        }


        #region HacsComponent

        [HacsConnect]
        protected virtual void Connect()
        {
            TurboPump = Find<IOnOff>(turboPumpName);
            RoughingPump = Find<IOnOff>(roughingPumpName);
            Manometer = Find<IManometer>(manometerName);
            ForelineManometer = Find<IManometer>(forelineManometerName);
            HighVacuumValve = Find<IValve>(highVacuumValveName);
            LowVacuumValve = Find<IValve>(lowVacuumValveName);
            BackingValve = Find<IValve>(backingValveName);
            RoughingValve = Find<IValve>(roughingValveName);
            VacuumManifold = Find<ISection>(vacuumManifoldName);
            MySection = Find<ISection>(mySectionName);
        }

        [HacsStart]
        protected virtual void Start()
        {
            stateThread = new Thread(stateLoop) { Name = $"{Name} ManageState", IsBackground = true };
            stateThread.Start();
            StateStopwatch.Restart();
        }

        [HacsPreStop]
        protected virtual void PreStop()
        {
            if (!BackingValve.IsOpened && TargetState != TargetStateCode.Standby)
                Isolate();
        }

        [HacsStop]
        protected virtual void Stop()
        {
            if (AutoManometer)
                DisableManometer();
            Stopping = true;
            stateSignal.Set();
            stoppedSignal.WaitOne();
        }

        ManualResetEvent stoppedSignal = new ManualResetEvent(true);

        // TODO this should be a protected override, no?
        public new bool Stopped => stoppedSignal.WaitOne(0);
        protected bool Stopping { get; set; }

        #endregion HacsComponent

        [JsonProperty("TurboPump")]
        string TurboPumpName { get => TurboPump?.Name; set => turboPumpName = value; }
        string turboPumpName;
        /// <summary>
        /// Turbo pump power.
        /// </summary>
        public IOnOff TurboPump
        {
            get => turboPump;
            set => Ensure(ref turboPump, value, NotifyPropertyChanged);
        }
        IOnOff turboPump;
        bool turboPumpIsOn => !TurboPump?.IsOff ?? true;

        [JsonProperty("RoughingPump")]
        string RoughingPumpName { get => RoughingPump?.Name; set => roughingPumpName = value; }
        string roughingPumpName;
        /// <summary>
        /// Roughing pump power.
        /// </summary>
        public IOnOff RoughingPump
        {
            get => roughingPump;
            set => Ensure(ref roughingPump, value, NotifyPropertyChanged);
        }
        IOnOff roughingPump;
        bool roughingPumpIsOn => !RoughingPump?.IsOff ?? true;

        [JsonProperty("Manometer")]
        string ManometerName { get => Manometer?.Name; set => manometerName = value; }
        string manometerName;
        /// <summary>
        /// The VacuumManifold pressure gauge.
        /// </summary>
        public IManometer Manometer
        {
            get => manometer;
            set => Ensure(ref manometer, value, NotifyPropertyChanged);
        }
        IManometer manometer;
        public double Pressure => Manometer.Pressure;

        [JsonProperty("ForelineManometer")]
        string ForelineManometerName { get => ForelineManometer?.Name; set => forelineManometerName = value; }
        string forelineManometerName;
        /// <summary>
        /// The foreline pressure gauge.
        /// </summary>
        public IManometer ForelineManometer
        {
            get => forelineManometer;
            set => Ensure(ref forelineManometer, value, NotifyPropertyChanged);
        }
        IManometer forelineManometer;

        [JsonProperty("HighVacuumValve")]
        string HighVacuumValveName { get => HighVacuumValve?.Name; set => highVacuumValveName = value; }
        string highVacuumValveName;
        /// <summary>
        /// The valve that joins the VacuumManifold to the turbo pump inlet.
        /// </summary>
        public IValve HighVacuumValve
        {
            get => highVacuumValve;
            set => Ensure(ref highVacuumValve, value, NotifyPropertyChanged);
        }
        IValve highVacuumValve;

        [JsonProperty("LowVacuumValve")]
        string LowVacuumValveName { get => LowVacuumValve?.Name; set => lowVacuumValveName = value; }
        string lowVacuumValveName;
        /// <summary>
        /// The valve that joins the VacuumManifold to the foreline.
        /// </summary>
        public IValve LowVacuumValve
        {
            get => lowVacuumValve;
            set => Ensure(ref lowVacuumValve, value, NotifyPropertyChanged);
        }
        IValve lowVacuumValve;

        [JsonProperty("BackingValve")]
        string BackingValveName { get => BackingValve?.Name; set => backingValveName = value; }
        string backingValveName;
        /// <summary>
        /// The valve that joins the turbo pump outlet to the foreline.
        /// </summary>
        public IValve BackingValve
        {
            get => backingValve;
            set => Ensure(ref backingValve, value, NotifyPropertyChanged);
        }
        IValve backingValve;

        [JsonProperty("RoughingValve")]
        string RoughingValveName { get => RoughingValve?.Name; set => roughingValveName = value; }
        string roughingValveName;
        /// <summary>
        /// The valve that joins the foreline to the roughing pump.
        /// </summary>
        public IValve RoughingValve
        {
            get => roughingValve;
            set => Ensure(ref roughingValve, value, NotifyPropertyChanged);
        }
        IValve roughingValve;

        [JsonProperty("VacuumManifold")]
        string VacuumManifoldName { get => VacuumManifold?.Name ; set => vacuumManifoldName = value; }
        string vacuumManifoldName;
        /// <summary>
        /// The Section (chamber) whose pressure is managed by this VacuumSystem.
        /// </summary>
        public ISection VacuumManifold
        {
            get => vacuumManifold;
            set => Ensure(ref vacuumManifold, value);
        }
        ISection vacuumManifold;

        [JsonProperty("MySection")]
        string MySectionName { get => MySection?.Name; set => mySectionName = value; }
        string mySectionName;
        /// <summary>
        /// The Section to be evacuated by this VacuumSystem during OpenLine().
        /// </summary>
        public ISection MySection
        {
            get => mySection;
            set => Ensure(ref mySection, value);
        }
        ISection mySection;

        protected enum TargetStateCode
        {
            /// <summary>
            /// The VacuumSystem is operationally idle. It controls no valves in this state.
            /// </summary>
            Standby,
            /// <summary>
            /// Isolate the pumping system (turbopump and foreline) from the VacuumManifold. Provide backing vacuum to turbopump.
            /// </summary>
            Isolate,
            /// <summary>
            /// Do not use the turbopump. Use the roughing pump to evacuate the VacuumManifold via the foreline,
            /// unless the VacuumManifold pressure is less than the HighVacuumRequired pressure, in which case,
            /// isolate the VacuumManifold from the pumping system and provide backing to the turbopump.
            /// </summary>
            Rough,
            /// <summary>
            /// Evacuate the VacuumManifold using the roughing pump via the foreline or the turbopump, as appropriate based
            /// on the VacuumManifold and foreline pressures.
            /// </summary>
            Evacuate,
            /// <summary>
            /// Shut down this VacuumSystem instance. Typically only used when HACS is shutting down.
            /// </summary>
            Stop
        }
        /// <summary>
        /// The desired operating state of the VacuumSystem.
        /// </summary>
        [JsonProperty("State")]
        protected TargetStateCode TargetState
        {
            get => targetState;
            set => Ensure(ref targetState, value, NotifyConfigChanged);
        }
        TargetStateCode targetState;

        /// <summary>
        /// The current VacuumManifold pressure goal, provided when a process needs to wait for
        /// the VacuumManifold to reach a certain pressure. This value may be altered dynamically
        /// while WaitForPressure() is active; that's its intended purpose.
        /// </summary>
        public double TargetPressure
        {
            get => targetPressure;
            set => Ensure(ref targetPressure, value, NotifyConfigChanged);
        }
        double targetPressure;

        /// <summary>
        /// The maximum acceptable pressure for the VacuumManifold to be considered sufficiently evacuated for any purpose.
        /// May be used to enable or prevent the auto-zeroing of most pressure gauges.
        /// </summary>
        [JsonProperty]
        public double BaselinePressure
        {
            get => baselinePressure;
            set => Ensure(ref baselinePressure, value, NotifyConfigChanged);
        }
        double baselinePressure;

        /// <summary>
        /// The VacuumSystem does not open or close the turbopump backing valve unless the foreline pressure is below this value.
        /// </summary>
        [JsonProperty]
        public double GoodBackingPressure
        {
            get => goodBackingPressure;
            set => Ensure(ref goodBackingPressure, value, NotifyConfigChanged);
        }
        double goodBackingPressure;  // open or close the turbopump backing valve when pForeline is less than this

        /// <summary>
        ///When TargetState is Evacuate and State is neither HighVacuum nor Roughing,
        ///the VacuumSystem transitions to Roughing unless pVM is less than HighVacuumPreferred,
        ///in which case it transitions to HighVacuum instead.
        /// </summary>
        [JsonProperty]
        public double HighVacuumPreferredPressure
        {
            get => highVacuumPreferredPressure;
            set => Ensure(ref highVacuumPreferredPressure, value, NotifyConfigChanged);
        }
        double highVacuumPreferredPressure;

        /// <summary>
        /// When TargetState is Evacuate and State is Roughing, the VacuumSystem
        /// transitions to HighVacuum if pVM is below HighVacuumRequired.
        /// </summary>
        [JsonProperty]
        public double HighVacuumRequiredPressure
        {
            get => highVacuumRequiredPressure;
            set => Ensure(ref highVacuumRequiredPressure, value, NotifyConfigChanged);
        }
        double highVacuumRequiredPressure;    // do not use LV below this pressure

        /// <summary>
        /// When TargetState is Evacuate and State is HighVacuum, the VacuumSystem
        /// transitions to Roughing if pVM is above LowVacuumRequired.
        /// </summary>
        [JsonProperty]
        public double LowVacuumRequiredPressure
        {
            get => lowVacuumRequiredPressure;
            set => Ensure(ref lowVacuumRequiredPressure, value, NotifyConfigChanged);
        }
        double lowVacuumRequiredPressure;    // do not use HV above this pressure

        /// <summary>
        /// A StepTracker to receive ongoing process state messages.
        /// </summary>
        public StepTracker ProcessStep
        {
            get => processStep ?? StepTracker.Default;
            set => Ensure(ref processStep, value);
        }
        StepTracker processStep;

        /// <summary>
        /// A defined operating (valve) State for the VacuumSystem.
        /// </summary>
        public enum StateCode
        {
            /// <summary>
            /// The VacuumSystem valve states do not correspond to any of the other
            /// defined StateCodes.
            /// </summary>
            Unknown,
            /// <summary>
            /// Both the HighVacuum and LowVacuum valves are closed, and
            /// the roughing pump is backing the turbopump.
            /// </summary>
            Isolated,
            /// <summary>
            /// The VacuumManifold is being evacuated by the roughing pump via the foreline.
            /// The HighVacuum and backing valves are closed.
            /// </summary>
            Roughing,
            /// <summary>
            /// Both the HighVacuum and LowVacuum valves are closed, and
            /// the roughing pump is evacuating the foreline. The turbopump
            /// backing valve is closed.
            /// </summary>
            RoughingForeline,
            /// <summary>
            /// The VacuumManifold is being evacuated by the turbopump,
            /// which is being backed by the roughing pump.
            /// The LowVacuum valve is closed.
            /// </summary>
            HighVacuum,
            /// <summary>
            /// The VacuumSystem is not operational.
            /// </summary>
            Stopped
        }

        /// <summary>
        /// The current operating (valve) State of the VacuumSystem.
        /// </summary>
        public StateCode State
        {
            get
            {
                if (Stopped)
                    return StateCode.Stopped;
                if (HighVacuumValve.IsClosed &&
                        LowVacuumValve.IsClosed &&
                        (turboPumpIsOn ? BackingValve.IsOpened : BackingValve.IsClosed) &&
                        (roughingPumpIsOn ? (RoughingValve?.IsOpened ?? true) : (RoughingValve?.IsClosed ?? true)))
                    return StateCode.Isolated;     // and backing
                if (HighVacuumValve.IsClosed && LowVacuumValve.IsOpened && BackingValve.IsClosed && (RoughingValve?.IsOpened ?? true))
                    return StateCode.Roughing;
                if (HighVacuumValve.IsClosed && LowVacuumValve.IsClosed && BackingValve.IsClosed && (RoughingValve?.IsOpened ?? true))
                    return StateCode.RoughingForeline;
                if (HighVacuumValve.IsOpened && LowVacuumValve.IsClosed && BackingValve.IsOpened && (RoughingValve?.IsOpened ?? true))
                    return StateCode.HighVacuum;
                return StateCode.Unknown;
            }
        }

        protected Stopwatch StateStopwatch { get; private set; } = new Stopwatch();
        public long MillisecondsInState => StateStopwatch.ElapsedMilliseconds;

        protected Stopwatch BaselineTimer { get; private set; } = new Stopwatch();
        public TimeSpan TimeAtBaseline => BaselineTimer.Elapsed;


        public override string ToString()
        {
            return $"{Name} ({TargetState}): {State}" + Utility.IndentLines($"\r\n{Manometer}");
        }

        #region Class Interface Methods -- Control the device using these functions

        /// <summary>
        /// Isolate the VacuumManifold.
        /// </summary>
        public void IsolateManifold() => VacuumManifold?.Isolate();

        /// <summary>
        /// IsolateManifold() but skip the specified valves.
        /// </summary>
        /// <param name="section">Skip these valves</param>
        public void IsolateExcept(IEnumerable<IValve> valves) =>
            VacuumManifold?.Isolation?.CloseExcept(valves);

        /// <summary>
        ///  Disables all automatic control of VacuumSystem.
        /// </summary>
        public void Standby()
        {
            if (State == StateCode.Stopped) Start();
            TargetState = TargetStateCode.Standby;
        }

        /// <summary>
        /// Isolates the pumps from the vacuum manifold.
        /// Returns only after isolation is complete.
        /// </summary>
        public void Isolate() => Isolate(true);

        /// <summary>
        /// Isolates the pumps from the vacuum manifold.
        /// </summary>
        /// <param name="waitForState">If true, returns only after isolation is complete.</param>
        public void Isolate(bool waitForState)
        {
            if (State == StateCode.Stopped) Start();
            TargetState = TargetStateCode.Isolate;
            if (waitForState)
                WaitFor(() => State == StateCode.Isolated || Stopping, -1, 35);
        }

        /// <summary>
        /// Requests Evacuation mode. Initiates pumping on the vacuum manifold and attempts to bring it to high vacuum.
        /// </summary>
        public void Evacuate()
        {
            if (State == StateCode.Stopped) Start();
            TargetState = TargetStateCode.Evacuate;
        }

        /// <summary>
        /// Requests Evacuate mode. Returns when the target pressure is reached.
        /// </summary>
        /// <param name="pressure">Target pressure</param>
        public void Evacuate(double pressure)
        {
            Evacuate();
            WaitFor(() => State == StateCode.Roughing || State == StateCode.HighVacuum || Stopping, -1, 35);
            WaitForPressure(pressure);
        }

        /// <summary>
        /// Opens and evacuates MySection
        /// </summary>
        public void OpenLine()
        {
            if (MySection == null) return;
            ProcessStep?.Start($"Open {Name} line");
            MySection.OpenAndEvacuate();
            ProcessStep.End();
        }

        /// <summary>
        /// Waits 3 seconds, then until the given pressure is reached.
        /// Use 0 to wait for baseline, &lt;0 to just wait 3 seconds.
        /// </summary>
        /// <param name="pressure">Use 0 to wait for baseline, &lt;0 to just wait 3 seconds.</param>
        public void WaitForPressure(double pressure)
        {
            WaitSeconds(3);             // always wait at least 3 seconds
            if (pressure < 0) return;   // don't wait for a specific pressure
            if (pressure == 0) pressure = BaselinePressure;
            TargetPressure = pressure;

            ProcessStep?.Start($"Wait for {Manometer.Name} < {TargetPressure:0.0e0} Torr");
            bool shouldStop()
            {
                if (pressure != TargetPressure)
                {
                    pressure = TargetPressure;
                    if (ProcessStep != null)
                        ProcessStep.CurrentStep.Description = $"Wait for {Manometer.Name} < {TargetPressure:0.0e0} Torr";
                }
                return Pressure <= TargetPressure || Stopping;
            }

            int timeouts = 0;
            while (!WaitFor(shouldStop, 15 * 60000, 100))
            {
                if (timeouts % (4 * 24) == 0)
                {
                    Announce($"{Manometer.Name} hasn't reached {TargetPressure:0.0e0} Torr",
                        $"It's been evacuating for {ProcessStep.Elapsed.TotalMinutes:0} minutes.");
                }
                timeouts++;
            }

            ProcessStep?.End();
        }

        /// <summary>
        /// Wait until the TimeAtBaseline timer reaches at least 10 seconds
        /// </summary>
        public virtual void WaitForStableBaselinePressure()
        {
            WaitFor(() => TimeAtBaseline.TotalSeconds >= 10 || Stopping, -1, 35); // TODO magic number
        }

        public virtual void WaitForStablePressure(double pressure, int seconds = 5)
        {
            var sw = new Stopwatch();
            bool shouldStop()
            {
                if (Pressure > pressure || !ForelineManometer.IsStable)
                    sw.Reset();
                else if (!sw.IsRunning)
                    sw.Restart();
                return sw.Elapsed.TotalSeconds >= seconds;
            }
            while (!WaitFor(shouldStop, 30 * 60000, 100)) // TODO magic number
            {
                if (Warn($"{Name} has a problem",
                    $"{Manometer.Name} still hasn't stabilized below {pressure:0.0e0} Torr\r\n" +
                    $"despite trying for over {sw.Elapsed.TotalMinutes:0} minutes.\r\n" +
                    $"Ok to keep waiting or Cancel to move on.").Ok())
                    continue;
                break;
            }
        }


        /// <summary>
        /// Request to evacuate vacuum manifold using low-vacuum pump only.
        /// Vacuum Manifold will be roughed and isolated alternately
        /// to maintain VM pressure between pressure_HV_required
        /// and pressure_LV_required
        /// </summary>
        public void Rough()
        {
            if (State == StateCode.Stopped) Start();
            TargetState = TargetStateCode.Rough;
        }

        protected void SetTargetState(TargetStateCode targetState)
        {
            switch (targetState)
            {
                case TargetStateCode.Standby:
                    Standby();
                    break;
                case TargetStateCode.Isolate:
                    Isolate(false);
                    break;
                case TargetStateCode.Rough:
                    Rough();
                    break;
                case TargetStateCode.Evacuate:
                    Evacuate();
                    break;
                case TargetStateCode.Stop:
                    Stop();
                    break;
                default:
                    break;
            }
        }

        #endregion

        #region State Manager

        Thread stateThread;
        AutoResetEvent stateSignal = new AutoResetEvent(false);

        /// <summary>
        /// Maximum time (milliseconds) for idle state manager to wait before doing something.
        /// </summary>
        [JsonProperty, DefaultValue(50)]
        protected int IdleTimeout { get; set; } = 50;

        bool valvesReady =>
            (HighVacuumValve?.Ready ?? true) &&
            (LowVacuumValve?.Ready ?? true) &&
            (BackingValve?.Ready ?? true) &&
            (RoughingValve?.Ready ?? true);

        // TODO: Need to monitor how long Backing has been closed, and
        // to periodically empty the turbo pump exhaust, or shut down the
        // turbo pump with an alert.
        void stateLoop()
        {
            stoppedSignal.Reset();
            try
            {
                while (!Stopping)
                {
                    var idleTimeout = IdleTimeout;

                    if (valvesReady)
                    {
                        switch (TargetState)
                        {
                            case TargetStateCode.Isolate:
                                if (State != StateCode.Isolated)
                                {
                                    if (AutoManometer)
                                        DisableManometer();
                                    HighVacuumValve.CloseWait();
                                    LowVacuumValve.CloseWait();

                                    // TODO need a timeout check somewhere in here with alert/failure.
                                    // The turbo pump can be damaged if it is runs without a backing
                                    // pump for too long.

                                    if (!roughingPumpIsOn)
                                        RoughingValve?.CloseWait();
                                    else
                                    {
                                        RoughingValve?.OpenWait();

                                        if (TurboPump?.IsOn ?? true)
                                        {
                                            if (!WaitFor(() => ForelineManometer.Pressure <= GoodBackingPressure || Hacs.Stopping, 10 * 60000, 35))
                                            {
                                                Announce($"{Name} backing pressure too high", 
                                                    $"Foreline pressure ({ForelineManometer.Pressure:0.0} {ForelineManometer.UnitSymbol}) is too high for Turbo Pump.", NoticeType.Error);
                                            }

                                            BackingValve.OpenWait();
                                        }
                                        else
                                            BackingValve.CloseWait();
                                    }
                                    StateStopwatch.Restart();
                                    NotifyPropertyChanged("State");
                                }
                                break;
                            case TargetStateCode.Rough:
                                if (RoughAsNeeded())
                                {
                                    StateStopwatch.Restart();
                                    NotifyPropertyChanged("State");
                                    idleTimeout = 10;        // work quickly until no action taken
                                }
                                break;
                            case TargetStateCode.Evacuate:
                                if (EvacuateOrRoughAsNeeded())
                                {
                                    StateStopwatch.Restart();
                                    NotifyPropertyChanged("State");
                                    idleTimeout = 10;        // work quickly until no action taken
                                }
                                break;
                            case TargetStateCode.Standby:
                                break;
                            default:
                                break;
                        }
                    }

                    ManageIonGauge();
                    MonitorBaseline();

                    stateSignal.WaitOne(idleTimeout);
                }
            }
            catch (Exception e)
            {
                Announce($"{Name} control has been lost.", 
                    $"Exception in {Name}'s state loop:\r\n" +
                    $"{e}\r\n" +
                    "The application must be restarted to recover.", NoticeType.Error);
            }
            stoppedSignal.Set();
        }

        /// <summary>
        /// The Vacuum system automatically controls whether Manometer is on or off.
        /// </summary>
        public bool AutoManometer
        {
            get => autoManometer;
            set
            {
                if (Ensure(ref autoManometer, value))
                {
                    if (Manometer is IManualMode m && m.ManualMode == value)
                        m.ManualMode = !value;

                    ManageIonGauge();
                }
            }
        }
        bool autoManometer = true;

        public void DisableManometer()
        {
            if (Manometer is ISwitchedManometer m && !m.IsOff)
                m.TurnOff();
        }

        public void EnableManometer()
        {
            if (Manometer is IDualManometer m)
            {
                if (m.ManualMode)
                    m.ManualMode = false;
            }
            else if (Manometer is ISwitchedManometer m2)
            {
                if (!m2.IsOn)
                    m2.TurnOn();
            }
        }

        protected void ManageIonGauge()
        {
            if (!AutoManometer) return;

            var ionOk = HighVacuumValve.IsOpened && BackingValve.IsOpened;
            if (!ionOk)
                DisableManometer();
            else
                EnableManometer();
        }

        protected void MonitorBaseline()
        {
            // monitor time at "baseline" (minimal pressure and steady foreline pressure)
            if (State == StateCode.HighVacuum &&
                Pressure <= BaselinePressure &&
                ForelineManometer.IsStable
            )
            {
                if (!BaselineTimer.IsRunning)
                    BaselineTimer.Restart();
            }
            else if (BaselineTimer.IsRunning)
                BaselineTimer.Reset();
        }

        protected bool EvacuateOrRoughAsNeeded()
        {
            if (State == StateCode.HighVacuum && Pressure < LowVacuumRequiredPressure ||
                State == StateCode.Roughing && Pressure > HighVacuumRequiredPressure)
                return false; // no action taken

            RoughingValve?.OpenWait();

            if (Pressure < HighVacuumPreferredPressure)               // ok to go to HV
            {
                if (LowVacuumValve.IsOpened)
                    LowVacuumValve.CloseWait();
                if (ForelineManometer.Pressure < GoodBackingPressure)
                    BackingValve.OpenWait();
                if (BackingValve.IsOpened)
                    HighVacuumValve.OpenWait();
            }
            else            // need LV
            {
                if (HighVacuumValve.IsOpened)
                    HighVacuumValve.CloseWait();
                if (ForelineManometer.Pressure < GoodBackingPressure)
                    BackingValve.CloseWait();
                if (BackingValve.IsClosed)
                    LowVacuumValve.OpenWait();
            }
            return true;    // even though no valve may have moved; might be waiting on pForeline for backing
        }

        protected bool RoughAsNeeded()
        {
            bool stateChanged = false;
            if (!HighVacuumValve.IsClosed)
                HighVacuumValve.CloseWait();

            if (LowVacuumValve.IsClosed)    // isolated
            {
                if (Pressure < LowVacuumRequiredPressure)
                {
                    // back TurboPump whenever possible
                    if (BackingValve.IsClosed && ForelineManometer.Pressure < GoodBackingPressure)
                    {
                        BackingValve.OpenWait();
                        stateChanged = true;    // backing
                    }
                }
                else    // need to rough
                {
                    if (BackingValve.IsOpened)
                        BackingValve.CloseWait();
                    LowVacuumValve.OpenWait();
                    stateChanged = true;    // roughing
                }
            }
            else    // currently roughing
            {
                if (Pressure <= HighVacuumRequiredPressure)
                {
                    LowVacuumValve.CloseWait();
                    stateChanged = true;    // isolated
                }
            }
            return stateChanged;
        }

        #endregion


        /// <summary>
        /// These event handlers are invoked whenever the desired device
        /// configuration changes. EventArgs.PropertyName is usually the
        /// name of an updated configuration property, but it may be null,
        /// or a generalized indication of the reason the event was raised,
        /// such as &quot;{Init}&quot;.
        /// </summary>
        public virtual PropertyChangedEventHandler ConfigChanged { get; set; }

        /// <summary>
        /// Raises the ConfigChanged event.
        /// </summary>
        protected virtual void NotifyConfigChanged(object sender, PropertyChangedEventArgs e)
        {
            stateSignal.Set();
            ConfigChanged?.Invoke(sender, e);
        }
    }
}


