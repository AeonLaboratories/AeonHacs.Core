using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.IO.Ports;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using static AeonHacs.Notify;

namespace AeonHacs.Utilities;

[JsonObject(MemberSerialization.OptIn)]
public class SerialDevice : INotifyPropertyChanged
{
    const int RXBUF_SIZE = 4096;

    static long InstanceCount = 0;

    #region Device constants

    public enum RtsModes { Enabled, Disabled, Toggle }

    #endregion Device constants

    /// <summary>
    /// Sets the operating mode for the RTS (Request To Send) signal.<br />
    /// Enabled means set the RTS line to its active state
    /// when the device is opened and leave it on.
    /// Disabled turns it off instead.<br />
    /// In Toggle mode, RTS is dynamically turned on when there
    /// are bytes in the transmit buffer, and off when the buffer
    /// is empty.<br />
    /// Default is Enabled.
    /// </summary>
    [JsonProperty, DefaultValue(RtsModes.Enabled)]
    public RtsModes RtsMode
    {
        get => rtsMode;
        set
        {
            rtsMode = value;
            if (connected && rtsMode == RtsModes.Toggle)
                lock (this) Reset();
            NotifyPropertyChanged();
        }
    }
    RtsModes rtsMode = RtsModes.Enabled;

    [JsonProperty]
    public SerialPortSettings PortSettings
    {
        get => portSettings;
        set
        {
            if (portSettings == value) return;
            portSettings = value;
            if (connected) lock (this) Reset();
            NotifyPropertyChanged();
        }
    }
    SerialPortSettings portSettings;

    /// <summary>
    /// If CRC is not to be used, leave or set CrcConfig = null
    /// </summary>
    [JsonProperty]
    public CrcOptions CrcConfig
    {
        get => crcConfig;
        set
        {
            if (crcConfig == value) return;
            crcConfig = value;
            if (connected) lock (this) Reset();
            NotifyPropertyChanged();
        }
    }
    CrcOptions crcConfig;

    /// <summary>
    /// Time delay (milliseconds) to insert between transmitted codewords (data+CRC+termchar).
    /// Use -1 for no delay. Default -1.
    /// </summary>
    [JsonProperty, DefaultValue(-1)]
    public int MillisecondsBetweenMessages
    {
        get => millisecondsBetweenMessages;
        set { millisecondsBetweenMessages = value; NotifyPropertyChanged(); }
    }
    int millisecondsBetweenMessages = -1;

    /// <summary>
    /// Time delay (milliseconds) to insert between transmitted characters.
    /// Use -1 for no delay. Default -1.
    /// </summary>
    [JsonProperty, DefaultValue(-1)]
    public int MillisecondsBetweenBytes
    {
        get => millisecondsBetweenBytes;
        set { millisecondsBetweenBytes = value; NotifyPropertyChanged(); }
    }
    int millisecondsBetweenBytes = -1;

    /// <summary>
    /// The period of silence (milliseconds) to be interpreted
    /// as message termination/completion. Default 5.
    /// </summary>
    [JsonProperty, DefaultValue(5)]
    public int MaximumMillisecondsSilenceInMessage
    {
        get => maximumMillisecondsSilenceInMessage;
        set { maximumMillisecondsSilenceInMessage = value; NotifyPropertyChanged(); }
    }
    int maximumMillisecondsSilenceInMessage = 5;

    /// <summary>
    /// If BinaryComms is true, binary sequences are recorded
    /// in log files as printable strings of hexadecimal byte codes
    /// separated by spaces, for example &quot;F3 9A 22 16 03&quot;.
    /// </summary>
    [JsonProperty, DefaultValue(false)]
    public bool BinaryComms
    {
        get => binaryComms;
        set { binaryComms = value; NotifyPropertyChanged(); }
    }
    bool binaryComms = false;

    /// <summary>
    /// If this value is true, transmitted and received data
    /// is recorded in log files after first "escaping" a
    /// minimal set of special characters (\, &quot;, $,
    /// whitespace, etc.) by replacing them with their escape
    /// codes. This instructs the regular expression engine to
    /// interpret the characters literally rather than as
    /// metacharacters.
    /// </summary>
    [JsonProperty, DefaultValue(false)]
    public bool EscapeLoggedData
    {
        get => escapeLoggedData;
        set { escapeLoggedData = value; NotifyPropertyChanged(); }
    }
    bool escapeLoggedData = false;

