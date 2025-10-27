using AeonHacs.Utilities;
using Newtonsoft.Json;
using System.Text;

namespace AeonHacs.Components;

public class TubeFurnace : Oven, ITubeFurnace,
    TubeFurnace.IDevice, TubeFurnace.IConfig
{
    #region HacsComponent

    [HacsConnect]
    protected virtual void Connect()
    {
        if (SetpointRamp != null)
        {
            SetpointRamp.Device = this;
            SetpointRamp.GetProcessVariable = () => Temperature;
        }
    }

    #endregion HacsComponent

    #region Class interface properties and methods

    #region Device interfaces

    public new interface IDevice : Oven.IDevice { }
    public new interface IConfig : Oven.IConfig { }

    public new IDevice Device => this;
    public new IConfig Config => this;

    #endregion Device interfaces

    #region Settings

    /// <summary>
    /// Set to null if Setpoint ramping is not used.
    /// </summary>
    [JsonProperty]
    public SetpointRamp SetpointRamp
    {
        get => setpointRamp;
        set => Ensure(ref setpointRamp, value);
    }
    SetpointRamp setpointRamp;

    #endregion Settings

    public virtual double TimeLimit
    {
        get => timeLimit;
        set => Ensure(ref timeLimit, value);
    }
    double timeLimit;
    public virtual bool UseTimeLimit
    {
        get => useTimeLimit;
        set => Ensure(ref useTimeLimit, value);
    }
    bool useTimeLimit;

    /// <summary>
    /// The "SetpointRamp" working setpoint. If no SetpointRamp
    /// has been defined, this is the same as Setpoint.
    /// </summary>
    public double RampingSetpoint => SetpointRamp?.WorkingSetpoint ?? Setpoint;

    public virtual double MinutesInState => MillisecondsInState / 60000.0;
    public virtual double MinutesOn => IsOn ? MinutesInState : 0;
    public virtual double MinutesOff => !IsOn ? MinutesInState : 0;

    /// <summary>
    /// Turn the furnace on.
    /// </summary>
    /// <returns></returns>
    public new virtual bool TurnOn() => base.TurnOn();

    /// <summary>
    /// Turn the furnace off.
    /// </summary>
    /// <returns></returns>
    public new virtual bool TurnOff() => base.TurnOff();

    /// <summary>
    /// Set the furnace temperature and turn it on.
    /// Later, if the furnace is still on when the specified time
    /// elapses, it is automatically turned off.
    /// </summary>
    /// <param name="setpoint">Desired furnace temperature (°C)</param>
    /// <param name="minutes">Maximum number of minutes to remain on</param>
    public virtual void TurnOn(double setpoint, double minutes)
    {
        TimeLimit = minutes;
        UseTimeLimit = true;
        TurnOn(setpoint);
    }

    #region State management

    #endregion State management

    public override string ToString()
    {
        StringBuilder sb = new StringBuilder($"{Name}:");
        return sb.ToString();
    }

    #endregion Class interface properties and methods

    protected Stopwatch StateStopwatch = new Stopwatch();

    #region Controller interactions

    #endregion Controller interactions

}