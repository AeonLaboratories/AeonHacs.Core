using AeonHacs.Utilities;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using static AeonHacs.Notify;
using static AeonHacs.Utilities.Utility;

namespace AeonHacs.Components;

public class Coldfinger : StateManager<Coldfinger.TargetStates, Coldfinger.States>, IColdfinger
{
    #region static
    static List<IColdfinger> List { get; set; }

    /// <summary>
    /// One or more of the FTCs supplied by the given LNManifold currently need it.
    /// </summary>
    public static bool AnyNeed(LNManifold lnManifold)
    {
        if (List == null) List = CachedList<IColdfinger>();
        return List.FirstOrDefault(ftc =>
            ftc.LNManifold == lnManifold &&
            ftc.IsActivelyCooling) != null;
    }

    #endregion static

    #region HacsComponent
    [HacsConnect]
    protected virtual void Connect()
    {
        LevelSensor = Find<IThermometer>(levelSensorName);
        LNValve = Find<IValve>(lnValveName);
        AirValve = Find<IValve>(airValveName);
        LNManifold = Find<ILNManifold>(lnManifoldName);
        AirThermometer = Find<HacsComponent>(airThermometerName);
        ambient = Find<Chamber>("Ambient");
    }

    [HacsInitialize]
    protected virtual void Initialize()
    {
        ChangeState(TargetState);
    }

    [HacsPreStop]
    protected virtual void PreStop()
    {
        switch (StopAction)
        {
            case StopAction.TurnOff:
                Standby();
                break;
            case StopAction.TurnOn:
                // not wise
                break;
            case StopAction.None:
                break;
            default:
                break;
        }
    }

    #endregion HacsComponent

    [JsonProperty("LevelSensor")]
    string LevelSensorName { get => LevelSensor?.Name; set => levelSensorName = value; }
    string levelSensorName;
    /// <summary>
    /// The Thermometer (thermocouple) used by this device to detect the level of
    /// liquid nitrogen in its reservoir.
    /// </summary>
    public IThermometer LevelSensor
    {
        get => levelSensor;
        set => Ensure(ref levelSensor, value, NotifyPropertyChanged);
    }
    IThermometer levelSensor;

    [JsonProperty("LNValve")]
    string LNValveName { get => LNValve?.Name; set => lnValveName = value; }
    string lnValveName;
    /// <summary>
    /// The valve that provides liquid nitrogen to this device.
    /// </summary>
    public IValve LNValve
    {
        get => lnValve;
        set => Ensure(ref lnValve, value, NotifyPropertyChanged);
    }
    IValve lnValve;

    /// <summary>
    /// The name of the LN valve operation to use for trickle flow.
    /// </summary>
    [JsonProperty("Trickle")]
    public string Trickle
    {
        get => trickle;
        set => Ensure(ref trickle, value, NotifyPropertyChanged);
    }
    string trickle;


    [JsonProperty("AirValve")]
    string AirValveName { get => AirValve?.Name; set => airValveName = value; }
    string airValveName;
    /// <summary>
    /// The valve that provides forced air to this device.
    /// </summary>
    public IValve AirValve
    {
        get => airValve;
        set => Ensure(ref airValve, value, NotifyPropertyChanged);
    }
    IValve airValve;

    [JsonProperty("LNManifold")]
    string LNManifoldName { get => LNManifold?.Name; set => lnManifoldName = value; }
    string lnManifoldName;
    /// <summary>
    /// The LNManifold where this device's LN valve is located.
    /// </summary>
    public ILNManifold LNManifold { get; set; }      // TODO make private?

    [JsonProperty("AirThermometer")]
    string AirThermometerName { get => AirThermometer?.Name; set => airThermometerName = value; }
    string airThermometerName;
    /// <summary>
    /// The device used to detect the air temperature around the FTC.
    /// </summary>
    public IHacsComponent AirThermometer
    {
        get => airThermometer;
        set => Ensure(ref airThermometer, value, NotifyPropertyChanged);
    }
    IHacsComponent airThermometer;