    /// <summary>
    /// Raised when the port has been successfully
    /// opened and configured.
    /// </summary>
    public event EventHandler Connected;

    /// <summary>
    /// Raised when the the port is about to disconnect
    /// or if an unexpected disconnection occurs.
    /// </summary>
    public event EventHandler Disconnecting;

    public event PropertyChangedEventHandler PropertyChanged;

    /// <summary>
    /// Recipient methods (handlers) for messages from the device.
    /// </summary>
    public Action<string> ResponseReceivedHandler { get; set; }

    /// <summary>
    /// If true, data that fails CRC check will be forwarded to
    /// ResponseReceived delegate anyway (but unaltered; i.e.,
    /// including any trailing whitespace and CRC code).
    /// </summary>
    public bool IgnoreCRCErrors
    {
        get => ignoreCRCErrors;
        set { ignoreCRCErrors = value; NotifyPropertyChanged(); }
    }
    bool ignoreCRCErrors = false;

    /// <summary>
    /// A place to record transmitted and received messages,
    /// and various status conditions for debugging.
    /// </summary>
    public LogFile Log
    {
        get => log;
        set
        {
            log?.Record($"SerialDevice Log = {value?.FileName}, was {log.FileName}");
            log?.Close();
            log = value;
        }
    }
    LogFile log;

    public bool HaveWork => transmitting || !commandQ.IsEmpty;

    /// <summary>
    /// The port is open and ready to transmit and receive.
    /// </summary>
    public bool Ready
    {
        get
        {
            if (port != null && connected)
                return true;
            Thread.Sleep(100);
            return port != null && connected;
        }
    }

    /// <summary>
    /// The device is Ready and doing work.
    /// </summary>
    public bool Busy => Ready && HaveWork;

    /// <summary>
    /// The device is not Ready or there is nothing to do.
    /// </summary>
    public bool Idle => !Busy;

    /// <summary>
    /// The device is Ready but doing nothing.
    /// </summary>
    public bool Free => Ready && !HaveWork;


    // Debugging and utility aids.
    public uint ReceiveEvents { get; private set; }
    public uint ETXCount { get; private set; }
    public uint ResponseCount { get; private set; }
    public uint TotalBytesRead { get; private set; }
    public bool ErrorBufferOverflow { get; private set; }
    public uint BufferOverflows { get; private set; }
    public UInt16 RxCrcCode { get; private set; }
    public bool ErrorCrc { get; private set; }
    public uint CRCErrors { get; private set; }
    public uint Resets { get; private set; }
    public double MillisecondsSinceLastTx => txSw.Elapsed.TotalMilliseconds;
    public double MillisecondsSinceLastRx => rxSw.Elapsed.TotalMilliseconds;
    public long LongestSilence => rxSw.Longest;


    /////////////////////////////////////////////////
    /// <summary>
    /// Accepts a message for transmission.
    /// </summary>
    /// <param name="command"></param>
    /// <returns>true if the device is Ready</returns>
    public bool Command(string command)
    {
        if (string.IsNullOrEmpty(command))
            return false;
        Log?.Record($"SerialDevice received Command \"{Escape(command)}\"");
        commandQ.Enqueue(command);
        txThreadSignal.Set();   // release txThread block
        return Ready;
    }

    /// <summary>
    /// Closes and releases the port resources.
    /// </summary>
    public void Close()
    {
        Disconnect();
    }

    /// <summary>
    /// Wait until the device is disconnected or has nothing to do.
    /// </summary>
    public void WaitForIdle() { while (!Idle) Thread.Sleep(5); }

    /// <summary>
    /// Disconnects and reconnects the serial port.
    /// </summary>
    public void Reset()
    {
        Log?.Record($"Resetting SerialDevice on {PortSettings?.PortName}...");
        Disconnect();
        Resets++;
        Connect();
        Log?.Record($"...{PortSettings?.PortName} reset complete.");
    }

    /// <summary>
    /// A utility method that tries to make the string
    /// at least somewhat human-friendly.
    /// </summary>
    /// <param name="s"></param>
    /// <returns></returns>
    public string Escape(string s = "")
    {
        if (s == null) s = "";
        if (BinaryComms)
            return s.ToByteString();
        if (EscapeLoggedData)
            return Regex.Escape(s);
        else
            return s;
    }



