﻿using AeonHacs.Utilities;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using static AeonHacs.Notify;
using static AeonHacs.Utilities.Utility;

namespace AeonHacs.Components
{
    /// <summary>
    /// Supplies a gas to a Destination Section via a Path Section.
    /// Set Path to null if v_source is on the Destination boundary.
    /// </summary>
    public class GasSupply : HacsComponent, IGasSupply
    {
        #region HacsComponent

        [HacsConnect]
        protected virtual void Connect()
        {
            FlowValve = Find<IRS232Valve>(flowValveName);
            SourceValve = Find<IValve>(sourceValveName);
            Meter = Find<IMeter>(meterName);
            Destination = Find<ISection>(destinationName);
            Path = Find<ISection>(pathName);
            FlowManager = Find<FlowManager>(flowManagerName);
        }

        #endregion HacsComponent

        /// <summary>
        /// A StepTracker to receive process details.
        /// </summary>
        public StepTracker ProcessStep
        {
            get => processStep ?? StepTracker.Default;
            set => Ensure(ref processStep, value);
        }
        StepTracker processStep;

        /// <summary>
        /// A StepTracker to receive process overview messages.
        /// </summary>
        public StepTracker MajorStep
        {
            get => majorStep ?? StepTracker.DefaultMajor;
            set => Ensure(ref majorStep, value);
        }
        StepTracker majorStep;


        /// <summary>
        /// The name of the gas.
        /// </summary>
        [JsonProperty]
        public string GasName
        {
            get => gasName ?? (Name.IsBlank() ? "gas" :
                gasName = Name.Split('.')[0]);
            set => gasName = value;
        }
        string gasName;

        [JsonProperty("SourceValve")]
        string SourceValveName { get => SourceValve?.Name; set => sourceValveName = value; }
        string sourceValveName;
        /// <summary>
        /// The gas supply shutoff valve.
        /// </summary>
        public IValve SourceValve
        {
            get => sourceValve;
            set => Ensure(ref sourceValve, value, NotifyPropertyChanged);
        }
        IValve sourceValve;

        [JsonProperty("FlowValve")]
        string FlowValveName { get => FlowValve?.Name; set => flowValveName = value; }
        string flowValveName;
        /// <summary>
        /// The valve (if any) that controls the gas supply flow.
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
        /// The Meter that is supposed to reach the target value when a
        /// controlled amount of gas is to be admitted to the Destination.
        /// </summary>
        public IMeter Meter
        {
            get => meter;
            set => Ensure(ref meter, value, NotifyPropertyChanged);
        }
        IMeter meter;

        /// <summary>
        /// The typical time between closing the shutoff valve and Meter.Value stability.
        /// </summary>
        [JsonProperty]
        public double SecondsSettlingTime
        {
            get => secondsSettlingTime;
            set => Ensure(ref secondsSettlingTime, value);
        }
        double secondsSettlingTime;

        [JsonProperty("FlowManager")]
        string FlowManagerName { get => FlowManager?.Name; set => flowManagerName = value; }
        string flowManagerName;
        /// <summary>
        /// The control system that manages the flow valve position to
        /// achieve a desired condition, usually a target Value for Meter or
        /// its RateOfChange.
        /// </summary>
        public IFlowManager FlowManager
        {
            get => flowManager;
            set => Ensure(ref flowManager, value);
        }
        IFlowManager flowManager;

        [JsonProperty("Destination")]
        string DestinationName { get => Destination?.Name; set => destinationName = value; }
        string destinationName;
        /// <summary>
        /// The Section to receive the gas. The Section's Isolation ValveList isolates the Destination
        /// and also the PathToVacuum. The Section's PathToVacuum ValveList joins the Destination
        /// volume to the Vacuum Manifold.
        /// </summary>
        public ISection Destination
        {
            get => destination;
            set => Ensure(ref destination, value, NotifyPropertyChanged);
        }
        ISection destination;

