using AeonHacs.Utilities;
using Newtonsoft.Json;
using System;
using System.ComponentModel;
using System.Threading;

namespace AeonHacs.Components
{
    // TODO consider deriving from SerialController (like the SableCA10 class)
    /// <summary>
    /// Liquid Nitrogen Scale: A Meter-based class that reads and converts mass data from a SerialDevice to Liters of LN.
    /// </summary>
    public class LnScale : Meter
    {
        #region HacsComponent

        [HacsPostStart]
        protected virtual void PostStart()
        {
            if (LogEverything) Log?.Record($"LnScale {Name}: Starting...");
            updateThread = new Thread(UpdateLoop) { Name = $"{Name} Update", IsBackground = true };
            updateThread.Start();
            if (LogEverything) Log?.Record($"...LnScale {Name}: Started.");
        }

        [HacsPostStop]
        protected virtual void PostStop()
        {
            stopping = true;
            if (LogEverything) Log?.Record($"LnScale {Name}: Stopping...");
            SerialDevice?.Close();
            if (LogEverything) Log?.Record($"...LnScale {Name}: Stopped.");
            log?.Close();
        }

        #endregion HacsComponent

        #region Properties

        /// <summary>
        /// Milliseconds between requests for data from the device.
        /// </summary>
        [JsonProperty, DefaultValue(2000)]
        public int IdleTimeout
        {
            get => idleTimeout;
            set => Ensure(ref idleTimeout, value);
        }
        int idleTimeout = 2000;

        /// <summary>
        /// The SerialDevice that transmits and receives messages for this device.
        /// </summary>
        [JsonProperty]
        public SerialDevice SerialDevice
        {
            get => serialDevice;
            set
            {
                if (serialDevice == value) return;
                if (serialDevice != null)
                {
                    serialDevice.ResponseReceivedHandler -= Receive;
                    serialDevice.Log = null;
                }
                serialDevice = value;
                if (serialDevice != null)
                {
                    UpdateSerialDeviceLog();
                    serialDevice.ResponseReceivedHandler -= Receive;
                    serialDevice.ResponseReceivedHandler += Receive;
                    serialDevice.Connect();
                }
                NotifyPropertyChanged();
            }
        }
        SerialDevice serialDevice;

        LogFile Log
        {
            get
            {
                if (log == null)
                    Log = new LogFile($"{Name} Log.txt");
                return log;
            }
            set
            {
                log = value;
            }
        }
        LogFile log;

        /// <summary>
        /// Log all commands, responses, and serial communications.
        /// </summary>
        [JsonProperty]
        public bool LogEverything
        {
            get { return logEverything; }
            set
            {
                logEverything = value;
                if (LogEverything && SerialDevice != null)
                    SerialDevice.Log = Log;
            }
        }
        bool logEverything = false;

        /// <summary>
        /// Log messages from the device.
        /// </summary>
        [JsonProperty]
        public bool LogResponses
        {
            get => logResponses;
            set => Ensure(ref logResponses, value);
        }
        bool logResponses = false;

        bool stopping = false;

        #endregion Properties

        void UpdateSerialDeviceLog()
        {
            if (SerialDevice == null) return;
            if (LogEverything)
            {
                if (SerialDevice?.Log != Log)
                    SerialDevice.Log = Log;
            }
            else if (SerialDevice.Log != null)
            {
                SerialDevice.Log = null;
            }
        }

        Thread updateThread;
        void UpdateLoop()
        {
            while (!stopping)
            {
                if (SerialDevice.Ready && SerialDevice.Idle)
                {
                    if (LogEverything)
                        Log.Record($"{Name}: Sending 'r'");
                    SerialDevice.Command("r"); // request a report
                }
                Utility.WaitFor(() => stopping, IdleTimeout, 50);
            }
        }

        // This is SerialDevice's ResponseReceived delegate.
        void Receive(string response)
        {
            if (LogResponses || LogEverything)
                Log.Record($"{Name}: Received '{response.TrimEnd()}'");
            try
            {
                var tokens = response.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                Update(double.Parse(tokens[0]));
            }
            catch { }
        }

        /// <summary>
        /// Returns a string that represents the current object.
        /// </summary>
        /// <returns>A string that represents the current object.</returns>
        public override string ToString()
        {
            return $"{Name}: {Value:0.0} {UnitSymbol}\r\n" +
                Utility.IndentLines($"{CalibratedValue:0.00} kg");
        }
    }
}