    public SerialDevice()
    {
        InstanceCount++;
        rx = new byte[RXBUF_SIZE];
    }

    public SerialDevice(SerialPortSettings portSettings) : this()
    {
        PortSettings = portSettings;
    }

    public SerialDevice(string portName, int baudRate) :
        this(new SerialPortSettings(portName, baudRate))
    { }

    public SerialDevice(string portName) :
        this(new SerialPortSettings(portName))
    { }


    USBSerialPort port;

    bool connected = false;

    /// <summary>
    /// The threads should keep running.
    /// </summary>
    bool active = false;

    byte[] rx;                          // serial receive data buffer
    Crc rxCrc = null;
    Crc txCrc = null;

    ConcurrentQueue<string> commandQ = new ConcurrentQueue<string>();
    bool transmitting;              // or waiting MillisecondsBetweenMessages or Bytes

    Thread txThread;
    AutoResetEvent txThreadSignal = new AutoResetEvent(false);

    Thread rxThread;
    AutoResetEvent rxThreadSignal = new AutoResetEvent(false);

    Thread prxThread;
    AutoResetEvent prxThreadSignal = new AutoResetEvent(false);

    Stopwatch txSw = new Stopwatch();
    Stopwatch rxSw = new Stopwatch();


    public bool Connect()
    {
        try
        {
            if (connected) return true;
            Log?.Record("SerialDevice connecting...");

            SerialPortMonitor.PortArrived -= OnPortConnected;   // avoid duplicates
            SerialPortMonitor.PortRemoved -= OnPortRemoved;
            port = new USBSerialPort
            {
                DiscardNull = false,
                PortName = PortSettings.PortName,
                BaudRate = PortSettings.BaudRate,
                Parity = PortSettings.Parity,
                DataBits = PortSettings.DataBits,
                StopBits = PortSettings.StopBits,
                Handshake = PortSettings.Handshake,
                ReceivedBytesThreshold = 1,     // redundant; the default is 1
                ReadTimeout = 20,
                WriteTimeout = 20,
                Encoding = Encoding.ASCII8
            };

            port.DataReceived += new
                SerialDataReceivedEventHandler(RxDetected);
            port.PinChanged += new
                SerialPinChangedEventHandler(PinChanged);

            port.Open();
            if (port.IsOpen)
            {
                port.DiscardOutBuffer();
                port.DiscardInBuffer();

                if (RtsMode == RtsModes.Enabled)
                    port.RtsEnable = true;
                else if (RtsMode == RtsModes.Disabled)
                    port.RtsEnable = false;
                else if (RtsMode == RtsModes.Toggle)
                    port.SetRtsControlToggle();
                port.DtrEnable = true;

                ClearRxb();
                rxCrc = CrcConfig == null ? null : new Crc(CrcConfig);
                txCrc = CrcConfig == null ? null : new Crc(CrcConfig);
                CRCErrors = 0;
                ResponseCount = 0;

                active = true;  // keep the threads alive

                // enable this thread to retrieve SerialPort data asynchronously
                rxThread = new Thread(Receive) { Name = $"SerialDevice {InstanceCount} receive", IsBackground = true };
                rxThread.Start();

                if (CrcConfig == null || CrcConfig.OmitTermChar)
                    prxThread = new Thread(ProcessRx2) { Name = $"SerialDevice {InstanceCount} process_rx2", IsBackground = true };
                else
                    prxThread = new Thread(ProcessRx) { Name = $"SerialDevice {InstanceCount} process_rx", IsBackground = true };
                prxThread.Start();

                txThread = new Thread(Transmit) { Name = $"SerialDevice {InstanceCount} transmit", IsBackground = true };
                txThread.Start();

                SerialPortMonitor.PortArrived += OnPortConnected;
                SerialPortMonitor.PortRemoved += OnPortRemoved;
                connected = true;
                Log?.Record("...SerialDevice connected.");
                Connected?.Invoke(this, null);
                return true;
            }
            else
                Log?.Record($"Failed to connect to {port.PortName}.");
        }
        catch { Disconnect(); }
        return false;
    }

