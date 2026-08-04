using System;
using static AeonHacs.Components.ActuatorController;

namespace AeonHacs.Components;

public class ActuatorControllerLegacy : ActuatorController
{
    protected override int ControllerDataResponses => 3;
    protected override int ControllerDataLines => 1;

    /// <summary>
    /// Error-bit assignments used by legacy controllers,
    /// those with firmware before V.20200411.
    /// </summary>
    [Flags]
    enum LegacyControllerErrorCodes
    {
        /// <summary>No error; status is normal.</summary>
        None = 0,
        /// <summary>The analog-to-digital converter reported an out-of-range value.</summary>
        AdcOutOfRange = 1,
        /// <summary>The RS232 receive buffer overflowed; commands are too frequent.</summary>
        RxBufferOverflow = 2,
        /// <summary>An RS232 cyclical redundancy check failed (CRC error).</summary>
        CRC = 4,
        /// <summary>An unrecognized command was received.</summary>
        BadCommand = 8,
        /// <summary>Timer 1 still running when Timer 0 reset</summary>
        Timer1Overrun = 16,
        /// <summary>An invalid device channel was specified.</summary>
        BadChannel = 32,
        /// <summary>The datalogging interval was outside its allowed range.</summary>
        BadDataLogInterval = 64,
        /// <summary>Identical to BadChannel; invalid servo channel</summary>
        ServoOutOfRange = 128,
        /// <summary>Servo control pulse width (CPW) out of range</summary>
        CpwOutOfRange = 256,
        /// <summary>The time-limit setting was outside its allowed range.</summary>
        TimeLimitOutOfRange = 512,
        /// <summary>Both actuator limit switches were engaged.</summary>
        BothLimitSwitchesEngaged = 1024,
        /// <summary>Low servo power supply voltage.</summary>
        LowPower = 2048,
        /// <summary>The current-limit setting was outside its allowed range.</summary>
        CurrentLimitOutOfRange = 4096,
        /// <summary>An invalid limit switch configuration was requested.</summary>
        BadStopLimit = 8192,
    }

    /// <summary>
    /// Translates an actuator-controller error value from the legacy bit layout
    /// to the current <see cref="ErrorCodes"/> layout.
    /// </summary>
    /// <param name="errors">The legacy controller error value.</param>
    protected override ErrorCodes EncodeErrors(int errors)
    {
        var legacy = (LegacyControllerErrorCodes)errors;
        var decoded = ErrorCodes.None;
        if (legacy.HasFlag(LegacyControllerErrorCodes.AdcOutOfRange)) decoded |= ErrorCodes.AdcOutOfRange;
        if (legacy.HasFlag(LegacyControllerErrorCodes.RxBufferOverflow)) decoded |= ErrorCodes.RxBufferOverflow;
        if (legacy.HasFlag(LegacyControllerErrorCodes.CRC)) decoded |= ErrorCodes.CRC;
        if (legacy.HasFlag(LegacyControllerErrorCodes.BadCommand)) decoded |= ErrorCodes.BadCommand;
        // Ignore this obsolete flag
        //if (legacy.HasFlag(LegacyControllerErrorCodes.Timer1Overrun)) decoded |= ErrorCodes.Timer1Overrun;
        if (legacy.HasFlag(LegacyControllerErrorCodes.BadChannel)) decoded |= ErrorCodes.BadChannel;
        if (legacy.HasFlag(LegacyControllerErrorCodes.BadDataLogInterval)) decoded |= ErrorCodes.BadDataLogInterval;
        // ServoOutOfRange predated BadChannel, but meant the same thing.
        if (legacy.HasFlag(LegacyControllerErrorCodes.ServoOutOfRange)) decoded |= ErrorCodes.BadChannel;
        if (legacy.HasFlag(LegacyControllerErrorCodes.CpwOutOfRange)) decoded |= ErrorCodes.CpwOutOfRange;
        if (legacy.HasFlag(LegacyControllerErrorCodes.TimeLimitOutOfRange)) decoded |= ErrorCodes.TimeLimitOutOfRange;
        if (legacy.HasFlag(LegacyControllerErrorCodes.BothLimitSwitchesEngaged)) decoded |= ErrorCodes.BothLimitSwitchesEngaged;
        if (legacy.HasFlag(LegacyControllerErrorCodes.LowPower)) decoded |= ErrorCodes.LowPower;
        if (legacy.HasFlag(LegacyControllerErrorCodes.CurrentLimitOutOfRange)) decoded |= ErrorCodes.CurrentLimitOutOfRange;
        if (legacy.HasFlag(LegacyControllerErrorCodes.BadStopLimit)) decoded |= ErrorCodes.BadStopLimit;
        return decoded;
    }
}
