using AeonHacs.Utilities;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text;
using static AeonHacs.Notify;

namespace AeonHacs.Components;

/// <summary>
/// Supports HC6 controllers with pre-V.20200411 firmware,
/// which uses a per-channel communications protocol.
/// </summary>
public class HC6ControllerLegacy : HC6Controller,
    HC6ControllerLegacy.IConfig, HC6ControllerLegacy.IDevice
{
    #region static and Translation helpers
    static readonly string PidSetupPrefix = "HC6ControllerLegacy.DeviceType.";

    static bool IsValidDeviceType(char deviceType) =>
        deviceType is >= '0' and <= '3';

    static bool TryGetDeviceType(PidSetup pidSetup, out char deviceType)
    {
        var name = pidSetup?.Name;
        deviceType = name?[^1] ?? '\0';
        return IsValidDeviceType(deviceType) && name == $"{PidSetupPrefix}{deviceType}";
    }

    static PidSetup TryGetPidSetup(char deviceType) =>
        IsValidDeviceType(deviceType) ? 
        Find<PidSetup>($"{PidSetupPrefix}{deviceType}") : null;

    static bool TryGetDeviceType(HC6Heater.IConfig config, out char deviceType) =>
        TryGetDeviceType(
            PidSetup.Find(
                config.PidGain,
                config.PidIntegral,
                config.PidDerivative,
                config.PidPreset),
            out deviceType);

    static HC6ControllerLegacy()
    {
        new PidSetup
        {
            Name = PidSetupPrefix+"0",
            EncodedGain = 753,
            EncodedIntegral = 891,
            EncodedDerivative = 1007,
            EncodedPreset = 687
        };

        new PidSetup
        {
            Name = PidSetupPrefix+"1",
            EncodedGain = 4693,
            EncodedIntegral = 3687,
            EncodedDerivative = 2647,
            EncodedPreset = 6323
        };

        new PidSetup
        {
            Name = PidSetupPrefix+"2",
            EncodedGain = 1689,
            EncodedIntegral = 243,
            EncodedDerivative = 4728,
            EncodedPreset = 709
        };

        new PidSetup
        {
            Name = PidSetupPrefix+"3",
            EncodedGain = 6349,
            EncodedIntegral = 2907,
            EncodedDerivative = 1667,
            EncodedPreset = 8273
        };
    }

    bool GetLegacyThermocoupleType(ThermocoupleType type, out int legacyThermocoupleType)
    { 
        legacyThermocoupleType = type switch
        {
            ThermocoupleType.None => 0,
            ThermocoupleType.K => 1,
            ThermocoupleType.T => 2,
            _ => -1,
        };

        return !ErrorCheck(legacyThermocoupleType == -1,
            $"Unsupported thermocouple type ({type}).");
    }

    bool GetThermocoupleType(int legacyThermocoupleType, out ThermocoupleType type)
    {
        type = legacyThermocoupleType switch
        {
            0 => ThermocoupleType.None,
            1 => ThermocoupleType.K,
            2 => ThermocoupleType.T,
            _ => ThermocoupleType.None,
        };

        return !ErrorCheck(legacyThermocoupleType < 0 || legacyThermocoupleType > 2,
            $"Unsupported legacy thermocouple type ({legacyThermocoupleType}).");
    }

    bool GetLegacyHeaterMode(HC6Heater.Modes mode, out char modeCommand)
    {
        modeCommand = mode switch
        {
            HC6Heater.Modes.Off => '0',
            HC6Heater.Modes.Manual => 'm',
            HC6Heater.Modes.Auto => 'a',
            _ => '\0',
        };
        return !ErrorCheck(modeCommand == '\0',
            $"{Name}: There is no legacy heater mode for ({mode}).");
    }

    bool GetHC6HeaterMode(char value, out HC6Heater.Modes mode)
    {
        mode = value switch
        {
            '0' => HC6Heater.Modes.Off,
            '1' => HC6Heater.Modes.Manual,
            '2' => HC6Heater.Modes.Auto,
            _ => HC6Heater.Modes.Off,
        };

        return !ErrorCheck(value < '0' || value > '2',
            $"{Name}: No HC6Heater mode matches legacy heater mode ({value}).");
    }

    void UpdateColdJunctionTemperature(int thermocoupleChannel, double temperature)
    {
        if (thermocoupleChannel < 8)
            Device.CJ0Temperature = temperature;
        else
            Device.CJ1Temperature = temperature;
    }

    static HC6ErrorCodes DecodeLegacyErrors(int value)
    {
        var legacy = (LegacyErrorCodes)value;
        var decoded = HC6ErrorCodes.None;

        if (legacy.HasFlag(LegacyErrorCodes.AdcOutOfRange)) decoded |= HC6ErrorCodes.AdcOutOfRange;
        if (legacy.HasFlag(LegacyErrorCodes.RxBufferOverflow)) decoded |= HC6ErrorCodes.RxBufferOverflow;
        if (legacy.HasFlag(LegacyErrorCodes.CRC)) decoded |= HC6ErrorCodes.CRC;
        if (legacy.HasFlag(LegacyErrorCodes.BadCommand)) decoded |= HC6ErrorCodes.BadCommand;
        if (legacy.HasFlag(LegacyErrorCodes.BadHeaterChannel)) decoded |= HC6ErrorCodes.BadHeaterChannel;
        if (legacy.HasFlag(LegacyErrorCodes.BadDataLogInterval)) decoded |= HC6ErrorCodes.BadDataLogInterval;
        if (legacy.HasFlag(LegacyErrorCodes.BadSetpoint)) decoded |= HC6ErrorCodes.BadSetpoint;
        if (legacy.HasFlag(LegacyErrorCodes.BadPowerLevel)) decoded |= HC6ErrorCodes.BadPowerLevel;
        if (legacy.HasFlag(LegacyErrorCodes.BadTCChannel)) decoded |= HC6ErrorCodes.BadTCChannel;
        if (legacy.HasFlag(LegacyErrorCodes.BadTCType)) decoded |= HC6ErrorCodes.BadTCType;
        if (legacy.HasFlag(LegacyErrorCodes.BadDeviceType)) decoded |= HC6ErrorCodes.BadConfig;
        if (legacy.HasFlag(LegacyErrorCodes.BadPowerLevelMax)) decoded |= HC6ErrorCodes.BadPowerLevelMax;
        if (legacy.HasFlag(LegacyErrorCodes.AutoCommandedButNoTC)) decoded |= HC6ErrorCodes.AutoCommandedButNoTC;
        if (legacy.HasFlag(LegacyErrorCodes.TemperatureOutOfRange)) decoded |= HC6ErrorCodes.TemperatureOutOfRange;

        return decoded;
    }

    #endregion static and Translation helpers

    #region HacsComponent
    #endregion HacsComponent

    #region Device constants
    #endregion Device constants

    #region Class interface properties and methods

    #region Device interfaces

    public new interface IDevice : HC6Controller.IDevice
    {
        double CJ0Temperature { get; set; }
        double CJ1Temperature { get; set; }
    }

    public new interface IConfig : HC6Controller.IConfig { }

    public new IDevice Device => this;
    public new IConfig Config => this;

    #endregion Device interfaces

    #region IDeviceManager
    #endregion IDeviceManager

    #region Settings
    #endregion Settings


    #region Legacy protocol definitions

    [Flags]
    enum LegacyErrorCodes
    {
        None = 0,
        TemperatureOutOfRange = 1,
        BadHeaterChannel = 2,
        BadCommand = 4,
        BadSetpoint = 8,
        BadPowerLevel = 16,
        BadDataLogInterval = 32,
        RxBufferOverflow = 64,
        BadTCChannel = 128,
        BadTCType = 256,
        BadDeviceType = 512,
        CRC = 1024,
        AdcOutOfRange = 2048,
        BadPowerLevelMax = 4096,
        AutoCommandedButNoTC = 8192,
    }

    const string ControllerDataCommand = "z";

    #endregion Legacy protocol definitions

    #region Retrieved device values

    /// <summary>
    /// Temperature of the cold junction sensor on thermocouple
    /// multiplexer 0. Used by thermocouple channels 0-7.
    /// </summary>
    public double ColdJunction0Temperature => cj0Temperature;
    double IDevice.CJ0Temperature
    {
        get => cj0Temperature;
        set => Ensure(ref cj0Temperature, value);
    }
    double cj0Temperature;

    /// <summary>
    /// Temperature of the cold junction sensor on thermocouple
    /// multiplexer 1. Used by thermocouple channels 8-15.
    /// </summary>
    public double ColdJunction1Temperature => cj1Temperature;
    double IDevice.CJ1Temperature
    {
        get => cj1Temperature;
        set => Ensure(ref cj1Temperature, value);
    }
    double cj1Temperature;

    #endregion Retrieved device values

    public override string ToString()
    {
        var sb = new StringBuilder($"{Name}");
        sb.Append($": {Model} S/N: {SerialNumber} {Firmware}");
        var sb2 = new StringBuilder();
        sb2.Append($"\r\nHch: {SelectedHeater} Tch: {SelectedThermocouple} Adc: {AdcCount}");
        sb2.Append($"\r\nCJ0: {ColdJunction0Temperature:0.00} °C");
        sb2.Append($"\r\nCJ1: {ColdJunction1Temperature:0.00} °C");
        sb.Append(Utility.IndentLines(sb2.ToString()));
        return sb.ToString();
    }

    #endregion Class interface properties and methods

    #region IDeviceManager
    #endregion IDeviceManager

    #region State Management
    // State is invalid if it is inconsistent with the desired Configuration,
    // or if the State doesn't fully and accurately represent the state of
    // the controller.
    #endregion State management

    #region Controller commands
    #endregion Controller commands

    #region Controller interactions

    protected IEnumerator<IManagedDevice> DeviceEnumerator;

    protected IManagedDevice NextDevice()
    {
        if (DeviceEnumerator != null && !DeviceEnumerator.MoveNext())
        {
            DeviceEnumerator.Dispose();
            DeviceEnumerator = null;
        }

        if (DeviceEnumerator == null)
        {
            DeviceEnumerator = Devices.Values.GetEnumerator();
            DeviceEnumerator.MoveNext();
        }

        return DeviceEnumerator.Current;
    }

    protected override void ServiceHC6Controller(HC6Controller c)
    {
        if (Stopping)
            SetServiceValues("");
        else if (c.Device.UpdatesReceived == 0)
            SetServiceValues(ControllerDataCommand, 4);
        else if (ServiceRequest == "{idle}" && Devices.Count > 0)
            DeviceConfigChanged(NextDevice(), new PropertyChangedEventArgs(ServiceRequest));
    }

    protected override void ServiceHC6Heater(HC6Heater heater)
    {
        var channel = ChannelNumber;
        if (heater.Device.UpdatesReceived == 0)
        {
            SetServiceValues($"n{channel} r", 1);
            if (ServiceRequest == "{idle}") ServiceRequest = "";    // any report satisfies the idle request
            return;
        }

        if (ServiceRequest == InitServiceRequest)
        {
            // UpdatesReceived > 0 satisfies the initial service request.
            SetServiceValues("");
            ServiceRequest = "";
            return;
        }

        // Power off takes precedence over all other configuration.
        if (heater.Config.Mode == HC6Heater.Modes.Off && heater.Device.Mode != heater.Config.Mode)
        {
            SetServiceValues($"n{channel} 0 r", 1);
            if (ServiceRequest == "{idle}") ServiceRequest = "";
            return;
        }

        if (heater.Device.ThermocoupleChannel != heater.Config.ThermocoupleChannel)
        {
            // Also remove power before changing the associated thermocouple.
            SetServiceValues($"n{channel} 0 tc{heater.Config.ThermocoupleChannel} r", 1);
            if (ServiceRequest == "{idle}") ServiceRequest = "";
            return;
        }

        // Most changes may be combined into one transaction.
        var commands = new StringBuilder($"n{channel}");
        var maximumPower = Round(heater.Config.MaximumPowerLevel, 2);
        if (Round(heater.Device.MaximumPowerLevel, 2) != maximumPower)
            commands.Append($" x{maximumPower}");

        if (!PidsMatch(heater.Device, heater.Config))
        {
            if (!TryGetDeviceType(heater.Config, out var deviceType))
            {
                ConfigurationError($"{heater.Name}: Configured PID setup is not supported on this Heater Controller.");
                SetServiceValues("");
                return;
            }
            commands.Append($" d{deviceType}");
        }

        var targetSetpoint = Round(heater.Config.Setpoint);
        if (Round(heater.Device.Setpoint) != targetSetpoint)
            commands.Append($" s{targetSetpoint}");

        var powerLevel = Round(heater.Config.PowerLevel, 2);
        if (Round(heater.Device.PowerLevel, 2) != powerLevel)
        {
            if (heater.Config.Mode != HC6Heater.Modes.Manual) heater.Manual();
            commands.Append($" m{powerLevel}");
        }

        if (commands.Length > $"n{channel}".Length)
        {
            commands.Append(" r");
            SetServiceValues(commands.ToString(), 1);
            if (ServiceRequest == "{idle}") ServiceRequest = "";
            return;
        }

        // Change mode from Off only after the device state
        // is otherwise confirmed to match the desired configuration.
        if (heater.Device.Mode != heater.Config.Mode)
        {
            if (!GetLegacyHeaterMode(heater.Config.Mode, out var modeCommand))
            {
                SetServiceValues("");
                return;
            }
            SetServiceValues($"n{channel} {modeCommand} r", 1);
            if (ServiceRequest == "{idle}") ServiceRequest = "";
            return;
        }

        if (ServiceRequest == "{idle}")
        {
            SetServiceValues($"n{channel} r", 1);
            ServiceRequest = "";
        }
        else
        {
            SetServiceValues("");
        }
    }

    protected override void ServiceHC6Thermocouple(HC6Thermocouple thermocouple)
    {
        var channel = ChannelNumber;
        if (thermocouple.Device.UpdatesReceived == 0)
        {
            SetServiceValues($"tn{channel} tr", 1);
            if (ServiceRequest == "{idle}") ServiceRequest = "";    // any report satisfies the idle request
            return;
        }

        if (ServiceRequest == InitServiceRequest)
        {
            // UpdatesReceived > 0 satisfies the initial service request.
            SetServiceValues("");
            ServiceRequest = "";
            return;
        }

        if (thermocouple.Config.Type != thermocouple.Device.Type)
        {
            if (!GetLegacyThermocoupleType(thermocouple.Config.Type, out var legacyType))
            {
                SetServiceValues("");
                return;
            }
            SetServiceValues($"tn{channel} tt{legacyType} tr", 1);
            if (ServiceRequest == "{idle}") ServiceRequest = "";
            return;
        }

        if (ServiceRequest == "{idle}")
        {
            SetServiceValues($"tn{channel} tr", 1);
            ServiceRequest = "";
        }
        else
        {
            SetServiceValues("");
        }
    }

    #region Response validation

    protected override bool ValidateResponse(string response, int which)
    {
        try
        {
            var lines = response.GetLines();
            var values = lines.Length > 0
                ? lines[0].GetValues()
                : [];

            var command = SerialController.CommandMessage;
            if (command[0] == ControllerDataCommand[0])
            {
                switch (which)
                {
                    case 0:
                        if (LengthError(lines, 1, "controller data line"))
                            return false;
                        if (LengthError(values, 4, "value", "on controller data response 1"))
                            return false;
                        Device.Model = values[2];
                        Device.Firmware = values[3];
                        break;
                    case 1:
                        if (LengthError(lines, 1, "controller data line"))
                            return false;
                        if (LengthError(values, 2, "value", "on controller data response 2"))
                            return false;
                        Device.SerialNumber = ParseInt(values[1]);
                        break;
                    case 2:
                        if (LengthError(lines, 1, "controller data line"))
                            return false;
                        if (LengthError(values, 4, "value", "on controller data response 3"))
                            return false;
                        Device.SelectedThermocouple = ParseInt(values[1]);
                        Device.Adc = ParseInt(values[3]);
                        break;
                    case 3:
                        if (LengthError(values, 0, "value", "on controller data response 4"))
                            return false;
                        break;
                    default:
                        return false;
                }
                Device.UpdatesReceived++;
            }
            else if (command.StartsWith("tn", StringComparison.Ordinal))
            {
                if (LengthError(lines, 1, "thermocouple report line"))
                    return false;

                if (LengthError(values, 5, "thermocouple report value"))
                    return false;

                var channel = ParseInt(values[0]);
                if (ErrorCheck(channel < 0 || channel >= ThermocoupleChannels,
                        $"Invalid channel in thermocouple report: {channel}"))
                    return false;
                Device.SelectedThermocouple = channel;

                var key = $"t{channel}";
                if (ErrorCheck(!Devices.ContainsKey(key),
                        $"{Name}: Report received, but nothing is assigned to {key}"))
                    return false;

                var tc = Devices[key] as HC6Thermocouple;
                if (ErrorCheck(tc == null, $"{Name}: The device at {key} isn't a {typeof(HC6Thermocouple)}"))
                    return false;


                if (!GetThermocoupleType(ParseInt(values[1]), out var tcType))
                    return false;

                tc.Device.Type = tcType;
                tc.Device.Temperature = ParseDouble(values[2]);
                UpdateColdJunctionTemperature(channel, ParseDouble(values[3]));

                var errors = DecodeLegacyErrors(ParseInt(values[4]));
                tc.Device.Errors = errors & ThermocoupleErrorFilter;
                Device.Errors = errors & ~ThermocoupleErrorFilter;

                tc.Device.UpdatesReceived++;
            }
            else if (command.StartsWith("n", StringComparison.Ordinal))
            {
                if (LengthError(lines, 1, "heater report line"))
                    return false;

                if (LengthError(values, 11, "heater report value"))
                    return false;

                var channel = ParseInt(values[0]);
                if (ErrorCheck(channel < 0 || channel >= HeaterChannels,
                        $"Invalid channel in heater report: {channel}"))
                    return false;
                Device.SelectedHeater = channel;

                var key = $"h{channel}";
                if (ErrorCheck(!Devices.ContainsKey(key),
                        $"{Name}: Report received, but nothing is assigned to {key}"))
                    return false;

                var heater = Devices[key] as HC6Heater;
                if (ErrorCheck(heater == null, $"{Name}: The device at {key} isn't a {typeof(HC6Heater)}"))
                    return false;

                var pidSetup = TryGetPidSetup(values[1][0]);
                if (pidSetup == null)
                {
                    ConfigurationError($"{Name}: unrecognized device type {values[1]} on {key}.");
                    return false;
                }

                if (!GetHC6HeaterMode(values[2][0], out var mode))
                    return false;

                var tcChannel = ParseInt(values[3]);

                if (!GetThermocoupleType(ParseInt(values[4]), out var tcType))
                    return false;

                heater.Device.Setpoint = ParseInt(values[7]);
                heater.Device.Mode = mode;
                heater.Device.PowerLevel = Math.Round(ParseDouble(values[5]), 2);
                heater.Device.MaximumPowerLevel = Math.Round(ParseDouble(values[6]), 2);
                heater.Device.ThermocoupleChannel = tcChannel;
                heater.Device.PidGain = pidSetup.EncodedGain;
                heater.Device.PidIntegral = pidSetup.EncodedIntegral;
                heater.Device.PidDerivative = pidSetup.EncodedDerivative;
                heater.Device.PidPreset = pidSetup.EncodedPreset;
                var temperature = ParseDouble(values[8]);
                var cjt = ParseDouble(values[9]);
                UpdateColdJunctionTemperature(tcChannel, cjt);

                var errors = DecodeLegacyErrors(ParseInt(values[10]));
                heater.Device.Errors = errors & HeaterErrorFilter;
                Device.Errors = errors & ~(HeaterErrorFilter | ThermocoupleErrorFilter);

                var tcKey = $"t{tcChannel}";
                Devices.TryGetValue(tcKey, out var managedDevice);
                var tc = managedDevice as HC6Thermocouple;

                if (tcType != ThermocoupleType.None)
                {
                    if (ErrorCheck(!Devices.ContainsKey(tcKey),
                            $"{Name}: Heater {key} claims TC Type {tcType} on {tcKey}, but nothing is assigned there."))
                        return false;

                    if (ErrorCheck(tc == null, $"{Name}: The device at {tcKey} isn't a {typeof(HC6Thermocouple)}"))
                        return false;
                }

                if (tc != null)
                {
                    tc.Device.Type = tcType;
                    tc.Device.Temperature = temperature; // Preserve -999 exactly as reported.
                    tc.Device.Errors = errors & ThermocoupleErrorFilter;
                    tc.Device.UpdatesReceived++;
                }

                heater.Device.UpdatesReceived++;
            }
            else
            {
                if (LogEverything)
                    Log?.Record($"Unrecognized response");
                return false;       // unrecognized response
            }

            if (!DataAcquired)
            {
                DataAcquired = Devices.Values
                    .OfType<HacsDevice>()
                    .All(d => d.Device.UpdatesReceived > 0);
            }

            if (LogEverything)
                Log?.Record($"Response successfully decoded");
            return true;
        }
        catch (Exception e)
        {
            Log?.Record($"{e}");
            return false;
        }
    }

    #endregion Response validation

    #endregion Controller interactions
}