    // Disposing the USBSerialPort makes sure it is registered by Windows
    // in HKEY_LOCAL_MACHINE\HARDWARE\DEVICEMAP\SERIALCOMM, so that
    // SerialPort.GetPortNames() can re-detect the port.
    public void Disconnect()
    {
        Log?.Record("SerialDevice disconnecting...");
        if (connected) Disconnecting?.Invoke(this, null);
        connected = false;
        active = false;

        SerialPortMonitor.PortArrived -= OnPortConnected;
        SerialPortMonitor.PortRemoved -= OnPortRemoved;

        port?.ClearRtsControlToggle();
        port?.Close();
        port?.Dispose();
        port = null;

        txThreadSignal?.Set();
        rxThreadSignal?.Set();
        prxThreadSignal?.Set();
        while (
            (txThread?.IsAlive ?? false) ||
            (rxThread?.IsAlive ?? false) ||
            (prxThread?.IsAlive ?? false)) Thread.Sleep(5);
        Log?.Record("...SerialDevice disconnected.");
    }

    void OnPortConnected(string portName)
    {
        if (PortSettings?.PortName is string s && s == portName)
            Connect();
    }

    void OnPortRemoved(string portName)
    {
        if (PortSettings?.PortName is string s && s == portName)
            Disconnect();
    }

    // Optionally paces the transmission rate so a slower
    // embedded processor can complete its byte-received ISR
    // before another character arrives. (This is independent
    // of baud rate.)
    void Transmit()
    {
        Log?.Record($"SerialDevice starting Transmit thread.");
        try
        {
            byte[] tx = { };                        // transmit data buffer
            int txbOut = 0;
            string command = "";
            txSw.Restart();

            while (active)
            {
                if (txbOut < tx.Length)
                {
                    try
                    {
                        if (MillisecondsBetweenBytes == -1)     // do not pace characters
                        {
                            if (MillisecondsSinceLastTx >= MillisecondsBetweenMessages)
                            {
                                Log?.Record($"SerialDevice Transmit ({MillisecondsSinceLastTx:0} ms since last): \"{Escape(command)}\"");
                                port.Write(tx, 0, tx.Length);
                                txSw.Restart();
                                txbOut = tx.Length;
                            }
                            else
                                Thread.Sleep(1);
                        }
                        else
                        {
                            if (MillisecondsSinceLastTx >= MillisecondsBetweenBytes)
                            {
                                Log?.Record($"SerialDevice Transmit ({MillisecondsSinceLastTx:0} ms since last) 0x{tx[txbOut]:x2} (\'{tx[txbOut]}\')");
                                port.Write(tx, txbOut++, 1);
                                txSw.Restart();
                            }
                            else
                                Thread.Sleep(1);
                        }
                    }
                    catch (Exception e)
                    {
                        // ignore port write errors?
                        Log?.Record("SerialDevice (transmit) exception: " + e.ToString());
                        // but take a breather
                        Thread.Sleep(Math.Max(20, MillisecondsBetweenMessages));
                    }
                }
                else
                {
                    transmitting = false;

                    if (commandQ.TryDequeue(out command))
                    {
                        txbOut = 0;
                        if (txCrc == null)
                        {
                            tx = command.ToASCII8ByteArray();
                        }
                        else
                        {
                            txCrc.Init();
                            tx = txCrc.Append(command);
                        }

                        transmitting = true;
                    }
                    else
                    {
                        txThreadSignal.WaitOne(1000);   // wait for a command
                    }
                }
            }
        }
        catch (Exception e)
        {
            Announce("Exception in SerialDevice.Transmit()", 
                $"{e}\r\nPort: {PortSettings?.PortName}", type: NoticeType.Error);
        }
        Log?.Record($"SerialDevice ending Transmit thread.");
    }

    #region process received bytes

    ///////////////////////////////////////////////////////
    volatile int Rxb_write = 0;     // place for next byte from receiver
    int Rxb_head = 0;               // location of first unretrieved byte

    ///////////////////////////////////////////////////////
    // ring buffer pointer movement and state
    int Advance(int p) { return ((p + 1) % RXBUF_SIZE); }
    int Retreat2(int p) { return ((p + RXBUF_SIZE - 2) % RXBUF_SIZE); }

    int ClearRxb() { Rxb_head = Rxb_write; return Rxb_head; }

    string RxbSequence(int tail)
    {
        if (tail >= Rxb_head)
        {
            return rx.ToString(Rxb_head, tail - Rxb_head);
        }
        else
        {
            return rx.ToString(Rxb_head, RXBUF_SIZE - Rxb_head) +
                rx.ToString(0, tail);
        }
    }

    void HandleCrcError()
    {
        ErrorCrc = true;
        CRCErrors++;
    }

