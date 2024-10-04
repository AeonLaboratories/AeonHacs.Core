using Newtonsoft.Json;

namespace AeonHacs.Components;
public class Parameter : BindableObject
{
    [JsonProperty]
    public string ParameterName
    {
        get => parameterName;
        set => Ensure(ref parameterName, value);
    }
    string parameterName;

    [JsonProperty]
    public double Value
    {
        get => _value;
        set => Ensure(ref _value, value);
    }
    double _value;

    [JsonProperty]
    public string Description
    {
        get => description;
        set => Ensure(ref description, value);
    }
    string description;

    public Parameter Clone()
    {
        return new Parameter()
        {
            ParameterName = ParameterName,
            Value = Value,
            Description = Description
        };
    }

    public override string ToString() => $"{ParameterName} = {Value}";
}