        [JsonProperty("Path")]
        string PathName { get => Path?.Name; set => pathName = value; }
        string pathName;
        /// <summary>
        /// The Section comprising the Chambers between v_source and Destination.
        /// Set Path to null if v_source is on the Destination boundary. Set
        /// Path.PathToVacuum to null if Path cannot be evacuated without also
        /// evacuating Destination. Path.InternalValves *is* the path except
        /// for the final valve between Path and Destination.
        /// </summary>
        public ISection Path
        {
            get => path;
            set => Ensure(ref path, value, NotifyPropertyChanged);
        }
        ISection path;

        /// <summary>
        /// When roughing through "Closed" v_flow, the vacuum system's foreline pressure should
        /// fall below this value.
        /// </summary>
        [JsonProperty, DefaultValue(2.0)]
        public double PurgePressure
        {
            get => purgePressure;
            set => Ensure(ref purgePressure, value);
        }
        double purgePressure = 2.0;


        /// <summary>
        /// When roughing through "Closed" v_flow, if it takes longer than this
        /// for the vacuum system's foreline pressure to fall below PurgePressure,
        /// issue a warning.
        /// </summary>
        [JsonProperty, DefaultValue(20)]
        public int SecondsToPurge
        {
            get => secondsToPurge;
            set => Ensure(ref secondsToPurge, value);
        }
        int secondsToPurge = 20;   // max

        /// <summary>
        /// Isolate Destination and Path, then Open the Destination and Path,
        /// joined together.
        /// </summary>
        public void IsolateAndJoin()
        {
            var toBeOpened = Destination?.InternalValves.SafeUnion(Path?.InternalValves);
            var joinsDestinationToPath = Destination?.Isolation.SafeIntersect(Path?.Isolation);
            toBeOpened = toBeOpened.SafeUnion(joinsDestinationToPath);
            var toBeClosed = Destination?.Isolation.SafeUnion(Path?.Isolation);

            Path?.ClosePorts();
            toBeClosed?.CloseExcept(toBeOpened);
            toBeOpened?.Open();
        }

        /// <summary>
        /// Open the Path.PathToVacuum, or Destination.PathToVacuum if
        /// Path doesn't exist.
        /// </summary>
        public void JoinToVacuumManifold()
        {
            if (Path != null)
                Path?.PathToVacuum?.Open();
            else
                Destination?.PathToVacuum.Open();
        }

        /// <summary>
        /// Isolates the Path and Destination from vacuum.
        /// </summary>
        public void IsolateFromVacuum()
        {
            if (Path?.PathToVacuum is List<IValve> list && list.Any())
                list.Close(list.First());
            else
                Path?.VacuumSystem?.Isolate();

            if (Destination?.PathToVacuum is List<IValve> list2 && list2.Any())
                list2.Close(list2.First());
            else
                Destination?.VacuumSystem?.Isolate();
        }

        /// <summary>
        /// Evacuate the Path, but not the Destination. Does nothing
        /// if this is not possible.
        /// </summary>
        public void EvacuatePath() { EvacuatePath(-1); }

        /// <summary>
        /// Evacuate the Path to pressure, but not the Destination.
        /// Does nothing if this is not possible.
        /// </summary>
        /// <param name="pressure"></param>
        public void EvacuatePath(double pressure)
        {
            // Do nothing if it's impossible to evacuate Path without
            // also evacuating Destination.
            if (Destination?.Isolation != null &&
                Path?.PathToVacuum != null &&
                Destination.Isolation.SafeIntersect(Path.PathToVacuum).Any())
                return;

            Path?.OpenAndEvacuate(pressure);
        }

        /// <summary>
        /// Stop the flow of gas.
        /// </summary>
        public void ShutOff() { ShutOff(false); }

        /// <summary>
        /// Close the source/shutoff valve, and optionally close the flow valve, too.
        /// </summary>
        /// <param name="alsoCloseFlow"></param>
        public void ShutOff(bool alsoCloseFlow)
        {
            if (FlowValve != null)
                FlowValve.Stop();
            SourceValve?.CloseWait();
            if (alsoCloseFlow)
                FlowValve?.CloseWait();
        }

