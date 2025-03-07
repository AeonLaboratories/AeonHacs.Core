using AeonHacs.Utilities;
using Newtonsoft.Json;
using System;
using System.ComponentModel;
using System.Text;

namespace AeonHacs.Components
{
    /// <summary>
    /// A valve whose position is determined by a
    /// control signal's pulse width, like an RC servo.
    /// </summary>
    public class CpwValve : CpwActuator, ICpwValve, CpwValve.IDevice, CpwValve.IConfig
    {

        #region Device interfaces

        public new interface IDevice : CpwActuator.IDevice, Valve.IDevice { }
        public new interface IConfig : CpwActuator.IConfig, Valve.IConfig { }
        public new IDevice Device => this;
        public new IConfig Config => this;

        #endregion Device interfaces

        #region Valve

        Valve.IDevice IValve.Device => this;
        Valve.IConfig IValve.Config => this;

        [JsonProperty]
        public virtual ValveState ValveState
        {
            get => valveState;
            protected set => Ensure(ref valveState, value);
        }
        ValveState valveState = ValveState.Unknown;
        ValveState Valve.IDevice.ValveState
        {
            get => ValveState;
            set => ValveState = value;
        }

        public virtual bool IsOpened => ValveState == ValveState.Opened;
        public virtual bool IsClosed => ValveState == ValveState.Closed;
        public virtual void Open() => DoOperation("Open");
        public virtual void Close() => DoOperation("Close");
        public virtual void OpenWait() { Open(); WaitForIdle(); }
        public virtual void CloseWait() { Close(); WaitForIdle(); }
        public virtual void DoWait(ActuatorOperation operation) { DoOperation(operation) ; WaitForIdle(); }
        public virtual void DoWait(string operation) { DoOperation(operation); WaitForIdle(); }

        public virtual void Exercise()
        {
            if (Idle)
            {
                if (IsOpened)
                { Close(); OpenWait(); }
                else if (IsClosed)
                { Open(); CloseWait(); }
            }
        }

        #endregion Valve

        [JsonProperty]
        public virtual int Position
        {
            get => position;
            set => Ensure(ref position, value);
        }
        int position;

        [JsonProperty, DefaultValue(0.0)]
        public virtual double OpenedVolumeDelta
        {
            get => openedVolumeDelta;
            set => Ensure(ref openedVolumeDelta, value);
        }
        double openedVolumeDelta = 0.0;

        protected override void OnOperationChanged(object sender, PropertyChangedEventArgs e)
        {
            if (sender is ObservableItemsCollection<ActuatorOperation> list && e == null)
            {
                foreach (var op in list)
                    OnOperationChanged(op, null);
            }
            else if (sender is ActuatorOperation op)
            {
                UpdateOpenedAndClosedValues(op);
            }
        }

        void UpdateOpenedAndClosedValues(ActuatorOperation op)
        {
            if (op.Name == "Open")
                OpenedValue = op.Value;
            else if (op.Name == "Close")
                ClosedValue = op.Value;
        }

        /// <summary>
        /// Position at which the valve is in the opened state.
        /// </summary>
        public virtual int OpenedValue
        {
            get => openedValue ?? 0;
            protected set => Ensure(ref openedValue, value);
        }
        int? openedValue;

        /// <summary>
        /// Position at which the valve is in the closed state.
        /// </summary>
        public virtual int ClosedValue
        {
            get => closedValue ?? 0;
            protected set => Ensure(ref closedValue, value);
        }
        int? closedValue;

        public virtual int CenterValue => (OpenedValue + ClosedValue) / 2;

        public virtual bool OpenIsPositive => OpenedValue >= ClosedValue;


        protected virtual ValveState cmdDirection(int cmd, int refcmd)
        {
            if (cmd == refcmd)                  // both command and ref are center
                return ValveState.Unknown;      // direction can't be determined
            else if ((cmd > refcmd) == OpenIsPositive)
                return ValveState.Opening;
            else
                return ValveState.Closing;
        }

        protected virtual ValveState OperationDirection(IActuatorOperation operation)
        {
            if (operation == null)
                return Operation == null ? ValveState.Unknown : OperationDirection(Operation);
            if (operation.Incremental)
                return cmdDirection(operation.Value, 0);
            return cmdDirection(operation.Value, Position);
        }

        protected ValveState CurrentMotion { get; set; }
        protected ValveState LastMotion { get; set; }

        public override bool ActionSucceeded
        {
            get => base.ActionSucceeded || StopRequested;
            protected set => base.ActionSucceeded = value;
        }
        public override IActuatorOperation ValidateOperation(IActuatorOperation operation)
        {
            if (operation == null) return null;
            var newPosition = operation.Incremental ? Position + operation.Value : Operation.Value;
            var maxPosition = Math.Max(OpenedValue, ClosedValue);
            var minPosition = Math.Min(OpenedValue, ClosedValue);
            if (newPosition > maxPosition) newPosition = maxPosition;
            if (newPosition < minPosition) newPosition = minPosition;
            if (newPosition == Position) return null;
            var newValue = operation.Incremental ? newPosition - Position : newPosition;
            operation.Value = newValue; // no point in checking whether they match here
            return operation;
        }

