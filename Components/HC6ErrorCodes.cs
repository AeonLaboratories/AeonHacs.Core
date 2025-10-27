using System;

namespace AeonHacs.Components;

/// <summary>
/// Error codes which may be reported by the HC6 heater controller
/// (updated for Revisions B2 and C)
/// </summary>
[Flags]
public enum HC6ErrorCodes
{
    /// <summary>
    /// No error, status is ok
    /// </summary>
    None = 0,
    /// <summary>
    /// ADC out of range (analog-to-digital converter error)
    /// </summary>
    AdcOutOfRange = 1,
    /// <summary>
    /// RS232 input buffer overflow; commands are too frequent
    /// </summary>
    RxBufferOverflow = 2,
    /// <summary>
    /// RS232 CRC error (cyclical redundancy check failed)
    /// </summary>
    CRC = 4,
    /// <summary>
    /// Unrecognized command
    /// </summary>
    BadCommand = 8,
    /// <summary>
    /// Invalid heater channel
    /// </summary>
    BadHeaterChannel = 16,
    /// <summary>
    /// Datalogging time interval out of range
    /// </summary>
    BadDataLogInterval = 32,
    /// <summary>
    /// Setpoint (SP, same units as PV) out of range
    /// </summary>
    BadSetpoint = 64,
    /// <summary>
    /// Control output (CO) power level out of range
    /// </summary>
    BadPowerLevel = 128,
    /// <summary>
    /// Invalid thermocouple channel selected
    /// </summary>
    BadTCChannel = 256,
    /// <summary>
    /// Invalid thermocouple type specified
    /// </summary>
    BadTCType = 512,
    /// <summary>
    /// Invalid configuration or configuration parameter
    /// </summary>
    BadConfig = 1024,
    /// <summary>
    /// Control output (CO) limit out of range.
    /// </summary>
    BadPowerLevelMax = 2048,
    /// <summary>
    /// Auto mode commanded on a heater with no thermocouple assigned.
    /// </summary>
    AutoCommandedButNoTC = 4096,
    /// <summary>
    /// Temperature (PV) out of range
    /// </summary>
    TemperatureOutOfRange = 8182
}
