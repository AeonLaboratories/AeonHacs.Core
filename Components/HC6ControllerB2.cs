using AeonHacs;
using Newtonsoft.Json;
using System;
using System.Text;
using AeonHacs.Utilities;
using static AeonHacs.Utilities.Utility;

namespace AeonHacs.Components
{
    public class HC6ControllerB2 : HC6Controller, IHC6ControllerB2,
        HC6ControllerB2.IConfig, HC6ControllerB2.IDevice
    {
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
            bool InterferenceSuppressionEnabled { get; set; }
        }
        public new interface IConfig : HC6Controller.IConfig
        {
            bool InterferenceSuppressionEnabled { get; }
        }

        public new IDevice Device => this;
        public new IConfig Config => this;

        #endregion Device interfaces

        #region IDeviceManager
        #endregion IDeviceManager

        #region Settings
        /// <summary>
        /// If true, temperature updates for a thermocouple are suppressed
        /// whenever its associated heater is receiving power.
        /// </summary>
        public bool InterferenceSuppressionEnabled
        {
            get => interferenceSuppressionEnabled;
            set => Ensure(ref TargetInterferenceSuppressionEnabled, value, NotifyConfigChanged, nameof(TargetInterferenceSuppressionEnabled));
        }

        [JsonProperty("InterferenceSuppressionEnabled")]
        bool TargetInterferenceSuppressionEnabled;
        bool IConfig.InterferenceSuppressionEnabled => TargetInterferenceSuppressionEnabled;

        bool IDevice.InterferenceSuppressionEnabled
        {
            get => interferenceSuppressionEnabled;
            set => Ensure(ref interferenceSuppressionEnabled, value);
        }
        bool interferenceSuppressionEnabled;

        #endregion Settings

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
            var sb = new StringBuilder(base.ToString());
            sb.Append($": {Model} S/N: {SerialNumber} {Firmware}");
            var sb2 = new StringBuilder();
            sb2.Append($"\r\nHch: {SelectedHeater} Tch: {SelectedThermocouple} Adc: {AdcCount}");
            sb2.Append($"\r\nCJ0: {ColdJunction0Temperature:0.00} °C");
            sb2.Append($"\r\nCJ1: {ColdJunction1Temperature:0.00} °C");
            sb2.Append($"\r\nReading rate: {ReadingRate:0}/s");
            if (InterferenceSuppressionEnabled)
                sb2.Append($"\r\n(Interference suppression enabled)");
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
        protected override bool StateInvalid =>
            base.StateInvalid ||
            interferenceSuppressionEnabled != TargetInterferenceSuppressionEnabled;
        #endregion State management

        #region Controller commands
        #endregion Controller commands


        #region Controller interactions

        protected override void ServiceHC6Controller(HC6Controller c)
        {
            if (!(c is HC6ControllerB2 b2))
                base.ServiceHC6Controller(c);
            else if (b2.Device.UpdatesReceived == 0)
                SetServiceValues(ControllerDataCommand, 1);
            else if (b2.Device.InterferenceSuppressionEnabled != b2.Config.InterferenceSuppressionEnabled)
                SetServiceValues($"i {ControllerDataCommand}", 1);
            else
                SetServiceValues(DataDumpCommand, 1);
        }

        protected override bool ValidateResponse(string response, int which)
        {
            try
            {
                var lines = response.GetLines();
                if (lines.Length == 0) return false;
                var values = lines[0].GetValues();
                var n = values.Length;

                if (SerialController.CommandMessage[0] == DataDumpCommand[0])
                {
                    if (LengthError(lines, 4, "status report line"))
                        return false;

                    if (LengthError(values, ThermocoupleChannels, "thermocouple temperature"))
                        return false;

                    for (int i = 0; i < n; ++i)
                    {
                        var key = $"t{i}";
                        if (Devices.ContainsKey(key) && Devices[key] is HC6Thermocouple t)
                        {
                            t.Device.Temperature = double.Parse(values[i]);
                            t.Device.UpdatesReceived++;
                        }
                    }

                    values = lines[1].GetValues();
                    n = values.Length;

                    if (LengthError(values, 2 * HeaterChannels, "heater status value"))
                        return false;

                    int j = 0;
                    for (int i = 0; i < n / 2; i++)
                    {
                        var key = $"h{i}";
                        var c = values[j++][0];
                        var pl = Math.Round(double.Parse(values[j++]), 2);
                        if (Devices.ContainsKey(key) && Devices[key] is HC6Heater h)
                        {
                            if (ErrorCheck(!GetValidHeaterMode(c, out HC6Heater.Modes mode),
                                    $"Unsupported heater mode ({c}) on channel {key}"))
                                return false;

                            h.Device.Mode = mode;
                            h.Device.PowerLevel = pl;
                            h.Device.UpdatesReceived++;
                        }
                    }

                    values = lines[2].GetValues();
                    n = values.Length;

                    if (LengthError(values, 2, "cold junction temperature"))
                        return false;

                    // Update this controller's data
                    Device.CJ0Temperature = double.Parse(values[0]);
                    Device.CJ1Temperature = double.Parse(values[1]);

                    values = lines[3].GetValues();
                    n = values.Length;

                    if (LengthError(values, 1, "reading counter value"))
                        return false;

                    // Update this controller's data
                    Device.ReadingCounter = int.Parse(values[0]);
                    Device.UpdatesReceived++;
                }
                else if (SerialController.CommandMessage[0] == ControllerDataCommand[0])       // Controller data
                {
                    if (LengthError(lines, 4, "controller data line"))
                        return false;

                    if (LengthError(values, 4, "value", "on controller data line 1"))
                        return false;

                    Device.Model = values[2];
                    Device.Firmware = values[3];

                    values = lines[1].GetValues();
                    n = values.Length;

                    if (LengthError(values, 2, "value", "on controller data line 2"))
                        return false;

                    Device.SerialNumber = int.Parse(values[1]);

                    values = lines[2].GetValues();
                    n = values.Length;
                    if (LengthError(values, 4, "value", "on controller data line 3"))
                        return false;

                    Device.SelectedHeater = int.Parse(values[1]);
                    Device.SelectedThermocouple = int.Parse(values[3]);

                    values = lines[3].GetValues();
                    n = values.Length;
                    if (LengthError(values, 2, "value", "on controller data line 4"))
                        return false;


                    var s = values[1];
                    Device.InterferenceSuppressionEnabled = s[s.Length - 1] == '!';
                    if (Device.InterferenceSuppressionEnabled)
                        s = s.Substring(0, s.Length - 1);
                    Device.Adc = int.Parse(s);
                    Device.UpdatesReceived++;
                }
                else
                    return base.ValidateResponse(response, which);

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

        #endregion Controller interactions
    }
}