using Newtonsoft.Json;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

namespace AeonHacs.Components;

public class Protocol : HacsComponent, IProtocol
{
    #region HacsComponent

    #endregion HacsComponent

    [JsonProperty]
    public InletPortType PortType
    {
        get => portType;
        set => Ensure(ref portType, value);
    }
    InletPortType portType;

    [JsonProperty]
    public List<ProtocolStep> Steps
    {
        get => steps;
        set => Ensure(ref steps, value);
    }
    List<ProtocolStep> steps;

    [JsonProperty]
    public List<string> CheckList
    {
        get => checkList;
        set => Ensure(ref checkList, value);
    }
    List<string> checkList;

    public Protocol() { }

    public Protocol(string name) : this(name, InletPortType.Combustion) { }

    public Protocol(string name, InletPortType source)
    {
        Name = name;
        PortType = source;
        Steps = new List<ProtocolStep>();
}

public Protocol Clone()
    {
        Protocol ps = new Protocol(Name, PortType);
        Steps.ForEach(pss => ps.Steps.Add(pss.Clone()));
        return ps;
    }

    public override string ToString() => Name;
}

// Process steps aren't NamedObjects: they aren't findable by name because
// there are often duplicates. But they have similar properties.
public class ProtocolStep : BindableObject, IProtocolStep
{
    [DisplayName]
    [JsonProperty]
    public virtual string Name
    {
        get => name;
        set => Ensure(ref name, value);
    }
    string name = "Protocol Status";

    [Description]
    [JsonProperty]
    public virtual string Description
    {
        get => description;
        set => Ensure(ref description, value);
    }
    string description = "";

    public ProtocolStep() { }

    public ProtocolStep(string name)
    {
        Name = name;
    }
    public ProtocolStep(string name, string description)
    {
        Name = name;
        Description = description;
    }

    public virtual ProtocolStep Clone() => new ProtocolStep(Name, Description);

    public override string ToString()
    {
        if (Name.EndsWith("_"))
            return Name.Substring(0, Name.Length - 1);
        return Name;
    }
}

public abstract class ParameterizedStep : ProtocolStep { }

[Description("Combustion step using local parameters. (Deprecated. Use named Parameters instead.")]
public class CombustionStep : ParameterizedStep, ICombustionStep
{
    [JsonProperty]
    public int Temperature { get; set; }
    [JsonProperty]
    public int Minutes { get; set; }
    [JsonProperty]
    public bool AdmitO2 { get; set; }

    [JsonProperty]
    public bool WaitForSetpoint { get; set; }

    public CombustionStep() : this(25, 0, false, false, false) { }

    public CombustionStep(int temperature, int minutes, bool admitO2, bool openLine, bool waitForSetpoint)
        : this("Combust", temperature, minutes, admitO2, waitForSetpoint) { }

    public CombustionStep(string name, int temperature, int minutes, bool admitO2, bool waitForSetpoint)
    {
        Name = name;
        Temperature = temperature;
        Minutes = minutes;
        AdmitO2 = admitO2;
        WaitForSetpoint = waitForSetpoint;
    }

    public override ProtocolStep Clone() =>
        new CombustionStep(Name, Temperature, Minutes, AdmitO2, WaitForSetpoint);

    public override string ToString()
    {
        var sb = new StringBuilder($"{Name} at {Temperature} for {Minutes} minutes.");
        if (AdmitO2)
            sb.Append(" Admit O2.");
        if (WaitForSetpoint)
            sb.Append(" Wait For Setpoint.");
        return sb.ToString();
    }
}

[Description("Wait step using local parameter. (Deprecated. Use 'WaitTimerMinutes' Parameter and 'Wait For Timer' instead")]
public class WaitMinutesStep : ParameterizedStep, IWaitMinutesStep
{
    [JsonProperty]
    public int Minutes { get; set; }

    public WaitMinutesStep() : this(0) { }

    public WaitMinutesStep(int minutes) : this("Wait Minutes", minutes) { }

    public WaitMinutesStep(string name, int minutes)
    {
        Name = name;
        Minutes = minutes;
    }

    public override ProtocolStep Clone() => new WaitMinutesStep(Name, Minutes);

    public override string ToString() => $"Wait for {Minutes} minutes.";
}

[Description("Assign a value to a named parameter.")]
public class ParameterStep : ParameterizedStep, IParameterStep
{
    public override string Name
    {
        get => base.Name;
        set
        {
            base.Name = value;
            Parameter.ParameterName = value;
        }
    }
    public override string Description
    {
        get => base.Description;
        set
        {
            base.Description = value;
            Parameter.Description = value;
        }
    }

    [JsonProperty]
    public virtual double Value
    {
        get => _value;
        set
        {
            if (Ensure(ref _value, value))
                Parameter.Value = value;
        }
    }
    double _value;

    public bool ShouldSerializeParameter() => false;
    [Browsable(false)]
    public Parameter Parameter { get; protected set; } = new Parameter();

    public ParameterStep() { }
    public ParameterStep(Parameter parameter)
    {
        Name = parameter.ParameterName;
        Description = parameter.Description;
        Value = parameter.Value;
    }

    public override ProtocolStep Clone() => new ParameterStep(Parameter);

    public override string ToString()
    {
        var basic = $"{Name} = {Value}";
        var desc = !Description.IsBlank() ? Description :
            NamedObject.FirstOrDefault<CegsPreferences>()?
            .DefaultParameters?.Find(x => x.ParameterName == Name)?
            .Description;
        return desc.IsBlank() ? basic : $"{basic}\r\n{desc}";
    }
}