    // Scans through the rx buffer, extracts CRC-validated messages,
    // and sends them to the ResponseReceived delegate.
    // Runs in its own thread, prxThread.
    // Requires a valid (non-null) CrcConfig
    void ProcessRx()
    {
        Log?.Record($"SerialDevice starting ProcessRx thread.");

        try
        {
            int rxb_read = ClearRxb();
            rxCrc.Init();
            ErrorCrc = false;

            int bwuTermChar = 0;  // bytes with unexpected TermChar
            while (active)
            {
                while (rxb_read != Rxb_write)
                {
                    if (bwuTermChar > 0) bwuTermChar++;
                    byte c = rx[rxb_read];
                    if (c == CrcConfig.TermChar)
                    {
                        ETXCount++;
                        if (rxCrc.Good())
                        {
                            string s = RxbSequence(Retreat2(rxb_read)); // don't include CRC code
                            Log?.Record("SerialDevice.ProcessRx: " + Escape(s.TrimEnd()));   // remove trailing whitespace
                            ResponseReceivedHandler?.Invoke(s);
                            ResponseCount++;
                        }
                        else bwuTermChar = 1;

                        if (rxCrc.Good() || ErrorCrc)
                        {
                            if (ErrorCrc)
                            {
                                string s = RxbSequence(rxb_read);
                                Log?.Record("SerialDevice.ProcessRx: " + Escape(s) + " [CRC Error]");
                                if (IgnoreCRCErrors)
                                {
                                    ResponseReceivedHandler?.Invoke(s);
                                    ResponseCount++;
                                }
                            }

                            // start fresh on the next byte
                            rxb_read = Rxb_head = Advance(rxb_read);
                            bwuTermChar = 0;
                            rxCrc.Init();
                            ErrorCrc = false;
                            continue;
                        }
                    }
                    RxCrcCode = rxCrc.Update(c);
                    rxb_read = Advance(rxb_read);

                    if (!ErrorCrc && bwuTermChar > 2)
                    {
                        HandleCrcError();
                        Log?.Record("SerialDevice.ProcessRx: " + Escape(RxbSequence(rxb_read)) + " [CRC Error]");
                    }
                }
                if (ErrorBufferOverflow)
                {
                    HandleCrcError();
                    rxb_read = ClearRxb();
                }
                // wait here until something more comes in

                prxThreadSignal.WaitOne();
            }
        }
        catch (Exception e)
        {
            Announce($"Unable to process messages from {PortSettings.PortName}.",
                e.ToString(), type: NoticeType.Error);
        }
        Log?.Record($"SerialDevice ending ProcessRx thread.");
    }

    // No TermChar; this version detects end of message by a period of silence.
    // the rx buffer is expected to contain a whole message
    void ProcessRx2()
    {
        Log?.Record($"SerialDevice starting ProcessRx2 thread.");
        try
        {
            while (prxThreadSignal.WaitOne() && active)
            {
                var tail = Rxb_write;

                if (ErrorBufferOverflow)
                {
                    ClearRxb();   // silently discard the (possibly partial) message
                    continue;
                }

                string s = RxbSequence(tail);
                Rxb_head = tail;       // remove message from rx[] in any case

                if (rxCrc != null)
                {
                    rxCrc.Init();
                    ErrorCrc = false;
                    RxCrcCode = rxCrc.Update(s);
                    if (rxCrc.Good())
                        s = s[0..^2];    // remove the CRC
                }

                if (rxCrc == null || rxCrc.Good())
                {
                    if (s.Length > 0)
                    {
                        Log?.Record($"SerialDevice.ProcessRx2: {Escape(s.TrimEnd())}");
                        ResponseReceivedHandler?.Invoke(s);
                        ResponseCount++;
                    }
                }
                else        // !rxCrc.Good()
                {
                    HandleCrcError();
                    Log?.Record($"SerialDevice.ProcessRx2: \"{Escape(s)}\" [CRC Error]");
                }
            }
        }
        catch (Exception e)
        {
            Announce($"Unable to process messages from {PortSettings.PortName}.",
                e.ToString(), type: NoticeType.Error);
        }
        Log?.Record($"SerialDevice ending ProcessRx2 thread.");
    }

    #endregion process received bytes


    // Transfer received bytes from the SerialPort buffer into this
    // SerialDevice's rx buffer for processing
    static int xferBufferSize = RXBUF_SIZE;
    byte[] xferBuffer = new byte[xferBufferSize];