    /// <summary>
    /// The temperature from the level sensor that the FTC uses to conclude
    /// that its liquid nitrogen reservoir is full; usually a few degrees warmer
    /// than -195.8 °C.
    /// </summary>
    [JsonProperty, DefaultValue(-192)]
    public int FrozenTemperature
    {
        get => frozenTemperature;
        set => Ensure(ref frozenTemperature, value);
    }
    int frozenTemperature = -192;

    /// <summary>
    /// In Freeze mode, the FTC will request liquid nitrogen
    /// if its Temperature is this much warmer than FrozenTemperature.
    /// </summary>
    [JsonProperty, DefaultValue(5)]
    public int FreezeTrigger
    {
        get => freezeTrigger;
        set => Ensure(ref freezeTrigger, value);
    }
    int freezeTrigger = 5;


    /// <summary>
    /// In Raise mode, if the LNValve doesn't have a Trickle operation, this device
    /// will request liquid nitrogen if its Temperature is this much warmer than
    /// FrozenTemperature.
    /// </summary>
    [JsonProperty, DefaultValue(2)]
    public int RaiseTrigger
    {
        get => raiseTrigger;
        set => Ensure(ref raiseTrigger, value);
    }
    int raiseTrigger = 2;


    /// <summary>
    /// Whenever liquid nitrogen is flowing, the FTC moves the
    /// LNValve (close-open cycle) every this many seconds, to prevent
    /// the valve from sticking open.
    /// </summary>
    [JsonProperty, DefaultValue(60)]
    public int MaximumSecondsLNFlowing
    {
        get => maximumSecondsLNFlowing;
        set => Ensure(ref maximumSecondsLNFlowing, value);
    }
    int maximumSecondsLNFlowing = 60;

    /// <summary>
    /// Maximum time allowed when waiting for the Coldfinger to reach the Frozen state.
    /// </summary>
    [JsonProperty, DefaultValue(4)]
    public int MaximumMinutesToFreeze
    {
        get => maximumMinutesToFreeze;
        set => Ensure(ref maximumMinutesToFreeze, value);
    }
    int maximumMinutesToFreeze = 4;

    /// <summary>
    /// Maximum time allowed when waiting for the Coldfinger to Thaw.
    /// </summary>
    [JsonProperty, DefaultValue(10)]
    public int MaximumMinutesToThaw
    {
        get => maximumMinutesToThaw;
        set => Ensure(ref maximumMinutesToThaw, value);
    }
    int maximumMinutesToThaw = 10;

    /// <summary>
    /// How many seconds to wait for temperature equilibrium after the Raise state
    /// is reached.
    /// </summary>
    [JsonProperty, DefaultValue(15)]
    public int SecondsToWaitAfterRaised
    {
        get => secondsToWaitAfterRaised;
        set => Ensure(ref secondsToWaitAfterRaised, value);
    }
    int secondsToWaitAfterRaised = 15;


    /// <summary>
    /// The FTC is "near" air temperature if it is within this
    /// many degrees of AirTemperature.
    /// </summary>
    [JsonProperty, DefaultValue(7.0)]
    public double NearAirTemperature
    {
        get => nearAirTemperature;
        set => Ensure(ref nearAirTemperature, value);
    }
    double nearAirTemperature = 7.0;

    /// <summary>
    /// The available target states for an FTColdfinger. The FTC
    /// is controlled by setting TargetState to one of these values.
    /// </summary>
    public enum TargetStates
    {
        /// <summary>
        /// Turn off active warming and cooling.
        /// </summary>
        Standby,
        /// <summary>
        /// Warm coldfinger until thawed, then switch to Standby.
        /// </summary>
        Thaw,
        /// <summary>
        /// Immerse the coldfinger in LN, and maintain a minimal level of liquid there.
        /// </summary>
        Freeze,
        /// <summary>
        /// Freeze if needed and raise the LN, to the level of a trickling overflow if possible.
        /// </summary>
        Raise
    }

