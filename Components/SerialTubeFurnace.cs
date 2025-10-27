using AeonHacs.Utilities;
using Newtonsoft.Json;
using System;
using System.ComponentModel;

namespace AeonHacs.Components;

public class SerialTubeFurnace : TubeFurnace, ISerialTubeFurnace,
    SerialTubeFurnace.IDevice, SerialTubeFurnace.IConfig
{
    #region HacsComponent

    #endregion HacsComponent

    #region Class interface properties and methods

    #region Device interfaces

    public new interface IDevice : TubeFurnace.IDevice { }
    public new interface IConfig : TubeFurnace.IConfig { }

    public new IDevice Device => this;
    public new IConfig Config => this;

    #endregion Device interfaces

    #region Settings

    [JsonProperty]
    public virtual SerialController SerialController
    {
        get => serialController;
        set
        {
            serialController = value;
            if (serialController != null)
            {
                serialController.SelectServiceHandler = SelectService;
                serialController.ResponseProcessor = ValidateResponse;
                serialController.LostConnection -= OnControllerLost;
                serialController.LostConnection += OnControllerLost;
            }
            NotifyPropertyChanged();
        }
    }
    SerialController serialController;

    #endregion Settings

    /// <summary>
    /// Turn the furnace on.
    /// </summary>
    /// <returns></returns>
    public override bool TurnOn()
    {
        if (base.TurnOn())
        {
            SerialController.Hurry = true;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Turn the furnace off.
    /// </summary>
    /// <returns></returns>
    public override bool TurnOff()
    {
        if (base.TurnOff())
        {
            SerialController.Hurry = true;
            return true;
        }
        return false;
    }

    #region State management

    public virtual bool Ready => SerialController.Ready;

    #endregion State management

    public override void OnConfigChanged(object sender, PropertyChangedEventArgs e)
    {
        var propertyName = e?.PropertyName;
        if (propertyName == nameof(TargetSetpoint))
        {
            if (SerialController != null)
                SerialController.Hurry = true;
        }
        NotifyConfigChanged(e.PropertyName);
    }

    #endregion Class interface properties and methods

    #region Controller interactions

    protected virtual bool LogEverything => SerialController?.LogEverything ?? false;
    protected virtual LogFile Log => SerialController?.Log;

    // to be overridden by derived class
    /// <summary>
    /// The SerialController invokes this method to obtain the next
    /// SerialController.Command.
    /// The Command contains a string message (the "command"), the
    /// number of responses to expect in return, and whether to
    /// "Hurry". Hurry tells the controller to check back here
    /// for another command as soon as the expected responses
    /// have been received and validated. Otherwise, the controller
    /// will check again after a timeout period.
    /// </summary>
    /// <returns></returns>
    protected virtual SerialController.Command SelectService()
    {
        return new SerialController.Command("", 0, false);
    }
    protected virtual void OnControllerLost(object sender, EventArgs e) { }

    // to be overridden by derived class
    /// <summary>
    /// Accepts a response string from the SerialController
    /// and returns whether it is a valid response or not.
    /// </summary>
    /// <param name="response">The SerialController's response.</param>
    /// <param name="which">When multiple responses are expected/returned, which one.</param>
    /// <returns>true if the response is valid</returns>
    protected virtual bool ValidateResponse(string response, int which)
    {
        return true;
    }

    #endregion Controller interactions

}