    void GetRxData()
    {
        Log?.Record($"SerialDevice GetRxData triggered");
        int n;
        try { n = port?.Read(xferBuffer, 0, xferBufferSize) ?? 0; }
        catch { n = 0; }
        if (n == 0)
        {
            Log?.Record($"SerialDevice (GetRxData): Nothing found.");
            return;
        }
        rxSw.Restart();

        // This section copies the data in one or two chunks -- the fewest possible.
        // Two copy operations are needed if the message will wrap past the end
        // of the rx buffer. Block-copying like this is faster than a simple
        // byte-copy loop, whenever messages are >= 2 bytes long.
        int availableBytes = Rxb_head > Rxb_write ? Rxb_head - Rxb_write : RXBUF_SIZE - Rxb_write + Rxb_head;
        if (ErrorBufferOverflow = n > availableBytes)           // assignment, not comparison
        {
            // If this ever happens, RXBUF_SIZE is too small; increase it.
            BufferOverflows++;
            return;     // the received data is lost; alternative is to wait for room in the rx
        }

        var end = Rxb_write + n - 1;
        var twoCopiesNeeded = end >= RXBUF_SIZE;
        if (twoCopiesNeeded)
        {
            end -= RXBUF_SIZE;
            var firstPart = n - end - 1;
            Array.Copy(xferBuffer, 0, rx, Rxb_write, firstPart);
            Array.Copy(xferBuffer, firstPart, rx, 0, end + 1);
        }
        else
            Array.Copy(xferBuffer, 0, rx, Rxb_write, n);
        Rxb_write = Advance(end);

        TotalBytesRead += (uint)n;
        // DEBUG CODE
        //Log?.Record($"SerialDevice (GetRxData): \"{Escape(RxbSequence(Rxb_write))}\" (TotalBytesRead = {TotalBytesRead})");
        Log?.Record($"SerialDevice (GetRxData): TotalBytesRead = {TotalBytesRead}");

        prxThreadSignal.Set();   // process what was received
    }


    // Serial port data-received signals often arrive several times
    // per millisecond, heralding as few as one byte each. This method
    // gives getRxData longer message fragments to process, with fewer calls.
    // When a signal arrives, it waits until a silence occurs before calling
    // getRxData. This should let getRxData essentially always transfer
    // whole messages at a time.
    void Receive()
    {
        Log?.Record($"SerialDevice starting Receive thread.");

        bool signalReceived = false;
        while (active)
        {
            try
            {
                // In addition to calling getRxData after signals-plus-silence, this version
                // calls getRxData every 500 ms if no signal arrives.
                //if (!(signalReceived = rxThreadSignal.WaitOne(signalReceived ? MaxMillisecondsSilenceInMessage : 500)))
                //    getRxData();

                // This version does not call getRxData unless at least one signal arrives.
                if (rxThreadSignal.WaitOne(signalReceived ? MaximumMillisecondsSilenceInMessage : 500))
                    signalReceived = true;
                else if (signalReceived)
                {
                    GetRxData();
                    signalReceived = false;
                }
            }
            catch (Exception e) { Log?.Record($"SerialDevice (Receive): {e}"); }
        }
        Log?.Record($"SerialDevice ending Receive thread.");
    }


    // This is an event handler invoked by port, a SerialPort.
    // Runs in a ThreadPool thread started by the SerialPort.
    void RxDetected(object sender, SerialDataReceivedEventArgs e)
    {
        ReceiveEvents++;
        //getRxData();                // for synchronous data retrieval
        rxThreadSignal.Set();       // grab the data asynchronously
    }

    void PinChanged(object sender, SerialPinChangedEventArgs e)
    {
        Log?.Record($"SerialDevice: A pin changed: {e.EventType}");
    }

    /// <summary>
    /// Raises the PropertyChanged event.
    /// </summary>
    /// <param name="propertyName"></param>
    protected virtual void NotifyPropertyChanged([CallerMemberName] string propertyName = "") =>
        NotifyPropertyChanged(this, new PropertyChangedEventArgs(propertyName));

    /// <summary>
    /// Raises the PropertyChanged event.
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    protected virtual void NotifyPropertyChanged(object sender, PropertyChangedEventArgs e) =>
        PropertyChanged?.Invoke(sender, e);
}