    /// <summary>
    /// The possible states of an FTColdfinger. The FTC is always
    /// in one of these states.
    /// </summary>
    public enum States
    {
        /// <summary>
        /// Coldfinger temperature is not being actively controlled.
        /// </summary>
        Standby,
        /// <summary>
        /// Warming coldfinger to ambient temperature.
        /// </summary>
        Thawing,
        /// <summary>
        /// Cooling the coldfinger using liquid nitrogen.
        /// </summary>
        Freezing,
        /// <summary>
        /// Maintaining a minimal level of liquid nitrogen on the coldfinger.
        /// </summary>
        Frozen,
        /// <summary>
        /// Raising the LN level on the coldfinger.
        /// </summary>
        Raising,
        /// <summary>
        /// Maintaining a maximum level of liquid nitrogen, with a trickling overflow if possible.
        /// </summary>
        Raised
    }

    protected Chamber ambient;

    /// <summary>
    /// The temperature (°C) reported by the level sensor.
    /// </summary>
    public double Temperature => LevelSensor.Temperature;

    /// <summary>
    /// The temperature (°C) of the air around the FTC.
    /// </summary>
    public double AirTemperature
    {
        get
        {
            if (AirThermometer is ITemperature t)
                return t.Temperature;
            if (AirThermometer is IThermometer th)
                return th.Temperature;
            if (AirThermometer is Meter m)
                return m;
            return ambient?.Temperature ?? 22; // room temperature TODO make property outside this class?
        }
    }

    /// <summary>
    /// The FTC is actively working to cool the coldfinger.
    /// </summary>
    public bool IsActivelyCooling =>
        TargetState == TargetStates.Freeze ||
        TargetState == TargetStates.Raise;

    /// <summary>
    /// The FTC is currently warming the coldfinger with forced air.
    /// </summary>
    public bool Thawing => State == States.Thawing;

    /// <summary>
    /// Whether the coldfinger temperature is warmer than a specified amount (NearAirTemperature)
    /// below air temperature.
    /// </summary>
    public bool Thawed =>
        Temperature > AirTemperature - NearAirTemperature;

    /// <summary>
    /// The FTC is at least as cold as FrozenTemperature and
    /// the TargetState is such as to maintain that condition.
    /// </summary>
    public bool Frozen => State switch
    {
        States.Frozen or States.Raising or States.Raised => true,
        _ => false
    };

    /// <summary>
    /// The FTC is currently maintaining a maximum level of liquid
    /// nitrogen, with a trickling overflow if possible.
    /// </summary>
    public bool Raised => State == States.Raised;

    /// <summary>
    /// The FTC is standing by.
    /// </summary>
    public bool Idle => State == States.Standby;










    /// <summary>
    /// Whether the LN valve has a Trickle operation;
    /// </summary>
    bool trickleSupported => LNValve.Operations.Contains(Trickle);
    Stopwatch valveOpenStopwatch = new Stopwatch();
    public double Target { get; protected set; }

    /// <summary>
    /// The present state of the FTC.
    /// </summary>
    public override States State
    {
        get
        {
            if (TargetState == TargetStates.Standby)
                return States.Standby;
            if (TargetState == TargetStates.Thaw)
                return States.Thawing;

            //else TargetState is Freeze or Raise

            if (Temperature >= FrozenTemperature + FreezeTrigger)
                return States.Freezing;

            if (TargetState == TargetStates.Freeze)
                return States.Frozen;

            // Target state is Raise
            if (Temperature <= FrozenTemperature + RaiseTrigger)
                return States.Raised;
            else
                return States.Raising;
        }
    }

    /// <summary>
    /// Restart the StateStopwatch and freezeThawTimer when the TargetState has changed.
    /// </summary>
    protected override void OnTargetStateChanged(TargetStates oldState, TargetStates newState)
    {
        StateStopwatch.Restart();
        freezeThawTimer.Reset();
    }

    /// <summary>
    /// What to do with the hardware device when this instance is Stopped.
    /// </summary>
    [JsonProperty("StopAction"), DefaultValue(StopAction.TurnOff)]
    public StopAction StopAction
    {
        get => stopAction;
        set => Ensure(ref stopAction, value);
    }
    StopAction stopAction = StopAction.TurnOff;

