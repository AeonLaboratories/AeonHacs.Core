using AeonHacs.Utilities;
using Newtonsoft.Json;
using System;
using System.ComponentModel;
using System.Threading;
using static AeonHacs.Utilities.Utility;

namespace AeonHacs.Components
{
    /// <summary>
    /// Controls a FlowValve to reach and maintain a specific condition monitored by the Meter.
    /// </summary>
    public class FlowManager : HacsComponent, IFlowManager
    {
        #region HacsComponent

        [HacsConnect]
        protected virtual void Connect()
        {
            FlowValve = Find<RS232Valve>(flowValveName);
            Meter = Find<IMeter>(meterName);
        }

        #endregion HacsComponent

        [JsonProperty("FlowValve")]
        string FlowValveName { get => FlowValve?.Name; set => flowValveName = value; }
        string flowValveName;
        /// <summary>
        /// The valve that alters the flow which changes Meter.Value.
        /// </summary>
        public IRS232Valve FlowValve
        {
            get => flowValve;
            set => Ensure(ref flowValve, value, NotifyPropertyChanged);
        }
        IRS232Valve flowValve;

        [JsonProperty("Meter")]
        string MeterName { get => Meter?.Name; set => meterName = value; }
        string meterName;
        /// <summary>
        /// A Meter whose Value varies as the flow valve is adjusted.
        /// </summary>
        public IMeter Meter
        {
            get => meter;
            set => Ensure(ref meter, value, NotifyPropertyChanged);
        }
        IMeter meter;

        /// <summary>
        /// Minimum control loop period.
        /// </summary>
        [JsonProperty, DefaultValue(35)]
        public int MillisecondsTimeout
        {
            get => millisecondsTimeout;
            set => Ensure(ref millisecondsTimeout, value);
        }
        int millisecondsTimeout = 35;


        /// <summary>
        /// Nominal time between valve movements
        /// </summary>
        [JsonProperty, DefaultValue(0.75)]
        public double SecondsCycle
        {
            get => secondsCycle;
            set => Ensure(ref secondsCycle, value);
        }
        double secondsCycle = 0.75;

        /// <summary>
        /// An initial valve movement, to be completed before entering 
        /// the flow management control loop.
        /// </summary>
        [JsonProperty, DefaultValue(0)]
        public int StartingMovement
        {
            get => startingMovement;
            set => Ensure(ref startingMovement, value);
        }
        int startingMovement = -0;       // usually negative

        /// <summary>
        /// Maximum valve movement for any single adjustment, in valve Position units.
        /// Aeon actuators have a resolution of 96 positions for 360 degrees.
        /// </summary>
        [JsonProperty, DefaultValue(24)]
        public int MaximumMovement
        {
            get => maximumMovement;
            set => Ensure(ref maximumMovement, value);
        }
        int maximumMovement = 24;

        /// <summary>
        /// A limiting Meter.Value rate of change, to regulate the flow or ramp rate while working
        /// toward the TargetValue.
        /// </summary>
        [JsonProperty, DefaultValue(10.0)]
        public double MaximumRate
        {
            get => maximumRate;
            set => Ensure(ref maximumRate, value);
        }
        double maximumRate = 10.0;

        /// <summary>
        /// Dead time plus lag, in seconds. The time expected to pass
        /// between a valve movement and the end of its effect on Meter.Value.
        /// </summary>
        [JsonProperty, DefaultValue(60.0)]
        public double Lag
        {
            get => lag;
            set => Ensure(ref lag, value);
        }
        double lag = 60.0;

        /// <summary>
        /// A tolerable difference between Value and TargetValue, within which
        /// no valve adjustment is made.
        /// </summary>
        [JsonProperty, DefaultValue(0.1)]
        public double Deadband
        {
            get => deadband;
            set => Ensure(ref deadband, Math.Abs(value));
        }
        double deadband = 0.1;

        /// <summary>
        /// If false, Deadband is a fixed constant in units of TargetValue;
        /// if true, the dead band is the product of Deadband and TargetValue.
        /// </summary>
        [JsonProperty, DefaultValue(false)]
        public bool DeadbandIsFractionOfTarget
        {
            get => deadbandIsFractionOfTarget;
            set => Ensure(ref deadbandIsFractionOfTarget, value);
        }
        bool deadbandIsFractionOfTarget = false;

