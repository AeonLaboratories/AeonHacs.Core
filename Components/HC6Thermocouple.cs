using AeonHacs.Utilities;
using System.Text;

namespace AeonHacs.Components;

public class HC6Thermocouple : ManagedThermocouple, IHC6Thermocouple,
    HC6Thermocouple.IConfig, HC6Thermocouple.IDevice
{
    #region static

    public static implicit operator double(HC6Thermocouple x)
    { return x?.Temperature ?? 0; }

    #endregion static


    #region Device interfaces

    public new interface IDevice : ManagedThermocouple.IDevice
    {
        HC6ErrorCodes Errors { get; set; }
    }

    public new interface IConfig : ManagedThermocouple.IConfig { }

    public new IDevice Device => this;
    public new IConfig Config => this;

    #endregion Device interfaces


    /// <summary>
    /// Error codes reported by the controller.
    /// </summary>
    public HC6ErrorCodes Errors => errors;
    HC6ErrorCodes IDevice.Errors
    {
        get => errors;
        set => Ensure(ref errors, value);
    }
    HC6ErrorCodes errors;


    public override string ToString()
    {
        StringBuilder sb = new StringBuilder($"{Name}:");
        if (Type != ThermocoupleType.None)
            sb.Append($" {Temperature:0.0} {UnitSymbol} (Type {Type})");

        sb.Append(Utility.IndentLines(ManagedDevice.ManagerString(this)));

        if (Errors != 0)
        {
            var sb2 = new StringBuilder();
            sb2.Append($"\r\nError = {Errors}");
            sb.Append(Utility.IndentLines(sb2.ToString()));
        }
        return sb.ToString();
    }
}
