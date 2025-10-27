using AeonHacs.Utilities;
using Newtonsoft.Json;
using System;
using System.Text;

namespace AeonHacs.Components;

public class RS485Controller : SerialDeviceManager
{
    #region HacsComponent
    #endregion HacsComponent

    #region Class interface properties and methods

    #region Device interfaces
    #endregion Device interfaces

    #region IDeviceManager
    #endregion IDeviceManager

    #region Settings

    [JsonProperty]
    public int Channels
    {
        get => channels;
        set => Ensure(ref channels, value);
    }
    int channels;
    #endregion Settings

    #region Retrieved device values

    /// <summary>
    /// The channel number of the currently selected switch.
    /// </summary>
    public int SelectedChannel { get; set; }

    #endregion Retrieved device values

    public override string ToString()
    {
        var sb = new StringBuilder(base.ToString());
        if (Devices.Count > 0)
        {
            var sb2 = new StringBuilder();
            foreach (var d in Devices.Values)
					sb2.Append($"\r\n{d}".Replace($"\r\n   ({Name} ", "("));
            sb.Append(Utility.IndentLines(sb2.ToString()));
        }
        return sb.ToString();
    }

    #endregion Class interface properties and methods

    #region IDeviceManager

    protected override IManagedDevice FindSupportedDevice(string name)
    {
        if (Find<IManagedDevice>(name) is IManagedDevice d) return d;
        return null;
    }

    #endregion IDeviceManager

    #region State Management
    #endregion State Management

    #region Controller commands
    #endregion Controller commands

    #region Controller interactions

    protected override bool StateInvalid => false;
    protected override void SelectDeviceService()
    {
        if (LogEverything)
            Log?.Record($"SelectDeviceService: Device = {ServiceDevice?.Name}, Request = \"{ServiceRequest}\"");
        SetServiceValues("");       // default to nothing needed

        if (ServiceDevice == default) return;

        var serviceValues = ServiceDevice.ServiceValues(ServiceRequest);
        SetServiceValues(serviceValues.command, serviceValues.responsesExpected);

        if (LogEverything)
            Log.Record($"ServiceDevice = {ServiceDevice?.Name}, ServiceCommand = \"{ServiceCommand}\", ResponsesExpected = {ResponsesExpected}");
    }

    protected override bool ValidateResponse(string response, int which)
    {
        try
        {
            if (ServiceDevice == null)
            {
                if (LogEverything)
                    Log.Record($"Unexpected response; no service device selected.");
            }
            else
            {
                ServiceDevice.Device.UpdatesReceived++;
                if (ServiceDevice.ValidateResponse(ServiceRequest, response, which))
                {
                    if (LogEverything)
                        Log.Record($"Response successfully decoded");
                }
                else
                {
                    if (LogEverything)
                        Log.Record($"Invalid response");
                    return false;
                }
            }
            return true;
        }
        catch (Exception e)
        {
            //if (LogEverything)
            Log.Record($"{e}");
            return false;
        }
    }

    #endregion Controller interactions

}
