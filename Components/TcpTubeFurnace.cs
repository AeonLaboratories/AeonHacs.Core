using AeonHacs.Utilities;
using Newtonsoft.Json;
using System;
using System.ComponentModel;
using static AeonHacs.Utilities.Utility;

namespace AeonHacs.Components
{
    public class TcpTubeFurnace : TubeFurnace,
        TcpTubeFurnace.IDevice, TcpTubeFurnace.IConfig
    {
        #region HacsComponent
        [HacsConnect]
        protected virtual void Connect()
        {
            TcpController.SelectService = SelectService;
            TcpController.ValidateResponse = ValidateResponse;
        }

        #endregion HacsComponent

        #region Device constants

        public enum FunctionCode { Read = 3, Write = 16 }
        public enum ErrorResponseCode { IllegalDataAddress = 2, IllegalDataValue = 03 }
        public enum ParameterCode
        {
            ProcessVariable = 1, 
            TargetSetpoint = 2,
            WorkingSetpoint = 5,
            WorkingOutput = 4,
            AutoManual = 273,
            ControlOutput = 3,      // ManualOP Output value while in Manual Mode

            SetpointRateLimit = 35, // SPRateUp Setpoint up rate limit
            SPRateDown = 1667,      // SPRateDown Setpoint down rate limit
            SetpointRateLimitUnits = 531,

            RangeHigh = 12,         // upper operating point (1200)
            RangeLow = 11,          // 'loop' lower operating point (0)

            OutputRateLimit = 37,   // OPRateUp Output rate up limit (%/sec)
            OPRateDown = 1861,      // OPRateDown Output rate down limit (%/sec)

            SummaryStatus = 75
        }
        public enum AutoManualCode { Auto = 0, Manual = 1, Unknown = -1 }
        public enum SetpointRateLimitUnitsCode { Seconds = 0, Minutes = 1, Hours = 2, Unknown = -1 }
        public enum SummaryStatusBitsCode { AL1 = 1, Manual = 16, SensorBroken = 32 }

        #endregion Device constants

        #region Class interface properties and methods

        #region Device interfaces

        public new interface IDevice : TubeFurnace.IDevice
        {
            double ProcessVariable { get; set; }
//            double Setpoint { get; set; }              // WorkingSetpoint, not TargetSetpoint
            int WorkingOutput { get; set; }
            int ControlOutput { get; set; }

            AutoManualCode OperatingMode { get; set; }
            bool AlarmRelayActivated { get; set; }
            bool SensorBroken { get; set; }

            int ParameterValue { get; set; }
        }
        public new interface IConfig : TubeFurnace.IConfig
        {
//            double Setpoint { get; set; }              // TargetSetpoint, not WorkingSetpoint
            AutoManualCode OperatingMode { get; }
            int ControlOutput { get; }
        }

        public new IDevice Device => this;
        public new IConfig Config => this;

        #endregion Device interfaces

        /// <summary>
        /// 
        /// </summary>
        public int ControlOutput
        {
            get => controlOutput;
            set
            {
                if (value < 0) value = 0;
                else if (value > 100) value = 100;

                Ensure(ref TargetControlOutput, value, NotifyConfigChanged, nameof(TargetControlOutput));
            }
        }

        [JsonProperty("ControlOutput"), DefaultValue(0)]
        int TargetControlOutput;
        int IConfig.ControlOutput => IsOn ? TargetControlOutput : 0;
        int IDevice.ControlOutput
        {
            get => controlOutput;
            set
            {
                Ensure(ref controlOutput, value);
                Device.OnOffState = 
                    (OperatingMode == AutoManualCode.Manual && Device.ControlOutput == 0) ||
                    (OperatingMode == AutoManualCode.Auto && Setpoint == 0) ?
                    OnOffState.Off : OnOffState.On;
            }
        }
        int controlOutput = -1;

        AutoManualCode IConfig.OperatingMode =>
            Config.State.IsOn() ? AutoManualCode.Auto : AutoManualCode.Manual;


        /// <summary>
        /// 
        /// </summary>
        [JsonProperty("OperatingMode"), DefaultValue(AutoManualCode.Manual)]
        public AutoManualCode OperatingMode
        {
            get => operatingMode;
            protected set
            {
                // When the Eurotherm controller switches into Manual Mode,
                // the ControlOutput value is ignored by the controller until 
                // a new value is written into the parameter. Meanwhile, the 
                // actual power to the furnace, the WorkingOutput, freezes.
                // In effect, the controller behaves as if the ControlOutput 
                // had been set to WorkingOutput. The following code invalidates
                // the IDevice.ControlOutput whenever the operating mode 
                // changes to Manual, so the state manager will know to update 
                // the controller's ControlOutput parameter.
                if (Ensure(ref operatingMode, value) && value == AutoManualCode.Manual)
                    Device.ControlOutput = -1;
            }
        }
        AutoManualCode operatingMode = AutoManualCode.Unknown;
        AutoManualCode IDevice.OperatingMode
        {
            get => OperatingMode;
            set => OperatingMode = value;
        }


        double IDevice.ProcessVariable
        {
            get => processVariable;
            set
            {
                Ensure(ref processVariable, value);
                Device.Temperature = value;
            }
        }
        double processVariable = -1;

        int IDevice.WorkingOutput { get => workingOutput; set => Ensure(ref workingOutput, value); }
        int workingOutput = -1;

        bool IDevice.AlarmRelayActivated { get => alarmRelayActivated; set => Ensure(ref alarmRelayActivated, value); }
        bool alarmRelayActivated = false;

        bool IDevice.SensorBroken { get => sensorBroken; set => Ensure(ref sensorBroken, value); }
        bool sensorBroken = false;

        int IDevice.ParameterValue { get => parameterValue; set => Ensure(ref parameterValue, value); }
        int parameterValue = -1;

        #region IOnOff

        /// <summary>
        /// Sets the furnace temperature and turns it on.
        /// </summary>
        /// <param name="setpoint">Desired furnace temperature (°C)</param>
        public new void TurnOn(double setpoint)
        {
            if (Config.OperatingMode != AutoManualCode.Auto)
            {
                Alert.DefaultAlertManager.Pause($"{Name} Error", "OperatingMode must be Auto for setpoint control.");
                return;
            }
            base.TurnOn(setpoint);
        }

        #endregion IOnOff

        [JsonProperty]
        public virtual TcpController TcpController { get; set; }


        /// <summary>
        /// Returns the current furnace power level (%).
        /// </summary>
        public int WorkingOutput => Device.WorkingOutput;

        public override string ToString()
        {
            return $"{Name}: {Temperature}, {IsOn.OnOff()}" +
                Utility.IndentLines(
                    $"\r\nSP: {Device.Setpoint:0}" +
                        $" PV: {Device.ProcessVariable:0}" +
                        $" CO: {Device.WorkingOutput:0}" +
                        $" ({Device.OperatingMode})"
                );
        }

        #endregion Class interface properties and methods

        string ParamToString(object o)
        {
            try
            {
                int i = Convert.ToInt32(o);
                ParameterCode p = (ParameterCode)i;
                return p.ToString();
            }
            catch { return o.ToString(); }
        }

        #region Controller commands

        #region Controller read commands
        //
        // Commands to retrieve information from the controller
        //

        int check = 1, nchecks = 4; // skip setpoint rate limit checks
        byte[] CheckStatus()
        {
            byte[] command = [];
            switch (check)
            {
                case 1:
                    command = CheckParameter(ParameterCode.ProcessVariable);
                    break;
                case 2:
                    command = CheckParameter(ParameterCode.WorkingSetpoint);
                    break;
                case 3:
                    command = CheckParameter(ParameterCode.WorkingOutput);
                    break;
                case 4:
                    command = CheckParameter(ParameterCode.SummaryStatus);
                    break;
                default:
                    break;
            }
            if (++check > nchecks) check = 1;
            return command;
        }

        byte[] CheckParameter(int param) =>
            FrameRead(param, 1);
        byte[] CheckParameter(ParameterCode param) =>
            CheckParameter((int)param);

        #endregion Controller read commands

        #region Controller write commands
        //
        // These functions issue commands to change the physical device,
        // and check whether they worked.

        /// <summary>
        /// Encodes a temperature by rounding it to the nearest integer.
        /// </summary>
        /// <param name="n"></param>
        /// <returns></returns>
        int EncodeTemperature(double n) => n.ToInt();

        // quick check; assumes valid commands
        bool sameCommand(byte[] cmd1, byte[] cmd2)
        {
            if (cmd1.Length != cmd2.Length) return false;
            for (int i = 7; i <= 11; ++i)
            {
                if (cmd1[i] != cmd2[i])
                    return false;
            }
            return true;
        }

        byte[] SetSetpoint()
        {
            var setCommand = SetParameter(ParameterCode.TargetSetpoint, EncodeTemperature(Config.Setpoint));
            return !sameCommand(Command, setCommand) ? setCommand :
                CheckParameter(ParameterCode.TargetSetpoint);
        }

        byte[] SetOperatingMode()
        {
            var setCommand = SetParameter(ParameterCode.AutoManual, (int)Config.OperatingMode);
            return !sameCommand(Command, setCommand) ? setCommand :
                CheckParameter(ParameterCode.AutoManual);
        }

        byte[] SetControlOutput()
        {
            var setCommand = SetParameter(ParameterCode.ControlOutput, Config.ControlOutput);
            return !sameCommand(Command, setCommand) ? setCommand :
                CheckParameter(ParameterCode.ControlOutput);
        }

        /// <summary>
        /// This is a dangerous function.
        /// Don't use it unless you know exactly what you are doing.
        /// </summary>
        /// <param name="param"></param>
        /// <param name="value"></param>
        byte[] SetParameter(int param, int value) =>
            FrameWrite(param, [value]);
        byte[] SetParameter(ParameterCode param, int value) =>
            SetParameter((int)param, value);

        #endregion Controller write commands

        #region Controller command generators
        //
        // These functions construct ModBus-format commands 
        // for communicating with a Eurotherm series 3000 controller.
        //

        private ushort transactionId = 0;
        private ushort getTransactionId => ++transactionId;
        private ushort protocolId = 0;

        byte[] FrameRead(int param, ushort words)
        {
            var tid = getTransactionId;
            var frame = new byte[12];
            frame[0] = (byte)MSB(tid);          // transaction id
            frame[1] = (byte)LSB(tid);
            frame[2] = 0;                       // protocol ID MSB
            frame[3] = 0;                       // protocol ID LSB
            frame[4] = 0;                       // length MSB: bytes to follow
            frame[5] = 6;                       // length LSB: bytes to follow
            frame[6] = 0xFF;                    // Unit ID (0xFF == not used)
            frame[7] = (byte)FunctionCode.Read;     // function code
            frame[8] = (byte)MSB(param);        // starting address
            frame[9] = (byte)LSB(param);
            frame[10] = (byte)MSB(words);       // number of registers to read
            frame[11] = (byte)LSB(words);
            return frame;
        }
        byte[] FrameRead(ParameterCode param, ushort words)
        { return FrameRead((int)param, words); }

        byte[] FrameWrite(int param, int[] data)
        {
            int byteCount = data.Length * 2;
            var tid = getTransactionId;
            var frame = new byte[13 + byteCount];
            frame[0] = (byte)MSB(tid);              // transaction id
            frame[1] = (byte)LSB(tid);
            frame[2] = 0;                           // protocol ID MSB
            frame[3] = 0;                           // protocol ID LSB
            frame[4] = (byte)MSB(7+byteCount);      // length MSB: bytes to follow
            frame[5] = (byte)LSB(7 + byteCount);    // length LSB: bytes to follow
            frame[6] = 0xFF;                        // Unit ID (0xFF == not used)
            frame[7] = (byte)FunctionCode.Write;    // function code
            frame[8] = (byte)MSB(param);            // starting address
            frame[9] = (byte)LSB(param);
            frame[10] = (byte)MSB(data.Length);     // number of registers to write
            frame[11] = (byte)LSB(data.Length);
            frame[12] = (byte)byteCount;
            var i = 12;
            foreach (int n in data)
            {
                frame[++i] = (byte)MSB(n);
                frame[++i] = (byte)LSB(n);
            }
            return frame;
        }
        byte[] FrameWrite(ParameterCode param, int[] data)
        { return FrameWrite((int)param, data); }

        #endregion Controller command generators

        #endregion Controller commands

        #region Controller interactions

        public byte[] Command
        {
            get => command;
            set => Ensure(ref command, value);
        }
        byte[] command = [];

        protected byte[] SelectService()
        {
            if (Device.Setpoint != Config.Setpoint)
            {
                Command = SetSetpoint();
            }
            else if (Device.OperatingMode != Config.OperatingMode)
            {
                Command = SetOperatingMode();
            }
            else if (Device.OperatingMode == AutoManualCode.Manual && Device.ControlOutput != Config.ControlOutput)
            {
                Command = SetControlOutput();
            }
            else
            {
                Command = CheckStatus();
            }
            return Command;
        }

        protected bool ValidateResponse(byte[] response)
        {
            try
            {
                var cmd = Command;       // TODO potential thread safety issue
                //if (LogEverything) Log.Record("Response to command: " + cmd);

                // Transaction IDs should match
                if (response[0] != cmd[0] || response[1] != cmd[1])
                    return false;

                // Function codes should match
                if (response[7] != cmd[7])
                    return false;

                // The following information is not present in Eurotherm controller responses
                // to parameter queries, so it must be retrieved from the issued command.
                var firstParam = (cmd[8] << 8) + cmd[9];

                var responseFunctionCode = (FunctionCode)response[7];
                if (responseFunctionCode == FunctionCode.Read)
                {
                    int wordsRequested = cmd[11];    // cmd[10] should be 0, so cmd[11] should be sufficient
                    int bytesIn = response[8];
                    int wordsIn = bytesIn / 2;
                    if (wordsIn != wordsRequested)
                        throw new Exception("read parameter count mismatcch");

                    int[] values = new int[wordsIn];
                    for (int i = 0, j = 9; i < wordsIn; i++)
                        values[i] = (response[j++] << 8) + response[j++];
                    int firstValue = values[0];

                    Device.ParameterValue = firstValue;
                    //if (LogEverything) Log.Record($"Device.ParameterValue = {firstValue}");

                    //if (LogEverything)
                    //    Log.Record(ParamToString(firstParam) + " param read: [" + firstParam.ToString() + "==" + firstValue.ToString() + "]");

                    bool wasOn = IsOn;

                    if (firstParam == (int)ParameterCode.ProcessVariable)
                    {
                        Device.ProcessVariable = firstValue;
                        //if (LogEverything) Log.Record($"{(ParameterCode)firstParam} = {firstValue}");
                    }
                    else if (firstParam == (int)ParameterCode.AutoManual)
                    {
                        Device.OperatingMode = (AutoManualCode)firstValue;
                        //if (LogEverything) Log.Record($"{(ParameterCode)firstParam} = {(AutoManualCode)firstValue}");
                    }
                    else if (firstParam == (int)ParameterCode.ControlOutput)
                    {
                        Device.ControlOutput = firstValue;
                        //if (LogEverything) Log.Record($"{(ParameterCode)firstParam} = {firstValue}");
                    }
                    else if (firstParam == (int)ParameterCode.TargetSetpoint)
                    {
                        Device.Setpoint = firstValue;
                        //if (LogEverything) Log.Record($"{(ParameterCode)firstParam} = {firstValue}");
                    }
                    else if (firstParam == (int)ParameterCode.WorkingOutput)
                    {
                        Device.WorkingOutput = firstValue;
                        //if (LogEverything) Log.Record($"{(ParameterCode)firstParam} = {firstValue}");
                    }
                    else if (firstParam == (int)ParameterCode.SummaryStatus)
                    {
                        Device.AlarmRelayActivated = (firstValue & (int)SummaryStatusBitsCode.AL1) != 0;
                        Device.SensorBroken = (firstValue & (int)SummaryStatusBitsCode.SensorBroken) != 0;
                        Device.OperatingMode = ((firstValue & (int)SummaryStatusBitsCode.Manual) != 0) ? AutoManualCode.Manual : AutoManualCode.Auto;
                        //if (LogEverything) Log.Record($"{(ParameterCode)firstParam} = {firstValue}");
                        //if (LogEverything && Device.AlarmRelayActivated) Log.Record("AlarmRelayActivated");
                    }

                    Device.OnOffState =
                        (OperatingMode == AutoManualCode.Manual && Device.ControlOutput == 0) ||
                        (OperatingMode == AutoManualCode.Auto && Setpoint == 0) ?
                        OnOffState.Off : OnOffState.On;
                         
                    if (wasOn != IsOn)
                        StateStopwatch.Restart();

                    if (UseTimeLimit && MinutesOn >= TimeLimit)
                    {
                        UseTimeLimit = false;
                        TurnOff();
                    }
                }
                else if (responseFunctionCode == FunctionCode.Write)
                {
                    /*
                    if (LogEverything)
                    {
                        int firstValue = Utility.toInt(command, 7);
                        Log.Record(ParamToString(firstParam) + " param written: [" + firstParam.ToString() + "=" + firstValue.ToString() + "]");
                        Log.Record($"Wrote {firstValue} to {(ParameterCode)firstParam}");
                    }
                    */
                }

                return true;
            }
            catch (Exception e)
            {
                //if (LogEverything) Log.Record(e.ToString());
                return false;
            }
        }

        #endregion Controller interactions
    }
}
