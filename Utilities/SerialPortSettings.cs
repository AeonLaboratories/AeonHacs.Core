using Newtonsoft.Json;
using System;
using System.IO.Ports;
using System.Xml.Serialization;

namespace AeonHacs.Utilities
{
    [JsonObject(MemberSerialization = MemberSerialization.OptIn)]
    public class SerialPortSettings
    {
        public static string[] PortNameValues
        {
            get
            {
                string[] physicalPorts = SerialPort.GetPortNames();
                int[] keys = new int[physicalPorts.Length];
                int i = 0;
                foreach (var p in physicalPorts)
                    keys[i++] = int.Parse(p.Substring(3));
                Array.Sort(keys, physicalPorts);
                return (physicalPorts?.Length > 0) ? physicalPorts :
                    new string[] { "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "COM10" };
            }
        }
        public static int[] BaudRateValues = { 115200, 57600, 38400, 28800, 19200, 14400, 9600, 4800, 2400, 1200, 600, 300 };
        public static int[] DataBitsValues = { 8, 7 };
        public static Parity[] ParityValues = (Parity[])System.Enum.GetValues(typeof(Parity));
        public static StopBits[] StopBitValues = (StopBits[])System.Enum.GetValues(typeof(StopBits));
        public static Handshake[] HandShakeValues = (Handshake[])System.Enum.GetValues(typeof(Handshake));

        static SerialPortSettings() { }


        [JsonProperty]
        public string PortName { get; set; }

        [JsonProperty]
        public int BaudRate
        {
            get { return baudRate; }
            set
            {
                for (int i = 0; i < BaudRateValues.Length; i++)
                {
                    if (value == BaudRateValues[i])
                    {
                        baudRate = value;
                        break;
                    }
                }
            }
        }
        int baudRate;

        [JsonProperty]
        public Parity Parity { get; set; }

        [JsonProperty]
        public int DataBits
        {
            get { return dataBits; }
            set
            {
                for (int i = 0; i < DataBitsValues.Length; i++)
                {
                    if (value == DataBitsValues[i])
                    {
                        dataBits = value;
                        break;
                    }
                }
            }
        }
        int dataBits;

        [JsonProperty]
        public StopBits StopBits { get; set; }
        [JsonProperty]
        public Handshake Handshake { get; set; }

        public SerialPortSettings(string portName, int baudRate, Parity parity, int dataBits, StopBits stopBits, Handshake handshake)
        {
            PortName = portName;
            BaudRate = baudRate;
            Parity = parity;
            DataBits = dataBits;
            StopBits = stopBits;
            Handshake = handshake;
        }

        public SerialPortSettings(string portName, int baudRate) :
            this(portName,
                baudRate,
                Parity.None,
                8,
                StopBits.One,
                Handshake.None)
        {}

        public SerialPortSettings(string portName) :
            this(portName, 115200)
        {}

        public SerialPortSettings() :
            this(PortNameValues[0])
        {}

        public SerialPortSettings Clone()
        {
            return new SerialPortSettings(PortName, BaudRate, Parity, DataBits, StopBits, Handshake);
        }
    }
}
