namespace AeonHacs.Components;

/// <summary>
/// Supports Switchbank controllers with pre-V.20200411 firmware,
/// which sends two responses to the ControllerDataCommand.
/// </summary>
public class SwitchBankLegacy : SwitchBank
{
    /// <summary>
    /// Sets the number of responses to expect from the controller 
    /// after sending a ControllerDataCommand.
    /// </summary>
    protected override int ControllerDataResponses => 2;
}