        /// <summary>
        /// Estimated Positions to move the flow valve to correct an error of one unit.
        ///     error = anticipated or true Value - TargetValue
        /// Error is negative when Value is below TargetValue.
        /// The movement (~gain * -error) is calculated to oppose the error.
        /// If error and gain are both negative, the movement will be negative.
        /// A negative movement is normally in the opening direction.
        /// Use a negative Gain if a negative movement increases Value.
        /// Use a positive Gain if a positive movement increases Value
        /// or if a negative movement decreases Value.
        /// </summary>
        [JsonProperty, DefaultValue(1.0)]
        public double Gain
        {
            get => gain;
            set => Ensure(ref gain, value);
        }
        double gain = 1.0;

        /// <summary>
        /// Divide Gain by Deadband when computing the amount to move the valve.
        /// </summary>
        [JsonProperty, DefaultValue(true)]
        public bool DivideGainByDeadband
        {
            get => divideGainByDeadband;
            set => Ensure(ref divideGainByDeadband, value);
        }
        bool divideGainByDeadband = true;

        /// <summary>
        /// Stop the flow manager if FlowValve reaches its fully opened position.
        /// </summary>
        [JsonProperty, DefaultValue(false)]
        public bool StopOnFullyOpened
        {
            get => stopOnFullyOpened;
            set => Ensure(ref stopOnFullyOpened, value);
        }
        bool stopOnFullyOpened = false;

        /// <summary>
        /// Stop the flow manager if FlowValve reaches its fully closed position.
        /// </summary>
        [JsonProperty, DefaultValue(false)]
        public bool StopOnFullyClosed
        {
            get => stopOnFullyClosed;
            set => Ensure(ref stopOnFullyClosed, value);
        }
        bool stopOnFullyClosed = false;

        /// <summary>
        /// The Value that the flow manager works to achieve.
        /// </summary>
        public double TargetValue
        {
            get => targetValue;
            set => Ensure(ref targetValue, value);
        }
        double targetValue = 0;

        /// <summary>
        /// Use Meter's rate of change instead of its absolute value.
        /// </summary>
        [JsonProperty, DefaultValue(false)]
        public bool UseRateOfChange
        {
            get => useRateOfChange;
            set => Ensure(ref useRateOfChange, value);
        }
        bool useRateOfChange = false;

        /// <summary>
        /// A StepTracker to receive ongoing process state messages.
        /// </summary>
        public StepTracker ProcessStep
        {
            get => processStep ?? StepTracker.Default;
            set => Ensure(ref processStep, value);
        }
        StepTracker processStep;


        Thread managerThread;
        AutoResetEvent stopSignal = new AutoResetEvent(false);

        /// <summary>
        /// Flow is actively being controlled.
        /// </summary>
        public bool Busy => managerThread != null && managerThread.IsAlive;

        /// <summary>
        /// Start managing the flow with the current TargetValue
        /// </summary>
        public void Start() => Start(TargetValue);

        /// <summary>
        /// Whether the control loop is active.
        /// </summary>
        public bool Looping
        {
            get => looping;
            set => Ensure(ref looping, value);
        }
        bool looping;

        /// <summary>
        /// Start managing the flow with the given targetValue
        /// </summary>
        public void Start(double targetValue)
        {
            TargetValue = targetValue;
            if (Busy) return;

            managerThread = new Thread(manageFlow)
            {
                Name = $"{FlowValve.Name} FlowManager",
                IsBackground = true
            };
            Looping = false;
            managerThread.Start();
            WaitFor(() => Looping, -1, 100);
        }

        /// <summary>
        /// Stop managing the flow
        /// </summary>
        public void Stop() => stopSignal.Set();

