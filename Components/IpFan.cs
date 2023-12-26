using AeonHacs;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AeonHacs.Components
{
    /// <summary>
    /// Operates a cooling fan as needed by a set of inlet ports.
    /// </summary>
    public class IpFan : StateManager<IpFan.TargetStates, IpFan.States>
    {
        #region HacsComponent

        [HacsConnect]
        protected virtual void Connect()
        {
            Fan = Find<ISwitch>(fanName);
            InletPorts = FindAll<IInletPort>(inletPortNames);
        }

        #endregion HacsComponent
        [JsonProperty("Fan")]
        string FanName { get => Fan?.Name; set => fanName = value; }
        string fanName;
        public virtual ISwitch Fan
        {
            get => fan;
            set => Ensure(ref fan, value);
        }
        ISwitch fan;

        [JsonProperty("InletPorts")]
        List<string> InletPortNames { get => InletPorts?.Names(); set => inletPortNames = value; }
        List<string> inletPortNames;
        /// <summary>
        /// The Heaters cooled by the fan.
        /// </summary>
        public List<IInletPort> InletPorts
        {
            get => inletPorts;
            set => Ensure(ref inletPorts, value);
        }
        List<IInletPort> inletPorts;

        #region Class interface properties and methods

        public enum TargetStates
        {
            /// <summary>
            /// Monitor dependent devices, activate as needed.
            /// </summary>
            Monitor,
            /// <summary>
            /// Keep the fan on, regardless of dependent devices.
            /// </summary>
            StayOn,
            /// <summary>
            /// Keep the fan off, regardless of dependent devices.
            /// </summary>
            StayOff,
            /// <summary>
            /// Do nothing automatically.
            /// </summary>
            Standby
        }

        public enum States
        {
            /// <summary>
            /// Monitoring dependent devices for service.
            /// </summary>
            Monitoring,
            /// <summary>
            /// Keeping the fan on, regardless of dependent devices.
            /// </summary>
            StayingOn,
            /// <summary>
            /// Keeping the fan off, regardless of dependent devices.
            /// </summary>
            StayingOff,
            /// <summary>
            /// Automatic operations are suspended but can
            /// be invoked manually.
            /// </summary>
            Standby
        }

        public virtual void Monitor() => ChangeState(TargetStates.Monitor);
        public virtual void StayOn() => ChangeState(TargetStates.StayOn);
        public virtual void StayOff() => ChangeState(TargetStates.StayOff);
        public virtual void Standby() => ChangeState(TargetStates.Standby);

        bool needsCooling(IInletPort p) =>
            (p.QuartzFurnace?.IsOn ?? false) || ((p.SampleFurnace?.Temperature ?? 0) > 40);

        void ManageFan() =>
            Fan.TurnOnOff(InletPorts?.Any(p => needsCooling(p)) ?? false);

        void ManageState()
        {
            if (!Connected || Hacs.Stopping) return;

            switch (TargetState)
            {
                case TargetStates.Monitor:
                    ManageFan();
                    State = States.Monitoring;
                    break;
                case TargetStates.StayOn:
                    Fan.TurnOn();
                    State = States.StayingOn;
                    break;
                case TargetStates.StayOff:
                    Fan.TurnOff();
                    State = States.StayingOff;
                    break;
                case TargetStates.Standby:
                    State = States.Standby;
                    break;
                default:
                    break;
            }
        }

        #endregion Class interface properties and methods

        public override string ToString() => base.ToString() + (State == States.Monitoring ? $" ({Fan.IsOn.OnOff()})" : "");

        public IpFan()
        {
            (this as IStateManager).ManageState = ManageState;
        }
    }
}
