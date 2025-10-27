using AeonHacs.Utilities;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using static AeonHacs.Utilities.Utility;

namespace AeonHacs.Components;

public class ManagedMFC : Auto, IMassFlowController, IManagedDevice, 
    ManagedMFC.IDevice, ManagedMFC.IConfig
{
    #region HacsComponent

    #region Device interfaces

    public new interface IDevice : Auto.IDevice, ManagedDevice.IDevice { }
    public new interface IConfig : Auto.IConfig, ManagedDevice.IConfig { }
    public new IDevice Device => this;
    public new IConfig Config => this;


    #endregion Device interfaces

    #region ManagedDevice
    ManagedDevice.IDevice IManagedDevice.Device => this;
    ManagedDevice.IConfig IManagedDevice.Config => this;

    public IDeviceManager Manager => ManagedDevice.Manager;
    IDeviceManager ManagedDevice.IDevice.Manager { get => ManagedDevice.Manager; set => ManagedDevice.Device.Manager = value; }

    public override void OnPropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (sender == ManagedDevice)
            NotifyPropertyChanged(e?.PropertyName);
        else
            base.OnPropertyChanged(sender, e);
    }

    public override void OnConfigChanged(object sender, PropertyChangedEventArgs e)
    {
        if (sender == ManagedDevice)
            NotifyConfigChanged(e?.PropertyName);
        else
            base.OnConfigChanged(sender, e);
    }

    public override string Name
    {
        get => base.Name;
        set { base.Name = value; ManagedDevice.Name = $"({value})"; }
    }

    ManagedDevice ManagedDevice;

    #endregion ManagedDevice


    [HacsConnect]
    protected virtual void Connect()
    {
        ShutoffValve = Find<IValve>(shutoffValveName);
    }

    [HacsInitialize]
    protected virtual void Initialize()
    {
        updateThread = new Thread(Update)
        {
            Name = $"{Name} Update",
            IsBackground = true
        };
        updateThread.Start();
    }

    

    #endregion HacsComponent

    #region Device constants
    public static Dictionary<string, string> ErrorCodes = new Dictionary<string, string>
    {
        {"01", "Checksum error" },
        {"10", "Syntax error" },
        {"11", "Data length error" },
        {"12", "Invalid data" },
        {"13", "Invalid operating mode" },
        {"14", "Invalid action" },
        {"15", "Invalid gas" },
        {"16", "Invalid control mode" },
        {"17", "Invalid command" },
        {"24", "Calibration error" },
        {"25", "Flow too large" },
        {"27", "Too many gases in gas table" },
        {"28", "Flow cal error; valve not open" },
        {"98", "Internal device error" },
        {"99", "Internal device error" }
    };

    public static Dictionary<string, string> CommandCodes = new Dictionary<string, string>
    {
        { nameof(Setpoint), "SX" },
        { nameof(FlowRate), "FX" },
        { nameof(TrackedFlow), "FT" },      // "Flow total" in the manual
        { "AutoZero", "AZ" },               // requires CAL_MODE
        { "Errors", "T" },
        { "Status Reset", "SR" },
        { "Gas Search", "GN" },
        { "Device Type", "DT" },
        { "Valve Type", "VT" },
        { "Manufacturer", "MF" },
        { "Model", "MD" },
        { "Serial Number", "SN" },
        { "Flow units", "U" },
        { nameof(MaximumSetpoint), "FS" },  // "Full Scale Range"
        { "Operating Mode", "OM" },         // RUN_MODE | CAL_MODE
        { "Address", "CA" },
        { nameof(ProgrammedGas), "PG" },
        { "Baud", "CC" },
    };

    #endregion Device constants

    [JsonProperty("ShutoffValve")]
    string ShutoffValveName { get => ShutoffValve?.Name; set => shutoffValveName = value; }
    string shutoffValveName;
    /// <summary>
    /// The gas supply shutoff valve.
    /// </summary>
    public IValve ShutoffValve
    {
        get => shutoffValve;
        set => Ensure(ref shutoffValve, value, NotifyPropertyChanged);
    }
    IValve shutoffValve;

    protected virtual bool LogEverything => Manager?.LogEverything ?? false;
    protected virtual LogFile Log => Manager?.Log;
    protected virtual void SoftLog(string message)
    {
        if (LogEverything) Log.Record(message);
    }


    public double FlowRate
    { 
        get => flowRate;
        protected set => Ensure(ref flowRate, value);
    }
    double flowRate;

    public void CheckFlowRate()
    {
        requests.Enqueue($"{CommandCodes[nameof(FlowRate)]}?");
    }
    public void CheckProgrammedGas()
    {
        requests.Enqueue($"{CommandCodes[nameof(ProgrammedGas)]}?");
    }
    public void CheckMaximumSetpoint()
    {
        requests.Enqueue($"{CommandCodes[nameof(MaximumSetpoint)]}?");
    }
    public void CheckSetpoint()
    {
        requests.Enqueue($"{CommandCodes[nameof(Setpoint)]}?");
    }
    public void CheckTrackedFlow()
    {
        requests.Enqueue($"{CommandCodes[nameof(TrackedFlow)]}?");
    }

    public double TrackedFlow
    {
        get => trackedFlow;
        set => Ensure(ref trackedFlow, value);
    }
    double trackedFlow;

    public string ProgrammedGas
    {
        get => programmedGas;
        set => Ensure(ref programmedGas, value);
    }
    string programmedGas;


    Thread updateThread;
    AutoResetEvent updateSignal = new AutoResetEvent(false);

    ConcurrentQueue<string> requests = new();

    int idleCheck = 0;
    List<string> idleChecks = new List<string>()
    {
        CommandCodes[nameof(FlowRate)] + "?",
        CommandCodes[nameof(TrackedFlow)] + "?",
    };

    public string PendingRequest
    { 
        get => pendingRequest; 
        set => Ensure(ref pendingRequest, value); 
    }
    string pendingRequest = "";
    object PendingRequestLocker = new();

    DateTime requestOpened = DateTime.Now;
    void Update()
    {
        var request = "";
        var timeout = 300;
        while (!Started && !Hacs.Stopping)
            Thread.Sleep(100);

        CheckProgrammedGas();
        CheckMaximumSetpoint();
        CheckSetpoint();
        CheckTrackedFlow();
        CheckFlowRate();

        while (!Hacs.Stopping)      // maybe stopped, and stop when flow = 0;
        {
            // prevent hanging on failed request
            lock (PendingRequestLocker)
            {
                if (!PendingRequest.IsBlank() && DateTime.Now - requestOpened > TimeSpan.FromMilliseconds(3000))
                {
                    //Notify.Announce($"{Name}: Request '{PendingRequest}' failed.");
                    PendingRequest = "";
                }
            }

            if (Manager.Ready && PendingRequest.IsBlank())
            {
                if (ShutoffValve?.Idle ?? false)
                {
                    if (IsOn)
                    {
                        // Wathc for backflow (emulate check valve)
                        if (ShutoffValve.IsOpened && FlowRate < -0.5)
                            Task.Run(ShutoffValve.CloseWait);
                        else if (ShutoffValve.IsClosed && FlowRate > -0.25)     // TODO: accommodate zero-drift
                            Task.Run(ShutoffValve.OpenWait);
                    }
                    else if (ShutoffValve.IsOpened)
                    {
                        Task.Run(ShutoffValve.CloseWait);
                    }
                }

                if (!requests.TryDequeue(out request))
                {
                    if (UpdatesReceived > 10 && Device.Setpoint != Config.Setpoint)
                    {
                        SetSetpoint(Config.Setpoint);
                        continue;
                    }

                    if (idleCheck >= idleChecks.Count) idleCheck = 0;
                    request = idleChecks[idleCheck];
                    idleCheck++;
                }
                // instead of a property name, request is a command code with an optional value
                NotifyConfigChanged(request);
                lock (PendingRequestLocker) PendingRequest = request;
                requestOpened = DateTime.Now;
            }
            if (updateSignal.WaitOne(timeout))
                Thread.Sleep(5);        // After ValidateResponse(), give Manager some time to
                                        // determine the prior request was satisfied.
        }
    }

    public void ZeroNow()
    {
        requests.Enqueue(CommandCodes["Operating Mode"] + $"!CAL_MODE");
        requests.Enqueue(CommandCodes["AutoZero"] + $"!");
        requests.Enqueue(CommandCodes["Operating Mode"] + $"!RUN_MODE");
        updateSignal.Set();
    }
    public void ResetTrackedFlow()
    {
        requests.Enqueue(CommandCodes[nameof(TrackedFlow)] + "!0");
        updateSignal.Set();
    }

    /// <summary>
    /// Set the flow rate to the given value in standard cubic centimeters per minute.
    /// </summary>
    /// <param name="setpoint">sccm</param>
    public void SetSetpoint(double setpoint)
    {
        if (setpoint == Setpoint) return;
        Setpoint = setpoint;
        requests.Enqueue($"{CommandCodes[nameof(Setpoint)]}!{setpoint}");
        CheckSetpoint();
        updateSignal.Set();
    }

    /// <summary>
    /// Set the flow rate to the given value in standard cubic centimeters per minute.
    /// </summary>
    /// <param name="setpoint">sccm</param>
    public override void TurnOn(double setpoint)
    {
        if (!Initialized) return;
        SetSetpoint(setpoint);
        Device.OnOffState = OnOffState.On;
    }

    public override bool TurnOn()
    {
        if (!Initialized) return false;
        Device.OnOffState = OnOffState.On;
        return true;
    }

    public override bool TurnOff()
    {
        if (!Initialized) return false;
        SetSetpoint(0);
        Device.OnOffState = OnOffState.Off;
        return true;
    }

    public int InstrumentAddress => Manager is IDeviceManager m ? int.Parse(m.Keys[this]) : 0;

    // Checksum verfication can be skipped by sending FF.
    // If FF is sent, the response checksum will also be FF.
    // command contains the command code and a ? or ! and possibly a value
    string EncodedCommand(string command)
    {
        // For queries "?" is appended to the command
        // To set a value, "!" is appended, followed by the.
        var cmd = $"@{InstrumentAddress:000}{command};";

        // Initialize to 0xFF to disable checksum; 0 to enable it.
        int checksum16 = 0xFF;  
        if (checksum16 == 0)
        {
            foreach (var c in cmd)
                checksum16 += c;
            checksum16 &= 0xFF;
        }
        return $"@@{cmd}{checksum16:X2}";
    }

    public (string command, int responsesExpected) ServiceValues(string request)
    {
        // service only encoded requests
        if (request.Includes("!") || request.Includes("?"))
        {
            if (PendingRequest == request)
                return (EncodedCommand(request), 1);

            lock (PendingRequestLocker) PendingRequest = "";    // cancel failed command
        }
        return ("", 0);
    }

    // "@@@000" + (("ACK" + response) | ("NAK" + errorCode)) + ";" + checkSum16 | "FF"
    public bool ValidateResponse(string request, string response, int which)
    {

        if (response.IsBlank())
        {
            SoftLog($"{Name} ({request}): Empty response is invalid.");
            return false;
        }

        if (!response.StartsWith("@@@000", StringComparison.Ordinal)) 
        {
            SoftLog($"{Name} ({request}): Incorrect prefix.");
            return false;
        }

        var len = response.Length;
        var termpos = len - 3;

        if (termpos < 9 || response[termpos] != ';')
        {
            SoftLog($"{Name} ({request}): Required terminator ';' is missing or checksum length is wrong.");
            return false;
        }

        var checksumText = response[^2..];
        if (checksumText != "FF")       // FF means skip checksum
        {
            int checksumReceived = 0;
            try
            {
                checksumReceived = int.Parse(checksumText, NumberStyles.HexNumber);
            }
            catch
            {
                SoftLog($"{Name} ({request}): Invalid checksum received: {checksumText}.");
                return false;
            }

            int checksum16 = 0;
            foreach (var c in response[0..(termpos+1)])
                checksum16 += c;
            checksum16 &= 0xFF;
            if (checksum16 != checksumReceived)
            {
                SoftLog($"{Name} ({request}): Checksum mismatch: received {checksumText}, computed {checksum16:X2}.");
                return false;
            }
        }

        var ack = response[6..9];
        var payload = response[9..termpos];     // empty string is ok

        if (ack == "NAK")
        {
            var error = ErrorCodes.GetValueOrDefault(payload);
            if (error.IsBlank()) error = "Unknown error";
            var msg = $"{Name} ({request}): {error}";
            SoftLog(msg);
            Notify.Announce(msg);
            return false;
        }
        else if (ack != "ACK")
        {
            SoftLog($"{Name} ({request}): Required ACK or NAK is missing.");
            return false; // invalid response
        }

        // valid response; info is in payload
        // now interpret and act on payload in context of request
        bool hurry = !requests.IsEmpty;

        if (request.StartsWith(CommandCodes[nameof(ProgrammedGas)] + "?"))
        {
            ProgrammedGas = payload;
        }
        else if (request.StartsWith(CommandCodes[nameof(MaximumSetpoint)] + "?"))
        {
            try { MaximumSetpoint = double.Parse(payload); }
            catch { SoftLog($"{Name} ({request}): Invalid Full Scale value (MaximumSetpoint): '{payload}'."); }
            hurry = true;
        }
        else if (request.StartsWith(CommandCodes[nameof(Setpoint)] + "?"))
        {
            try { Device.Setpoint = double.Parse(payload); }
            catch { SoftLog($"{Name} ({request}): Invalid Setpoint value: '{payload}'."); }
        }
        else if (request.StartsWith(CommandCodes[nameof(FlowRate)] + "?"))
        {
            try { FlowRate = double.Parse(payload); }
            catch { SoftLog($"{Name} ({request}): Invalid Flow Rate value: '{payload}'."); }
        }
        else if (request.StartsWith(CommandCodes[nameof(TrackedFlow)] + "?"))
        {
            try { TrackedFlow = double.Parse(payload); }
            catch { SoftLog($"{Name} ({request}): Invalid Tracked Flow value: '{payload}'."); }
        }
        lock (PendingRequestLocker) PendingRequest = "";

        if (hurry) updateSignal.Set();

        return true;
    }

    public ManagedMFC(IHacsDevice d = null) : base(d)
    {
        ManagedDevice = new ManagedDevice(this);
    }

    public override string ToString()
    {
        return $"{Name}: {FlowRate:0.00} sccm ({TrackedFlow:0} scc total)" +
            $"\r\nSP = {Setpoint:0.00} sccm, Max = {MaximumSetpoint:0.00}, Gas = {ProgrammedGas}" +
            IndentLines(ManagedDevice.ManagerString(this));
    }
}
