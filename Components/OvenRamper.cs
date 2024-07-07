using Newtonsoft.Json;

namespace AeonHacs.Components;
public class OvenRamper : StateManager
{
    SetpointRamp ramp = new();

    [JsonProperty]
    public double RateDegreesPerMinute
    {
        get => ramp.Rate;
        set => ramp.Rate = value;
    }

    public double Setpoint
    {
        get => ramp.Setpoint;
        set => ramp.Setpoint = value;
    }

    public IOven Oven
    {
        get => oven;
        set
        {
            if (Ensure(ref oven, value))
            {
                ramp.Device = value;
                ramp.GetProcessVariable = () => oven.Temperature;
            }
        }
    }
    IOven oven;

    public bool Enabled
    {
        get => enabled;
        set => Ensure(ref enabled, value);
    }
    bool enabled;

    public void Enable() => Enabled = true;

    public void Disable() => Enabled = false;

    protected virtual void ManageSetpoint()
    {
        if (!(Enabled && Oven.IsOn)) return;
        if (oven.Setpoint != ramp.WorkingSetpoint)
            oven.Setpoint = ramp.WorkingSetpoint;
    }

    public OvenRamper()
    {
        (this as IStateManager).ManageState = ManageSetpoint;
    }
}