        // Called when the valve becomes "Active", whenever a report
        // is received while active, and finally, once when the valve
        // becomes inactive.
        protected virtual void UpdateValveState()
        {
            if (Operation != null)
            {
                ValveState =
                    Active ? CurrentMotion :
                    (PositionDetectable ? !LimitSwitchDetected : !ActionSucceeded) ? ValveState.Unknown :
                    (StopRequested && !TimeLimitDetected) ? ValveState.Unknown :
                    Position == ClosedValue ? ValveState.Closed :
                    Position == OpenedValue ? ValveState.Opened :
                    Position == Operation?.Value ? ValveState.Opened :
                    ValveState.Unknown;
            }
        }

        protected override void OperationStarting()
        {
            base.OperationStarting();
            CurrentMotion = OperationDirection(Operation);
            UpdateValveState();
        }

        protected override void OperationEnding()
        {
            base.OperationEnding();
            UpdateValveState();
            LastMotion = CurrentMotion;
        }

        protected override bool ReviewOperation() =>
            !OperationFailed && (Operation == null || Position == Operation.Value);

        public override long UpdatesReceived
        {
            get => base.UpdatesReceived;
            protected set
            {
                base.UpdatesReceived = value;
                UpdateValveState();
            }
        }

        public CpwValve(IHacsDevice d = null) : base(d) { }

        public override string ToString()
        {
            var sb = new StringBuilder($"{Name}: {ValveState} ({(ValveState == ValveState.Unknown ? "~" : "")}{Position})");
            var sb2 = new StringBuilder();
            sb2.Append($"\r\nPending Operations: {PendingOperations}");
            sb2.Append(Active ? $", Motion: {CurrentMotion}" : $", Last Motion: {LastMotion}");
            if (LastMotion != ValveState.Unknown)
            {
                if (Active)
                    sb2.Append(StopRequested ? ", Stopping" : ", Active");
                else
                    sb2.Append(StopRequested ? ", Stopped" : ActionSucceeded ? ", Succeeded" : ", Failed");
            }
            if (Operation != null)
            {
                var which = Active ? "Current" : "Prior";
                sb2.Append($"\r\n{which} Operation: \"{Operation.Name}\", Value: {Operation.Value}, Updates Received: {UpdatesReceived}");
                if (UpdatesReceived > 0)
                {
                    var si = Device.Settings.CurrentLimit > 0 ? $"Current: {Current} / {Device.Settings.CurrentLimit} mA" : "";
                    var st = Device.Settings.TimeLimit > 0 ? $"Time: {Elapsed} / {Device.Settings.TimeLimit} s" : "";
                    var slim0 = Device.Settings.Limit0Enabled ? $"Limit0: {LimitSwitch0Engaged.ToString("Engaged", "Enabled")}" : "";
                    var slim1 = Device.Settings.Limit1Enabled ? $"Limit1: {LimitSwitch1Engaged.ToString("Engaged", "Enabled")}" : "";
                    var all = string.Join(" ", si, st, slim0, slim1);
                    if (all.Length > 0)
                        sb2.Append($"\r\n{all}");
                }
            }
            if (Manager != null)
                sb2.Append(ManagerString(this));
            sb.Append(Utility.IndentLines(sb2.ToString()));
            return sb.ToString();
        }
    }

    public static class CpwValveExtensions
    {
        private static void CloseBy(this ICpwValve valve, int amount)
        {
            var operation = valve.FindOperation("Close");
            if (operation == null)
                return; // something went wrong

            if (valve.OpenIsPositive)
                amount = -amount;

            operation.Value += amount;
            if (valve.OpenIsPositive != amount < 0)
                valve.OpenWait();
            valve.CloseWait();
        }

        private static void OpenBy(this ICpwValve valve, int amount)
        {
            var operation = valve.FindOperation("Open");
            if (operation == null)
                return; // something went wrong

            if (valve.OpenIsPositive)
                amount = -amount;

            operation.Value -= amount;
            if (valve.OpenIsPositive != amount < 0)
                valve.CloseWait();
            valve.OpenWait();
        }

        public static void CloseABitMore(this ICpwValve valve) =>
            valve.CloseBy(10);

        public static void CloseMore(this ICpwValve valve) =>
            valve.CloseBy(50);

        public static void CloseABitLess(this ICpwValve valve) =>
            valve.CloseBy(-10);

        public static void CloseLess(this ICpwValve valve) =>
            valve.CloseBy(-50);

        public static void OpenABitMore(this ICpwValve valve) =>
            valve.OpenBy(10);

        public static void OpenMore(this ICpwValve valve) =>
            valve.OpenBy(50);

        public static void OpenABitLess(this ICpwValve valve) =>
            valve.OpenBy(-10);

        public static void OpenLess(this ICpwValve valve) =>
            valve.OpenBy(-50);
    }
}
