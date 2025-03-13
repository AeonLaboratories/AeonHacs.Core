using AeonHacs.Utilities;
using System;
using System.Text;
using static AeonHacs.Utilities.Utility;

namespace AeonHacs.Components
{
    /// <summary>
    /// This class currently supports HC6-C.
    /// </summary>
    public class HC6Controller : SerialDeviceManager, IHC6Controller,
        HC6Controller.IConfig, HC6Controller.IDevice
    {
        #region HacsComponent
        #endregion HacsComponent

        #region Device constants
        static public int HeaterChannels = 6;
        static public int ThermocoupleChannels = 16;

        static protected HC6ErrorCodes HeaterErrorFilter =
            HC6ErrorCodes.AutoCommandedButNoTC;
        static protected HC6ErrorCodes ThermocoupleErrorFilter =
            HC6ErrorCodes.AdcOutOfRange |
            HC6ErrorCodes.TemperatureOutOfRange;

        #endregion Device constants

        #region Class interface properties and methods

        #region Device interfaces

        public new interface IDevice : SerialDeviceManager.IDevice
        {
            string Model { get; set; }
            string Firmware { get; set; }
            int SerialNumber { get; set; }
            int SelectedHeater { get; set; }
            int SelectedThermocouple { get; set; }
            int Adc { get; set; }
            int ReadingCounter { get; set; }
            double CJTemperature { get; set; }
//            bool InterferenceSuppressionEnabled { get; set; }
            HC6ErrorCodes Errors { get; set; }
        }
        public new interface IConfig : SerialDeviceManager.IConfig
        {
 //           bool InterferenceSuppressionEnabled { get; }
        }

        public new IDevice Device => this;
        public new IConfig Config => this;

        #endregion Device interfaces

        #region IDeviceManager

        public override bool IsSupported(IManagedDevice d, string key)
        {
            if (
                IsValidKey(key, "h", HeaterChannels - 1) && d is HC6Heater ||
                IsValidKey(key, "t", ThermocoupleChannels - 1) && d is HC6Thermocouple
               )
                return true;

            Log?.Record($"Connect: {d.Name}'s key \"{key}\" and type ({d.GetType()}) are not supported together." +
                $"\r\n\tOne of them is invalid or they are not compatible.");
            return false;
        }

        #endregion IDeviceManager

        #region Settings
        /// <summary>
        /// If true, temperature updates for a thermocouple are suppressed
        /// whenever its associated heater is receiving power.
        /// </summary>
        //public bool InterferenceSuppressionEnabled
        //{
        //    get => interferenceSuppressionEnabled;
        //    set => Ensure(ref TargetInterferenceSuppressionEnabled, value, NotifyConfigChanged, nameof(TargetInterferenceSuppressionEnabled));
        //}

        //[JsonProperty("InterferenceSuppressionEnabled")]
        //bool TargetInterferenceSuppressionEnabled;
        //bool IConfig.InterferenceSuppressionEnabled => TargetInterferenceSuppressionEnabled;

        //bool IDevice.InterferenceSuppressionEnabled
        //{
        //    get => interferenceSuppressionEnabled;
        //    set => Ensure(ref interferenceSuppressionEnabled, value);
        //}
        //bool interferenceSuppressionEnabled;

        #endregion Settings

        #region Retrieved device values

        /// <summary>
        /// The device model identifier.
        /// </summary>
        public string Model => model;
        string IDevice.Model
        {
            get => model;
            set => Set(ref model, value);
        }
        string model;

        /// <summary>
        /// The firmware revision identifier.
        /// </summary>
        public string Firmware => firmware;
        string IDevice.Firmware
        {
            get => firmware;
            set => Set(ref firmware, value);
        }
        string firmware;

        /// <summary>
        /// The device serial number.
        /// </summary>
        public int SerialNumber => serialNumber;
        int IDevice.SerialNumber
        {
            get => serialNumber;
            set => Set(ref serialNumber, value);
        }
        int serialNumber;

        /// <summary>
        /// The channel number of the currently selected heater.
        /// </summary>
        public int SelectedHeater => selectedHeater;
        int IDevice.SelectedHeater
        {
            get => selectedHeater;
            set => Set(ref selectedHeater, value);
        }
        int selectedHeater;

        /// <summary>
        /// The channel number of the currently selected Thermocouple.
        /// </summary>
        public int SelectedThermocouple => selectedThermocouple;
        int IDevice.SelectedThermocouple
        {
            get => selectedThermocouple;
            set => Set(ref selectedThermocouple, value);
        }
        int selectedThermocouple;

        /// <summary>
        /// The analog-to-digital converter (ADC) count reported
        /// by the controller.
        /// </summary>
        public int AdcCount => adc;
        int IDevice.Adc
        {
            get => adc;
            set => Ensure(ref adc, value);
        }
        int adc;

        /// <summary>
        /// Temperature of the cold junction sensor.
        /// </summary>
        public double ColdJunctionTemperature => cjTemperature;
        double IDevice.CJTemperature
        {
            get => cjTemperature;
            set => Ensure(ref cjTemperature, value);
        }
        double cjTemperature;

        /// <summary>
        /// An approximation of the controller's aggregate data
        /// update rate for all temperature measurements, including
        /// Thermocouples and cold junction sensors.
        /// The units are (stable - unstable readings) / second.
        /// </summary>
        public double ReadingRate
        {
            get => readingRate;
            protected set
            {
                var now = DateTime.Now;
                var seconds = (now - priorReadingCounterTime).TotalSeconds;
                var count = (int)value;
                if (count < priorReadingCounter) priorReadingCounter -= 65536;
                var delta = count - priorReadingCounter;

                // The 3 in the following expression is the ADC_STABLE value from the HC6 firmware
                readingRate.Update(delta / seconds / 3);

                priorReadingCounterTime = now;
                priorReadingCounter = count;
                NotifyPropertyChanged();
            }
        }
        int priorReadingCounter;
        DateTime priorReadingCounterTime = DateTime.Now;
        AveragingFilter readingRate = new AveragingFilter(0.9);

        int IDevice.ReadingCounter
        {
            get => priorReadingCounter;
            set => ReadingRate = value;
        }

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

        #endregion Retrieved device values

        /// <summary>
        /// Data has been received from the Device.
        /// </summary>
        public bool DataAcquired
        {
            get => dataAcquired;
            protected set => Ensure(ref dataAcquired, value);
        }
        bool dataAcquired = false;

        public override string ToString()
        {
            var sb = new StringBuilder(base.ToString());
            sb.Append($": {Model} S/N: {SerialNumber} {Firmware}");
            var sb2 = new StringBuilder();
            sb2.Append($"\r\nHch: {SelectedHeater} Tch: {SelectedThermocouple} Adc: {AdcCount}");
            sb2.Append($"\r\nCJ: {ColdJunctionTemperature:0.00} °C");
            sb2.Append($"\r\nReading rate: {ReadingRate:0}/s");
            //if (InterferenceSuppressionEnabled)
            //    sb2.Append($"\r\n(Interference suppression enabled)");
            sb.Append(Utility.IndentLines(sb2.ToString()));
            return sb.ToString();
        }

        #endregion Class interface properties and methods

        #region IDeviceManager

        protected override IManagedDevice FindSupportedDevice(string name)
        {
            if (Find<HC6Thermocouple>(name) is HC6Thermocouple t) return t;
            if (Find<HC6Heater>(name) is HC6Heater h) return h;
            return null;
        }

        #endregion IDeviceManager

        #region State Management
        // State is invalid if it is inconsistent with the desired Configuration,
        // or if the State doesn't fully and accurately represent the state of
        // the controller.
        //protected override bool StateInvalid =>
        //    base.StateInvalid ||
        //    interferenceSuppressionEnabled != TargetInterferenceSuppressionEnabled;
        #endregion State management

        #region Controller commands
        protected string ControllerDataCommand => $"z";
        protected string DataDumpCommand => $"r";

        protected char TCTypeCode
        {
            get
            {
                ThermocoupleType tt = (ServiceDevice as HC6Thermocouple.IConfig).Type;
                if (tt == ThermocoupleType.None) return '~';
                if (tt == ThermocoupleType.K) return 'K';
                else if (tt == ThermocoupleType.T) return 'T';
                return '\0';
            }
        }

        #endregion Controller commands


        #region Controller interactions

        protected override void SelectDeviceService()
        {
            if (LogEverything)
                Log?.Record($"SelectDeviceService: Device = {ServiceDevice?.Name}, Request = \"{ServiceRequest}\"");
            SetServiceValues("");       // default to nothing needed

            if (ServiceDevice is HC6Controller c)
                ServiceHC6Controller(c);
            else if (ServiceDevice is HC6Heater h)
                ServiceHC6Heater(h);
            else if (ServiceDevice is HC6Thermocouple t)
                ServiceHC6Thermocouple(t);
            else
                Log?.Record($"{ServiceDevice?.Name}'s device type ({ServiceDevice?.GetType()}) is not supported.");
            if (LogEverything)
                Log?.Record($"ServiceDevice = {ServiceDevice?.Name}, ServiceCommand = \"{ServiceCommand}\", ResponsesExpected = {ResponsesExpected}");
        }

        protected virtual void ServiceHC6Controller(HC6Controller c)
        {
            if (c.Device.UpdatesReceived == 0)
                SetServiceValues(ControllerDataCommand, 1);
            //else if (c.Device.InterferenceSuppressionEnabled != c.Config.InterferenceSuppressionEnabled)
            //    SetServiceValues($"i {ControllerDataCommand}", 1);
            else if (Stopping)
                SetServiceValues("", 1);
            else
                SetServiceValues(DataDumpCommand, 1);
        }
        protected virtual void ServiceHC6Heater(HC6Heater h)
        {
            var modeConfigured = h.Device.Mode == h.Config.Mode;
            var MaximumPowerLevel = Math.Round(h.Config.MaximumPowerLevel, 2);
            var powerLevel = Math.Round(h.Config.PowerLevel, 2);

            var sb = new StringBuilder();
            if (h.Device.ThermocoupleChannel != h.Config.ThermocoupleChannel)
                sb.Append($"ht{h.Config.ThermocoupleChannel} ");

            if (h.Device.MaximumPowerLevel != MaximumPowerLevel)
                sb.Append($"x{MaximumPowerLevel:0.00} ");

            if (h.Pid != null && !h.PidConfigured())
            {
                if (h.Device.PidGain != h.Config.PidGain)
                    sb.Append($"ck{h.Config.PidGain} ");
                if (h.Device.PidIntegral != h.Config.PidIntegral)
                    sb.Append($"ci{h.Config.PidIntegral} ");
                if (h.Device.PidDerivative != h.Config.PidDerivative)
                    sb.Append($"cd{h.Config.PidDerivative} ");
                if (h.Device.PidPreset != h.Config.PidPreset)
                    sb.Append($"cr{h.Config.PidPreset} ");
            }

            if (h.UpdatesReceived == 0)
                SetServiceValues($"{Keys[ServiceDevice]}", 1);
            else if (sb.Length > 0)          // configuration needed
            {
                sb.Insert(0, $"n{ChannelNumber} ");     // select the heater
                sb.Append($"h");                        // get a report when done
                SetServiceValues(sb.ToString(), 1);
            }
            else if (!modeConfigured && h.Config.Mode == HC6Heater.Modes.Off)
                SetServiceValues($"n{ChannelNumber} 0 h", 1);
            else if (h.Config.ManualMode && h.Device.PowerLevel != powerLevel)
                SetServiceValues($"n{ChannelNumber} m{powerLevel:0.00} h", 1);
            else if (!modeConfigured && h.Config.Mode == HC6Heater.Modes.Manual)
                SetServiceValues($"n{ChannelNumber} m h", 1);
            else if (!h.Config.ManualMode && Math.Round(h.Device.Setpoint) != Math.Round(h.Config.Setpoint))
                SetServiceValues($"n{ChannelNumber} s{Math.Round(h.Config.Setpoint)} h", 1);         // only ints are allowed
            else if (!modeConfigured && h.Config.Mode == HC6Heater.Modes.Auto)
                SetServiceValues($"n{ChannelNumber} a h", 1);
        }
        protected virtual void ServiceHC6Thermocouple(HC6Thermocouple t)
        {
            if (t.UpdatesReceived == 0)
                SetServiceValues($"{Keys[ServiceDevice]}", 1);
            else if (t.Config.Type != t.Type)
                SetServiceValues($"tn{ChannelNumber} t{TCTypeCode} t", 1);
        }

        #region helper properties and methods for controller responses
        protected bool GetValidThermocoupleType(char c, out ThermocoupleType tt)
        {
            tt = ThermocoupleType.None;
            if (c == 'K') tt = ThermocoupleType.K;
            else if (c == 'T') tt = ThermocoupleType.T;
            else if (c != '~') return false;
            return true;
        }
        protected bool GetValidHeaterMode(char c, out HC6Heater.Modes mode)
        {
            mode = HC6Heater.Modes.Off;
            if (c == 'a') mode = HC6Heater.Modes.Auto;
            else if (c == 'm') mode = HC6Heater.Modes.Manual;
            else if (c != '0') return false;
            return true;
        }

        protected bool ErrorCheck(bool errorCondition, string errorMessage)
        {
            if (errorCondition)
            {
                if (LogEverything)
                    Log?.Record($"{errorMessage}");
                return true;
            }
            return false;
        }
        protected bool LengthError(object[] elements, int nExpected, string elementDescription = "value", string where = "")
        {
            var n = elements.Length;
            if (!where.IsBlank()) where = $" {where}";
            return ErrorCheck(n != nExpected,
                $"Expected {ToUnitsString(nExpected, elementDescription)}{where}, not {n}.");
        }

        #endregion helper properties and methods for controller responses

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

                    if (LengthError(values, 1, "cold junction temperature"))
                        return false;

                    // Update this controller's data
                    Device.CJTemperature = double.Parse(values[0]);

                    values = lines[3].GetValues();
                    n = values.Length;

                    if (LengthError(values, 1, "reading counter value"))
                        return false;

                    // Update this controller's data
                    Device.ReadingCounter = int.Parse(values[0]);
                    Device.UpdatesReceived++;
                    DataAcquired = true;
                }
                else if (SerialController.CommandMessage[0] == 'h')       // heater report
                {
                    if (LengthError(lines, 1, "heater report line"))
                        return false;

                    if (LengthError(values, 11, "heater report value"))
                        return false;

                    var i = int.Parse(values[0]);
                    if (ErrorCheck(i < 0 || i >= HeaterChannels,
                            $"Invalid channel in heater report: {i}"))
                        return false;
                    Device.SelectedHeater = i;

                    var key = $"h{i}";
                    if (ErrorCheck(!Devices.ContainsKey(key),
                            $"Report received, but no heater is assigned to channel {i}"))
                        return false;

                    var h = Devices[key] as HC6Heater;
                    if (ErrorCheck(h == null,
                            $"The device at {key} isn't a {typeof(HC6Heater)}"))
                        return false;

                    var c = values[2][0];
                    if (ErrorCheck(!GetValidHeaterMode(c, out var mode),
                            $"Unsupported heater mode ({c}) on channel {key}"))
                        return false;

                    h.Device.Setpoint = int.Parse(values[1]);
                    h.Device.Mode = mode;
                    h.Device.PowerLevel = Math.Round(double.Parse(values[3]), 2);
                    h.Device.MaximumPowerLevel = Math.Round(double.Parse(values[4]), 2);
                    h.Device.ThermocoupleChannel = int.Parse(values[5]);
                    h.Device.PidGain = int.Parse(values[6]);
                    h.Device.PidIntegral = int.Parse(values[7]);
                    h.Device.PidDerivative = int.Parse(values[8]);
                    h.Device.PidPreset = int.Parse(values[9]);
                    HC6ErrorCodes errors = (HC6ErrorCodes)int.Parse(values[10]);

                    h.Device.Errors = errors & HeaterErrorFilter;  // device-specific errors
                    Device.Errors = errors & ~HeaterErrorFilter;   // other (controller) errors

                    h.Device.UpdatesReceived++;
                }
                else if (SerialController.CommandMessage[0] == 't')       // Thermocouple report
                {
                    if (LengthError(lines, 1, "thermocouple report line"))
                        return false;
                    if (LengthError(values, 4, "thermocouple report value"))
                        return false;

                    var i = int.Parse(values[0]);
                    if (ErrorCheck(i < 0 || i >= ThermocoupleChannels,
                            $"Invalid channel in thermocouple report: {i}"))
                        return false;
                    Device.SelectedThermocouple = i;

                    var key = $"t{i}";
                    if (ErrorCheck(!Devices.ContainsKey(key),
                            $"Report received, but no thermocouple is assigned to channel {i}"))
                        return false;

                    var t = Devices[key] as HC6Thermocouple;
                    if (ErrorCheck(t == null,
                            $"The device at {key} isn't a {typeof(HC6Thermocouple)}"))
                        return false;

                    var c = values[1][0];
                    if (ErrorCheck(!GetValidThermocoupleType(c, out ThermocoupleType tt),
                            $"Unsupported thermocouple type ({c}) on channel {key}"))
                        return false;

                    t.Device.Type = tt;
                    t.Device.Temperature = double.Parse(values[2]);

                    HC6ErrorCodes errors = (HC6ErrorCodes)int.Parse(values[3]);

                    t.Device.Errors = errors & ThermocoupleErrorFilter;  // device-specific errors
                    Device.Errors = errors & ~ThermocoupleErrorFilter;   // other (controller) errors

                    t.Device.UpdatesReceived++;
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
                    //Device.InterferenceSuppressionEnabled = s[s.Length - 1] == '!';
                    //if (Device.InterferenceSuppressionEnabled)
                    //    s = s.Substring(0, s.Length - 1);
                    Device.Adc = int.Parse(s);
                    Device.UpdatesReceived++;
                }
                else
                {
                    if (LogEverything)
                        Log?.Record($"Unrecognized response");
                    return false;       // unrecognized response
                }
                if (LogEverything)
                    Log?.Record($"Response successfully decoded");
                return true;
            }
            catch (Exception e)
            {
                //if (LogEverything)
                Log?.Record($"{e}");
                return false;
            }
        }

        #endregion Controller interactions
    }
}