        void manageFlow()
        {
            stopSignal.Reset();
            bool stopRequested = false;

            ProcessStep.Start($"{Name}: starting");

            var operationName = "_Move";   // temporary
            var operation = FlowValve.FindOperation(operationName) as ActuatorOperation;
            if (operation != null) FlowValve.ActuatorOperations.Remove(operation);
            operation = new ActuatorOperation()
            {
                Name = operationName,
                Value = StartingMovement,
                Incremental = true,
                Configuration = FlowValve.FindOperation("Close").Configuration
            };
            FlowValve.ActuatorOperations.Add(operation);

            Stopwatch actionStopwatch = new Stopwatch();

            // starting motion
            if (StartingMovement != 0)
                FlowValve.DoWait(operation);
            actionStopwatch.Restart();

            var deadband = DeadbandIsFractionOfTarget ? Deadband * Math.Abs(TargetValue) : Deadband;
            var gain = DivideGainByDeadband ? Gain / deadband : Gain;
            if (FlowValve.OpenIsPositive) gain = -gain;     // usually, positive movement means closing

            var value = Meter.Value;
            var roc = Meter.RateOfChange.Value;
            var pos = FlowValve.Position;
            var priorError = value - TargetValue;
            var priorRate = roc;
            var priorPos = pos;
            var moved = false;
            var ppr = gain;         // Positions per dRateOfChange
            var gscale = 1.0;       // gain scaling factor, for adaptive gain

            Looping = true;
            while (!(stopRequested || StopOnFullyOpened && FlowValve.IsOpened || StopOnFullyClosed && FlowValve.IsClosed))
            {
                value = Meter.Value;
                roc = Meter.RateOfChange.Value;
                pos = FlowValve.Position;

                var anticipatedValue = value + roc * Lag;
                var manageRate = UseRateOfChange || Lag > secondsCycle || Math.Abs(roc) > MaximumRate;
                var error = manageRate ?
                    (UseRateOfChange ? roc : anticipatedValue) - TargetValue :
                    value - TargetValue;
                var anticipatedTimeToTarget =
                    error == 0 ? 0.0 :
                    roc == 0 ? double.PositiveInfinity :
                    (TargetValue - value) / roc;

                var outOfDeadband = Math.Abs(error) > deadband;

                if (actionStopwatch.Elapsed.TotalSeconds >= SecondsCycle)
                {
                    // Used when managing the Value; Cycle time is on the
                    // order of Lag, and |roc| is typically zero or very low
                    // at the end of each cycle.
                    // Adjust gain based on the latest change in error
                    if (moved && outOfDeadband)
                    {
                        if ((error > 0) != (priorError > 0))    // overshot
                            gscale *= 0.9;
                        else if (Math.Abs(error) > Math.Abs(priorError))   // error increased
                            gscale *= 1.2;
                    }
                    var g = gscale * gain;      // adaptive gain

                    // Used when managing the roc; in these cases, roc is usually
                    // significantly non-zero at the end of most cycles. E.g., UseRateOfChange
                    // is true, or the goal is to approach the TargetValue and stop there (in
                    // which case managing the roc is more effective than managing the Value.
                    var targetRate = UseRateOfChange ? TargetValue : -error / Lag;
                    if (Math.Abs(targetRate) > MaximumRate)
                        targetRate = targetRate < 0 ? -MaximumRate : MaximumRate;

                    var dpos = pos - priorPos;
                    var drate = roc - priorRate;
                    if (drate != 0)
                    {
                        var newPpr = dpos / drate;
                        if (Math.Abs(drate) > 0.5 && Math.Abs(dpos) >= 2 && (newPpr > 0) == (ppr > 0))
                            ppr = DigitalFilter.WeightedUpdate(newPpr, ppr, 0.95);
                    }

                    var errorBasedMovement = g * -error;
                    var rateBasedMovement = ppr * (targetRate - roc); // targetRate - roc is analogous to -error

                    var movement = manageRate ?
                        rateBasedMovement :  // manage roc
                        errorBasedMovement;  // manage value

                    ProcessStep.End();
                    if (manageRate)
                        ProcessStep.Start($"{Name}: e={error:0} r={roc:0.00}/{targetRate:0.00} ppr={ppr:0.0} m={movement:0.00}");
                    else
                        ProcessStep.Start($"{Name}: e={error:0.000} r={roc:0.000}/{targetRate:0.000} g={g:0.0} m={movement:0.0}");

                    int amountToMove = Math.Min(Math.Abs(MaximumMovement), Math.Abs(movement)).ToInt();
                    if (amountToMove == 0 && (manageRate || outOfDeadband) && anticipatedTimeToTarget > Math.Max(2 * Lag, 10 * secondsCycle))
                        amountToMove = g > 0 ? 1 : -1;

                    if (amountToMove > 0)
                    {
                        if (movement < 0) amountToMove = -amountToMove;
                        operation.Value = amountToMove;
                        FlowValve.DoWait(operation);
                    }
                    actionStopwatch.Restart();
                    priorError = error;
                    priorRate = roc;
                    moved = pos != priorPos;
                    priorPos = pos;
                }

                stopRequested = stopSignal.WaitOne(MillisecondsTimeout);
            }
            FlowValve.ActuatorOperations.Remove(operation);

            ProcessStep.End();
        }
    }
}