        /// <summary>
        /// Wait for pressure, but stop waiting if 10 seconds elapse with no increase.
        /// </summary>
        /// <param name="pressure"></param>
        public void WaitForPressure(double pressure, bool thenCloseShutoff = false)
        {
            var sw = new Stopwatch();
            double peak = Meter.Value;
            ProcessStep?.Start($"Wait for {pressure:0} {Meter.UnitSymbol} {GasName} in {Destination.Name}");
            sw.Restart();
            bool shouldStop()
            {
                if (Meter.Value > peak)
                {
                    peak = Meter.Value;
                    sw.Restart();
                }
                return Meter.Value >= pressure || sw.Elapsed.TotalSeconds >= 10;
            }

            while (Meter.Value < pressure)
            {
                WaitFor(shouldStop, -1, 35);

                if (Meter.Value < pressure)
                {
                    ShutOff();

                    if (Warn($"It's taking too long for {Meter.Name} to reach {pressure:0} Torr.",
                        $"Ok to try again or Cancel to move on.\r\n" +
                        $"Restart the application to abort the process.").Ok())
                    {
                        SourceValve.OpenWait();
                        continue;
                    }
                }
                break;
            }

            if (thenCloseShutoff)
                ShutOff();

            ProcessStep?.End();
        }

        /// <summary>
        /// Connect the gas supply to the Destination. Destination Ports are not changed.
        /// </summary>
        public void Admit()
        {
            IsolateAndJoin();
            SourceValve?.OpenWait();
            FlowValve?.Open();            // but don't wait for flow valve
        }

        /// <summary>
        /// Admit the given pressure of gas into the Destination,
        /// then close the supply and flow valves. If there is no
        /// pressure reading available, silently waits one second
        /// before closing the valves. Ports on the Destination
        /// are not changed.
        /// </summary>
        public void Admit(double pressure) { Admit(pressure, true); }

        /// <summary>
        /// Admit the given pressure of gas into the Destination,
        /// then close the source/shutoff valve and, optionally,
        /// the flow valve. If no pressure reading is available,
        /// silently waits one second before closing the valves.
        /// Ports on the Destination are not changed.
        /// </summary>
        public void Admit(double pressure, bool thenCloseFlow)
        {
            string subject, message;

            MajorStep?.Start($"Admit {pressure:0} {Meter?.UnitSymbol} {gasName} into {Destination.Name}");
            if (Meter == null)
            {
                Admit();
                WaitSeconds(1);
                ShutOff(thenCloseFlow);
            }
            else
            {
                while (!Hacs.Stopping)
                {
                    if (pressure > Meter.MaxValue)
                    {
                        Announce($"Requested pressure ({pressure:0} {Meter.UnitSymbol}) too high.",
                            $"Reducing target to maximum ({Meter.MaxValue:0} {Meter.UnitSymbol}).", NoticeType.Warning);
                        pressure = Meter.MaxValue;
                    }
                    for (int i = 0; i < 5; ++i)
                    {
                        Admit();
                        WaitForPressure(pressure);
                        ShutOff(thenCloseFlow);
                        WaitSeconds(3);
                        if (Meter.Value >= pressure)
                            break;
                    }

                    // accept 98% of the target
                    if (Meter.Value >= 0.98 * pressure)
                        break;

                    // If the pressure is lower than 98% of the target, ask the user what to do.
                    subject = "Process Exception";
                    message = $"Couldn't admit {pressure:0} {Meter.UnitSymbol} of {GasName} into {Destination.Name}.\r\n" +
                                  $"Ok to try again.\r\n" +
                                  $"Cancel to continue at {Meter.Value:0} {Meter.UnitSymbol}.\r\n" +
                                  $"Restart the application to abort the process.";

                    if (!Warn($"Couldn't admit {pressure:0} {Meter.UnitSymbol} of {GasName} into {Destination.Name}.",
                        $"Ok to try again.\r\n" +
                        $"Cancel to continue with only {Meter.Value:0} {Meter.UnitSymbol}.\r\n" +
                        $"Restart the application to abort the process.").Ok())
                    {
                        break;
                    }
                }
            }
            MajorStep.End();
        }