    /// <summary>
    /// Puts the FTC in Standby (turn off active cooling and warming).
    /// </summary>
    public void Standby()
    {
        ChangeState(TargetStates.Standby);
        LNOff();
        AirOff();
    }

    /// <summary>
    /// Fill and maintain a minimal level of liquid nitrogen in the reservoir.
    /// </summary>
    public void Freeze()
    {
        Target = FrozenTemperature;
        ChangeState(TargetStates.Freeze);
    }

    /// <summary>
    /// Reach and maintain a maximum level of liquid nitrogen,
    /// with a trickling overflow if possible.
    /// </summary>
    public void Raise()
    {
        Target = FrozenTemperature;
        ChangeState(TargetStates.Raise);
    }

    /// <summary>
    /// Warm the coldfinger with forced air.
    /// </summary>
    public void Thaw() => 
        Thaw(AirTemperature - NearAirTemperature);


    /// <summary>
    /// Warm the coldfinger with forced air to the specified temperature.
    /// </summary>
    /// <param name="temperature"></param>
    public void Thaw(double temperature)
    {
        Target = temperature;
        ChangeState(TargetStates.Thaw);
    }

    /// <summary>
    /// whether overflow trickling is presently preferred
    /// </summary>
    bool trickling =>
        trickleSupported &&
        TargetState == TargetStates.Raise;

    int trigger =>
        TargetState == TargetStates.Freeze ? FreezeTrigger : RaiseTrigger;

    /// <summary>
    /// Controls the LNValve as needed to maintain the desired LN level in the reservoir.
    /// </summary>
    void manageLNLevel()
    {
        if (!LNValve.IsClosed)
        {
            if (!valveOpenStopwatch.IsRunning) // in case it wasn't (re)started by LNOn().
                valveOpenStopwatch.Restart();
            if (valveOpenStopwatch.Elapsed.TotalSeconds > MaximumSecondsLNFlowing)
                LNOff();
            else if (Temperature <= Target)
            {
                if (!trickling)
                    LNOff();
                else if ((LNValve as IActuator).Operation?.Name != Trickle)
                    LNOn();
            }
        }
        else if (Temperature > Target + trigger || trickling)
            LNOn();
    }

    /// <summary>
    /// Starts the flow of liquid nitrogen to the reservoir.
    /// </summary>
    void LNOn()
    {
        if (!LNManifold.IsCold)
            return;
        if (trickling && Temperature < Target + RaiseTrigger)
            LNValve.DoOperation(Trickle);
        else
            LNValve.OpenWait();
        valveOpenStopwatch.Restart();
    }

    /// <summary>
    /// Stop the liquid nitrogen flow.
    /// </summary>
    void LNOff()
    {
        LNValve.WaitForIdle();
        if (!LNValve.IsClosed) LNValve.CloseWait();
        valveOpenStopwatch.Reset();
    }

    /// <summary>
    /// Blow air through the reservoir, to eject liquid
    /// nitrogen and warm the chamber.
    /// </summary>
    void AirOn()
    {
        if (AirValve == null) return;
        if (!AirValve.IsOpened)
            AirValve.OpenWait();
        else if (Temperature > Target)
            AirValve.CloseWait();
    }

    /// <summary>
    /// Stop the air flow.
    /// </summary>
    void AirOff()
    {
        AirValve?.CloseWait();
    }

