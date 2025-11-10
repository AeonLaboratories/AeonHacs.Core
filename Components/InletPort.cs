using AeonHacs.Utilities;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

namespace AeonHacs.Components;

public class InletPort : LinePort, IInletPort
{
    #region HacsComponent

    protected override void Connect()
    {
        base.Connect();
        QuartzFurnace = Find<IHeater>(quartzFurnaceName);
        SampleFurnace = Find<IOven>(sampleFurnaceName);
        //PathToFirstTrap = FindAll<IValve>(pathToVTTValveNames);
    }

    #endregion HacsComponent

    [JsonProperty]
    public virtual List<InletPortType> SupportedPortTypes
    {
        get => supportedPortTypes;
        set => Ensure(ref supportedPortTypes, value);
    }
    List<InletPortType> supportedPortTypes;

    [JsonProperty]
    [StandardValuesSource(nameof(SupportedPortTypes))]
    public virtual InletPortType PortType
    {
        get => portType;
        set => Ensure(ref portType, value);
    }
    InletPortType portType;

    public override string Contents => Sample?.LabId ?? "<none>";

    [JsonProperty("QuartzFurnace")]
    string QuartzFurnaceName { get => QuartzFurnace?.Name; set => quartzFurnaceName = value; }
    string quartzFurnaceName;
    public IHeater QuartzFurnace
    {
        get => quartzFurnace;
        set => Ensure(ref quartzFurnace, value);
    }
    IHeater quartzFurnace;

    [JsonProperty("SampleFurnace")]
    string SampleFurnaceName { get => SampleFurnace?.Name; set => sampleFurnaceName = value; }
    string sampleFurnaceName;
    public IOven SampleFurnace
    {
        get => sampleFurnace;
        set => Ensure(ref sampleFurnace, value);
    }
    IOven sampleFurnace;

    public virtual void TurnOffFurnaces()
    {
        QuartzFurnace?.TurnOff();
        SampleFurnace?.TurnOff();
    }

    public override string ToString()
    {
        var sb = new StringBuilder($"{Name}: {State}");
        if (Sample == null)
            sb.Append(" (no sample)");
        else
            sb.Append($", {Sample.LabId}, {Sample.Grams:0.000000} g");
        var sb2 = new StringBuilder();
        if (SampleFurnace != null) sb2.Append($"\r\n{SampleFurnace}");
        if (QuartzFurnace != null) sb2.Append($"\r\n{QuartzFurnace}");
        return sb.Append(Utility.IndentLines(sb2.ToString())).ToString();
    }
}