        /// <summary>
        /// Perform three flushes, each time admitting gas at pressureHigh
        /// into Destination, and then evacuating to pressureLow.
        /// </summary>
        /// <param name="pressureHigh">pressure of gas to admit</param>
        /// <param name="pressureLow">evacuation pressure</param>
        public void Flush(double pressureHigh, double pressureLow)
        { Flush(pressureHigh, pressureLow, 3); }

        /// <summary>
        /// Perform the specified number of flushes, each time admitting gas
        /// at pressureHigh into Destination, and then evacuating to pressureLow.
        /// </summary>
        /// <param name="pressureHigh">pressure of gas to admit</param>
        /// <param name="pressureLow">evacuation pressure</param>
        /// <param name="flushes">number of times to flush</param>
        public void Flush(double pressureHigh, double pressureLow, int flushes)
        { Flush(pressureHigh, pressureLow, flushes, null); }

        /// <summary>
        /// Perform the specified number of flushes, each time admitting gas
        /// at pressureHigh into Destination, and then evacuating to pressureLow.
        /// If a port is specified, then all Destination ports are closed before
        /// the gas is admitted, and the given port is opened before evacuation.
        /// </summary>
        /// <param name="pressureHigh">pressure of gas to admit</param>
        /// <param name="pressureLow">evacuation pressure</param>
        /// <param name="flushes">number of times to flush</param>
        /// <param name="port">port to be flushed</param>
        public void Flush(double pressureHigh, double pressureLow, int flushes, IPort port) =>
            Flush(pressureHigh, pressureLow, flushes, port, 0, 0);

        /// <summary>
        /// Perform the specified number of flushes, each time admitting gas
        /// at pressureHigh into Destination, waiting minutesAtPressureHigh,
        /// then evacuating to pressureLow. Between pressureLow and pressureHigh,
        /// wait minutesBetweenFlushes. That is, there is no wait after the final
        /// pressureLow or before the first pressureHigh.
        /// If a port is specified, then all Destination ports are closed before
        /// the gas is admitted, and the given port is opened before evacuation.
        /// </summary>
        /// <param name="pressureHigh">pressure of gas to admit</param>
        /// <param name="pressureLow">evacuation pressure</param>
        /// <param name="flushes">number of times to flush</param>
        /// <param name="port">port to be flushed</param>
        /// <param name="minutesAtPressureHigh">dwell time at pressureHigh</param>
        /// <param name="minutesBetweenFlushes">dwell time between flushes, i.e., between pressureLow and pressureHigh</param>
        public void Flush(double pressureHigh, double pressureLow, int flushes, IPort port, double minutesAtPressureHigh, double minutesBetweenFlushes = 0)
        {
            if (pressureHigh > Meter.MaxValue)
            {
                Announce($"Requested pressure ({pressureHigh:0} {Meter.UnitSymbol}) too high.",
                    $"Reducing target to maximum ({Meter.MaxValue:0} {Meter.UnitSymbol}).", NoticeType.Warning);

                pressureHigh = Meter.MaxValue;
            }

            for (int i = 1; i <= flushes; i++)
            {
                MajorStep?.Start($"Flush {Destination.Name} with {GasName} ({i} of {flushes})");
                if (port != null) Destination.ClosePorts();
                Admit(pressureHigh, false);
                ProcessStep?.Start($"Wait for {minutesAtPressureHigh} minutes at {pressureHigh:0} Torr");
                WaitFor(() => false, (int)(60000 * minutesAtPressureHigh), 500);
                ProcessStep?.End();
                port?.Open();
                Destination.Evacuate(pressureLow);
                if (i < flushes)
                {
                    ProcessStep?.Start($"Wait for {minutesBetweenFlushes} minutes before next flush");
                    WaitFor(() => false, (int)(60000 * minutesBetweenFlushes), 500);
                    ProcessStep?.End();
                }
                MajorStep?.End();
            }
            FlowValve?.CloseWait();
        }