    Stopwatch freezeThawTimer = new();
    void ManageState()
    {
        if (!Connected || Hacs.Stopping) return;
        switch (TargetState)
        {
            case TargetStates.Standby:
                Target = AirTemperature;
                //These are called when Standby() happens. Is there really a need to constantly enforce them?
                //They have been temporarily? commented out to make integration easier.
                //LNOff();
                //AirOff();
                break;
            case TargetStates.Thaw:
                LNOff();
                AirOn();
                if (AirValve == null || !AirValve.IsOpened) Standby();
                break;
            case TargetStates.Freeze:
            case TargetStates.Raise:
                AirOff();
                manageLNLevel();
                break;
            default:
                break;
        }


        if (TargetState == TargetStates.Freeze || TargetState == TargetStates.Raise)
        {
            if (Frozen)
                freezeThawTimer.Reset();
            else if (!freezeThawTimer.IsRunning)
                freezeThawTimer.Restart();
            else if (freezeThawTimer.Elapsed.TotalMinutes > MaximumMinutesToFreeze)
            {
                SlowToFreeze?.Invoke();
                freezeThawTimer.Reset();
            }
        }
        else if (TargetState == TargetStates.Thaw)
        {
            if (!freezeThawTimer.IsRunning)
                freezeThawTimer.Restart();
            else if (freezeThawTimer.Elapsed.TotalMinutes > MaximumMinutesToThaw)
            {
                Announce($"{Name} is taking too long to thaw.", 
                    "Compressed air problem?");
                freezeThawTimer.Reset();
            }
        }
        else
            freezeThawTimer.Reset();
    }

    public Action SlowToFreeze { get; set; }

    public void ThawWait() =>
        ThawWait(AirTemperature - NearAirTemperature);

    public virtual void ThawWait(double temperature)
    {
        if (TargetState != TargetStates.Thaw)
            Thaw(temperature);
        else
            Target = temperature;
        var step = StatusChannel.Default?.Start($"Wait for {Name} > {AirTemperature - NearAirTemperature} °C");
        WaitFor(() => Thawed || Hacs.Stopping, interval: 1000); // timeout handled in ManageState
        step?.End();
    }


    /// <summary>
    /// Sets the coldfinger to Freeze and waits for it to reach the Frozen state.
    /// </summary>
    public virtual void FreezeWait()
    {
        if (TargetState != TargetStates.Raise)
            Freeze();
        var step = StatusChannel.Default?.Start($"Wait for {Name} < {FrozenTemperature + FreezeTrigger} °C");
        WaitFor(() => Frozen || Hacs.Stopping, interval: 1000); // timeout handled in ManageState
        step?.End();
    }

    /// <summary>
    /// Raises the LN level, and once it has reached its peak, waits a few seconds for equilibrium.
    /// Uses:
    ///     * in anticipation of opening chamber to release incondensables
    ///     * before introducing H2 to GR or He to d13C port
    ///     * before closing the destination chamber during transfer
    /// To avoid waste, switch back to Freeze when Raise is no longer necessary.
    /// </summary>
    public virtual void RaiseLN()
    {
        if (Raised && trickling) return;

        var statusChannel = StatusChannel.Default;
        var step = statusChannel?.Start($"Wait for {Name} LN Raised");
        Raise();
        LNOn();     // force LN on immediately; ManageState will turn it off.
        WaitFor(() => Raised || Hacs.Stopping, interval: 1000); // timeout handled in ManageState
        step?.End();
        if (Hacs.Stopping) return;

        if (!trickling)
        {
            step = statusChannel?.Start($"Wait for {Name} LN level to peak");
            WaitFor(() => LNValve.IsClosed || Hacs.Stopping);
            step?.End();
            if (Hacs.Stopping) return;
        }

        step = statusChannel?.Start($"Wait {SecondsToWaitAfterRaised} seconds with LN raised");
        WaitFor(() => false, SecondsToWaitAfterRaised * 1000, 1000);
        step?.End();
    }


    public Coldfinger()
    {
        (this as IStateManager).ManageState = ManageState;
    }

    public override string ToString()
    {
        var name = Name.IsBlank() ? nameof(Coldfinger) : Name;
        StringBuilder sb = new StringBuilder($"{name}: {State}, {Temperature:0.###} °C");
        if (State != States.Standby)
            sb.Append($", Target = {Target:0.###} °C");
        StringBuilder sb2 = new StringBuilder();
        sb2.Append($"\r\n{LevelSensor}");
        sb2.Append($"\r\n{LNValve}");
        sb2.Append($"\r\n{AirValve?.ToString() ?? "(no AirValve)"}");
        sb.Append(Utility.IndentLines(sb2.ToString()));
        return sb.ToString();
    }
}
