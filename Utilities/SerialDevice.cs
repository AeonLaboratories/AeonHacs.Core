using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.IO.Ports;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using static AeonHacs.Notify;
using static AeonHacs.Utilities.Utility;

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
            var was = log?.FileName;
            log?.Record($"SerialDevice @{PortSettings?.PortName} Log = {value?.FileName}");
            log?.Close();
            log = value;
            log?.Record($"SerialDevice @{PortSettings?.PortName} Log was {was}");
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
        Log?.Record($"SerialDevice @{PortSettings?.PortName} received Command \"{Escape(command)}\"");
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
    public void WaitForIdle() => WaitFor(() => Idle, -1, 5);

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

    /// <summary>
    /// Creates a new communications session, including buffers,
    /// queues, protocol engines, timers, parser state, and other
    /// transient per-connection data.
    /// 
    /// Discards all in-flight communication, queued commands, 
    /// parser state, transient error conditions, and timing data.
    ///
    /// Does not modify configuration, event subscriptions,
    /// response handlers, logging, or lifetime diagnostics.
    /// </summary>
    void CreateCommsSession()
    {
        commandQ = new ConcurrentQueue<string>();
        transmitting = false;

        rx = new byte[RXBUF_SIZE];
        Rxb_write = 0;
        Rxb_head = 0;

        rxCrc = CrcConfig == null ? null : new Crc(CrcConfig);
        txCrc = CrcConfig == null ? null : new Crc(CrcConfig);

        ErrorBufferOverflow = false;
        ErrorCrc = false;
        RxCrcCode = 0;
        ResponseCount = 0;

        txSw.Reset();
        rxSw.Reset();

        while (txThreadSignal.WaitOne(0)) { }
        while (rxThreadSignal.WaitOne(0)) { }
        while (prxThreadSignal.WaitOne(0)) { }
    }


    /// <summary>
    /// Raises the Connected event while isolating SerialDevice
    /// from subscriber exceptions, which could otherwise interrupt
    /// connection initialization.
    /// </summary>
    void DispatchConnected()
    {
        try
        {
            Connected?.Invoke(this, null);
        }
        catch (Exception e)
        {
            Log?.Record($"SerialDevice @{PortSettings?.PortName} Connected handler exception: {e}");

            Announce($"SerialDevice {PortSettings?.PortName}: Connected event handler failed.",
                e.ToString(), type: NoticeType.Error);
        }
    }

    /// <summary>
    /// Raises the Disconnecting event while isolating SerialDevice
    /// from subscriber exceptions, which could otherwise interrupt
    /// disconnection and recovery operations.
    /// </summary>
    void DispatchDisconnecting()
    {
        try
        {
            Disconnecting?.Invoke(this, null);
        }
        catch (Exception e)
        {
            Log?.Record($"SerialDevice @{PortSettings?.PortName} Disconnecting handler exception: {e}");

            Announce($"SerialDevice {PortSettings?.PortName}: Disconnecting event handler failed.",
                e.ToString(), type: NoticeType.Error);
        }
    }

    /// <summary>
    /// Opens and configures the serial port, creates a new
    /// communications session, and starts the worker threads
    /// responsible for transmitting, receiving, and processing
    /// messages.
    ///
    /// Returns true if the port was successfully opened and the
    /// communications session was started; otherwise, returns false.
    /// </summary>
    public bool Connect()
    {
        try
        {
            if (connected) return true;
            Log?.Record($"SerialDevice @{PortSettings?.PortName} connecting...");

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
                Encoding = EncodingType.ASCII8
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

                CreateCommsSession();
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
                Log?.Record($"...SerialDevice connected to @{PortSettings?.PortName}.");
                DispatchConnected();
                return true;
            }
            else
            {
                Log?.Record($"SerialDevice @{PortSettings?.PortName}: port did not open.");
                Announce($"SerialDevice {PortSettings?.PortName}: Connect failed.",
                    "The serial port did not open.",
                    type: NoticeType.Error);
            }
        }
        catch (Exception e)
        {
            Log?.Record($"SerialDevice @{PortSettings?.PortName} Connect exception: {e}");
            Announce($"SerialDevice {PortSettings?.PortName}: Connect failed.",
                e.ToString(), type: NoticeType.Error);
            Disconnect();
        }
        return false;
    }

    /// <summary>
    /// Stops the communications session, closes and disposes the
    /// serial port, and waits a limited time for the worker threads
    /// to terminate.
    ///
    /// Returns true unless errors occurred during disconnection
    /// that may have left resources unreclaimed. Such errors are
    /// logged. A false return may indicate that one or more worker 
    /// threads are still running.
    /// 
    /// Despite any errors, SerialDevice attempts to close the port
    /// and release as many resources as possible. Reconnection may
    /// safely be re-attempted.
    /// </summary>
    public bool Disconnect()
    {
        var errors = 0;
        Log?.Record($"SerialDevice @{PortSettings?.PortName} disconnecting...");
        if (connected) DispatchDisconnecting();
        connected = false;
        active = false;

        SerialPortMonitor.PortArrived -= OnPortConnected;
        SerialPortMonitor.PortRemoved -= OnPortRemoved;

        txThreadSignal?.Set();
        rxThreadSignal?.Set();
        prxThreadSignal?.Set();

        try { port?.ClearRtsControlToggle(); }
        catch (Exception e) { errors++; Log?.Record($"SerialDevice @{PortSettings?.PortName} ClearRtsControlToggle exception: {e}"); }

        try { port?.Close(); }
        catch (Exception e) { errors++; Log?.Record($"SerialDevice @{PortSettings?.PortName} port close exception: {e}"); }

        // Disposing the USBSerialPort makes sure it is registered by Windows
        // in HKEY_LOCAL_MACHINE\HARDWARE\DEVICEMAP\SERIALCOMM, so that
        // SerialPort.GetPortNames() can re-detect the port.
        try { port?.Dispose(); }
        catch (Exception e) { errors++; Log?.Record($"SerialDevice @{PortSettings?.PortName} port dispose exception: {e}"); }

        port = null;

        bool allStopped = WaitFor(() =>
            !(txThread?.IsAlive ?? false) &&
            !(rxThread?.IsAlive ?? false) &&
            !(prxThread?.IsAlive ?? false),
            100, 5);

        if (!allStopped)
        {
            errors++;
            var hungThreads =
                $"{((txThread?.IsAlive ?? false) ? "Transmit " : "")}" +
                $"{((rxThread?.IsAlive ?? false) ? "Receive " : "")}" +
                $"{((prxThread?.IsAlive ?? false) ? "ProcessRx " : "")}";
            Log?.Record($"SerialDevice @{PortSettings?.PortName}: worker thread shutdown timeouts: {hungThreads.Trim()}");

            Announce($"SerialDevice {PortSettings?.PortName}: worker thread shutdown timeout.",
                $"The following thread(s) failed to terminate: {hungThreads.Trim()}",
                type: NoticeType.Warning);
        }

        Log?.Record($"...SerialDevice @{PortSettings?.PortName} disconnected.");

        return errors == 0;
    }

    /// <summary>
    /// Rebuilds the communications session by disconnecting and
    /// reconnecting the serial port while preserving configuration,
    /// event subscriptions, response handlers, logging, and
    /// lifetime diagnostics.
    ///
    /// All in-flight communication, queued commands, parser state,
    /// transient error conditions, and timing data are discarded.
    ///
    /// Returns true if a new communications session was successfully
    /// established; otherwise, returns false.
    ///
    /// The previous communications session is terminated to the best
    /// of SerialDevice's ability. Failure to cleanly terminate the
    /// previous session does not necessarily prevent a successful
    /// reset.
    /// </summary>
    public bool Reset()
    {
        Log?.Record($"Resetting SerialDevice on {PortSettings?.PortName}...");
        Disconnect();
        Resets++;
        var connected = Connect();
        Log?.Record(connected
            ? $"...{PortSettings?.PortName} reset complete."
            : $"...{PortSettings?.PortName} reset failed.");
        return connected;
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
        Log?.Record($"SerialDevice @{PortSettings?.PortName} starting Transmit thread.");
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
                                Log?.Record($"SerialDevice @{PortSettings?.PortName} Transmit ({MillisecondsSinceLastTx:0} ms since last): \"{Escape(command)}\"");
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
                                Log?.Record($"SerialDevice @{PortSettings?.PortName} Transmit ({MillisecondsSinceLastTx:0} ms since last) 0x{tx[txbOut]:x2} (\'{tx[txbOut]}\')");
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
                        Log?.Record($"SerialDevice @{PortSettings?.PortName} (transmit) exception: {e}");
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
            Log?.Record($"SerialDevice @{PortSettings?.PortName} fatal Transmit exception: {e}");
            Announce($"SerialDevice {PortSettings?.PortName}: Transmit thread failed.",
                e.ToString(), type: NoticeType.Error);
        }
        finally
        {
            Log?.Record($"SerialDevice @{PortSettings?.PortName} ending Transmit thread.");
        }
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

    /// <summary>
    /// Replace the last 'tail' characters of s with their byte codes enclosed with [].
    /// E.g., HexifyTail("A line\r\n", 2) returns "A line[13 10]"
    /// tail is 5 by default because messages often end with \r\n[CRC1 CRC2][ETX].
    /// </summary>
    string HexifyTail(string s, int tail = 5)
    {
        if (s.Length < tail) return s;
        return $"{Escape(s[..^tail])} [{s[^tail..].ToByteString()}]";
    }

    /// <summary>
    /// Invokes ResponseReceivedHandler while isolating SerialDevice
    /// from handler exceptions, which could otherwise terminate
    /// ProcessRx() or ProcessRx2().
    /// </summary>
    bool DispatchResponse(string s)
    {
        try
        {
            ResponseReceivedHandler?.Invoke(s);
            return true;
        }
        catch (Exception e)
        {
            Log?.Record($"SerialDevice @{PortSettings?.PortName} ResponseReceivedHandler exception: {e}");
            Announce($"SerialDevice @{PortSettings?.PortName} response handler failed.",
                e.ToString(), type: NoticeType.Error);
            return false;
        }
    }

    // Scans through the rx buffer, extracts CRC-validated messages,
    // and sends them to the ResponseReceived delegate.
    // Runs in its own thread, prxThread.
    // Requires a valid (non-null) CrcConfig
    void ProcessRx()
    {
        Log?.Record($"SerialDevice @{PortSettings?.PortName} starting ProcessRx thread.");

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
                            string s = RxbSequence(Retreat2(rxb_read)); // don't include CRC code (or TermChar)
                            Log?.Record($"SerialDevice @{PortSettings?.PortName}.ProcessRx: {Escape(s.TrimEnd())}");   // remove trailing whitespace
                            DispatchResponse(s);
                            ResponseCount++;
                        }
                        else bwuTermChar = 1;

                        if (rxCrc.Good() || ErrorCrc)
                        {
                            if (ErrorCrc)
                            {
                                var s = HexifyTail(RxbSequence(rxb_read));
                                Log?.Record($"SerialDevice @{PortSettings?.PortName}.ProcessRx: {s} [CRC Error]");
                                if (IgnoreCRCErrors)
                                {
                                    DispatchResponse(s);
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

                        // Assume the TermChar was the end of a message with a bad CRC
                        // and the byte following it is the start of a new message.
                        rxb_read = Retreat2(rxb_read);

                        var s = HexifyTail(RxbSequence(rxb_read), 5);
                        Log?.Record($"SerialDevice @{PortSettings?.PortName}.ProcessRx: {s} [CRC Error b]");

                        Rxb_head = rxb_read;
                        bwuTermChar = 0;
                        rxCrc.Init();
                        ErrorCrc = false;
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
            Log?.Record($"SerialDevice @{PortSettings?.PortName} fatal ProcessRx exception: {e}");
            Announce($"SerialDevice {PortSettings?.PortName}: ProcessRx thread failed.",
                e.ToString(), type: NoticeType.Error);
        }
        Log?.Record($"SerialDevice @{PortSettings?.PortName} ending ProcessRx thread.");
    }

    // No TermChar; this version detects end of message by a period of silence.
    // the rx buffer is expected to contain a whole message
    void ProcessRx2()
    {
        Log?.Record($"SerialDevice @{PortSettings?.PortName} starting ProcessRx2 thread.");
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
                        Log?.Record($"SerialDevice @{PortSettings?.PortName}.ProcessRx2: {Escape(s.TrimEnd())}");
                        DispatchResponse(s);
                        ResponseCount++;
                    }
                }
                else        // !rxCrc.Good()
                {
                    HandleCrcError();
                    Log?.Record($"SerialDevice @{PortSettings?.PortName}.ProcessRx2: \"{Escape(s)}\" [CRC Error]");
                }
            }
        }
        catch (Exception e)
        {
            Log?.Record($"SerialDevice @{PortSettings?.PortName} fatal ProcessRx2 exception: {e}");
            Announce($"SerialDevice {PortSettings?.PortName}: ProcessRx2 thread failed.",
                e.ToString(), type: NoticeType.Error);
        }
        Log?.Record($"SerialDevice @{PortSettings?.PortName} ending ProcessRx2 thread.");
    }

    #endregion process received bytes


    // Transfer received bytes from the SerialPort buffer into this
    // SerialDevice's rx buffer for processing
    static int xferBufferSize = RXBUF_SIZE;
    byte[] xferBuffer = new byte[xferBufferSize];

    void GetRxData()
    {
        Log?.Record($"SerialDevice @{PortSettings?.PortName} GetRxData triggered");
        int n;
        try { n = port?.Read(xferBuffer, 0, xferBufferSize) ?? 0; }
        catch { n = 0; }
        if (n == 0)
        {
            Log?.Record($"SerialDevice @{PortSettings?.PortName} (GetRxData): Nothing found.");
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
        var s = HexifyTail(RxbSequence(Rxb_write), 5);
        Log?.Record($"SerialDevice @{PortSettings?.PortName} (GetRxData): \"{s}\" (TotalBytesRead = {TotalBytesRead})");

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
        Log?.Record($"SerialDevice @{PortSettings?.PortName} starting Receive thread.");

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
                {
                    if (!active) break;
                    signalReceived = true;
                }
                else if (signalReceived)
                {
                    if (!active) break;
                    GetRxData();
                    signalReceived = false;
                }
            }
            catch (Exception e)
            {
                Log?.Record($"SerialDevice @{PortSettings?.PortName} Receive exception: {e}"); 
            }
        }
        Log?.Record($"SerialDevice @{PortSettings?.PortName} ending Receive thread.");
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
        Log?.Record($"SerialDevice @{PortSettings?.PortName}: A pin changed: {e.EventType}");
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