        /// <summary>
        /// Admit a gas into the Destination, controlling the flow rate
        /// to achieve a higher level of precision over a wider range
        /// of target pressures. Requires a flow/metering valve.
        /// </summary>
        /// <param name="pressure">desired final pressure</param>
        public void Pressurize(double pressure)
        {
            string subject, message;

            if (pressure > Meter.MaxValue)
            {
                Announce($"Requested pressure ({pressure:0} {Meter.UnitSymbol}) too high.",
                    $"Reducing target to maximum ({Meter.MaxValue:0} {Meter.UnitSymbol}).", NoticeType.Warning);

                pressure = Meter.MaxValue;
            }
            bool normalized = false;

            for (int tries = 0; tries < 5; tries++)
            {
                normalized = NormalizeFlow(tries == 1);
                if (normalized) break;
                RestoreRegulation();
            }

            if (!normalized)
            {
                Announce($"{FlowValve.Name} minimum flow is too high.",
                    $"Increase the PurgePressure above {PurgePressure:0.00} Torr, or\r\n" +
                    $"or adjust {FlowValve.Name}'s minimum flow rate below that.", NoticeType.Warning);
            }

            FlowPressurize(pressure);
        }

        /// <summary>
        /// Remove excessive gas from gas supply line to reduce the
        /// pressure into the regulated range.
        /// </summary>
        void RestoreRegulation() => RestoreRegulation(5);

        /// <summary>
        /// Remove excessive gas from gas supply line to reduce the
        /// pressure into the regulated range.
        /// </summary>
        /// <param name="secondsFlow">Seconds to evacuate at maximum flow rate.</param>
        void RestoreRegulation(int secondsFlow)
        {
            var vacuumSystem = Path?.VacuumSystem;
            if (vacuumSystem == null) vacuumSystem = Destination.VacuumSystem;
            if (vacuumSystem == null) return;

            MajorStep?.Start($"Restore {GasName} pressure regulation");

            IsolateAndJoin();
            vacuumSystem.IsolateManifold();

            FlowValve.Open();
            SourceValve.OpenWait();
            JoinToVacuumManifold();
            vacuumSystem.Rough();

            while (!WaitFor(() => vacuumSystem.State == VacuumSystem.StateCode.Roughing || vacuumSystem.State == VacuumSystem.StateCode.Isolated, 30 * 1000, 35))
            {
                SourceValve.CloseWait();
                vacuumSystem.Isolate();

                if (Warn($"{vacuumSystem.Name} is taking too long to reach roughing or isolated state.",
                    $"Establish {vacuumSystem.Name}'s state manually and\r\n" +
                    $"Ok to try again or Cancel to move on.\r\n" +
                    $"Restart the application to abort the process.").Ok())
                {
                    SourceValve.OpenWait();
                    vacuumSystem.Rough();
                    continue;
                }
                break;
            }

            WaitSeconds(secondsFlow);

            Path?.InternalValves?.CloseLast();
            IsolateFromVacuum();
            SourceValve.Close();
            FlowValve.CloseWait();
            vacuumSystem.Isolate();

            MajorStep?.End();
        }

        /// <summary>
        /// Evacuate most of the gas from between the shutoff and flow valves.
        /// </summary>
        /// <param name="calibrate">calibrate the flow valve first</param>
        /// <returns>success</returns>
        public bool NormalizeFlow(bool calibrate = false)
        {
            //Stopwatch sw = new Stopwatch();

            var vacuumSystem = Path?.VacuumSystem ?? Destination.VacuumSystem;
            if (vacuumSystem == null)
                throw new Exception("NormalizeFlow requires a VacuumSystem");

            MajorStep?.Start($"Normalize {GasName}-{Destination.Name} flow conditions");

            FlowValve.CloseWait();
            if (calibrate)
            {
                ProcessStep?.Start("Calibrate flow valve");
                FlowValve.Calibrate();
                ProcessStep?.End();
            }

            var toBeOpened = Destination?.InternalValves.SafeUnion(Path?.InternalValves);
            if (Path?.PathToVacuum != null)
                toBeOpened = toBeOpened.SafeUnion(Path.PathToVacuum);
            else
                toBeOpened = toBeOpened.SafeUnion(Destination?.PathToVacuum);
            var toBeClosed = Destination?.Isolation.SafeUnion(Path?.Isolation);

            vacuumSystem.Isolate();
            vacuumSystem.IsolateExcept(toBeOpened);
            Path?.ClosePorts();
            toBeClosed?.CloseExcept(toBeOpened);
            toBeOpened?.Open();
            SourceValve.OpenWait();
            vacuumSystem.Evacuate();

            // TODO timeout
            WaitFor(() => vacuumSystem.HighVacuumValve.IsOpened || vacuumSystem.LowVacuumValve.IsOpened);
            WaitSeconds(2);
            MajorStep?.End();

            MajorStep?.Start("Drain flow-supply volume");
            bool success = WaitFor(() => vacuumSystem.Pressure <= PurgePressure, SecondsToPurge * 1000);
            SourceValve.CloseWait();
            IsolateFromVacuum();
            MajorStep?.End();

            return success;
        }

        /// <summary>
        /// Pressurize Destination to the given target value. Requires a flow valve.
        /// </summary>
        /// <param name="targetValue">desired final pressure or other metric</param>
        public void FlowPressurize(double targetValue)
        {
            string subject, message;

            if (!(FlowManager is IFlowManager))
            {
                ConfigurationError($"GasSupply {Name}: FlowPressurize() requires a FlowManager.");
                return;
            }

            bool gasIsCO2 = Name.Contains("CO2");
            if (gasIsCO2)
                MajorStep?.Start($"Admit {targetValue:0} {Meter.UnitSymbol} into the {Destination.Name}");
            else
                MajorStep?.Start($"Pressurize {Destination.Name} to {targetValue:0} {Meter.UnitSymbol} with {GasName}");

            if (targetValue > Meter.MaxValue)
            {
                Announce($"Requested value ({targetValue:0} {Meter.UnitSymbol}) too high.",
                    $"Reducing target to maximum ({Meter.MaxValue:0} {Meter.UnitSymbol}).", NoticeType.Warning);

                targetValue = Meter.MaxValue;
            }

            IsolateAndJoin();
            SourceValve.OpenWait();

            FlowManager.Start(targetValue);
            double anticipatedValue = 0;
            bool overshootAnticipated()
            {
                anticipatedValue = Meter.Value + Meter.RateOfChange * SecondsSettlingTime;
                return anticipatedValue > targetValue;
            }
            WaitFor(() => !FlowManager.Busy || overshootAnticipated());
            if (anticipatedValue > targetValue)
                Hacs.SystemLog.Record($"{Name} Stop to avoid overshoot ({anticipatedValue:0.0} > {targetValue:0.0})");
            SourceValve.CloseWait();
            FlowManager.Stop();

            Destination?.Isolate();
            FlowValve.CloseWait();

            var absError = Math.Abs(Meter.Value - targetValue);
            if (absError > 15)   // TODO: magic number
            {
                Warn($"{Name} Excessive error (value is:{Meter.Value:0.0} {Meter.UnitSymbol}, should be: {targetValue:0.0})." +
                    $"Ok or Cancel to accept this and move on.\r\n" +
                    $"Restart the application to abort the process.");
            }
            MajorStep?.End();
        }
    }
}