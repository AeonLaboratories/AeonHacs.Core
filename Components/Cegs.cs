using AeonHacs.Utilities;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static AeonHacs.Components.CegsPreferences;
using static AeonHacs.Notify;
using static AeonHacs.Utilities.Utility;

namespace AeonHacs.Components;

public class Cegs : ProcessManager, ICegs
{
    #region HacsComponent

    [HacsPreConnect]
    protected virtual void PreConnect()
    {
        #region Logs
        SampleLog = Find<HacsLog>(samplelogName);
        TestLog = Find<HacsLog>(testlogName);
        SampleRecords = Find<HacsLog>(sampleRecordsName);

        List<string> somePotentialPressureLogNames = new()
        {
            "VMPressureLog",
            "VM1PressureLog",
            "VM2PressureLog"
        };
        somePotentialPressureLogNames.ForEach((logName) =>
        {
            if (Find<DataLog>(logName) is DataLog log)
                log.Changed = (col) => col.Resolution > 0 && col.Source is Manometer m ?
                    (col.PriorValue is double p ?
                        Manometer.SignificantChange(p, m.Pressure) :
                        true) :
                    false;
        });

        #endregion Logs
    }

    [HacsConnect]
    protected virtual void Connect()
    {
        Sample = Find<Sample>(sampleName);
        if (inletPort == null)
            inletPort = FirstOrDefault<IInletPort>();
        if (inletPort == null)
            ConfigurationError("No InletPort exists; a CEGS must have at least one.");

        Power = Find<Power>(powerName);
        Ambient = Find<Chamber>(ambientName);

        IM = Find<Section>(imName);
        CT = Find<Section>(ctName);
        VTT = Find<Section>(vttName);
        MC = Find<Section>(mcName);
        Split = Find<Section>(splitName);
        GM = Find<Section>(gmName);
        VTT_MC = Find<Section>(vtt_mcName);
        MC_Split = Find<Section>(mc_splitName);

        d13C = Find<Section>(_d13CName);
        d13CM = Find<Section>(_d13CMName);

        ugCinMC = Find<Meter>(sampleMeterName);
        IpOvenRamper = Find<OvenRamper>(ipOvenRamperName);

        InletPorts = CachedList<IInletPort>();
        GraphiteReactors = CachedList<IGraphiteReactor>();
        d13CPorts = CachedList<Id13CPort>();
    }


    /// <summary>
    /// Notify the operator if the required object is missing.
    /// </summary>
    private void CegsNeeds(object obj, string objName, string expectedName)
    {
        if (obj is not null)
            return;

        ConfigurationError($"Can't find {objName} \"{expectedName}\".");
    }


    [HacsPostConnect]
    protected virtual void PostConnect()
    {
        // check that the essentials were found
        CegsNeeds(Ambient, nameof(Ambient), ambientName);
        CegsNeeds(IM, nameof(IM), imName);
        CegsNeeds(VTT, nameof(VTT), vttName);
        CegsNeeds(MC, nameof(MC), mcName);
        CegsNeeds(Split, nameof(Split), splitName);
        CegsNeeds(GM, nameof(GM), gmName);
        CegsNeeds(VTT_MC, nameof(VTT_MC), vtt_mcName);
        CegsNeeds(MC_Split, nameof(MC_Split), mc_splitName);
        CegsNeeds(ugCinMC, nameof(ugCinMC), sampleMeterName);

        foreach (var coldfinger in Coldfingers.Values)
        {
            var cf = coldfinger;
            cf.SlowToFreeze += () => OnSlowToFreeze(cf);
        }

        if (Power is Power p)
        {
            p.MainsDown += OnMainsDown;
            p.MainsFailed += OnMainsFailed;
            p.MainsRestored += OnMainsRestored;
        }

        foreach (var manifold in LNManifolds.Values)
        {
            var x = manifold;
            x.OverflowDetected += () => OnOverflowDetected(x);
            x.SlowToFill += () => OnSlowToFill(x);
        }

        //ugCinMC depends on both of these, but they are both updated
        //every daq cycle so only one needs to trigger the update
        //MC.Manometer.PropertyChanged += UpdateSampleMeasurement;
        MC.Thermometer.PropertyChanged += UpdateSampleMeasurement;
        MaximumAliquotsPerSample = 1 + MC.Ports?.Count ?? 0;

        // TODO: move this functionality into VolumeCalibration class
        foreach (var c in VolumeCalibrations.Values)
        {
            c.ProcessStep = ProcessStep;
            c.ProcessSubStep = ProcessSubStep;
            c.OpenLine = OpenLine;
            c.OkPressure = OkPressure;
            c.Log = TestLog;
        }
    }

    [HacsPostStart]
    protected virtual void PostStart()
    {
        Stopping = false;
        StartThreads();
        SystemUpTime.Start();
        Started = true;
        SaveSettingsToFile("startup.json");
    }


    [HacsPreStop]
    protected virtual void PreStop()
    {
        try
        {
            Hacs.SystemLog.Record("System shutting down.");
            Stopping = true;

            lowPrioritySignal.Set();
            stoppedSignal2.WaitOne();

            // Note: controllers of multiple devices should shutdown in Stop()
            // The devices they control should have their shutdown states
            // effected in PreStop()

        }
        catch (Exception e)
        {
            Hacs.SystemLog.Record(e.ToString());
        }
    }

    protected bool StopLogging { get; set; } = false;


    [HacsPostStop]
    protected virtual void PostStop()
    {
        try
        {
            UpdateTimer?.Dispose();

            StopLogging = true;
            SystemLogSignal.Set();
            stoppedSignal1.WaitOne();

            SaveSettings();

            SerialPortMonitor.Stop();
        }
        catch (Exception e)
        {
            Hacs.SystemLog.Record(e.ToString());
        }
    }

    /// <summary>
    /// system status logs stopped
    /// </summary>
    ManualResetEvent stoppedSignal1 = new ManualResetEvent(true);

    /// <summary>
    /// low priority activities stopped
    /// </summary>
    ManualResetEvent stoppedSignal2 = new ManualResetEvent(true);
    public new bool Stopped => stoppedSignal1.WaitOne(0) && stoppedSignal2.WaitOne(0);
    protected bool Stopping { get; set; }

    #endregion HacsComponent

    #region System configuration

    #region Component lists
    [JsonProperty] public Dictionary<string, IDeviceManager> DeviceManagers { get; set; }
    [JsonProperty] public Dictionary<string, IManagedDevice> ManagedDevices { get; set; }
    [JsonProperty] public Dictionary<string, IMeter> Meters { get; set; }
    [JsonProperty] public Dictionary<string, IValve> Valves { get; set; }
    [JsonProperty] public Dictionary<string, IHeater> Heaters { get; set; }
    [JsonProperty] public Dictionary<string, IPidSetup> PidSetups { get; set; }

    [JsonProperty] public Dictionary<string, ILNManifold> LNManifolds { get; set; }
    [JsonProperty] public Dictionary<string, IColdfinger> Coldfingers { get; set; }

    [JsonProperty] public Dictionary<string, IVacuumSystem> VacuumSystems { get; set; }
    [JsonProperty] public Dictionary<string, IChamber> Chambers { get; set; }
    [JsonProperty] public Dictionary<string, ISection> Sections { get; set; }
    [JsonProperty] public Dictionary<string, IGasSupply> GasSupplies { get; set; }
    [JsonProperty] public Dictionary<string, IFlowManager> FlowManagers { get; set; }
    [JsonProperty] public Dictionary<string, IVolumeCalibration> VolumeCalibrations { get; set; }
    [JsonProperty] public Dictionary<string, IHacsLog> Logs { get; set; }

    // The purpose of the FindAll().ToDictionary is to automate deletions
    // from the settings file (i.e., to avoid needing a backing variable and
    // Samples.Remove())
    [JsonProperty]
    public Dictionary<string, Sample> Samples
    {
        get => FindAll<Sample>().ToDictionary(s => s.Name, s => s);
        set { }
    }

    [JsonProperty] public Dictionary<string, IHacsComponent> HacsComponents { get; set; }

    #endregion Component lists

    #region HacsComponents
    [JsonProperty("Power")]
    string PowerName { get => Power?.Name; set => powerName = value; }
    string powerName = "Power";
    /// <summary>
    /// The power monitor.
    /// </summary>
    public virtual Power Power
    {
        get => power;
        set => Ensure(ref power, value, NotifyPropertyChanged);
    }
    Power power;


    #region Data Logs
    [JsonProperty("SampleLog"), DefaultValue("SampleLog")]
    string SampleLogName { get => SampleLog?.Name; set => samplelogName = value; }
    string samplelogName;
    /// <summary>
    /// The sample data log.
    /// </summary>
    public virtual HacsLog SampleLog
    {
        get => samplelog;
        set => Ensure(ref samplelog, value, NotifyPropertyChanged);
    }
    HacsLog samplelog;

    [JsonProperty("TestLog"), DefaultValue("TestLog")]
    string TestLogName { get => TestLog?.Name; set => testlogName = value; }
    string testlogName;
    /// <summary>
    /// The test data log.
    /// </summary>
    public virtual HacsLog TestLog
    {
        get => testlog;
        set => Ensure(ref testlog, value, NotifyPropertyChanged);
    }
    HacsLog testlog;

    [JsonProperty("SampleRecords"), DefaultValue("SampleRecords")]
    string SampleRecordsName { get => SampleRecords?.Name; set => sampleRecordsName = value; }
    string sampleRecordsName;
    /// <summary>
    /// Sample data in tabular form.
    /// </summary>
    public virtual HacsLog SampleRecords
    {
        get => sampleRecords;
        set => Ensure(ref sampleRecords, value, NotifyPropertyChanged);
    }
    HacsLog sampleRecords;
    #endregion Data Logs

    [JsonProperty("Ambient"), DefaultValue("Ambient")]
    string AmbientName { get => Ambient?.Name; set => ambientName = value; }
    string ambientName;
    /// <summary>
    /// The ambient 'Chamber'.
    /// </summary>
    public virtual IChamber Ambient
    {
        get => ambient;
        set => Ensure(ref ambient, value, NotifyPropertyChanged);
    }
    IChamber ambient;

    [JsonProperty("InletManifold"), DefaultValue("IM")]
    string IMName { get => IM?.Name; set => imName = value; }
    string imName;
    /// <summary>
    /// The default Inlet Manifold Section, IM.
    /// </summary>
    public virtual ISection IM
    {
        get => im;
        set => Ensure(ref im, value, NotifyPropertyChanged);
    }
    ISection im;

    [JsonProperty("CoilTrap"), DefaultValue("CT")]
    string CTName { get => CT?.Name; set => ctName = value; }
    string ctName;
    /// <summary>
    /// The default Coil Trap Section, CT.
    /// </summary>
    public virtual ISection CT
    {
        get => ct;
        set => Ensure(ref ct, value, NotifyPropertyChanged);
    }
    ISection ct;

    [JsonProperty("VariableTemperatureTrap"), DefaultValue("VTT")]
    string VTTName { get => VTT?.Name; set => vttName = value; }
    string vttName;
    /// <summary>
    /// The default Variable Temperature Trap Section, VTT.
    /// </summary>
    public virtual ISection VTT
    {
        get => vtt;
        set => Ensure(ref vtt, value, NotifyPropertyChanged);
    }
    ISection vtt;

    [JsonProperty("MeasurementChamber"), DefaultValue("MC")]
    string MCName { get => MC?.Name; set => mcName = value; }
    string mcName;
    /// <summary>
    /// The Measurement Chamber Section, MC.
    /// </summary>
    public virtual ISection MC
    {
        get => mc;
        set => Ensure(ref mc, value, NotifyPropertyChanged);
    }
    ISection mc;

    [JsonProperty("SplitSection"), DefaultValue("Split")]
    string SplitName { get => Split?.Name; set => splitName = value; }
    string splitName;
    /// <summary>
    /// The discard Split Section, Split.
    /// </summary>
    public virtual ISection Split
    {
        get => split;
        set => Ensure(ref split, value, NotifyPropertyChanged);
    }
    ISection split;

    [JsonProperty("d13CSection"), DefaultValue("d13C")]
    string d13CName { get => d13C?.Name; set => _d13CName = value; }
    string _d13CName;
    /// <summary>
    /// The d13C volume Section, d13C.
    /// </summary>
    public virtual ISection d13C
    {
        get => _d13C;
        set => Ensure(ref _d13C, value, NotifyPropertyChanged);
    }
    ISection _d13C;

    [JsonProperty("GraphiteManifold"), DefaultValue("GM")]
    string GMName { get => GM?.Name; set => gmName = value; }
    string gmName;
    /// <summary>
    /// The default Graphite Manifold Section, GM.
    /// </summary>
    public virtual ISection GM
    {
        get => gm;
        set => Ensure(ref gm, value, NotifyPropertyChanged);
    }
    ISection gm;

    [JsonProperty("d13CManifold"), DefaultValue("d13CM")]
    string d13CMName { get => d13CM?.Name; set => _d13CMName = value; }
    string _d13CMName;
    /// <summary>
    /// The default d13C Manifold Section, d13CM.
    /// </summary>
    public virtual ISection d13CM
    {
        get => _d13CM;
        set => Ensure(ref _d13CM, value, NotifyPropertyChanged);
    }
    ISection _d13CM;

    [JsonProperty("VTT_MC"), DefaultValue("VTT_MC")]
    string VTT_MCName { get => VTT_MC?.Name; set => vtt_mcName = value; }
    string vtt_mcName;
    /// <summary>
    /// The VTT-MC Section.
    /// </summary>
    public virtual ISection VTT_MC
    {
        get => vtt_mc;
        set => Ensure(ref vtt_mc, value, NotifyPropertyChanged);
    }
    ISection vtt_mc;

    [JsonProperty("MC_Split"), DefaultValue("MC_Split")]
    string MC_SplitName { get => MC_Split?.Name; set => mc_splitName = value; }
    string mc_splitName;
    /// <summary>
    /// The MC-Split Section.
    /// </summary>
    public virtual ISection MC_Split
    {
        get => mc_split;
        set => Ensure(ref mc_split, value, NotifyPropertyChanged);
    }
    ISection mc_split;

    // insist on an actual Meter, to enable implicit double
    [JsonProperty("SampleMeter"), DefaultValue("ugCinMC")]
    string SampleMeterName { get => ugCinMC?.Name; set => sampleMeterName = value; }
    string sampleMeterName;
    /// <summary>
    /// The Sample Meter, ugCinMC.
    /// </summary>
    public virtual Meter ugCinMC
    {
        get => _ugCinMC;
        set => Ensure(ref _ugCinMC, value, NotifyPropertyChanged);
    }
    Meter _ugCinMC;

    public virtual double umolCinMC => ugCinMC.Value / gramsCarbonPerMole;

    [JsonProperty("IpOvenRamper"), DefaultValue("IpOvenRamper")]
    string IpOvenRamperName { get => IpOvenRamper?.Name; set => ipOvenRamperName = value; }
    string ipOvenRamperName;
    /// <summary>
    /// Ramped temperature controller for Inlet Port
    /// </summary>
    public virtual OvenRamper IpOvenRamper
    {
        get => ipOvenRamper;
        set => Ensure(ref ipOvenRamper, value, NotifyPropertyChanged);
    }
    OvenRamper ipOvenRamper;


    /// <summary>
    /// The sample gas collection path.
    /// </summary>
    protected virtual ISection IM_FirstTrap
    {
        get
        {
            if (im_FirstTrap != null) return im_FirstTrap;
            if (!IpIm(out ISection im)) return null;

            var trap = CT ?? VTT;
            if (trap == null)
            {
                ConfigurationError("Can't find first trap");
                return null;
            }

            var im_trap = JoinedSection(im, trap);
            if (im_trap == null)
            {
                ConfigurationError($"Can't find Section linking {im.Name} and {trap.Name}");
            }

            return im_trap;
        }
        set => Ensure(ref im_FirstTrap, value);
    }
    ISection im_FirstTrap = null;

    /// <summary>
    /// The Section containing only one chamber, the first trap.
    /// </summary>
    protected virtual ISection FirstTrap
    {
        get
        {
            var chamber = IM_FirstTrap?.Chambers?.Last();
            if (chamber == null)        // error handled in IM_FirstTrap
                return null;
            var trap = FirstOrDefault<Section>(s =>
               s.Chambers?.First() == chamber &&
               s.Chambers.Count() == 1);
            if (trap == null)
            {
                ConfigurationError($"Can't find a Section containing only chamber {chamber.Name}.");
            }
            return trap;
        }
    }

    #endregion HacsComponents

    #region Constants

    [JsonProperty]
    public virtual CegsPreferences CegsPreferences { get; set; }

    #region Globals

    #region UI Communications

    public virtual Func<bool, List<ISample>> SelectSamples { get; set; }

    #endregion UI Communications

    public override bool Busy
    {
        get => base.Busy || !Free;
        protected set => base.Busy = value;
    }

    public bool Free
    {
        get => free;
        protected set
        {
            if (Ensure(ref free, value))
            {
                NotifyPropertyChanged(nameof(Busy));
                NotifyPropertyChanged(nameof(NotBusy));
            }
        }
    }
    bool free = true;

    [JsonProperty]
    public bool AutoFeedEnabled
    {
        get => autoFeedEnabled;
        set => Ensure(ref autoFeedEnabled, value);
    }
    bool autoFeedEnabled = false;

    public virtual List<IInletPort> InletPorts { get; set; }
    public virtual List<IGraphiteReactor> GraphiteReactors { get; set; }
    public virtual List<Id13CPort> d13CPorts { get; set; }

    [JsonProperty("Sample")]
    string SampleName { get => Sample?.Name; set => sampleName = value; }
    string sampleName;
    public virtual ISample Sample
    {
        get => sample;
        set
        {
            void samplePropertyChanged(object sender, PropertyChangedEventArgs e)
            {
                InletPort = sample?.InletPort;
                NotifyPropertyChanged(nameof(Sample));
            }

            Ensure(ref sample, value, samplePropertyChanged);
        }
    }
    ISample sample;

    public virtual IInletPort InletPort
    {
        get => inletPort;
        set
        {
            if (value != null)
                Ensure(ref inletPort, value);
        }
    }
    IInletPort inletPort;

    protected virtual Id13CPort d13CPort
    {
        get => _d13CPort ?? Guess_d13CPort();
        set => _d13CPort = value;
    }
    Id13CPort _d13CPort;

    protected virtual Id13CPort Guess_d13CPort() =>
        Guess_d13CPort(Sample);

    protected virtual Id13CPort Guess_d13CPort(ISample sample) =>
        Guess_d13CPort(sample?.InletPort);

    protected virtual Id13CPort Guess_d13CPort(IInletPort inletPort)
    {
        if (inletPort?.Name is string ipName &&
            ipName.StartsWith("IP") &&
            ipName.Length > 2 &&
            FirstOrDefault<Id13CPort>(p => p.Name.EndsWith(ipName[2..]) && !p.ShouldBeClosed) is Id13CPort p)
            return p;
        return FirstOrDefault<Id13CPort>(p => !p.ShouldBeClosed);
    }

    #endregion Globals



    #endregion Constants

    #endregion System configuration

    #region System elements not saved/restored in Settings

    public new virtual bool Started { get; protected set; }

    protected Action DuringBleed;

    #region Threading

    public Timer UpdateTimer { get; set; }

    // logging
    Thread systemLogThread;
    protected AutoResetEvent SystemLogSignal { get; private set; } = new AutoResetEvent(false);

    // low priority activity
    Thread lowPriorityThread;
    protected AutoResetEvent lowPrioritySignal { get; private set; } = new AutoResetEvent(false);

    #endregion Threading

    // system conditions
    protected Stopwatch SystemUpTime { get; private set; } = new Stopwatch();
    public virtual TimeSpan Uptime => SystemUpTime.Elapsed;

    // process management
    public virtual bool SampleIsRunning => ProcessSequenceIsRunning;

    #endregion System elements not saved in/restored from Settings

    #region Startup and ShutDown

    protected virtual void StartThreads()
    {
        Hacs.SystemLog.Record("System Started");
        systemLogThread = new Thread(LogSystemStatus)
        {
            Name = $"{Name} logSystemStatus",
            IsBackground = true
        };
        systemLogThread.Start();

        lowPriorityThread = new Thread(LowPriorityActivities)
        {
            Name = $"{Name} lowPriorityActivities",
            IsBackground = true
        };
        lowPriorityThread.Start();

        UpdateTimer = new Timer(UpdateTimerCallback, null, 0, UpdateIntervalMilliseconds);
    }

    #endregion Startup and ShutDown

    #region elementary utility functions

    /// <summary>
    /// From (pv = nkt): n = pv / kt
    /// </summary>
    /// <param name="pressure">Torr</param>
    /// <param name="volume">milliliters</param>
    /// <param name="temperature">°C</param>
    /// <returns>number of particles</returns>
    protected virtual double Particles(double pressure, double volume, double temperature) =>
        pressure * volume / BoltzmannConstantTorr_mL / (ZeroDegreesC + temperature);

    /// <summary>
    /// Temperature-dependent pressure for number of particles in a fixed volume (in milliliters).
    /// </summary>
    /// <param name="particles">number of particles</param>
    /// <param name="volume">milliliters</param>
    /// <returns></returns>
    protected virtual double TorrPerKelvin(double particles, double volume) =>
        particles * BoltzmannConstantTorr_mL / volume;

    /// <summary>
    /// From (pv = nkt): p = nkt / v.
    /// Units are Torr, K, milliliters
    /// </summary>
    /// <param name="particles">number of particles</param>
    /// <param name="volume">milliliters</param>
    /// <param name="temperature">°C</param>
    /// <returns>pressure in Torr</returns>
    protected virtual double Pressure(double particles, double volume, double temperature) =>
        (ZeroDegreesC + temperature) * TorrPerKelvin(particles, volume);

    /// <summary>
    /// The mass of carbon in a quantity of CO2 gas, given its pressure, volume and temperature.
    /// </summary>
    /// <param name="pressure">Torr</param>
    /// <param name="volume">mL</param>
    /// <param name="temperature">°C</param>
    /// <returns></returns>
    protected virtual double MicrogramsCarbon(double pressure, double volume, double temperature) =>
        Particles(pressure, volume, temperature) / CarbonAtomsPerMicrogram;

    /// <summary>
    /// The mass of carbon in the chamber, assuming it contains only gaseous CO2.
    /// </summary>
    /// <param name="ch"></param>
    /// <returns></returns>
    protected virtual double MicrogramsCarbon(IChamber ch) =>
        MicrogramsCarbon(ch.Pressure, ch.MilliLiters, ch.Temperature);

    /// <summary>
    /// The mass of carbon in the section, assuming it contains only gaseous CO2.
    /// </summary>
    /// <param name="section"></param>
    /// <returns></returns>
    protected virtual double MicrogramsCarbon(ISection section) =>
        MicrogramsCarbon(section.Pressure, section.CurrentVolume(true), section.Temperature);

    #endregion elementary utility functions

    #region Periodic system activities & maintenance

    #region Logging
    protected virtual void LogSystemStatus()
    {
        stoppedSignal1.Reset();
        try
        {
            bool exceptionAnnounced = false;
            while (!StopLogging)
            {
                if (!Started) continue;
                try { HacsLog.UpdateAll(); }
                catch (Exception e)
                {
                    if (!exceptionAnnounced)
                    {
                        Tell("Exception while updating logs", 
                            e.ToString(), NoticeType.Error);
                        exceptionAnnounced = true;
                    }
                }
                SystemLogSignal.WaitOne(500);
            }
        }
        catch (Exception e)
        {
            Tell("Logs will no longer be updated.", 
                e.ToString(), type: NoticeType.Error);
        }
        stoppedSignal1.Set();
    }

    #endregion Logging

    protected virtual Sample GetSampleToRun()
    {
        var sampleFile = "runSample.json";
        var errorFile = "runSampleError.json";
        var activeFile = "runSampleActive.json";

        try
        {
            try { File.Delete(activeFile); } catch { }
            File.Move(sampleFile, activeFile, true);
            var jsonData = File.ReadAllText(activeFile);
            var runSample = JsonConvert.DeserializeObject<Sample>(jsonData);
            //runSample.Connect();
            try { File.Delete(errorFile); } catch { }
            return runSample;
        }
        catch
        {
            try { File.Move(activeFile, errorFile, true); } catch { }
            return null;
        }
    }

    protected virtual void RunAProvidedSample()
    {
        if (Busy || !AutoFeedEnabled) return;
        try
        {
            if (GetSampleToRun() is Sample sample)
            {
                var ip = sample.InletPort;
                if (ip == null) ip = InletPorts.FirstOrDefault();
                if (ip != null)
                {
                    ip.Sample = sample;
                    ip.State = LinePort.States.Prepared;    // assume this was done by the sample provider
                    if (Sample is Sample) Sample.State = Components.Sample.States.Prepared;
                    Task.Run(() => RunSample(sample));
                }
            }
        }
        catch { }   // ignore any errors
    }

    bool updateExceptionOccurred;
    protected virtual void UpdateTimerCallback(object state)
    {
        try
        {
            if (!Stopping)
                Update();
        }
        catch (Exception e)
        {
            if (updateExceptionOccurred) return;

            Announce("Exception in CEGS Update loop",
                $"{e}\r\n" +
                $"System should be restarted.\r\n" +
                $"This message will not be repeated.", 
                NoticeType.Error);

            updateExceptionOccurred = true;
        }
    }

    int msUpdateLoop = 0;
    bool allDaqsOk = false;
    bool monitorPower = false;
    List<IDaq> daqs = CachedList<IDaq>();
    protected virtual void Update()
    {
        #region DAQs
        var daqsOk = true;
        foreach (var daq in daqs)
        {
            if (!daq.IsUp)
            {
                daqsOk = false;
                if (!daq.IsStreaming)
                    Hacs.EventLog.LogParsimoniously(daq.Name + " is not streaming");
                else if (!daq.DataAcquired)
                    Hacs.EventLog.LogParsimoniously(daq.Name + ": waiting for stream to start");
                else
                {
                    var error = daq.Error;
                    if (error != default)
                        Hacs.EventLog.LogParsimoniously(error);
                    daq.ClearError();
                }
            }
        }
        if (!allDaqsOk && daqsOk)
        {
            var ugcFilter = ugCinMC?.Filter as ButterworthFilter;
            var pMCFilter = MC?.Manometer?.Filter as ButterworthFilter;
            if (ugcFilter != null && pMCFilter != null)
                ugcFilter.SamplingFrequency = pMCFilter.SamplingFrequency;
        }
        allDaqsOk = daqsOk;
        #endregion DAQs

        #region Power failure watchdog
        if (!monitorPower)
            monitorPower = EnableWatchdogs && (Power?.MainsDetect?.UpdatesReceived ?? 0) > 5;
        else
            Power?.Update();
        #endregion Power failure watchdog

        #region 200 ms
        if (daqsOk && msUpdateLoop % 200 == 0)
        {
            SystemLogSignal.Set();
        }
        #endregion 200 ms

        #region 500 ms
        if (daqsOk && Started && msUpdateLoop % 500 == 0)
        {
            lowPrioritySignal.Set();
        }
        #endregion 500 ms

        #region 1000 ms
        if (daqsOk && Started && msUpdateLoop % 1000 == 0)
        {
            DeleteCompletedSamples();
            RunAProvidedSample();
        }
        #endregion 1000 ms

        if (msUpdateLoop % 3600000 == 0) msUpdateLoop = 0;
        msUpdateLoop += UpdateIntervalMilliseconds;
    }

    protected virtual void PostUpdateGR(IGraphiteReactor gr)
    {
        if (gr.Busy)
        {
            // GR.State is "Stop" for exactly one GR.Update() cycle.
            if (gr.State == GraphiteReactor.States.Stop)
            {
                SampleLog.Record(
                    "Graphitization complete:\r\n" +
                    $"\tGraphite {gr.Contents}");
                if (BusyGRCount() == 1 && !SampleIsRunning)  // the 1 is this GR; "Stop" is still 'Busy'
                {
                    Announce("Last graphite reactor finished.", 
                        (PreparedGRs() < 1) ? "Graphite reactors need service." : "");
                }
            }
        }
        else if (gr.State == GraphiteReactor.States.WaitService)
        {
            if (gr.Aliquot != null)
            {
                IAliquot a = gr.Aliquot;
                if (!a.ResidualMeasured)
                {
                    double ambientTemperature = Manifold(gr).Temperature;
                    if (Math.Abs(ambientTemperature - gr.SampleTemperature) < 10 &&        // TODO: magic number
                        Math.Abs(ambientTemperature - gr.ColdfingerTemperature) < 10)
                    {
                        // residual is P/T (Torr/kelvin)
                        a.ResidualPressure = gr.Pressure / (ZeroDegreesC + ambientTemperature);

                        SampleLog.Record(
                            "Residual measurement:\r\n" +
                            $"\tGraphite {a.Name}\t{a.ResidualPressure:0.000}\tTorr/K"
                            );
                        a.ResidualMeasured = true;

                        if (a.ResidualPressure > MaximumResidual * a.ExpectedResidualPressure)
                        {
                            if (a.Tries > 1)
                            {
                                SampleLog.Record(
                                    "Excessive residual pressure. Graphitization failed.\r\n" +
                                    $"\tGraphite {a.Name}"
                                    );
                            }
                            else
                            {
                                SampleLog.Record(
                                    "Excessive residual pressure. Trying again.\r\n" +
                                    $"\tGraphite {a.Name}"
                                    );
                                gr.Start();  // try again
                                a.ResidualMeasured = false;
                            }
                        }
                    }
                }
            }
        }
    }

    #region device event handlers

    protected virtual void OnMainsDown()
    {
        Announce("Mains Power is down",
            type: NoticeType.Warning);
    }

    protected virtual void OnMainsRestored()
    {
        Announce("Mains Power restored",
            $"Power was down {Power.MainsDownTimer.ElapsedMilliseconds} ms)");
    }

    protected virtual void OnMainsFailed()
    {
        Announce("Mains Power Failure.",
            "Shutting down...", NoticeType.Error);

        ShutDownAllColdfingers();
        FindAll<IHeater>().ForEach(h => h.TurnOff());
        FindAll<IVacuumSystem>().ForEach(vs =>
        {
            vs.Isolate();
            vs.IsolateManifold();
        });
        FindAll<IGasSupply>().ForEach(gs => gs.ShutOff());

        // Shut down the application
        Hacs.Stop();
        Hacs.CloseApplication?.Invoke();
    }

    protected virtual void OnOverflowDetected(ILNManifold manifold)
    {
        Pause("LN containment failure.", 
            $"{manifold?.Name ?? "LN Manifold"} service suspended.\r\n" +
            $"Correct the issue and Ok to continue.");
    }

    protected virtual void OnSlowToFill(ILNManifold manifold)
    {
        Announce($"{manifold?.Name ?? "An LN Manifold"} is slow to fill.");
    }

    protected virtual void OnSlowToFreeze(IColdfinger coldfinger)
    {
        var coldfingers = FindAll<IColdfinger>(cf => cf.IsActivelyCooling);
        coldfingers.ForEach(cf => cf.Standby());
        var list = string.Join(", ", coldfingers.Select(cf => cf.Name));
        Pause($"{coldfinger?.Name ?? "A coldfinger"} is taking too long to freeze.",
            $"\r\nActive coldfingers set to Standby: {list}.\r\n" +
            $"The process is paused for operator.\r\n" +
            $"Restore their states manually and Ok to continue or \r\n" +
            $"Restart the application to abort the process."
        );
    }

    protected virtual void ShutDownAllColdfingers()
    {
        FindAll<IColdfinger>().ForEach(cf => cf.Standby());
        FindAll<IVTColdfinger>().ForEach(vtc => vtc.Standby());
    }
    #endregion device event handlers

    protected virtual void LowPriorityActivities()
    {
        stoppedSignal2.Reset();
        try
        {
            while (!Stopping)
            {
                if (!Hacs.Started) continue;    // Wait for all devices to be started, not just the CEGS
                if (EnableAutozero) ZeroPressureGauges();

                GraphiteReactors?.ForEach(gr => { gr.Update(); PostUpdateGR(gr); });

                SaveSettings();
                lowPrioritySignal.WaitOne(500);
            }
        }
        catch (Exception e)
        {
            Announce($"Low priority activities stopped.",
                e.ToString(), NoticeType.Error);
        }
        stoppedSignal2.Set();
    }

    /// <summary>
    /// Event handler for MC temperature and pressure changes. Whenever the MC sample 
    /// measurement (in ugC) changes, notify subscribers that umolCinMC has changed as well.
    /// </summary>
    protected virtual void UpdateSampleMeasurement(object sender = null, PropertyChangedEventArgs e = null)
    {
        if (e?.PropertyName == nameof(IValue.Value))
        {
            var ugC = ugCinMC.Value;
            ugCinMC.Update(MicrogramsCarbon(MC));
            if (ugCinMC.Value != ugC)
                NotifyPropertyChanged(nameof(umolCinMC));
        }
    }

    /// <summary>
    /// Tries to auto-zero the manometer for the 'chamber'. The chamber must
    /// be a Section or a GraphiteReactor.
    /// </summary>
    /// <param name="chamber"></param>
    protected virtual void TryToZeroManometer(IChamber chamber)
    {
        if (Busy) return;
        var gr = chamber as IGraphiteReactor;
        if (gr != null && !gr.IsOpened) return;
        var section = chamber as Section ?? Manifold(gr);
        if (section == null) return;
        if (section.VacuumSystem.TimeAtBaseline.TotalSeconds < 20) return;
        if (!section.IsOpened) return;
        var m = gr?.Manometer ?? section.Manometer;
        if (m == null) return;
        var excessiveError = 5 * m.Sensitivity;     // TODO: magic number
        if (Math.Abs(m.Value) < excessiveError) return;
        m.ZeroNow();
    }

    /// <summary>
    /// Attempts to zero the Manometers on the given chambers.
    /// </summary>
    /// <param name="chambers">A set of Sections and/or GraphiteReactors with manometers to be zeroed.</param>
    protected virtual void ZeroPressureGauges(IEnumerable<IChamber> chambers)
    {
        foreach (var chamber in chambers)
            TryToZeroManometer(chamber);
    }

    /// <summary>
    /// Auto-zero the manometers that normally need it.
    /// </summary>
    protected virtual void ZeroPressureGauges() => ZeroPressureGauges([MC, CT, VTT, IM, GM, .. GraphiteReactors]);

    /// <summary>
    /// Clear references to the sample and remove it from the NamedObject Dictionary.
    /// </summary>
    public virtual void DeleteSample(Sample s)
    {
        if (s == null) return;
        if (s.InletPort?.Sample == s)       // if it's still in its inlet port,
            s.InletPort.ClearContents();      //     remove it from there
        if (s.d13CPort?.Sample == s)        // if it's still in its d13C port,
            s.d13CPort.ClearContents();       //     remove it from there

        // Delete aliquots
        s.Aliquots.ForEach(a =>
        {
            if (!a.GraphiteReactor.IsBlank() &&
                Find<GraphiteReactor>(a.GraphiteReactor) is GraphiteReactor gr &&
                gr.Aliquot == a)
            {
                gr.ClearContents();          // also sets gr.Sample to null
            }
        });
        s.Aliquots.Clear();

        if (s == Sample)                    // if it's the currently selected sample,
            Sample = null;                  //     clear the the currently selected sample
        s.Name = null;                      // remove it from the NamedObject Dictionary.
    }

    public virtual bool OkToDeleteSample(Sample s)
    {
        if (s == null)
            return false;       // nothing to delete
        if (s.Name.IsBlank())
            return false;       // it's not in the NamedObject Dictionary
        if (s.Aliquots.Any(a => Find<GraphiteReactor>(a.GraphiteReactor)?.Aliquot == a))
            return false;       // it still has aliquots in graphite reactors
        if (s.d13CPort?.Sample == s)
            return false;       // it still has a split in a d13CPort
        if (s.InletPort?.Sample == s && s.InletPort.State != LinePort.States.Complete)
            return false;       // it still incomplete in its inlet port...
        if (s.State != Components.Sample.States.Complete)
            return false;       // it hasn't completed a process-sequence run
        return true;
    }

    public virtual bool TryDeleteSample(Sample s)
    {
        if (!OkToDeleteSample(s)) return false;
        DeleteSample(s);
        return true;
    }

    protected virtual void DeleteCompletedSamples() =>
        FindAll<Sample>(TryDeleteSample);

    #endregion Periodic system activities & maintenance

    #region Process Management

    #region Process Control Parameters
    /// <summary>
    /// Sets the named process control parameter to the given value.
    /// </summary>
    /// <param name="name"></param>
    /// <param name="value"></param>
    protected void SetParameter(string name, double value) =>
        SetParameter(new Parameter() { ParameterName = name, Value = value });

    /// <summary>
    /// Sets a process control parameter for the current Sample. Ignored if
    /// Sample is null.
    /// </summary>
    /// <param name="parameter"></param>
    public override void SetParameter(Parameter parameter) =>
        Sample?.SetParameter(parameter);

    /// <summary>
    /// Return the current value of the Sample's process control parameter with the given name,
    /// unless Sample is null, in which case return the value from CegsPreferences instead.
    /// </summary>
    /// <param name="name"></param>
    /// <returns></returns>
    public double GetParameter(string name) =>
        Sample?.Parameter(name) ?? CegsPreferences.Parameter(name);

    /// <summary>
    /// Checks for the parameter with the given name and returns true if it is 
    /// found and its value is a non-zero number; otherwise returns false.
    /// </summary>
    /// <param name="name"></param>
    public bool ParameterTrue(string name)
    {
        var p = GetParameter(name);
        return p.IsANumber() && p != 0;
    }

    /// <summary>
    /// Gets the parameter, Warns if it's not defined and pauses for Operator
    /// interaction, and finally returns the retrieved value.
    /// Once the warning is dismissed, the retrieved value is returned,
    /// even if it's not a number.
    /// </summary>
    /// <param name="parameter"></param>
    /// <returns></returns>
    public double GetValidParameter(string parameter)
    {
        var value = GetParameter(parameter);
        if (!value.IsANumber())
        {
            ConfigurationError($"Sample Parameter '{parameter}' is not defined!");
        }
        return value;
    }

    /// <summary>
    /// Sets the parameter to double.NaN.
    /// </summary>
    /// <param name="name"></param>
    protected void ClearParameter(string name) => SetParameter(name, double.NaN);

    // * * * * * * * * * * * * * * * * * * * * * * * * * * * * * *
    // Parameters only need to be defined here if they are referenced within this class.
    // * * * * * * * * * * * * * * * * * * * * * * * * * * * * * *

    /// <summary>
    /// The effective gas load below which a volume is considered leak free.
    /// </summary>
    public double LeakTightTorrLitersPerSecond => GetParameter("LeakTightTorrLitersPerSecond");

    /// <summary>
    /// How long to evacuate freshly serviced graphite reactors.
    /// </summary>
    public double GRFirstEvacuationMinutes => GetParameter("GRFirstEvacuationMinutes");

    /// <summary>
    /// Minimum Graphitization time.
    /// </summary>
    public double MinimumGRMinutes => GetParameter("MinimumGRMinutes");
    public double PressureOverAtm => GetParameter("PressureOverAtm");
    public double OkPressure => GetParameter("OkPressure");
    public double CleanPressure => GetParameter("CleanPressure");
    public double VPInitialHePressure => GetParameter("VPInitialHePressure");
    public double VPErrorPressure => GetParameter("VPErrorPressure");
    public double MaximumResidual => GetParameter("MaximumResidual");
    public double IMO2Pressure => GetParameter("IMO2Pressure");
    public double FirstTrapBleedPressure => GetParameter("FirstTrapBleedPressure");
    public double FirstTrapEndPressure => GetParameter("FirstTrapEndPressure");
    public double FirstTrapFlowBypassPressure => GetParameter("FirstTrapFlowBypassPressure");
    public double IronPreconditionH2Pressure => GetParameter("IronPreconditionH2Pressure");
    public double IMPluggedTorrPerSecond => GetParameter("IMPluggedTorrPerSecond");
    public double IMLoadedTorrPerSecond => GetParameter("IMLoadedTorrPerSecond");
    public double GRCompleteTorrPerMinute => GetParameter("GRCompleteTorrPerMinute");
    public double RoomTemperature => GetParameter("RoomTemperature");
    public double SulfurTrapTemperature => GetParameter("SulfurTrapTemperature");
    public double IronPreconditioningTemperature => GetParameter("IronPreconditioningTemperature");
    public double IronPreconditioningTemperatureCushion => GetParameter("IronPreconditioningTemperatureCushion");
    public double CollectionMinutes => GetParameter("CollectionMinutes");
    public double ExtractionMinutes => GetParameter("ExtractionMinutes");
    public double CO2TransferMinutes => GetParameter("CO2TransferMinutes");
    public double MeasurementSeconds => GetParameter("MeasurementSeconds");
    public double IronPreconditioningMinutes => GetParameter("IronPreconditioningMinutes");
    public double QuartzFurnaceWarmupMinutes => GetParameter("QuartzFurnaceWarmupMinutes");
    public double SulfurTrapMinutes => GetParameter("SulfurTrapMinutes");
    public double H2_CO2GraphitizationRatio => GetParameter("H2_CO2GraphitizationRatio");
    public double H2DensityAdjustment => GetParameter("H2DensityAdjustment");
    public double SmallSampleMicrogramsCarbon => GetParameter("SmallSampleMicrogramsCarbon");
    public double MinimumUgCThatPermits_d13CSplit => GetParameter("MinimumUgCThatPermits_d13CSplit");
    public double DilutedSampleMicrogramsCarbon => GetParameter("DilutedSampleMicrogramsCarbon");
    public double MaximumSampleMicrogramsCarbon => GetParameter("MaximumSampleMicrogramsCarbon");

    /// <summary>
    /// During sample collection, close the Inlet Port when the Inlet Manifold pressure falls to this value,
    /// provided that it is a number (i.e., not NaN).
    /// </summary>
    public double CollectCloseIpAtPressure => GetParameter("CollectCloseIpAtPressure");

    /// <summary>
    /// During sample collection, close the Inlet Port when the Coil Trap pressure falls to this value,
    /// provided that it is a number (i.e., not NaN).
    /// </summary>
    public double CollectCloseIpAtCtPressure => GetParameter("CollectCloseIpAtCtPressure");

    /// <summary>
    /// Stop collecting into the coil trap when the Inlet Port temperature rises to this value,
    /// provided that it is a number (i.e., not NaN).
    /// </summary>
    public double CollectUntilTemperatureRises => GetParameter("CollectUntilTemperatureRises");

    /// <summary>
    /// Stop collecting into the coil trap when the Inlet Port temperature falls to this value,
    /// provided that it is a number (i.e., not NaN).
    /// </summary>
    public double CollectUntilTemperatureFalls => GetParameter("CollectUntilTemperatureFalls");

    /// <summary>
    /// Stop collecting when the Coil Trap pressure falls to or below this value,
    /// provided that it is a number (i.e., not NaN).
    /// </summary>
    public double CollectUntilCtPressureFalls => GetParameter("CollectUntilCtPressureFalls");

    /// <summary>
    /// Stop collecting into the coil trap when this much time has elapsed.
    /// provided that the value is a number (i.e., not NaN).
    /// </summary>
    public double CollectUntilMinutes => GetParameter("CollectUntilMinutes");

    /// <summary>
    /// How many minutes to wait.
    /// </summary>
    public double WaitTimerMinutes => GetParameter("WaitTimerMinutes");

    /// <summary>
    /// Inlet Port sample furnace working setpoint ramp rate (degrees per minute).
    /// </summary>
    public double IpRampRate => GetParameter("IpRampRate");

    /// <summary>
    /// The Inlet Port sample furnace's target setpoint (the final setpoint when ramping).
    /// </summary>
    public double IpSetpoint => GetParameter("IpSetpoint");

    /// <summary>
    /// The desired Inlet Manifold pressure, used for filling or flow management.
    /// </summary>
    public double ImPressureTarget => GetParameter("ImPressureTarget");

    /// <summary>
    /// What pressure to evacuate InletPort to.
    /// </summary>
    public double IpEvacuationPressure => GetParameter("IpEvacuationPressure");
    public double IpMinutes => (int)GetParameter("IpMinutes");
    public double IpTemperatureCushion => (int)GetParameter("IpTemperatureCushion");
    public double MaximumMinutesIpToReachTemperature => (int)GetParameter("MaximumMinutesIpToReachTemperature");
    //public double OpenLineDuringCombustion => GetParameter("OpenLineDuringCombustion") != 0;
    public double MaximumSecondsAdmitGas => (int)GetParameter("MaximumSecondsAdmitGas");
    public double MaximumMinutesGrToReachTemperature => (int)GetParameter(nameof(MaximumMinutesGrToReachTemperature));
    public bool KeepFeUnderH2
    {
        get
        {
            var d = GetParameter("KeepFeUnderH2");
            return !(double.IsNaN(d) || d == 0);
        }
    }

    #endregion Process Control Parameters

    #region Process Control Properties

    /// <summary>
    /// Change the Inlet Port Sample furnace setpoint at a controlled
    /// ramp rate, rather than immediately to the given value.
    /// </summary>
    public virtual bool EnableIpSetpointRamp { get; set; } = false;

    /// <summary>
    /// Monitors the time elapsed since the current sample collection phase began.
    /// </summary>
    public Stopwatch CollectStopwatch { get; set; } = new Stopwatch();

    #endregion Process Control Properties

    #region Process Steps

    /// <summary>
    /// Wait for WaitTimerMinutes minutes.
    /// </summary>
    protected virtual void WaitForTimer() => WaitMinutes((int)WaitTimerMinutes);

    /// <summary>
    /// Wait for IpMinutes minutes.
    /// </summary>
    protected virtual void WaitIpMinutes() => WaitMinutes((int)IpMinutes);

    /// <summary>
    /// Turn on the Inlet Port quartz furnace.
    /// </summary>
    protected virtual void TurnOnIpQuartzFurnace() => InletPort.QuartzFurnace.TurnOn();

    /// <summary>
    /// Turn off the Inlet Port quartz furnace.
    /// </summary>
    protected virtual void TurnOffIpQuartzFurnace() => InletPort.QuartzFurnace.TurnOff();

    /// <summary>
    /// Adjust the Inlet Port sample furnace setpoint. If its
    /// setpoint ramp is enabled, the working setpoint will be managed
    /// to reach the new setpoint at the programmed ramp rate.
    /// </summary>
    protected virtual void AdjustIpSetpoint()
    {
        if (IpSetpoint.IsNaN()) return;
        if (IpOvenRamper?.Enabled ?? false)
            IpOvenRamper.Setpoint = IpSetpoint;
        else
            InletPort.SampleFurnace.Setpoint = IpSetpoint;
    }

    /// <summary>
    /// Wait for Inlet Port temperature to fall below IpSetpoint
    /// </summary>
    protected virtual void WaitIpFallToSetpoint()
    {
        AdjustIpSetpoint();
        ProcessStep.Start($"Waiting for {InletPort.Name} to reach {IpSetpoint:0} °C");
        while (!WaitFor(() => Stopping || InletPort.Temperature < IpSetpoint, (int)MaximumMinutesIpToReachTemperature * 60000, 1000))
        {
            InletPort.SampleFurnace.TurnOff();
            if (Warn($"{InletPort.SampleFurnace.Name} is taking too long to reach {IpSetpoint:0} °C.",
                $"Ok to keep waiting or Cancel to move on.\r\n" +
                $"Restart the application to abort the process.").Ok())
            {
                InletPort.SampleFurnace.TurnOn(); ;
                continue;
            }
            break;
        }
        ProcessStep.End();
    }

    /// <summary>
    /// Turn on the Inlet Port sample furnace.
    /// </summary>
    protected virtual void TurnOnIpSampleFurnace()
    {
        AdjustIpSetpoint();
        InletPort.SampleFurnace.TurnOn();
    }

    /// <summary>
    /// Wait for the InletPort sample furnace to reach to within IpTemperatureCushion °C of its setpoint.
    /// </summary>
    protected virtual void WaitIpRiseToSetpoint()
    {
        AdjustIpSetpoint();
        var closeEnough = IpSetpoint - IpTemperatureCushion;
        ProcessStep.Start($"Waiting for {InletPort.Name} to reach {closeEnough:0} °C");
        while (!WaitFor(() => Stopping || InletPort.Temperature >= closeEnough, (int)MaximumMinutesIpToReachTemperature * 60000, 1000))
        {
            InletPort.SampleFurnace.TurnOff();
            if (Warn($"{InletPort.SampleFurnace.Name} is taking too long to reach {closeEnough:0} °C.",
                $"Ok to keep waiting or Cancel to move on.\r\n" +
                $"Restart the application to abort the process.").Ok())
            {
                InletPort.SampleFurnace.TurnOn(); ;
                continue;
            }
            break;
        }
        ProcessStep.End();
    }

    /// <summary>
    /// Turn off the Inlet Port sample furnace.
    /// </summary>
    protected virtual void TurnOffIpSampleFurnace() => InletPort.SampleFurnace.TurnOff();

    /// <summary>
    /// Adjust the Inlet Port sample furnace ramp rate.
    /// </summary>
    protected virtual void AdjustIpRampRate() => IpOvenRamper.RateDegreesPerMinute = IpRampRate;

    /// <summary>
    /// Enable the Inlet Port sample furnace setpoint ramp.
    /// </summary>
    protected virtual void EnableIpRamp()
    {
        IpOvenRamper.Oven = InletPort.SampleFurnace;
        IpOvenRamper.Enable();
    }

    /// <summary>
    /// Disable the Inlet Port sample furnace setpoint ramp.
    /// </summary>
    protected virtual void DisableIpRamp() => IpOvenRamper.Disable();

    /// <summary>
    /// Start collecting sample into the first trap.
    /// </summary>
    protected virtual void StartCollecting() => StartSampleFlow(true);

    protected virtual void StartSampleFlow(bool freezeTrap)
    {
        var collectionPath = IM_FirstTrap;
        var trap = FirstTrap;
        var status = freezeTrap ?
            $"Start collecting sample in {trap.Name}" :
            $"Start gas flow through {trap.Name}";
        ProcessStep.Start(status);

        trap.Evacuate();
        if (freezeTrap)
            trap.FreezeWait();
        collectionPath.Isolate();
        collectionPath.FlowValve?.CloseWait();
        if (collectionPath.FlowManager != null)
            collectionPath.OpenAndEvacuate();
        InletPort.Open();
        InletPort.State = LinePort.States.InProcess;
        if (Sample is Sample) Sample.State = Components.Sample.States.InProcess;
        Sample.AddTrap(trap.Name);
        CollectStopwatch.Restart();
        collectionPath.FlowManager?.Start(FirstTrapBleedPressure);

        ProcessStep.End();
    }

    /// <summary>
    /// Set all collection condition parameters to NaN
    /// </summary>
    protected virtual void ClearCollectionConditions()
    {
        ClearParameter("CollectUntilTemperatureRises");
        ClearParameter("CollectUntilTemperatureFalls");
        ClearParameter("CollectCloseIpAtPressure");
        ClearParameter("CollectCloseIpAtCtPressure");
        ClearParameter("CollectUntilCtPressureFalls");
        ClearParameter("CollectUntilMinutes");
    }

    // These are actions which are taken during collection when
    // a condition occurs. (Supersedes the old "DuringBleed()".
    // Override this method to add more actions.)
    protected virtual List<Action> CollectionActions()
    {
        IpIm(out ISection im);
        double p;

        var CollectionActions = new List<Action>();

        p = GetParameter("FirstTrapOpenFlowPressure");
        if (p.IsANumber() && im != null)
        {
            var value = p;
            CollectionActions.Add(() =>
            {
                if (im.Pressure - FirstTrap.Pressure < value)
                    FirstTrap.FlowValve?.OpenWait();   // Fully open flow valve
            });
        }

        p = GetParameter("FirstTrapFlowBypassPressure");
        if (p.IsANumber() && im != null)
        {
            var value = p;
            CollectionActions.Add(() =>
            {
                if (im.Pressure - FirstTrap.Pressure < value)
                    FirstTrap.Open();   // open bypass if available
            });
        }

        p = GetParameter("CollectCloseIpAtPressure");
        if (p.IsANumber())
        {
            var value = p;
            CollectionActions.Add(() =>
            {
                var pressure = im.Pressure;
                if (InletPort.IsOpened && pressure <= value)
                {
                    InletPort.Close();
                    SampleLog.Record($"{Sample.LabId}\tClosed {InletPort.Name} at {im.Manometer.Name} = {pressure:0} Torr");
                }
            });
        }

        p = GetParameter("CollectCloseIpAtCtPressure");
        if (p.IsANumber())
        {
            var value = p;
            CollectionActions.Add(() =>
            {
                var pressure = FirstTrap.Pressure;
                if (InletPort.IsOpened && pressure <= value)
                {
                    InletPort.Close();
                    SampleLog.Record($"{Sample.LabId}\tClosed {InletPort.Name} at {FirstTrap.Manometer.Name} = {pressure:0} Torr");
                }
            });
        }

        return CollectionActions;
    }

    // These are conditions which stop collection when they occur.
    // Each CollectionCondition returns a string reason for stopping collection,
    // or an empty string if the condition isn't met.
    protected virtual List<Func<string>> CollectionConditions()
    {
        IpIm(out ISection im);
        double p;

        var CollectionConditions = new List<Func<string>>
        {
            () => Stopping ? "CEGS is shutting down" : ""
        };

        p = GetParameter("CollectUntilTemperatureRises");
        if (p.IsANumber())
        {
            var value = p;
            CollectionConditions.Add(() => InletPort.Temperature >= value ?
                $"InletPort.Temperature rose to {value:0} °C" : "");
        }

        p = GetParameter("CollectUntilTemperatureFalls");
        if (p.IsANumber())
        {
            var value = p;
            CollectionConditions.Add(() => InletPort.Temperature <= value ?
                $"InletPort.Temperature fell to {value:0} °C" : "");
        }

        p = GetParameter("CollectUntilCtPressureFalls");
        if (p.IsANumber())
        {
            var value = p;
            CollectionConditions.Add(() => FirstTrap.Pressure <= value &&
                (im == null || im.Pressure < Math.Ceiling(value) + 2) ?
                $"{FirstTrap.Name}.Pressure fell to {value:0.00} Torr" : "");
        }

        p = GetParameter("FirstTrapEndPressure");
        if (p.IsANumber())
        {
            var value = p;
            CollectionConditions.Add(() => FirstTrap.Pressure <= value &&
                (im == null || im.Pressure < Math.Ceiling(value) + 2) ?
                $"{FirstTrap.Name}.Pressure fell to {value:0.00} Torr" : "");
        }

        p = GetParameter("CollectUntilMinutes");
        if (p.IsANumber())
        {
            var value = p;
            CollectionConditions.Add(() => CollectStopwatch.Elapsed.TotalMinutes >= value ?
                $"{MinutesString((int)value)} elapsed" : "");
        }

        return CollectionConditions;
    }


    /// <summary>
    /// Wait for a collection stop condition to occur.
    /// </summary>
    protected virtual void CollectUntilConditionMet()
    {
        ProcessStep.Start($"Wait for a collection stop condition");
        var actions = CollectionActions();
        var conditions = CollectionConditions();

        string stoppedBecause = "";

        while (!Stopping && stoppedBecause == "")
        {
            // do all the actions
            actions.ForEach(action => action());

            // check for stop conditions
            foreach (var condition in conditions)
            {
                stoppedBecause = condition();
                if (stoppedBecause != "") break;
            }

            // has it been too long?
            var p = GetParameter("MinutesCollectingTooLong");
            if (p.IsANumber() && CollectStopwatch.Elapsed.TotalMinutes >= p)
            {
                //what to do?
            }
            WaitMilliseconds(1000, null);
        }
        SampleLog.Record($"{Sample.LabId}\tCollection stop condition met:\t{stoppedBecause}");

        ProcessStep.End();
    }

    /// <summary>
    /// Stop collecting immediately
    /// </summary>
    protected virtual void StopCollectingImmediately() => StopCollecting(true);

    protected virtual void StopCollecting() => StopCollecting(ParameterTrue("FinishCollecting"));

    /// <summary>
    /// Close the IP and wait for CT pressure to bleed down until it stops falling.
    /// </summary>
    protected virtual void StopCollectingAfterBleedDown() => StopCollecting(false);

    /// <summary>
    /// Stop collecting. If 'immediately' is false, wait for CT pressure to bleed down after closing IP
    /// </summary>
    /// <param name="immediately">If false, wait for CT pressure to bleed down after closing IP</param>
    protected virtual void StopCollecting(bool immediately = true)
    {
        ProcessStep.Start("Stop Collecting");

        IM_FirstTrap.FlowManager?.Stop();
        InletPort.Close();
        InletPort.State = LinePort.States.Complete;
        if (!immediately)
            FinishCollecting();
        IM_FirstTrap.Close();
        FirstTrap.Isolate();
        FirstTrap.FlowValve?.CloseWait();

        ProcessStep.End();
    }

    /// <summary>
    /// Wait until the trap pressure stops falling
    /// </summary>
    protected virtual void FinishCollecting()
    {
        var p = FirstTrap?.Manometer;
        var im = Manifold(InletPort);
        var flowValveIsOpened = IM_FirstTrap.FlowValve?.IsOpened ?? true;
        ProcessStep.Start($"Wait for {FirstTrap.Name} pressure to stop falling");
        if ( p != null) // don't wait if there's no manometer
            WaitFor(() => 
            {
                if (!flowValveIsOpened && im.Pressure <= FirstTrapEndPressure)
                {
                    IM_FirstTrap.FlowValve.OpenWait();
                    flowValveIsOpened = true;
                }
                return !p.IsFalling; 
            });
        ProcessStep.End();
    }

    /// <summary>
    /// Get the condensable sample gases into the first trap.
    /// </summary>
    protected virtual void Collect()
    {
        var collectionPath = IM_FirstTrap;
        collectionPath.VacuumSystem.MySection.Isolate();
        collectionPath.Isolate();
        collectionPath.FlowValve?.OpenWait();
        collectionPath.OpenAndEvacuate(OkPressure);
        if (collectionPath.FlowManager == null)
            collectionPath.IsolateFromVacuum();

        StartCollecting();
        CollectUntilConditionMet();
        StopCollecting(false);
        InletPort.State = LinePort.States.Complete;

        if (FirstTrap != VTT)
            TransferCO2FromCTToVTT();
    }

    #endregion Process Steps

    protected virtual void SampleRecord(ISample sample) { }
    protected virtual void SampleRecord(IAliquot aliquot) { }


    /// <summary>
    /// The gas supply that delivers the specified gas to the destination.
    /// </summary>
    protected virtual GasSupply GasSupply(string gas, ISection destination)
    {
        if (FirstOrDefault<GasSupply>((s) => s.GasName == gas && s.Destination == destination) is GasSupply gasSupply)
            return gasSupply;

        //Warn("Process Alert!",
        //    $"Cannot admit {gas} into {destination.Name}. There is no such GasSupply.");
        return null;
    }

    /// <summary>
    /// The Section's He supply, if there is one; otherwise, its, N2 or Ar supply;
    /// null if none are found.
    /// </summary>
    protected virtual GasSupply InertGasSupply(ISection section) =>
        GasSupply("He", section) ?? GasSupply("N2", section) ?? GasSupply("Ar", section);

    /// <summary>
    /// Find the smallest Section that contains the port and has a manometer.
    /// Note: It usually is, but might not actually be, a manifold.
    /// </summary>
    /// <param name="port"></param>
    /// <returns>The section found, or null if there isn't one</returns>
    protected virtual Section Manifold(IPort port)
    {
        Section manifold = null;
        var sections = FindAll<Section>(s => (s.Ports?.Contains(port) ?? false) && s.Manometer != null);
        sections.ForEach(s =>
        {
            if (manifold == null || s.MilliLiters < manifold.MilliLiters)
                manifold = s;
        });
        return manifold;

        // equivalent and simpler, but slower:
        // protected virtual Section Manifold(IPort port) => Manifold([port]);
    }

    /// <summary>
    /// Find the smallest Section that contains all of the ports and has a manometer.
    /// Note: It usually is, but might not actually be, a manifold.
    /// </summary>
    /// <param name="ports"></param>
    /// <returns>The section found, or null if there isn't one</returns>
    protected virtual Section Manifold(IEnumerable<IPort> ports)
    {
        var n = ports?.Count() ?? 0;
        if (n == 0) return null;
        Section manifold = null;

        var sections = FindAll<Section>(s => s.Ports != null && s.Manometer != null);
        sections.ForEach(s =>
        {
            if (ports.Count(s.Ports.Contains) == n && (manifold == null || s.MilliLiters < manifold.MilliLiters))
                manifold = s;
        });
        return manifold;
    }



    /// <summary>
    /// Gets InletPort's manifold.
    /// </summary>
    /// <returns>false if InletPort is null or the manifold is not found.</returns>
    protected virtual bool IpIm(out ISection im)
    {
        im = Manifold(InletPort);
        if (InletPort == null)
        {
            Pause("Can't find Inlet Manifold",
                "No InletPort is selected.\r\n" +
                "Restart the application to abort the process.",
                NoticeType.Error,
                response: "Continue without it");
            return false;
        }
        if (im == null)
        {
            ConfigurationError($"Can't find manifold section for {InletPort.Name}.");
            return false;
        }
        return true;
    }

    /// <summary>
    /// Gets InletPort's manifold and O2 gas supply.
    /// </summary>
    /// <returns>false if InletPort is null, or if the manifold or gas supply is not found.</returns>
    protected virtual bool IpImO2(out ISection im, out IGasSupply gs)
    {
        gs = null;
        if (IpIm(out im))
            gs = GasSupply("O2", im);
        return gs != null;
    }

    /// <summary>
    /// Gets InletPort's manifold and inert gas supply.
    /// </summary>
    /// <returns>false if InletPort is null, or if the manifold or gas supply is not found.</returns>
    protected virtual bool IpImInertGas(out ISection im, out IGasSupply gs)
    {
        gs = null;
        if (!IpIm(out im)) return false;
        gs = InertGasSupply(im);
        if (gs != null) return true;

        OkCancel($"Section {im.Name} has no inert GasSupply.", "Configuration Error?", NoticeType.Error);
        return false;
    }

    /// <summary>
    /// Gets the path from the InletPort to the FirstTrap
    /// </summary>
    /// <param name="im_trap"></param>
    /// <returns>Whether the path was found.</returns>
    protected virtual bool IpImToTrap(out ISection im_trap)
    {
        im_trap = null;
        if (!IpIm(out ISection im)) return false;

        var trap = FirstTrap;
        if (trap == null)
        {
            OkCancel($"Can't find Section {trap.Name}.", "Configuration Error?", NoticeType.Error);
            return false;
        }

        var firstChamber = im.Chambers?.First();
        var lastChamber = trap.Chambers?.Last();
        im_trap = FirstOrDefault<Section>(s =>
            s.Chambers?.First() == firstChamber &&
            s.Chambers?.Last() == lastChamber);

        if (im_trap == null)
        {
            OkCancel($"Can't find Section linking {im.Name} and {trap.Name}.", "Configuration Error?", NoticeType.Error);
            return false;
        }
        return true;
    }

    /// <summary>
    /// Gets the GraphiteReactor's manifold.
    /// </summary>
    /// <returns>false if gr is null or the manifold is not found.</returns>
    protected virtual bool GrGm(IEnumerable<IGraphiteReactor> grs, out ISection gm)
    {
        gm = Manifold(grs);
        if (gm == null)
        {
            OkCancel("Can't find graphite manifold",
                $"None contain [{string.Join(", ", grs.Select(gr => gr.Name))}]\r\n"+
                "Configuration Error?", NoticeType.Error);
            return false;
        }
        return true;
    }

    /// <summary>
    /// Gets the GraphiteReactor's manifold and H2 gas supply.
    /// </summary>
    /// <returns>false if gr is null, or if the manifold or gas supply is not found.</returns>
    protected virtual bool GrGmH2(IEnumerable<IGraphiteReactor> grs, out ISection gm, out IGasSupply gs)
    {
        gs = null;
        if (GrGm(grs, out gm))
            gs = GasSupply("H2", gm);
        return gs != null;
    }

    /// <summary>
    /// Gets the GraphiteReactor's manifold and inert gas supply.
    /// </summary>
    /// <returns>false if gr is null, or if the manifold or gas supply is not found.</returns>
    protected virtual bool GrGmInertGas(IEnumerable<IGraphiteReactor> grs, out ISection gm, out IGasSupply gs)
    {
        gs = null;
        if (!GrGm(grs, out gm)) return false;
        gs = InertGasSupply(gm);
        if (gs != null) return true;
        OkCancel($"Section {gm.Name} has no inert GasSupply.", "Configuration Error?", NoticeType.Error);
        return false;
    }


    #region parameterized processes (deprecated)
    /// <summary>
    /// This temporary stand-in replaces the old Combust() parameterized process, 
    /// this version being implemented using Process Control Variables.
    /// This method is deprecated. Replace its use in process sequences with 
    /// the appropriate parts of the contents below.
    /// </summary>
    /// <param name="temperature"></param>
    /// <param name="minutes"></param>
    /// <param name="admitO2"></param>
    /// <param name="waitForSetpoint"></param>
    protected override void Combust(int temperature, int minutes, bool admitO2, bool waitForSetpoint)
    {
        SetParameter("IpSetpoint", temperature);
        SetParameter("IpTemperatureCushion", 20);   // if waitForSetpoint and cushion is ok
        SetParameter("IpMinutes", minutes);

        if (admitO2) AdmitIPO2();       // (im.VacuumSystem.)openLine is assumed to be == admitO2; this has always been the case.
        TurnOnIpSampleFurnace();
        if (waitForSetpoint) WaitIpRiseToSetpoint();
        WaitIpMinutes();
    }

    #endregion parameterized processes


    #region Valve operations

    protected virtual void ExerciseAllValves() => ExerciseAllValves(0);

    protected virtual void ExerciseAllValves(int secondsBetween)
    {
        ProcessStep.Start("Exercise all opened valves");
        foreach (var v in Valves?.Values)
            if ((v is CpwValve || v is PneumaticValve) && v.IsOpened)
            {
                ProcessSubStep.Start($"Exercising {v.Name}");
                v.Exercise();
                if (secondsBetween > 0)
                    WaitSeconds(secondsBetween);
                ProcessSubStep.End();
            }
        ProcessStep.End();
    }

    protected virtual void CloseAllValves()
    {
        ProcessStep.Start("Close all opened valves");
        foreach (var v in Valves?.Values)
            if ((v is CpwValve || v is PneumaticValve) && !(v is RS232Valve))
                v.CloseWait();
        ProcessStep.End();
    }

    protected virtual void ExerciseLNValves()
    {
        ProcessStep.Start("Exercise all LN Manifold valves");
        foreach (var valve in FindAll<IColdfinger>().Select(x => (x is VTColdfinger v ? v.Coldfinger : x as Coldfinger).LNValve))
            valve?.Exercise();
        ProcessStep.End();
    }

    protected virtual void CloseLNValves()
    {
        foreach (var valve in FindAll<IColdfinger>().Select(x => (x is VTColdfinger v ? v.Coldfinger : x as Coldfinger).LNValve))
            valve?.Close();
    }

    protected virtual void CalibrateRS232Valves()
    {
        foreach (var v in Valves.Values)
            if (v is RS232Valve rv)
                rv.Calibrate();
    }

    protected virtual void OpenRS232Valves()
    {
        foreach (var v in Valves.Values)
            if (v is RS232Valve rv)
                rv.Open();
    }

    /// <summary>
    /// Moving the valves like this sometimes helps pinpoint vacuum performance issues.
    /// </summary>
    protected void ExerciseValvesForever()
    {
        while (true)
        {
            ExerciseAllValves(4);
            WaitMinutes(2);
        }
    }
    #endregion Valve operations

    #region Support and general purpose functions

    protected virtual void WaitForStablePressure(IVacuumSystem vacuumSystem, double pressure, int seconds = 5)
    {
        ProcessSubStep.Start($"Wait for stable pressure below {pressure} {vacuumSystem.Manometer.UnitSymbol}");
        vacuumSystem.WaitForStablePressure(pressure, seconds);
        ProcessSubStep.End();
    }

    protected virtual void TurnOffIPFurnaces() =>
        InletPort?.TurnOffFurnaces();

    protected virtual void HeatQuartz(bool openLine)
    {
        if (InletPort == null) return;

        ProcessStep.Start($"Heat Combustion Chamber Quartz ({QuartzFurnaceWarmupMinutes} minutes)");
        InletPort.QuartzFurnace.TurnOn();
        if (InletPort.State == LinePort.States.Loaded ||
            InletPort.State == LinePort.States.Prepared)
            InletPort.State = LinePort.States.InProcess;
        if (Sample is Sample s && s.State < Components.Sample.States.InProcess)
            s.State = Components.Sample.States.InProcess;
        if (openLine) Manifold(InletPort).VacuumSystem.OpenLine();
        WaitRemaining((int)QuartzFurnaceWarmupMinutes);

        if (InletPort.NotifySampleFurnaceNeeded)
        {
            WaitForOperator(
                $"{Sample?.LabId} is ready for sample furnace.\r\n" +
                $"Remove any coolant from combustion tube and \r\n" +
                $"raise the sample furnace at {InletPort.Name}.");
        }
        ProcessStep.End();
    }

    /// <summary>
    /// Heat the InletPort's quartz bed, while evacuating the rest
    /// of the line.
    /// </summary>
    [Description("Heat the InletPort's quartz bed, while evacuating the rest of the line.")]
    protected virtual void HeatQuartzOpenLine() => HeatQuartz(true);

    protected virtual void Admit(string gas, ISection destination, IPort port, double pressure)
    {
        if (!(GasSupply(gas, destination) is GasSupply gasSupply))
            return;
        gasSupply.Destination.ClosePorts();
        gasSupply.Admit(pressure);

        if (port != null)
        {
            ProcessSubStep.Start($"Admit {gasSupply.GasName} into {port.Name}");
            port.Open();
            WaitSeconds(2);
            port.Close();
            ProcessSubStep.End();
            WaitSeconds(5);
        }
    }

    /// <summary>
    /// Admit O2 into the InletPort
    /// </summary>
    protected virtual void AdmitIPO2()
    {
        if (!IpIm(out ISection im)) return;
        Admit("O2", im, InletPort, IMO2Pressure);
        im.Evacuate(OkPressure);
        im.VacuumSystem.OpenLine();
    }

    protected virtual void AdmitIPInertGas(double pressure)
    {
        if (!IpImInertGas(out ISection im, out IGasSupply gs)) return;
        Admit(gs.GasName, im, InletPort, pressure);
    }

    protected virtual void DiscardIPGases()
    {
        if (!IpIm(out ISection im)) return;
        ProcessStep.Start($"Discard gases at ({InletPort.Name})");
        im.Isolate();
        InletPort.Open();
        WaitSeconds(10);                // give some time to record a measurement
        im.Evacuate(OkPressure);    // allow for high pressure due to water
        ProcessStep.End();
    }

    protected virtual void DiscardMCGases()
    {
        ProcessStep.Start("Discard sample from MC");
        SampleRecord(Sample);
        MC?.Evacuate();
        ProcessStep.End();
    }

    protected virtual void Flush(ISection section, int n, IPort port = null)
    {
        if (InertGasSupply(section) is GasSupply gs)
            gs.Flush(PressureOverAtm, 0.1, n, port);
        else
        {
            Warn($"Section {section.Name} has no inert GasSupply.", "Configuration Error?");
        }
    }

    protected virtual void FlushIP()
    {
        if (!IpIm(out ISection im)) return;

        InletPort.State = LinePort.States.InProcess;
        if (Sample is Sample s && s.State < Components.Sample.States.InProcess)
            s.State = Components.Sample.States.InProcess;
        EvacuateIP(0.1);
        Flush(im, 3, InletPort);

        // Residual inert gas is undesirable only to the extent that it
        // displaces O2. An O2 concentration of 99.99% -- more than
        // adequate for perfect combustion -- equates to 0.01% inert gas.
        // The admitted O2 pressure always exceeds 1000 Torr;
        // 0.01% of 1000 is 0.1 Torr.
        im.VacuumSystem.WaitForPressure(0.1);
        InletPort.Close();
    }

    protected virtual void CloseIP()
    {
        InletPort.Close();
    }

    #region GR operations
    protected virtual IGraphiteReactor NextGR(string thisOne, GraphiteReactor.Sizes size = GraphiteReactor.Sizes.Standard)
    {
        bool passedThisOne = false;
        IGraphiteReactor foundOne = null;
        foreach (var gr in GraphiteReactors)
        {
            if (passedThisOne)
            {
                if (gr.Prepared && gr.Aliquot == null && gr.Size == size) return gr;
            }
            else
            {
                if (foundOne == null && gr.Prepared && gr.Aliquot == null && gr.Size == size)
                    foundOne = gr;
                if (gr.Name == thisOne)
                    passedThisOne = true;
            }
        }
        return foundOne;
    }

    protected virtual bool IsSulfurTrap(IGraphiteReactor gr) =>
        gr?.Aliquot?.Name == "sulfur";

    protected virtual IGraphiteReactor NextSulfurTrap(string thisGr)
    {
        bool passedThisOne = false;
        IGraphiteReactor foundOne = null;
        foreach (var gr in GraphiteReactors)
        {
            if (passedThisOne)
            {
                if (IsSulfurTrap(gr) && gr.State != GraphiteReactor.States.WaitService) return gr;
            }
            else
            {
                if (foundOne == null && IsSulfurTrap(gr) && gr.State != GraphiteReactor.States.WaitService)
                    foundOne = gr;
                if (gr.Name == thisGr)
                    passedThisOne = true;
            }
        }
        if (foundOne != null) return foundOne;
        return NextGR(thisGr);
    }

    //protected virtual void OpenNextGRs()
    //{

    //    string grName = PriorGR;
    //    for (int i = 0; i < Sample.AliquotsCount; ++i)
    //    {
    //        if (NextGR(grName) is IGraphiteReactor gr)
    //        {
    //            gr.Open();
    //            grName = gr.Name;
    //        }
    //    }
    //}

    //protected virtual void OpenNextGRsAndd13C()
    //{
    //    if (!GrGm(NextGR(PriorGR), out ISection gm)) return;
    //    VacuumSystem.Isolate();
    //    OpenNextGRs();
    //    gm.JoinToVacuum();

    //    if (Sample.Take_d13C)
    //    {
    //        var port = d13CPort;
    //        if (port == null)
    //        {
    //            Alert.Warn("Sample Alert",
    //                $"Can't find d13C port for Sample {Sample.LabId} from {InletPort?.Name}");
    //        }
    //        else if (port.State == LinePort.States.Prepared)
    //        {
    //            var manifold = Manifold(port);
    //            if (manifold == null)
    //            {
    //                Alert.Warn("Configuration Error", $"Can't find manifold Section for d13C port {port.Name}.");
    //            }
    //            else
    //            {
    //                manifold.ClosePorts();
    //                manifold.Isolate();
    //                port.Open();
    //                manifold.JoinToVacuum();
    //            }
    //        }
    //    }
    //    Evacuate();
    //}

    protected virtual void CloseAllGRs() => CloseAllGRs(null);

    protected virtual void CloseAllGRs(IGraphiteReactor exceptGR)
    {
        foreach (var gr in GraphiteReactors)
            if (gr != exceptGR)
                gr.Close();
    }

    protected virtual int BusyGRCount() => GraphiteReactors.Count(gr => gr.Busy);

    protected virtual int PreparedGRs() =>
        GraphiteReactors.Count(gr => gr.Prepared);

    protected virtual bool EnoughGRs()
    {
        int needed = Sample?.AliquotsCount ?? 1;
        if ((Sample?.SulfurSuspected ?? false) && !IsSulfurTrap(NextSulfurTrap(PriorGR)))
            needed++;
        return PreparedGRs() >= needed;
    }

    protected virtual void OpenPreparedGRs()
    {
        foreach (var gr in GraphiteReactors)
            if (gr.Prepared)
                gr.Open();
    }

    protected virtual bool PreparedGRsAreOpened() =>
        !GraphiteReactors.Any(gr => gr.Prepared && !gr.IsOpened);

    protected virtual void ClosePreparedGRs()
    {
        foreach (var gr in GraphiteReactors)
            if (gr.Prepared)
                gr.Close();
    }

    #region GR service

    protected virtual void PressurizeGRsWithInertGas(IEnumerable<IGraphiteReactor> grs)
    {
        ProcessStep.Start("Backfill the graphite reactors with inert gas");
        if (!GrGmInertGas(grs, out ISection gm, out IGasSupply gasSupply)) return;

        var pressure = Ambient.Pressure + 10;

        ProcessSubStep.Start($"Admit {pressure:0} Torr {gasSupply.GasName} into {gm.Name}");
        gm.ClosePorts();
        var preAdmitPressure = gm.Pressure;
        gasSupply.Admit();

        // make sure gas is flowing into the destination
        while (!WaitFor(() => Hacs.Stopping || gm.Pressure > preAdmitPressure + 5, 3000, 20))
        {
            gasSupply.ShutOff();
            if (Warn($"{gasSupply.GasName} is not flowing into {gm.Name}.",
                $"Ok to try again or Cancel to continue without gas.\r\n" +
                $"Restart the application to abort the process.").Ok())
            {
                gasSupply.Admit();
                continue;
            }
            break;
        }

        while (!WaitFor(() => Hacs.Stopping || gm.Pressure >= pressure, (int)MaximumSecondsAdmitGas * 1000, 250))
        {
            gasSupply.ShutOff();
            if (Warn($"It's taking too long for {gm.Name} to reach {pressure:0} Torr.",
                $"Ok to keep waiting or Cancel to move on.\r\n" +
                $"Restart the application to abort the process.").Ok())
            {
                gasSupply.Admit();
                continue;
            }
            break;
        }
        ProcessSubStep.End();


        ProcessSubStep.Start($"Open graphite reactors that are awaiting service.");
        foreach (var gr in grs) gr.Open();
        ProcessSubStep.End();

        ProcessSubStep.Start($"Ensure {gm.Name} pressure is ≥ {pressure:0} Torr");
        WaitSeconds(1);
        while (!WaitFor(() => Hacs.Stopping || gm.Pressure >= pressure, (int)MaximumSecondsAdmitGas * 1000 / 2, 250))
        {
            gasSupply.ShutOff();
            if (Warn($"It's taking too long for {gm.Name} to reach {pressure:0} Torr.",
                $"Ok to keep waiting or Cancel to move on.\r\n" +
                $"Restart the application to abort the process.").Ok())
            {
                gasSupply.Admit();
                continue;
            }
            break;
        }
        gasSupply.ShutOff(true);
        ProcessSubStep.End();

        ProcessSubStep.Start("Isolate the graphite reactors");
        CloseAllGRs();
        ProcessSubStep.End();

        ProcessStep.End();
    }

    protected virtual void PrepareGRsForService()
    {
        var grs = new List<IGraphiteReactor>();
        foreach (var gr in GraphiteReactors)
        {
            if (gr.State == GraphiteReactor.States.WaitService)
                grs.Add(gr);
            else if (gr.State == GraphiteReactor.States.Prepared && gr.Contents == "sulfur")
                gr.ServiceComplete();
        }

        if (grs.Count < 1)
        {
            Tell("No graphite reactors are awaiting service.", 
                "Nothing to do.");
            return;
        }

        grs.ForEach(gr => SampleRecord(gr.Aliquot));

        WaitForOperator("Mark Fe/C tubes with graphite IDs.");

        PressurizeGRsWithInertGas(grs);

        List<IAliquot> toDelete = new List<IAliquot>();
        grs.ForEach(gr =>
        {
            toDelete.Add(gr.Aliquot);
            gr.ServiceComplete();
        });

        toDelete.ForEach(a =>
        {
            if (a?.Sample is ISample s)
            {
                s.Aliquots.Remove(a);
                a.Name = null;          // remove the aliquot from the NamedObject Dictionary.
            }
        });

        Announce("Ready to load new iron and desiccant.");
    }

    protected virtual bool AnyUnderTemp(List<IGraphiteReactor> grs, int targetTemp)
    {
        foreach (var gr in grs)
            if (gr.SampleTemperature < targetTemp)
                return true;
        return false;
    }

    /// <summary>
    ///
    /// </summary>
    /// <param name="section"></param>
    protected virtual void HoldForLeakTightness(ISection section)
    {
        var basePressure = GetParameter("LeakTestBasePressure");
        if (!basePressure.IsANumber())
            basePressure = OkPressure; // OkPressure is a convenient but high starting pressure;
                                       // ideally, ror tests start at ultimate pressure.

        // ports often have higher gas loads, usually due to water
        var leakRateLimit = 2 * LeakTightTorrLitersPerSecond;
        while (SectionLeakRate(section, leakRateLimit) > leakRateLimit)
        {
            if (Warn($"{section.Name} is leaking.",
                $"Something in the {section.Name} isn't holding vacuum well enough.\r\n" +
                $"Ok to try again or Cancel to move on.\r\n" +
                $"Restart the application to abort the process.").Ok())
            {
                section.VacuumSystem.Evacuate(basePressure);
                continue;
            }
            break;
        }
    }




    protected virtual void PreconditionGRs()
    {
        var grs = GraphiteReactors.FindAll(gr => gr.State == GraphiteReactor.States.WaitPrep);

        if (grs.Count < 1)
        {
            Tell("No graphite reactors are awaiting preparation.", 
                "Nothing to do.");
            return;
        }

        if (!GrGmInertGas(grs, out ISection gm, out IGasSupply gsInert)) return;

        var gsH2 = GasSupply("H2", gm);
        var gsH2Needed = IronPreconditionH2Pressure > 0;
        if (gsH2 == null && gsH2Needed)
        {
            ConfigurationError($"Can't find H2 gas supply for {gm.Name}.");
            return;
        }

        // close grs that aren't awaiting prep
        foreach (var gr in GraphiteReactors.Except(grs))
            gr.Close();

        var count = grs.Count;
        ProcessStep.Start($"Calibrate GR {"manometer".Plurality(count)} and {"volume".Plurality(count)}");

        // On the first flush, Check for leaks, measure the GR volumes, and calibrate their manometers
        ProcessSubStep.Start("Evacuate graphite reactors");
        gm.Isolate();
        grs.ForEach(gr => gr.Open());
        gm.OpenAndEvacuate();
        WaitForStablePressure(gm.VacuumSystem, CleanPressure);
        WaitMinutes((int)GRFirstEvacuationMinutes);     // some time to reduce the gas load due to water in the desiccant.
        HoldForLeakTightness(gm);
        ProcessSubStep.End();

        ProcessSubStep.Start($"Zero GR manometers.");
        grs.ForEach(gr => gr.Manometer.ZeroNow());
        WaitFor(() => !grs.Any(gr => gr.Manometer.Zeroing));    // TODO: add timeout to this wait?
        grs.ForEach(gr => gr.Close());
        ProcessSubStep.End();

        foreach (var gr in grs)
        {
            ProcessStep.Start($"Measure {gr.Name} volume");
            gsInert.Admit(PressureOverAtm);
            gm.Isolate();
            WaitSeconds(10);
            var p0 = gm.Manometer.WaitForAverage((int)MeasurementSeconds);
            var gmMilliLiters = gm.CurrentVolume(true);
            gr.Open();
            WaitSeconds(5);
            gr.Close();
            WaitSeconds(5);
            var p1 = gm.Manometer.WaitForAverage((int)MeasurementSeconds);

            gr.MilliLiters = gmMilliLiters * (p0 / p1 - 1);
            gr.Size = EnableSmallReactors && gr.MilliLiters < 2.0 ? GraphiteReactor.Sizes.Small : GraphiteReactor.Sizes.Standard;
            ProcessStep.End();
        }

        grs.ForEach(gr => gr.Open());
        gm.OpenAndEvacuate(OkPressure);
        ProcessStep.End();

        ProcessStep.Start("Evacuate & Flush GRs with inert gas");
        Flush(gm, 2);
        gm.VacuumSystem.WaitForPressure(OkPressure);
        ProcessStep.End();

        if (IronPreconditioningMinutes > 0)
        {
            if (IronPreconditionH2Pressure > 0)
            {
                ProcessStep.Start("Admit H2 into GRs");
                gm.IsolateFromVacuum();
                gsH2.FlowPressurize(IronPreconditionH2Pressure);
                grs.ForEach(gr => gr.Close());
                ProcessStep.End();
            }

            ProcessStep.Start("Start Heating Fe under vacuum");
            grs.ForEach(gr => gr.TurnOn(IronPreconditioningTemperature));
            ProcessStep.End();

            int targetTemp = (int)IronPreconditioningTemperature - (int)IronPreconditioningTemperatureCushion;
            ProcessStep.Start($"Wait for GRs to reach {targetTemp} °C.");
            while (!WaitFor(() => Hacs.Stopping || !AnyUnderTemp(grs, targetTemp), (int)MaximumMinutesGrToReachTemperature * 60000, 1000))
            {
                var sluggish = grs.Where(gr => gr.SampleTemperature < targetTemp);
                count = sluggish.Count();
                if (count == 0) break;      // bad timing
                var which = count == 1 ?
                    "GR " + sluggish.First().Name + " is " :
                    "GRs " + string.Join(", ", sluggish.Select(gr => gr.Name)) + " are ";

                if (Warn($"{which} taking too long to reach {targetTemp} °C.",
                    $"Ok to keep waiting or Cancel to move on.\r\n" +
                    $"Restart the application to abort the process.").Ok())
                {
                    grs.ForEach(gr => gr.TurnOn(IronPreconditioningTemperature));
                    continue;
                }
                break;
            }
            ProcessStep.End();


            ProcessStep.Start("Precondition iron for " + MinutesString((int)IronPreconditioningMinutes));
            if (IronPreconditionH2Pressure > 0)
            {
                gm.OpenAndEvacuate(OkPressure);
                gm.VacuumSystem.OpenLine();
            }
            WaitRemaining((int)IronPreconditioningMinutes);
            ProcessStep.End();


            if (KeepFeUnderH2 || IronPreconditionH2Pressure <= 0)
                grs.ForEach(gr => gr.TurnOff());
            else
            {
                ProcessStep.Start("Evacuate GRs");
                gm.Isolate();
                CloseAllGRs();
                grs.ForEach(gr => { gr.Heater.TurnOff(); gr.Open(); });
                gm.OpenAndEvacuate(OkPressure);
                ProcessStep.End();

                ProcessStep.Start("Flush GRs with inert gas");
                Flush(gm, 3);
                ProcessStep.End();
            }
        }

        grs.ForEach(gr => gr.PreparationComplete());

        gm.VacuumSystem.OpenLine();

        Announce("Graphite reactor preparation complete.");
    }

    protected virtual void PrepareIPsForCollection() =>
        PrepareIPsForCollection(null);

    protected virtual void PrepareIPsForCollection(List<IInletPort> ips = null)
    {
        bool targeted(IInletPort ip) =>
            ip.State == LinePort.States.Loaded &&
            ip.PortType != InletPortType.GasSupply;

        if (ips == null)
            ips = InletPorts.FindAll(targeted);
        else
            ips = ips.FindAll(targeted);

        if (ips.Count < 1) return;
        var portNames = string.Join(", ", ips.Select(cf => cf.Name));

        // close ips that aren't awaiting prep
        foreach (var ip in InletPorts.Except(ips))
            ip.Close();

        var im = Manifold(ips);
        if (im == null)
        {
            ConfigurationError($"No manifold includes {portNames}:");
            return;
        }

        ProcessStep.Start("Evacuate & Flush IPs with inert gas");
        im.VacuumSystem.MySection.Isolate();
        im.Isolate();
        ips.ForEach(ip => ip.Open());
        im.Evacuate(OkPressure);
        WaitForStablePressure(im.VacuumSystem, CleanPressure);
        HoldForLeakTightness(im);

        Flush(im, 3);
        im.VacuumSystem.WaitForPressure(CleanPressure);

        ProcessStep.End();

        ips.ForEach(ip => ip.Close());

        ProcessStep.Start("Release the samples");
        WaitForOperator(
            "Release the samples at ports:\r\n" +
            $"\t{portNames}.");
        ProcessStep.End();

        ips.ForEach(ip => ip.State = LinePort.States.Prepared);
        OpenLine();
    }

    protected virtual void Prepare_d13CPorts() { }

    protected virtual void ChangeSulfurFe()
    {
        var grs = GraphiteReactors.FindAll(gr =>
            IsSulfurTrap(gr) && gr.State == GraphiteReactor.States.WaitService);

        if (grs.Count < 1)
        {
            Tell("No sulfur traps are awaiting service.", 
                "Nothing to do.");
            return;
        }
        var gm = Manifold(grs);
        if (gm == null)
        {
            var grList = string.Join(", ", grs.Select(gr => gr.Name));
            ConfigurationError($"Can't find graphite manifold for {grList}.");
            return;
        }

        PressurizeGRsWithInertGas(grs);

        WaitForOperator("Replace iron in sulfur traps.");

        // assume the Fe has been replaced

        ProcessStep.Start("Evacuate sulfur traps");

        gm.Isolate();
        grs.ForEach(gr => gr.Open());
        gm.OpenAndEvacuate(OkPressure);
        ProcessStep.End();

        ProcessStep.Start("Flush GRs with He");
        Flush(gm, 3);
        ProcessStep.End();

        grs.ForEach(gr => gr.PreparationComplete());

        gm.VacuumSystem.OpenLine();
    }

    #endregion GR service

    #endregion GR operations

    #endregion Support and general purpose functions

    #region Vacuum System

    protected virtual void EvacuateIP(double pressure)
    {
        if (!IpIm(out ISection im)) return;
        ProcessStep.Start($"Evacuate {InletPort.Name} to below {pressure:0.0e0}.");
        im.OpenAndEvacuate(pressure, InletPort);
        ProcessStep.End();
    }
    protected virtual void EvacuateIP() => EvacuateIP(OkPressure);

    #endregion Vacuum System

    #region OpenLine
    protected virtual void CloseGasSupplies()
    {
        ProcessSubStep.Start("Close gas supplies");

        // Look only in CEGS vacuum systems; ignore other process managers
        var cegsGasSupplies = GasSupplies.Values.Where(gs => VacuumSystems.ContainsValue(gs.Destination.VacuumSystem)).ToList();
        cegsGasSupplies.ForEach(gs => gs.ShutOff());
        // close gas flow valves after all shutoff valves are closed
        cegsGasSupplies.ForEach(gs => gs.FlowValve?.CloseWait());

        ProcessSubStep.End();
    }

    /// <summary>
    /// Open and evacuate the entire vacuum line. This establishes
    /// the baseline system state: the condition it is normally left in
    /// when idle, and the expected starting point for major
    /// processes such as running samples.
    /// </summary>
    protected virtual void OpenLine()
    {
        ProcessStep.Start("Close gas supplies");
        CloseGasSupplies();
        ProcessStep.End();
        OpenLine(MC.VacuumSystem);
    }

    /// <summary>
    /// Opens all sections serviced by the vacuumSystem, first thawing, if needed, to ensure they are above freezing.
    /// </summary>
    /// <param name="vacuumSystem"></param>
    protected virtual void OpenLine(IVacuumSystem vacuumSystem)
    {
        ProcessStep.Start($"Ensure all {vacuumSystem.Name}'s sections are all well above freezing.");
        var coldSections = Sections.Values.Where(s =>
            s.VacuumSystem == vacuumSystem &&
            s.Coldfinger is IColdfinger c &&
            c.Temperature < 5).ToList();
        coldSections.ForEach(s => s.Thaw(10));
        if (!WaitFor(() => coldSections.All(s => s.Temperature > 5), 600 * 1000, 1000))
        {
            Announce("A coldfinger is taking too long to thaw.",
                "Compressed air problem?", NoticeType.Warning);
        }
        ProcessStep.End();

        vacuumSystem.OpenLine();
    }

    #endregion OpenLine

    #region Running samples

    public override void RunProcess(string processToRun)
    {
        EnsureProcessStartConditions();
        if (processToRun == "Run samples")
            RunSamples();
        else if (processToRun == "Run selected sample")
            RunSample();
        else
            base.RunProcess(processToRun);
    }

    /// <summary>
    /// These conditions are assumed at the start of a process.
    /// Sometimes they are changed during a process, which should always
    /// restore them to the "normal" state before or on completion. However,
    /// if the process is abnormally interrupted (e.g. aborted) they can
    /// be left in the incorrect state.
    /// </summary>
    protected virtual void EnsureProcessStartConditions()
    {
        //VacuumSystems.Values.ToList().ForEach(vs => vs.AutoManometer = true);
        //FindAll<GasSupply>().ForEach(gs => gs.ShutOff());
    }

    protected virtual void RunSample()
    {
        Hacs.SystemLog.Record($"Run sample {Sample?.Name} ({Sample?.LabId})");

        if (Sample == null)
        {
            Tell(InletPort == null ? "No sample to run." : $"{InletPort.Name} doesn't contain a sample.",
                "Nothing to do.");
            return;
        }

        if (InletPort != null && InletPort.State > LinePort.States.Prepared)
        {
            Tell($"{InletPort.Name} is not ready to run.", 
                "Nothing to do.");
            return;
        }

        if (LNManifolds.Values.All(x => x.SupplyEmpty))
        {
            double liters = LNManifolds.Values.Sum(x => x.Liters.Value);

            if (!Warn($"LN supply is low.",
                $"There might not be enough to finish the sample: ({liters:0.0} L)\r\n" +
                "Ok to proceed anyway, or Cancel to abort.").Ok())
            {
                return;
            }



        }

        if (!EnoughGRs())
        {
            Announce("Not enough GRs are prepared.",
                "Unable to start process.");
            return;
        }

        if (!ProcessSequences.ContainsKey(Sample.Process))
        {
            Announce($"Process '{Sample.Process}' not found",
                $"{Sample.Name} at {InletPort.Name} needs it.", NoticeType.Error);
            return;
        }

        CegsPreferences.DefaultParameters.ForEach(Sample.SetParameter);

        SampleLog.WriteLine("");
        Sample.DateTime = DateTime.Now;
        SampleLog.Record(
            $"Start Process:\t{Sample.Process}\r\n" +
            $"\t{Sample.LabId}\t{Sample.Milligrams:0.0000}\tmg\r\n" +
            $"\t{Sample.AliquotsCount}\t{"aliquot".Plurality(Sample.AliquotsCount)}");

        base.RunProcess(Sample.Process);
        WaitFor(() => !base.Busy, -1, 1000);
    }

    /// <summary>
    /// Present the sample selector with all samples initially unselected.
    /// </summary>
    protected virtual void RunSamples() =>
        RunSamples(false);

    /// <summary>
    /// Present the sample selector with all samples initially selected.
    /// </summary>
    protected virtual void RunAllSamples() =>
        RunSamples(true);

    /// <summary>
    /// Get a list of samples to run from the sample selector and run them.
    /// </summary>
    /// <param name="all"></param>
    protected virtual void RunSamples(bool all)
    {
        if (!(SelectSamples?.Invoke(all) is List<ISample> samples))
            samples = new List<ISample>();
        RunSamples(samples);
    }

    /// <summary>
    /// Run the listed samples.
    /// </summary>
    /// <param name="samples"></param>
    protected virtual void RunSamples(List<ISample> samples)
    {
        var list = string.Join(", ", samples.Select(p => $"{p.Name} ({p.LabId})"));
        Hacs.SystemLog.Record($"Run samples {list}");
        samples.ForEach(RunSample);
    }


    /// <summary>
    /// Run a sample.
    /// </summary>
    /// <param name="sample"></param>
    protected virtual void RunSample(ISample sample)
    {
        Sample = sample;
        RunSample();
    }

    protected override void ProcessStarting(string message = "")
    {
        if (message.IsBlank())
        {
            var sequence = ProcessType == ProcessTypeCode.Sequence ? "sequence " : "";
            var happened = "starting";
            message = $"Process {sequence}{happened}: {ProcessToRun}";
        }
        base.ProcessStarting(message);
    }

    protected override void ProcessEnded(string message = "")
    {
        if (message.IsBlank())
        {
            var sequence = ProcessType == ProcessTypeCode.Sequence ? "sequence " : "";
            var happened = RunCompleted ? "completed" : "aborted";
            message = $"Process {sequence}{happened}: {ProcessToRun}";
        }

        if (ProcessType == ProcessTypeCode.Sequence)
        {
            if (Sample is Sample && ProcessToRun == Sample.Process)
                Sample.State = Components.Sample.States.Complete;
            SampleLog.Record(message + "\r\n\t" + Sample.LabId);
        }

        Alert(message);
        base.ProcessEnded(message);
    }

    #endregion Running samples

    #region Sample loading and preparation

    /// <summary>
    /// Admit a quantity of dead CO2 into the MC.
    /// </summary>
    /// <param name="ugc_targetSize"></param>
    /// <param name="cleanItUp">Transfer it to the VTT and extract() before measuring.</param>
    protected virtual void AdmitDeadCO2(double ugc_targetSize, bool cleanItUp = false)
    {
        var CO2 = GasSupply("CO2", MC);
        if (CO2 == null) return;

        ProcessStep.Start("Evacuate MC");
        MC.Isolate();
        var particles = ugc_targetSize * CarbonAtomsPerMicrogram;
        if (Pressure(particles, MC.CurrentVolume(true), MC.Temperature) > MC.Manometer.MaxValue)
            MC.Ports[0].Open();
        if (Pressure(particles, MC.CurrentVolume(true), MC.Temperature) > MC.Manometer.MaxValue)
            MC.Ports[1].Open();
        if (Pressure(particles, MC.CurrentVolume(true), MC.Temperature) > MC.Manometer.MaxValue)
        {
            var ugcMax = Particles(MC.Manometer.MaxValue, MC.CurrentVolume(true), MC.Temperature) / CarbonAtomsPerMicrogram;
            
            Warn("Dead CO2 sample is too big",
                $"Requested amount ({ugc_targetSize} µgC) exceeds {MC.Name} limit ({ugcMax} µgC).\r\n"+
                "Admit Dead CO2 process will abort.");

            ProcessStep.End();
            return;
        }

        MC.Evacuate(CleanPressure);
        MC.VacuumSystem.WaitForStablePressure(CleanPressure);
        ZeroMC();
        ProcessStep.End();

        ProcessStep.Start("Admit CO2 into the MC");
        CO2.Pressurize(ugc_targetSize);
        ProcessStep.End();

        if (cleanItUp)
            CleanupCO2InMC();
        else
        {
            ProcessSubStep.Start("Take measurement");
            WaitForMCStable();
            SampleLog.Record($"Admitted CO2:\t{ugCinMC:0.0}\tµgC\t={ugCinMC / GramsCarbonPerMole:0.00}\tµmolC\t(target was {ugc_targetSize:0.0}\tµgC)");
            ProcessSubStep.End();
        }
    }

    protected virtual void AdmitDeadCO2()
        => AdmitDeadCO2(Sample.Micrograms, true);

    protected virtual void AdmitSealedCO2IP()
    {
        if (InletPort.State == LinePort.States.Prepared) return;    // already done
        if (Sample is Sample && Sample.State >= Components.Sample.States.Prepared) return;    // already done
        if (!IpIm(out ISection im)) return;

        ProcessStep.Start($"Evacuate and flush {InletPort.Name}");
        im.ClosePortsExcept(InletPort);
        im.Isolate();
        InletPort.Open();
        im.Evacuate(OkPressure);
        WaitForStablePressure(im.VacuumSystem, OkPressure);
        Flush(im, 3);
        im.VacuumSystem.WaitForPressure(CleanPressure);
        ProcessStep.End();

        InletPort.Close();

        ProcessStep.Start("Release the sample");
        WaitForOperator($"Release the sealed sample '{Sample.LabId}' at {InletPort.Name}.");
        ProcessStep.End();

        InletPort.State = LinePort.States.Prepared;
        if (Sample is Sample) Sample.State = Components.Sample.States.Prepared;
    }

    /// <summary>
    /// prepare a carbonate sample for acidification
    /// </summary>
    protected virtual void PrepareCarbonateSample()
    {
        if (!IpImInertGas(out ISection im, out IGasSupply gs)) return;

        LoadCarbonateSample();
        EvacuateIP();
        Flush(im, 3, InletPort);

        ProcessStep.Start($"Wait for {im.VacuumSystem.Manometer.Name} < {CleanPressure:0.0e0} Torr");
        im.VacuumSystem.WaitForPressure(CleanPressure);

        gs.Admit();
        gs.WaitForPressure(PressureOverAtm, true);
        InletPort.Close();
        var pIM = im.Pressure;
        gs.ShutOff(true);

        ProcessStep.End();

        SampleLog.Record($"Carbonate vial pressure: {pIM:0}");
        im.VacuumSystem.OpenLine();
        Announce($"Carbonate sample contains {pIM:0} Torr He.");
    }

    protected virtual void LoadCarbonateSample()
    {
        if (!IpImInertGas(out ISection im, out IGasSupply gs)) return;

        ProcessStep.Start($"Provide positive He pressure at {InletPort.Name} needle");
        im.ClosePorts();
        im.Isolate();
        gs.Admit();
        gs.WaitForPressure(PressureOverAtm);
        InletPort.Open();
        WaitSeconds(5);
        gs.WaitForPressure(PressureOverAtm);
        ProcessStep.End();

        PlaySound();
        ProcessStep.Start("Remove previous sample or plug from IP needle");
        WaitFor(() => im.Manometer.IsFalling, 20 * 1000, 100);
        ProcessStep.End();

        ProcessStep.Start("Wait for stable He flow at IP needle");
        WaitFor(() => im.Manometer.IsStable, 30 * 1000, 100);   // TODO: Take some action if this fails?
        ProcessStep.End();

        PlaySound();
        ProcessStep.Start("Load next sample vial or plug at IP needle");
        if (WaitFor(() => im.Manometer.RateOfChange >= IMPluggedTorrPerSecond, 30 * 1000, 100))
            InletPort.State = LinePort.States.Loaded;
        else
            InletPort.State = LinePort.States.Complete;
        ProcessStep.End();

        InletPort.Close();
        gs.ShutOff();
    }

    /// <summary>
    /// Prompt the operator to remove and replace the vials at Completed d13C ports, 
    /// and then prepare the new vials along with any other ports that are Loaded.
    /// If no ports are Completed, then already Loaded ports may be prepared.
    /// </summary>
    protected virtual void Reload_d13CPorts()
    {
        var ports = FindAll<d13CPort>(p => p.State == LinePort.States.Complete);
        var count = ports.Count;

        // TODO: allow ampoules, needs different words
        //      how to know whether they are vials or ampoules?

        if (count == 0)
        {
            if (FirstOrDefault<d13CPort>(p => p.State == LinePort.States.Loaded) == null)
            {
                Tell("No vial ports are Completed or Loaded.", 
                    "Nothing to do.");
                return;
            }
            else
            {
                if (!OkCancel("No vial ports are Completed.",
                    $"Ok to prepare previously Loaded vials, or\r\n" +
                    $"Cancel the procedure").Ok())
                {
                    return;
                }
            }
        }
        else
        {
            // TODO: should this procedure do the loading under positive inertGas pressure?
            var list = string.Join(", ", ports.Select(p => p.Name));
            if (!OkCancel("Ready for new vials",
                $"Remove and replace the vials at Completed d13C {"port".Plurality(count)}\r\n" +
                $"{list}.\r\n" +
                $"Ok when ready to evacuate the new vials, or" +
                $"Cancel the procedure.").Ok())
            {
                return;
            }
            ports.ForEach(p => p.State = LinePort.States.Loaded);
        }

        // also prepare any other ports that are loaded
        ports = FindAll<d13CPort>(p => p.State == LinePort.States.Loaded);
        Prepare_d13CPorts(ports);
    }

    /// <summary>
    /// Evacuate and flush the given d13C ports, then update their states to Prepared.<br/>
    /// <em><b>Warning</b>: The present state of the ports is ignored!</em>
    /// </summary>
    /// <param name="ports"></param>
    protected virtual void Prepare_d13CPorts(IEnumerable<Id13CPort> ports)
    {
        if (ports == null || ports.Count() == 0) return;
        var manifold = Manifold(ports);
        if (manifold == null)
        {
            var list = string.Join(", ", ports.Select(p => p.Name));
            ConfigurationError($"Can't find d13C port manifold for {list}");
            return;
        }

        ProcessStep.Start($"Prepare d13C ports");
        manifold.ClosePortsExcept(ports);
        manifold.Isolate();
        foreach (var p in ports) p.Open();

        ProcessSubStep.Start($"Evacuate {manifold.Name} to {OkPressure:0.0e0} Torr");
        manifold.OpenAndEvacuate(OkPressure);
        ProcessSubStep.End();

        Flush(manifold, 3);

        ProcessSubStep.Start($"Evacuate {manifold.Name} to {CleanPressure:0.0e0} Torr");
        manifold.OpenAndEvacuate();
        manifold.VacuumSystem.WaitForStablePressure(CleanPressure, 5);
        ProcessSubStep.End();

        ProcessStep.Start("Check d13C ports for leaks");
        HoldForLeakTightness(manifold);
        ProcessStep.End();

        ProcessStep.Start("Close prepared d13C ports");
        manifold.ClosePorts();
        ProcessStep.End();

        foreach (var p in ports) p.State = LinePort.States.Prepared;
        manifold.VacuumSystem.OpenLine();
        ProcessStep.End();
    }

    /// <summary>
    /// Evacuate and flush all Loaded d13C ports.
    /// </summary>
    protected virtual void PrepareLoaded_d13CPorts()
    {
        var ports = FindAll<d13CPort>(p => p.State == LinePort.States.Loaded);
        if (ports.Count == 0) 
        {
            Tell("No vial ports are Loaded.", 
                "Nothing to do.");
            return;
        }
        Prepare_d13CPorts(ports);
    }

    /// <summary>
    /// Evacuates and flushes the currently selected d13C port.
    /// </summary>
    protected virtual void Prepare_d13CPort()
    {
        var port = d13CPort;
        if (port == null) return;
        if (port.State == LinePort.States.Prepared) return;

        if (port.State != LinePort.States.Loaded)
        {
            if (!Warn($"Port {port.Name} is not available.",
                "It may contain a prior d13C sample.\r\n" +
                "Ok to evacuate it anyway, or \r\n" +
                "Cancel the procedure."
                ).Ok())
            {
                return;
            }
        }

        Prepare_d13CPorts([port]);
    }

    #endregion Sample loading and preparation

    #region Sample collection, extraction and measurement

    #region Collect

    protected virtual void FreezeVtt() => VTT.EmptyAndFreeze(CleanPressure);

    protected virtual void IpFreezeToTrap(ISection trap)
    {

        if (trap == null)
        {
            ConfigurationError($"Can't find Section {trap.Name}");
            return;
        }

        if (!IpIm(out ISection im)) return;
        var im_trap = FirstOrDefault<Section>(s =>
            s.Chambers?.First() == im.Chambers?.First() &&
            s.Chambers?.Last() == trap.Chambers?.Last());

        if (im_trap == null)
        {
            ConfigurationError($"Can't find Section linking {im.Name} and {trap.Name}");
            return;
        }

        ProcessStep.Start($"Evacuate {im.Name}");
        im.Evacuate(CleanPressure);
        WaitForStablePressure(im.VacuumSystem, CleanPressure);
        ProcessStep.End();

        ProcessStep.Start($"Evacuate and Freeze {trap.Name}");
        trap.EmptyAndFreeze(CleanPressure);
        trap.FreezeWait();
        ProcessStep.End();

        ProcessStep.Start($"Freeze the CO2 from {InletPort.Name} into the {trap.Name}");
        im_trap.Close();
        InletPort.Open();
        InletPort.State = LinePort.States.InProcess;
        if (Sample is Sample) Sample.State = Components.Sample.States.InProcess;
        im_trap.Open();
        WaitMinutes((int)CollectionMinutes);
        trap.Isolate();
        InletPort.Close();
        InletPort.State = LinePort.States.Complete;
        ProcessStep.End();
    }

    protected virtual void TransferCO2FromCTToVTT()
    {
        Sample.AddTrap(VTT.Name);
        TransferCO2(FirstTrap, VTT, -30);
    }

    #endregion Collect

    #region Extract
    protected virtual void ExtractAt(int targetTemp)
    {
        ProcessStep.Start($"Extract CO2 at {targetTemp:0} °C");
        //SampleLog.Record($"\tCO2 extraction temperature:\t{targetTemp:0}\t°C");

        VTT_MC.Isolate();
        VTT_MC.Close();

        var vtc = VTT.Coldfinger as VTColdfinger;
        vtc.Regulate(targetTemp);
        MC.FreezeWait();

        targetTemp -= 1;            // continue at 1 deg under
        ProcessSubStep.Start($"Wait for {VTT.Name} to reach {targetTemp:0} °C");
        while (!WaitFor(() => Hacs.Stopping || vtc.Temperature >= targetTemp, 10 * 60000, 1000))
        {
            ShutDownAllColdfingers();

            if (Warn($"{vtc.Name} is taking too long to reach {targetTemp:0} °C.",
                "All coldfingers have have been set to Standby.\r\n" +
                "Establish the desired Extraction conditions manually," +
                "then Ok to try again,\r\n" +
                "or Cancel to move on now.\r\n" +
                "Restart the application to abort the process.").Ok())
            {
                continue;
            }
            break;
        }
        ProcessSubStep.End();

        VTT_MC.Open();
        WaitMinutes((int)ExtractionMinutes);
        MC.RaiseLN();

        MC.Manometer.WaitForStable(5);
        MC.Isolate();
        vtc.Standby();

        ProcessStep.End();
    }


    protected virtual void Extract()
    {
        ProcessStep.Start($"Extract CO2 from {VTT.Name} to {MC.Name}");
        MC.OpenAndEvacuateAll(CleanPressure);
        ZeroMC();
        MC.ClosePorts();
        MC.Isolate();
        ExtractAt(CO2ExtractionTemperature);
        ProcessStep.End();
    }

    #endregion Extract

    #region Measure

    protected virtual void WaitForMCStable(int seconds)
    {
        ProcessSubStep.Start($"Wait for μgC in MC to stabilize for {SecondsString(seconds)}");
        ugCinMC.WaitForStable(seconds);
        ProcessSubStep.End();
    }

    protected virtual void WaitForMCStable() => WaitForMCStable(5);

    protected virtual void ZeroMC()
    {
        WaitForMCStable();
        ProcessSubStep.Start($"Zero {MC.Manometer.Name}");
        MC.Manometer.ZeroNow();
        WaitFor(() => !MC.Manometer.Zeroing);       // TODO: add timeout?
        ProcessSubStep.End();
    }

    /// <summary>
    /// Apportion the currently SelectedMicrogramsCarbon into aliquots
    /// based on the MC chamber and port volumes.
    /// </summary>
    /// <param name="sample"></param>
    /// <returns></returns>
    protected virtual void ApportionAliquots()
    {
        var ugC = Sample.SelectedMicrogramsCarbon;
        // if no aliquots were specified, create one
        if (Sample.AliquotsCount < 1) Sample.AliquotsCount = 1;
        var n = Sample.AliquotsCount;
        var v0 = MC.Chambers[0].MilliLiters;
        var vTotal = v0;
        if (n > 1)
        {
            var v1 = MC.Ports[0].MilliLiters;
            vTotal += v1;
            if (n > 2)
            {
                var v2 = MC.Ports[1].MilliLiters;
                vTotal += v2;
                Sample.Aliquots[2].MicrogramsCarbon = ugC * v2 / vTotal;
            }
            Sample.Aliquots[1].MicrogramsCarbon = ugC * v1 / vTotal;
        }
        Sample.Aliquots[0].MicrogramsCarbon = ugC * v0 / vTotal;
    }

    protected virtual void TakeMeasurement()
    {
        ProcessStep.Start("Take measurement");
        if (MC.Manometer.OverRange)
        {
            ProcessSubStep.Start("Expand sample into MC+MCU");
            MC.Ports[0].Open();
            WaitSeconds(15);
            ProcessSubStep.End();
            if (MC.Manometer.OverRange)
            {
                ProcessSubStep.Start("Expand sample into MC+MCU+MCL");
                MC.Ports[1].Open();
                WaitSeconds(15);
                ProcessSubStep.End();
            }
        }

        WaitForMCStable();

        // this is the measurement; a negative sample mass confounds H2 calculation, so it is disallowed
        double ugC = Math.Max(0, ugCinMC.WaitForAverage((int)MeasurementSeconds));
        if (Sample != null)
        {
            Sample.SelectedMicrogramsCarbon = ugC;
            ApportionAliquots();

            string yield = "";
            if (Sample.TotalMicrogramsCarbon == 0)    // first measurement
            {
                yield = $"\tYield: {100 * ugC / Sample.Micrograms:0.00}%";
                Sample.TotalMicrogramsCarbon = ugC;
            }

            SampleLog.Record("Sample measurement:\r\n" +
                $"\t{Sample.LabId}\t{Sample.Milligrams:0.0000}\tmg\r\n" +
                $"\tCarbon:\t{ugC:0.0} µgC (={ugC / GramsCarbonPerMole:0.00} µmolC){yield}"
            );
        }
        else
        {
            TestLog.Record("CO2 measurement:\r\n" +
                $"\tCarbon:\t{ugC:0.0} µgC (={ugC / GramsCarbonPerMole:0.00} µmolC)"
            );
        }
        ProcessStep.End();
    }


    protected virtual void Measure()
    {
        ProcessStep.Start("Prepare to measure MC contents");
        MC.Isolate();

        if (MC.IsActivelyCooling)
        {
            #region release incondensables
            ProcessStep.Start("Release incondensables");

            MC.OpenPorts();
            MC.RaiseLN();
            MC.JoinToVacuum();
            MC.VacuumSystem.Evacuate(CleanPressure);

            ZeroMC();
            if ((Sample?.AliquotsCount ?? 1) < 3)
            {
                MC.Ports[1].Close();
                if ((Sample?.AliquotsCount ?? 1) < 2) MC.Ports[0].Close();
                WaitSeconds(5);
            }
            ProcessStep.End();
            #endregion release incondensables

            MC.Isolate();
        }

        if (!MC.Thawed)
        {
            ProcessSubStep.Start("Bring MC to uniform temperature");
            MC.Thaw();
            bool thawed()
            {
                if (MC.Manometer.OverRange)
                {
                    if (MC.Ports[0].IsClosed)
                        MC.Ports[0].Open();
                    else if (MC.Ports[1].IsClosed)
                        MC.Ports[1].Open();
                }
                return MC.Thawed;
            }
            WaitFor(() => thawed(), -1, 1000); // (timeout is handled by Coldfinger)
            ProcessSubStep.End();
        }

        ProcessStep.End();

        ProcessStep.Start("Measure Sample");
        TakeMeasurement();
        ProcessStep.End();
    }


    protected virtual void DiscardSplit()
    {
        ProcessStep.Start("Discard Excess sample");
        while (Sample.Aliquots[0].MicrogramsCarbon > MaximumSampleMicrogramsCarbon)
        {
            ProcessSubStep.Start("Evacuate Split");
            Split.Evacuate(CleanPressure);
            ProcessSubStep.End();

            ProcessSubStep.Start("Split sample");
            Split.IsolateFromVacuum();
            MC_Split.Open();

            var seconds = (int)GetParameter("SplitEquilibrationSeconds");
            ProcessSubStep.Start($"Wait {SecondsString(seconds)} for sample to equilibrate.");
            WaitSeconds(seconds);
            ProcessSubStep.End();

            MC_Split.Close();
            ProcessSubStep.End();

            ProcessSubStep.Start("Discard split");
            Split.Evacuate(CleanPressure);
            ProcessSubStep.End();

            Sample.Discards++;
            SampleLog.Record(
                $"Split discarded:\r\n\t{Sample.LabId}\t{Sample.Milligrams:0.0000}\tmg"
            );
            TakeMeasurement();
        }
        if (Sample.Discards > 0)
        {
            var splitRatio = 1 + Split.MilliLiters / MC.MilliLiters;
            var estimatedTotal = Sample.SelectedMicrogramsCarbon * Math.Pow(splitRatio, Sample.Discards);
            if (estimatedTotal > Sample.TotalMicrogramsCarbon)
            {
                var ugC = Sample.TotalMicrogramsCarbon = estimatedTotal;
                var yield = $"\tYield: {100 * ugC / Sample.Micrograms:0.00}%";

                SampleLog.Record($"Updated TotalMicrogramsCarbon based on post-Discard measurement\r\n" +
                    $"\t{Sample.LabId}\t{Sample.Milligrams:0.0000}\tmg\r\n" +
                    $"\tCarbon:\t{ugC:0.0} µgC (={ugC / GramsCarbonPerMole:0.00} µmolC){yield}"
                );
            }
        }
        ProcessStep.End();
    }

    #endregion Measure

    #region Graphitize

    protected virtual void DivideAliquots()
    {
        // When exactly two aliquots are to be taken, but MCP1 was opened
        // due to the total sample size, then before continuing, the sample
        // must be re-frozen from MC+MCP0+MCP1 to the MC, and then re-thawed,
        // expanding it into just the MC+MCP0 (i.e., after having closed MCP1).
        // In all other cases, the correct chambers should be opened already.
        if (Sample.AliquotsCount == 2 &&
            MC.Ports.Count > 2 &&
            MC.Ports[1].IsOpened) // MC.Ports[0].Opened can be presumed, since AliquotsCount == 2
        {
            MC.FreezeWait();

            ProcessSubStep.Start($"Wait for the sample to freeze in {MC.Name}");
            // CO2TransferMinutes is certainly excessive here, but how much is right?
            WaitFor(() => ProcessSubStep.Elapsed.TotalMinutes >= CO2TransferMinutes / 2);
            MC.RaiseLN();
            ProcessSubStep.End();

            MC.Ports[1].Close();

            ProcessSubStep.Start($"Thaw and expand sample into {MC.Name}..{MC.Ports[0].Name}");
            MC.Thaw();
            WaitFor(() => MC.Thawed);    // TODO: add timeout?
            ProcessSubStep.End();
        }

        // Don't close the ports if only 1 aliquot; it might be
        // expanded to avoid an MC.Manometer.Overrange
        if (Sample.AliquotsCount > 1)
            MC.ClosePorts();
    }

    // TODO: move this routine to the graphite reactor class?
    protected virtual void TrapSulfur(IGraphiteReactor gr)
    {
        var ftc = gr.Coldfinger;
        var h = gr.Heater;

        ProcessStep.Start("Trap sulfur.");
        SampleLog.Record(
            $"Trap sulfur in {gr.Name} at {SulfurTrapTemperature} °C for {MinutesString((int)SulfurTrapMinutes)}");
        ftc.Thaw();
        gr.TurnOn(SulfurTrapTemperature);
        ProcessSubStep.Start($"Wait for {gr.Name} to reach sulfur trapping temperature (~{SulfurTrapTemperature} °C).");
        WaitFor(() => ftc.Temperature > 0 && gr.SampleTemperature > SulfurTrapTemperature - 5);    // TODO: add timeout
        ProcessSubStep.End();

        ProcessSubStep.Start("Hold for " + MinutesString((int)SulfurTrapMinutes));
        WaitMinutes((int)SulfurTrapMinutes);
        ProcessSubStep.End();

        h.TurnOff();
        ProcessStep.End();
    }

    protected virtual void RemoveSulfur()
    {
        if (!Sample.SulfurSuspected) return;

        ProcessStep.Start("Remove sulfur.");

        IGraphiteReactor gr = NextSulfurTrap(PriorGR);
        gr.Reserve("sulfur");
        gr.Aliquot.ResidualMeasured = true;    // prevent graphitization retry
        TransferCO2FromMCToGR(gr, 0, true);
        TrapSulfur(gr);
        TransferCO2FromGRToMC(gr, false);
        gr.State = GraphiteReactor.States.WaitService;

        ProcessStep.End();
        Measure();
    }

    protected virtual void Freeze(Aliquot aliquot)
    {
        if (aliquot == null) return;
        if (aliquot.Name.IsBlank())
        {
            aliquot.Name = NextGraphiteNumber.ToString();
            NextGraphiteNumber++;
        }

        var size = EnableSmallReactors && aliquot.MicrogramsCarbon <= SmallSampleMicrogramsCarbon ?
            GraphiteReactor.Sizes.Small :
            GraphiteReactor.Sizes.Standard;
        IGraphiteReactor gr = NextGR(PriorGR, size);
        if (gr == null)
        {
            Pause("No graphite reactor available",
                $"Can't find a suitable graphite reactor for aliquot {aliquot.Name}.\r\n" +
                $"Sample size: {aliquot.MicrogramsCarbon:0.0} µgC ({aliquot.MicromolesCarbon:0.00} µmol).\r\n" +
                "Restart the application to abort the process."
                , NoticeType.Error);

            return;
        }

        TransferCO2FromMCToGR(gr, aliquot.Sample.AliquotIndex(aliquot));
    }

    protected virtual double[] AdmitGasToPort(IGasSupply gs, double initialTargetPressure, IPort port)
    {
        if (!(gs?.FlowManager?.Meter is IMeter meter))
        {
            ConfigurationError($"AdmitGasToPort: {gs?.Name}.FlowManager.Meter is invalid.");
            return [double.NaN, double.NaN];
        }

        gs.Pressurize(initialTargetPressure);
        gs.IsolateFromVacuum();

        double pInitial = meter.WaitForAverage((int)MeasurementSeconds);
        if (port.Coldfinger?.IsActivelyCooling ?? false)
            port.Coldfinger.RaiseLN();

        ProcessSubStep.Start($"Admit {gs.GasName} into {port.Name}");
        port.Open();
        WaitSeconds(5);
        port.Close();
        ProcessSubStep.End();
        WaitSeconds(5);
        double pFinal = meter.WaitForAverage((int)MeasurementSeconds);
        return [pInitial, pFinal];
    }

    protected virtual void AddH2ToGR(IAliquot aliquot)
    {
        var gr = Find<IGraphiteReactor>(aliquot.GraphiteReactor);
        if (!GrGmH2([gr], out ISection gm, out IGasSupply H2)) return;

        double mL_GR = gr.MilliLiters;

        double nCO2 = aliquot.MicrogramsCarbon * CarbonAtomsPerMicrogram;  // number of CO2 particles in the aliquot
        double nH2target = H2_CO2GraphitizationRatio * nCO2;   // ideal number of H2 particles for the reaction

        // The pressure of nH2target in the frozen GR, where it will be denser.
        var targetFinalH2Pressure = Pressure(nH2target, mL_GR, gm.Temperature);
        // the small reactors don't seem to require the density adjustment.
        if (gr.Size == GraphiteReactor.Sizes.Standard)
            targetFinalH2Pressure *= H2DensityAdjustment;

        double nH2 = 0;
        double pH2ratio = 0;
        for (int i = 0; i < 3; ++i)     // up to three tries
        {
            var targetInitialH2Pressure = targetFinalH2Pressure +
                Pressure(nH2target, gm.MilliLiters, gm.Temperature);

            // The GM pressure drifts a bit after the H2 is introduced, generally downward.
            // This value compensates for the consequent average error, which was about -4,
            // averaged over 14 samples in Feb-Mar 2018.
            // The compensation is bumped by a few more Torr to shift the variance in
            // target error toward the high side, as a slight excess of H2 is not
            // deleterious, whereas a deficiency could be.
            double driftAndVarianceCompensation = 9;    // TODO this should be a setting

            gm.Isolate();
            var p = AdmitGasToPort(
                H2,
                targetInitialH2Pressure + driftAndVarianceCompensation,
                gr);
            var pH2initial = p[0];
            var pH2final = p[1];

            // this is what we actually got
            nH2 += Particles(pH2initial - pH2final, gm.MilliLiters, gm.Temperature);
            aliquot.H2CO2PressureRatio = pH2ratio = nH2 / nCO2;

            double nExpectedResidual;
            if (pH2ratio > H2_CO2StoichiometricRatio)
                nExpectedResidual = nH2 - nCO2 * H2_CO2StoichiometricRatio;
            else
                nExpectedResidual = nCO2 - nH2 / H2_CO2StoichiometricRatio;

            aliquot.InitialGmH2Pressure = pH2initial;
            aliquot.FinalGmH2Pressure = pH2final;
            aliquot.ExpectedResidualPressure = TorrPerKelvin(nExpectedResidual, mL_GR);

            SampleLog.Record(
                $"GR hydrogen measurement:\r\n\t{Sample.LabId}\r\n\t" +
                $"Graphite {aliquot.Name}\t{aliquot.MicrogramsCarbon:0.0}\tµgC\t={aliquot.MicromolesCarbon:0.00}\tµmolC\t{aliquot.GraphiteReactor}\t" +
                $"pH2:CO2\t{pH2ratio:0.00}\t" +
                $"{aliquot.InitialGmH2Pressure:0} => {aliquot.FinalGmH2Pressure:0}\r\n\t" +
                $"expected residual:\t{aliquot.ExpectedResidualPressure:0.000}\tTorr/K"
                );

            if (pH2ratio >= H2_CO2StoichiometricRatio * 1.05)
                break;

            // try to add more H2
            targetFinalH2Pressure *= nH2target / nH2;

            if (targetFinalH2Pressure > 1500)       // way more than reasonable
            {
                if (!Warn($"Excessive H2 pressure required.",
                    $"The H2 is not going into {aliquot.GraphiteReactor}.\r\n" +
                    $"Process paused.\r\n" +
                    $"Ok to continue trying or Cancel to move on as is.\r\n" +
                    $"Restart the application to abort the process.").Ok())
                {
                    break;
                }
            }
        }

        if (pH2ratio < H2_CO2StoichiometricRatio * 1.05)
        {
            Pause($"Not enough H2 in {aliquot.GraphiteReactor}",
                "Process paused.\r\n" +
                "Restart the application to abort the process,\r\n" +
                "or choose Ok to continue as is."
                , NoticeType.Warning);
        }
    }

    protected virtual void Dilute()
    {
        if (Sample == null || DilutedSampleMicrogramsCarbon <= SmallSampleMicrogramsCarbon || Sample.TotalMicrogramsCarbon > SmallSampleMicrogramsCarbon) return;

        ProcessStep.Start($"Dilute sample to {DilutedSampleMicrogramsCarbon}");
        //Clean(VTT);
        TransferCO2FromMCToVTT();
        AdmitDeadCO2(DilutedSampleMicrogramsCarbon - Sample.TotalMicrogramsCarbon);
        TransferCO2FromMCToVTT();   // add the dilution gas to the sample
        Extract();
        Measure();
        ProcessStep.End();
    }

    protected virtual void FreezeAliquots()
    {
        foreach (Aliquot aliquot in Sample.Aliquots)
            Freeze(aliquot);
    }

    protected virtual void GraphitizeAliquots()
    {
        var grs = Sample.Aliquots.Select(a => Find<IGraphiteReactor>(a.GraphiteReactor));

        var gm = Manifold(grs);
        if (gm == null)
        {
            var grList = string.Join(", ", grs.Select(gr => gr.Name));
            ConfigurationError($"Can't find graphite manifold for {grList}.");
            return;
        }

        gm.IsolateFromVacuum();
        foreach (Aliquot aliquot in Sample.Aliquots)
        {
            ProcessStep.Start("Graphitize aliquot " + aliquot.Name);
            AddH2ToGR(aliquot);
            Find<IGraphiteReactor>(aliquot.GraphiteReactor).Start();
            ProcessStep.End();
        }
        gm.ClosePorts();
        gm.OpenAndEvacuate();
    }


    #endregion Graphitize

    protected virtual void CollectEtc()
    {
        Collect();
        ExtractEtc();
    }

    protected virtual void ExtractEtc()
    {
        Extract();
        MeasureEtc();
    }

    protected virtual void MeasureEtc()
    {
        Measure();
        DiscardSplit();
        RemoveSulfur();
        GraphitizeEtc();
    }

    protected virtual void GraphitizeEtc()
    {
        try
        {
            Dilute();
            DivideAliquots();
            FreezeAliquots();
            var holdSampleAtPorts = GetParameter("HoldSampleAtPorts");
            if (!holdSampleAtPorts.IsANumber() || holdSampleAtPorts == 0)
                GraphitizeAliquots();
        }
        catch (Exception e)
        {
            Pause("Exception in GraphtitizeEtc()",
                $"{e}\r\n" +
                "Process paused.\r\n" +
                "Restart the application to abort the process,\r\n" +
                "or choose Ok to continue as is."
                , NoticeType.Error);
        }
    }

    #endregion Sample extraction and measurement

    #region Transfer CO2 between chambers

    /// <summary>
    /// Find a defined Section with the provided first and last Chambers.
    /// </summary>
    /// <param name="first">The first chamber in the Section's Chamber list.</param>
    /// <param name="last">The last chamber in the Section's Chamber list.</param>
    /// <returns></returns>
    protected virtual ISection SectionSpanning(IChamber first, IChamber last) =>
        FirstOrDefault<ISection>(s => s.Chambers.First() == first && s.Chambers.Last() == last);

    /// <summary>
    /// Find a defined Section that starts with fromSection and ends with toSection or vice-versa.
    /// </summary>
    /// <param name="fromSection">A (sub)Section at one extreme end of the desired Section.</param>
    /// <param name="toSection">A (sub)Section at the other extreme end of the desired Section.</param>
    /// <returns></returns>
    protected virtual ISection JoinedSection(ISection fromSection, ISection toSection)
    {
        var first = fromSection.Chambers.First();
        var last = toSection.Chambers.Last();
        return SectionSpanning(first, last) ?? SectionSpanning(last, first);
    }

    // If fromSection doesn't have a Coldfinger or VTColdfinger, this method
    // assumes fromSection is thawed (i.e., if there is an LN dewar on
    // fromSection, it must be removed before calling this method).
    protected virtual void TransferCO2(ISection fromSection, ISection toSection, double thawTemperature = double.NaN)
    {
        var combinedSection = JoinedSection(fromSection, toSection);
        if (combinedSection == null)
        {
            ConfigurationError($"Can't find Section linking {fromSection.Name} and {toSection.Name}");
            return;
        }

        ProcessStep.Start($"Transfer CO2 from {fromSection.Name} to {toSection.Name}");
        fromSection.Isolate();

        var toSectionEvacuatesThroughFromSection =
            toSection.PathToVacuum.SafeIntersect(fromSection.Isolation).Count > 0;

        if (toSectionEvacuatesThroughFromSection)
        {
            toSection.Isolate();
            toSection.Freeze();
        }
        else
            toSection.EmptyAndFreeze(CleanPressure);

        // start thawing
        if (thawTemperature.IsANumber())
            fromSection.Thaw(thawTemperature);
        else
            fromSection.Thaw();

        ProcessStep.Start("Wait for transfer start conditions.");
        WaitFor(() =>
            toSection.Frozen &&
            fromSection.Temperature > CO2TransferStartTemperature,
            interval: 1000); // timeout checked by coldfinger
        ProcessStep.End();

        ProcessSubStep.Start($"Join {toSection.Name} to {fromSection.Name}");
        combinedSection.Isolate();
        combinedSection.Open();
        ProcessSubStep.End();

        ProcessSubStep.Start("Wait for CO2 transfer complete.");
        WaitMinutes((int)CO2TransferMinutes);
        ProcessSubStep.End();

        toSection.Raise();

        ProcessSubStep.Start($"Isolate {toSection.Name}");
        toSection.Isolate();
        ProcessSubStep.End();

        ProcessStep.End();
    }

    protected virtual void TransferCO2FromMCToVTT() =>
        TransferCO2(MC, VTT);

    protected virtual void TransferCO2FromMCToStandardGR() =>
        TransferCO2FromMCToGR(NextGR(PriorGR), 0);

    protected virtual void TransferCO2FromMCToGR() =>
        TransferCO2FromMCToGR(NextGR(PriorGR,
            EnableSmallReactors && ugCinMC <= SmallSampleMicrogramsCarbon ?
                GraphiteReactor.Sizes.Small :
                GraphiteReactor.Sizes.Standard), 0);

    protected virtual void Open_d13CPort(Id13CPort port) => port.Open();
    protected virtual void Close_d13CPort(Id13CPort port) => port.Close();

    protected virtual void TransferCO2FromMCToGR(IGraphiteReactor gr, int aliquotIndex = 0, bool skip_d13C = false)
    {
        if (gr == null) return;
        if (!GrGm([gr], out ISection gm)) return;

        var mc_gm = JoinedSection(MC, gm);

        var nHoldSampleAtPorts = GetParameter("HoldSampleAtPorts");
        var holdSampleAtPorts = nHoldSampleAtPorts.IsANumber() && nHoldSampleAtPorts != 0;

        if (mc_gm == null)
        {
            ConfigurationError($"Can't find Section joining {MC.Name} to {gm.Name}");
            return;
        }
        PriorGR = gr.Name;

        IAliquot aliquot = gr.Aliquot;
        if (aliquot == null && Sample != null && Sample.AliquotsCount > aliquotIndex)
        {
            aliquot = Sample.Aliquots[aliquotIndex];
            gr.Reserve(aliquot);
        }

        var take_d13C = !skip_d13C && (Sample?.Take_d13C ?? false) && aliquotIndex == 0;
        if (take_d13C && gr.Aliquot != null && gr.Aliquot.MicrogramsCarbon < MinimumUgCThatPermits_d13CSplit)
        {
            Announce($"{Sample.LabId} ({gr.Aliquot.MicrogramsCarbon:0.0} µgC) is too small for d13C",
                $"d13C was requested but the sample mass\r\n" +
                $"is too small to take the split (< {MinimumUgCThatPermits_d13CSplit}).");

            //if (Prompt("Sample too small for d13C",
            //    $"d13C was requested but the sample ({gr.Aliquot.MicrogramsCarbon:0.0} µmol) is too small." +
            //    "Process paused.\r\n" +
            //    "To abort the process, restart the application.\r\n" +
            //    "Take the d13C split anyway, or skip d13C?",
            //    NoticeType.Error, responses: ["Take d13C split", "Skip d13C"]).Message != "Take d13C split")
            //{
                take_d13C = false;
            //}

        }

        Id13CPort d13CPort = take_d13C ? Guess_d13CPort(Sample) : null;

        ProcessStep.Start("Evacuate paths to sample destinations");
        MC.Isolate();
        gm.ClosePortsExcept(gr);
        if (gr.IsOpened)
            gm.IsolateExcept(gm.PathToVacuum);
        else
        {
            gm.Isolate();
            gr.Open();
        }

        var toBeOpened = gm.PathToVacuum.SafeUnion(gm.InternalValves);
        if (d13CPort != null)
        {
            if (d13CPort.ShouldBeClosed)
            {
                Pause($"{d13CPort.Name} is not available",
                    $"Need to take d13C, but {d13CPort.Name} is not available.\r\n" +
                    $"To abort the process, restart the application.\r\n" +
                    $"Or, make {d13CPort.Name} available (Loaded or Prepared) and\r\n" +
                    $"Choose Ok to continue." +
                    $"If {d13CPort.Name} is still unavailable when you continue,\r\n" +
                    $"the d13C split will not be taken.");
            }

            if (d13CPort.ShouldBeClosed)
            {
                d13CPort = null;
            }
            else
            {
                toBeOpened = toBeOpened.SafeUnion(d13C.PathToVacuum);
                if (d13CM != null)
                {
                    toBeOpened = toBeOpened.SafeUnion(d13CM.PathToVacuum);
                    if (d13CPort.IsOpened)
                        d13CM.IsolateExcept(toBeOpened);
                    else
                        d13CM.Isolate();
                    d13CM.ClosePortsExcept(d13CPort);
                }
                else
                {
                    if (d13CPort.IsOpened)
                        d13C.IsolateExcept(toBeOpened);
                    else
                        d13C.Isolate();
                }
                Open_d13CPort(d13CPort);
                Sample.d13CPort = d13CPort;
            }
        }
        mc_gm.IsolateExcept(toBeOpened);
        gm.VacuumSystem.IsolateExcept(toBeOpened);
        toBeOpened.Open();
        ProcessSubStep.Start($"Waiting for {gm.VacuumSystem.Manometer.Name} < {CleanPressure:0.00} Torr");
        gm.VacuumSystem.Evacuate(CleanPressure);
        ProcessSubStep.End();
        gr.Manometer.ZeroNow(true); // wait to finish
        ProcessStep.End();

        ProcessStep.Start("Expand the sample");

        if (d13CPort != null)
        {
            if (d13CM == null)
                Close_d13CPort(d13CPort);
            else
                d13CM.Isolate();

            gr.Close();
        }

        mc_gm.IsolateFromVacuum();

        // release the sample
        var mcPort = aliquotIndex > 0 ? MC.Ports[aliquotIndex - 1] : null;
        mcPort?.Open();      // take it from from an MC port

        mc_gm.Open();

        ProcessStep.End();


        if (d13CPort != null)
        {
            ProcessStep.Start("Take d13C split");

            var seconds = (int)GetParameter("SplitEquilibrationSeconds");
            ProcessSubStep.Start($"Wait {SecondsString(seconds)} for sample to equilibrate.");
            WaitSeconds(seconds);
            ProcessSubStep.End();

            d13C.IsolateFrom(mc_gm);
            Sample.Micrograms_d13C = aliquot.MicrogramsCarbon * d13C.MilliLiters / (d13C.MilliLiters + mc_gm.MilliLiters);
            aliquot.MicrogramsCarbon -= Sample.Micrograms_d13C;
            d13C.JoinTo(d13CM); // does nothing if there is no d13CM
            Open_d13CPort(d13CPort);
            d13CPort.State = LinePort.States.InProcess;
            d13CPort.Aliquot = aliquot;
            d13CPort.Coldfinger.Freeze();
            ProcessStep.End();

            ProcessStep.Start($"Freeze sample into {gr.Name} and {d13CPort.Name}");
            gr.Open();
        }
        else
            ProcessStep.Start($"Freeze sample into {gr.Name}");

        var grCF = gr.Coldfinger;
        grCF.Freeze();

        var grDone = false;
        var d13CDone = d13CPort == null;
        ProcessSubStep.Start($"Wait for coldfinger{(d13CPort == null ? "" : "s")} to freeze");
        bool bothFrozen()
        {
            if (!grDone) grDone = grCF.Frozen;
            if (!d13CDone) d13CDone = d13CPort.Coldfinger.Frozen;
            return grDone && d13CDone;
        }
        WaitFor(bothFrozen, interval: 1000);
        ProcessSubStep.End();

        WaitMinutes((int)CO2TransferMinutes);

        ProcessSubStep.Start("Raise LN");
        void jgr()
        {
            grCF.RaiseLN();
            gr.Close();
            // Note: removed release incondensables
        }
        void jd13()
        {
            if (d13CPort == null) return;
            d13CPort.Coldfinger.RaiseLN();
            Close_d13CPort(d13CPort);
            if (!holdSampleAtPorts)
                AddCarrierTo_d13C();
            d13CPort.Coldfinger.Standby();
            d13CPort.State = LinePort.States.Complete;
        }
        var tgr = Task.Run(jgr);
        var td13 = Task.Run(jd13);
        WaitFor(() => tgr.IsCompleted && td13.IsCompleted);
        ProcessSubStep.End();

        if (holdSampleAtPorts)
            grCF.Standby();

        ProcessStep.End();
    }

    protected virtual void AddCarrierTo_d13C()
    {
        if (Sample == null || !Sample.Take_d13C) return;

        var port = Sample.d13CPort;
        if (port == null) return;
        var manifold = Manifold(port);
        var gasSupply = InertGasSupply(manifold);

        manifold.Evacuate();
        port.Coldfinger.Freeze();

        // Desired final vial pressure at room temperature.
        var pTarget = PressureOverAtm;
        var nTarget = Particles(pTarget, port.MilliLiters, RoomTemperature);
        var nCO2 = Sample.Micrograms_d13C * CarbonAtomsPerMicrogram;
        var totalMilliliters = manifold.MilliLiters + port.MilliLiters;

        // How far the manifold pressure needs to fall to produce the nominal vial pressure.
        var dropTarget = Pressure(nTarget, totalMilliliters, RoomTemperature);

        // VPInitialHePressure is found empirically to produce
        // PressureOverAtm inside the vial when it is at room temperature.
        var pa = AdmitGasToPort(gasSupply, VPInitialHePressure, port);
        var pHeInitial = pa[0];
        var pHeFinal = pa[1];

        var manifoldTemperature = manifold.Thermometer?.Temperature ?? RoomTemperature;
        // This is what we actually got:
        var nHe = Particles(pHeInitial - pHeFinal, manifold.MilliLiters, manifoldTemperature);
        var n = nHe + nCO2;
        Sample.d13CPartsPerMillion = 1e6 * nCO2 / n;

        var pVP = Pressure(n, port.MilliLiters, RoomTemperature);
        SampleLog.Record(
            $"d13C measurement:\r\n\t{Sample.LabId}\r\n" +
            $"\tGraphite {Sample.Aliquots[0].Name}" +
            $"\td13C:\t{Sample.Micrograms_d13C:0.0}\tugC" +
            $"\t{Sample.d13CPartsPerMillion:0}\tppm" +
            $"\tvial pressure:\t{pVP:0} / {pTarget:0}\tTorr"
        );

        var pError = pVP - pTarget;
        if (Math.Abs(pError) > VPErrorPressure)
        {
            if (pError > 3 * VPErrorPressure || pError < -2 * VPErrorPressure)
                SampleLog.Record("\tWarning: excessive vial pressure error: ");
            else
                SampleLog.Record("\tVial pressure out of nominal range: ");
            SampleLog.Record(
                $"\t\tpHeGM: ({pHeInitial:0} => {pHeFinal:0}) / " +
                $"({VPInitialHePressure:0} => {VPErrorPressure - dropTarget})");
            Tell($"Vial He pressure error: {pError:0}");
        }
        port.Coldfinger.Thaw();
    }

    protected virtual void TransferCO2FromGRToMC() =>
        TransferCO2FromGRToMC(Find<IGraphiteReactor>(PriorGR), true);

    protected virtual void TransferCO2FromGRToMC(IGraphiteReactor gr, bool firstFreezeGR)
    {
        if (!GrGm([gr], out ISection gm)) return;
        var mc_gm = JoinedSection(MC, gm);
        if (mc_gm == null)
        {
            ConfigurationError($"Can't find Section joining {MC.Name} to {gm.Name}");
            return;
        }

        var grCF = gr.Coldfinger;

        ProcessStep.Start($"Transfer CO2 from {gr.Name} to {MC.Name}.");

        if (firstFreezeGR)
        {
            gr.Close();        // it should be closed already
            grCF.Freeze();
        }

        mc_gm.OpenAndEvacuate(CleanPressure);
        MC.ClosePorts();

        if (firstFreezeGR)
        {
            grCF.RaiseLN();

            ProcessSubStep.Start("Evacuate incondensables.");
            mc_gm.OpenAndEvacuate(CleanPressure);
            gr.Open();
            mc_gm.VacuumSystem.WaitForPressure(CleanPressure);
            mc_gm.IsolateFromVacuum();
            ProcessSubStep.End();
        }
        else
        {
            mc_gm.IsolateFromVacuum();
            ProcessSubStep.Start($"Open the path from {gr.Name} to {MC.Name}");
            gr.Open();
            mc_gm.Open();
            ProcessSubStep.End();
        }

        grCF.Thaw();
        var mcCF = MC.Coldfinger;
        mcCF.FreezeWait();

        ProcessSubStep.Start($"Wait for sample to freeze into {MC.Name}");
        WaitMinutes((int)CO2TransferMinutes);
        mcCF.RaiseLN();
        mc_gm.Close();
        gr.Close();
        ProcessSubStep.End();

        ProcessStep.End();
    }

    protected virtual void TransferCO2FromMCToIP() => TransferCO2FromMCToIPViaVM();

    /// <summary>
    /// Transfer CO2 from the MC to the IP via a 
    /// transfer vessel at the graphite reactor.
    /// </summary>
    protected virtual void TransferCO2FromMCToIPviaGR()
    {
        var grName = Sample.Aliquots[0].GraphiteReactor;
        var gr = Find<GraphiteReactor>(grName);
        if (gr == null)
        {
            TransferCO2FromMCToGR();
            grName = Sample.Aliquots[0].GraphiteReactor;
        }
        else
            TransferCO2FromMCToGR(gr);

        WaitForOperator($"Move transfer vessel to {Sample.InletPort.Name}\r\n" +
            $"Close the transfer vessel,\r\n" +
            $"then move it from {grName} to {Sample.InletPort.Name}." +
            $"Keep it closed.");
        gr.Coldfinger.Standby();

        InletPort.State = LinePort.States.Loaded;
        InletPort.Sample = Sample;
        if (Sample is Sample) Sample.State = Components.Sample.States.Loaded;
    }

    /// <summary>
    /// Transfer CO2 from the MC to the IP via the VM.
    /// Useful when IM_MC contains a flow restriction.
    /// </summary>
    protected virtual void TransferCO2FromMCToIPViaVM()
    {
        if (!IpIm(out ISection im)) return;
        ProcessStep.Start($"Evacuate and join {InletPort.Name}..Split via VM");
        EvacuateIP();
        im.IsolateFromVacuum();
        Split.Evacuate();
        im.JoinToVacuum();
        im.VacuumSystem.WaitForPressure(0);
        ProcessStep.End();

        ProcessStep.Start($"Transfer CO2 from MC to {InletPort.Name}");
        Pause("Operator Needed",
            $"Almost ready for LN on {InletPort.Name}.\r\n" +
            $"First choose Ok to continue, then raise LN onto {InletPort.Name} coldfinger.");

        im.VacuumSystem.Isolate();
        MC_Split.Open();
        WaitMinutes((int)CO2TransferMinutes, $"Wait {MinutesString((int)CO2TransferMinutes)} for CO2 to freeze into {InletPort.Name}");

        WaitForOperator($"Raise {InletPort.Name} LN one inch.");

        WaitSeconds(15, "Wait 15 seconds for LN to settle.");
        InletPort.Close();
        ProcessStep.End();
    }

    #endregion Transfer CO2 between chambers

    #endregion Process Management

    #region Chamber volume calibration routines

    /// <summary>
    /// Install the CalibratedKnownVolume chamber in place of
    /// the port specified in the VolumeCalibration for the MC.
    /// Sets the value of MC.MilliLiters.
    /// </summary>
    protected virtual void MeasureVolumeMC()
    {
        FindAll<VolumeCalibration>().FirstOrDefault(vol => vol.ExpansionVolumeIsKnown)?.Calibrate();
    }

    /// <summary>
    /// Make sure the normal MC ports are installed (and not the
    /// CalibratedKnownVolume or a port plug).
    /// TODO: make sure the settings calibration order is preserved
    /// (add SequenceIndex property to VolumeCalibration?)
    /// </summary>
    protected virtual void MeasureRemainingVolumes()
    {
        VolumeCalibrations.Values.ToList().ForEach(vol =>
        { if (!vol.ExpansionVolumeIsKnown) vol.Calibrate(); });
    }

    /// <summary>
    /// Replace the chamber at the the port specified in the
    /// MC VolumeCalibration with a plug inserted flush to
    /// the fitting shoulder.
    /// Measures a valve's headspace and "OpenedVolumeDelta" which
    /// is the volume added to two chambers joined by the valve when
    /// the valve is opened, as compared to the combined volumes
    /// of the two chambers when the valve is closed.
    /// </summary>
    protected virtual void MeasureValveVolumes()
    {
        TestLog.Record($"MC\tMC+vH+vOD\tMC+vH");

        var volumeCalibrationPortValve = FindAll<VolumeCalibration>().FirstOrDefault(vol => vol.ExpansionVolumeIsKnown).Expansions?[0].ValveList?[0];
        double t2sum = 0, t3sum = 0;

        var n = 5;
        for (int i = 1; i <= n; ++i)
        {
            ProcessStep.Start($"Measure valve volumes (pass {i})");
            ProcessSubStep.Start($"Evacuate, admit gas");
            MC.Isolate();
            MC.OpenPorts();
            MC.Evacuate(OkPressure);
            MC.ClosePorts();
            var GasSupply = InertGasSupply(MC);
            GasSupply.Pressurize(95);
            ProcessSubStep.End();

            ProcessSubStep.Start($"get p0");
            WaitMinutes(1);
            var p0 = MC.Manometer.WaitForAverage((int)MeasurementSeconds) / (MC.Temperature + ZeroDegreesC);
            ProcessSubStep.End();

            // When compared to p2, p1 reveals the volume
            // difference between the valve's Opened and Closed
            // positions.
            ProcessSubStep.Start($"get p1");
            volumeCalibrationPortValve.Open();
            WaitMinutes(1);
            var p1 = MC.Manometer.WaitForAverage((int)MeasurementSeconds) / (MC.Temperature + ZeroDegreesC);
            ProcessSubStep.End();

            // When compared to p0, p2 reveals the valve's
            // port side headspace, if the port
            // is plugged flush with the fitting shoulder.
            ProcessSubStep.Start($"get p2");
            volumeCalibrationPortValve.Close();
            WaitMinutes(1);
            var p2 = MC.Manometer.WaitForAverage((int)MeasurementSeconds) / (MC.Temperature + ZeroDegreesC);
            TestLog.Record($"{p0:0.00000}\t{p1:0.00000}\t{p2:0.00000}");
            t2sum += p0 / p1 - 1;
            t3sum += p0 / p2 - 1;
            ProcessSubStep.End();
            ProcessStep.End();
        }
        var t2 = t2sum / n;
        var t3 = t3sum / n;

        // correct the initially determined MC chamber volume
        // to account for the KV port valve volumes
        var kv = Find<Chamber>("CalibratedKnownVolume").MilliLiters;
        var mc = Find<Chamber>("MC");
        var prior = mc.MilliLiters;
        var t1 = kv / prior;
        mc.MilliLiters = kv / (t1 - t3);
        TestLog.Record($"{mc.Name} (mL): {prior} => {mc.MilliLiters}");

        // valve headspace
        var vH = MC.MilliLiters * t3;
        TestLog.Record($"{volumeCalibrationPortValve.Name} headspace (mL) = {vH}");

        // valve OpenedVolumeDelta;
        var vOD = MC.MilliLiters * t2 - vH;
        TestLog.Record($"{volumeCalibrationPortValve.Name} OpenedVolumeDelta (mL) = {vOD}");

        MC.VacuumSystem.OpenLine();
    }

    #endregion Chamber volume calibration routines

    #region Other calibrations


    protected virtual void CalibrateGRH2()
    {
        var gm = Manifold(GraphiteReactors[0]);

        //var gms = new List<Section>();
        //GraphiteReactors.ForEach(gr =>
        //{
        //    var gm = Manifold(gr);
        //    if (!gms.Contains(gm))
        //        gms.Add(gm);
        //});

        //gms.ForEach(gm =>
        //{
        var grs = new List<IGraphiteReactor>();
        GraphiteReactors.ForEach(gr =>
        {
            if (Manifold(gr) == gm && gr.Prepared)
                grs.Add(gr);
        });
        if (grs.Count > 0)
            CalibrateGRH2(grs);
        //});
    }

    // all of the listed grs must be on the same manifold
    protected virtual void CalibrateGRH2(List<IGraphiteReactor> grs)
    {
        grs.ForEach(gr =>
        {
            if (!gr.Prepared)
            {
                var list = string.Join(", ", grs.Select(gr => gr.Name));
                Tell("CalibrateGRH2() requires all of the selected grs to be Prepared.",
                    $"Selected: {list}\r\n" +
                    $"{gr.Name} (at least) is not.", NoticeType.Error);
                return;
            }
        });

        ProcessStep.Start("Freeze graphite reactors");
        grs.ForEach(gr => gr.Coldfinger.Raise());

        WaitFor(() => grs.All(gr => gr.Coldfinger.Frozen), interval: 1000);

        ProcessStep.End();

        TestLog.WriteLine();
        TestLog.Record("H2DensityRatio test");
        TestLog.Record("GR\tpInitial\tpFinal\tpNormalized\tpRatio");

        GrGmH2(grs, out ISection gm, out IGasSupply gs);

        ProcessStep.Start("Isolate graphite reactors");
        gm.ClosePorts();
        gm.Isolate();
        ProcessStep.End();
        for (int pH2 = 850; pH2 > 100; pH2 /= 2)
        {
            ProcessStep.Start($"Measure density ratios starting with {pH2:0} Torr");

            ProcessSubStep.Start("Evacuate graphite reactors");
            grs.ForEach(gr => gr.Open());
            gm.Evacuate(OkPressure);
            ProcessSubStep.End();

            ProcessSubStep.Start("Isolate graphite reactors");
            gm.ClosePorts();
            ProcessSubStep.End();


            ProcessSubStep.Start($"Pressurize {gm.Name} to {pH2:0} Torr");
            gs.Pressurize(pH2);
            gs.IsolateFromVacuum();
            ProcessSubStep.End();
            grs.ForEach(gr =>
            {
                WaitSeconds(30);
                var pInitial = gm.Manometer.WaitForAverage((int)MeasurementSeconds);

                ProcessSubStep.Start($"Admit H2 into {gr.Name}");
                gr.Coldfinger.RaiseLN();
                gr.Open();
                WaitSeconds(5);
                gr.Close();
                WaitSeconds(60);
                ProcessSubStep.End();

                var pFinal = gm.Manometer.WaitForAverage((int)MeasurementSeconds);

                // pFinal is the pressure in the cold GR with n particles of H2,
                // whereas p would be the pressure if the GR were at the GM temperature.
                // densityAdjustment = pFinal / p
                var n = Particles(pInitial - pFinal, gm.MilliLiters, gm.Temperature);
                var p = Pressure(n, gr.MilliLiters, gm.Temperature);
                // The above uses the measured, GR-specific volume. To avoid errors,
                // this procedure should only be performed if the Fe and perchlorate
                // tubes have never been altered since the GR volumes were measured.
                TestLog.Record($"{gr.Name}\t{pInitial:0.00}\t{pFinal:0.00}\t{p:0.00}\t{pFinal / p:0.000}");
            });
            ProcessStep.End();
        }
        grs.ForEach(gr => gr.Coldfinger.Standby());
        gm.VacuumSystem.OpenLine();
    }

    protected virtual void CalibrateVPHeP0()      // need to test
    {
        var port = FirstOrDefault<Id13CPort>(p => !p.ShouldBeClosed);
        if (port == null)
        {
            Tell("No vial port available", 
                "One must be Loaded or Prepared to find VPInitialHePressure.", NoticeType.Error);
            return;
        }

        var param = CegsPreferences.DefaultParameters.Find(p => p.ParameterName == "VPInitialHePressure");

        TestLog.WriteLine();
        TestLog.Record("d13C He Calibration");
        TestLog.Record($"\tVPInitialHePressure = {param.Value:0}");
        TestLog.Record($"pInitial\tpFinal\tpTarget\tpVP\terror");

        var ftc = port.Coldfinger;
        var manifold = Manifold(port);
        var gasSupply = InertGasSupply(manifold);
        var vacuumSystem = manifold.VacuumSystem;
        var totalMilliLiters = manifold.MilliLiters + port.MilliLiters;

        vacuumSystem.OpenLine();
        ftc.RaiseLN();

        bool adjusted = false;
        do
        {
            vacuumSystem.WaitForPressure(OkPressure);
            port.Close();
            var pa = AdmitGasToPort(gasSupply, VPInitialHePressure, port);
            port.Open();
            manifold.Evacuate();

            var pTarget = PressureOverAtm;
            var nTarget = Particles(pTarget, port.MilliLiters, RoomTemperature);
            var dropTarget = Pressure(nTarget, totalMilliLiters, manifold.Temperature);
            var n = Particles(pa[0] - pa[1], totalMilliLiters, manifold.Temperature);
            var p = Pressure(n, port.MilliLiters, RoomTemperature);

            var error = pTarget - p;
            if (Math.Abs(error) / VPErrorPressure > 0.4)
            {
                var multiplier = pTarget / p;       // no divide-by-zero check
                param.Value = VPInitialHePressure * multiplier;
                adjusted = true;
            }
            else
            {
                adjusted = false;
            }
            SampleLog.Record($"{pa[0]:0}\t{pa[1]:0}\t{pTarget:0}\t{p:0}\t{error:0.0}");

        } while (adjusted);

        ftc.Thaw();
        vacuumSystem.OpenLine();
    }

    #endregion Other calibrations

    #region Test functions

    /// <summary>
    /// Measures the leak rate using a 2-minute rate of rise test.
    /// </summary>
    /// <param name="port"></param>
    /// <returns></returns>
    protected virtual double PortLeakRate(IPort port)
    {
        var manifold = Manifold(port);
        if (manifold == null) return 0;     // can't check; assume ok

        ProcessStep.Start($"Leak checking {port.Name}.");

        var basePressure = GetParameter("LeakTestBasePressure");
        if (!basePressure.IsANumber())
            basePressure = OkPressure; // OkPressure is a convenient but high starting pressure;
                                       // ideally, ror tests start at ultimate pressure.

        ProcessSubStep.Start($"Evacuate {manifold.Name}+{port.Name} to below {basePressure:0.0e0} Torr");
        manifold.Isolate();
        manifold.ClosePortsExcept(port);
        manifold.Open();
        port.Open();
        manifold.Evacuate(basePressure);
        ProcessSubStep.End();

        ProcessStep.End();

        return SectionLeakRate(manifold, LeakTightTorrLitersPerSecond);
    }

    protected void LeakCheckAllPorts()
    {
        var ports = FindAll<IPort>();
        List<string> exclude = ["MCP1", "MCP2", "DeadCO2"];
        ports.ForEach(port =>
        {
            if (!exclude.Contains(port.Name))
            {
                var rate = PortLeakRate(port);
                SampleLog.Record($"{port.Name} leak rate: {rate:0.0e0} Torr L/s");
            }
        });
    }

    /// <summary>
    /// Checks a section's leak rate. The section must be currently isolated and under vacuum.
    /// </summary>
    /// <param name="section"></param>
    /// <returns>Torr L/sec</returns>
    protected double SectionLeakRate(ISection section, double leakRateLimit)
    {
        var testSeconds = 120;      // Aeon's standard rate-of-rise test duration

        ProcessStep.Start($"Leak checking {section.Name}.");

        // For completeness, PathToVacuum's equivalent set of chambers should be included, too. It's
        // neglected for now (it would add little because all Manifold(port)'s reach their VM except for MCP1 and MCP2).
        var liters = (section.CurrentVolume(true) + section.VacuumSystem.VacuumManifold.MilliLiters) / 1000;  // volume in Liters
        var torr = testSeconds * leakRateLimit / liters;    // change in pressure at leakRateLimit for testSeconds
        var torrLiters = torr * liters;

        var p0 = section.VacuumSystem.Pressure;
        section.VacuumSystem.Isolate();
        var torrLimit = p0 + torr;
        ProcessSubStep.Start($"Wait up to {testSeconds:0} seconds for {torrLimit:0.0e0} Torr");
        var leaky = WaitFor(() => section.VacuumSystem.Pressure > torrLimit, testSeconds * 1000, 1000);
        var elapsed = ProcessSubStep.Elapsed.TotalSeconds;
        torr = section.VacuumSystem.Pressure - p0;     // actual change in pressure
        ProcessSubStep.End();

        ProcessStep.End();
        return torr * liters / elapsed;
    }

    protected void CalibrateManualHeater(IHeater h, IThermocouple tc)
    {
        // Constants
        var oneSecond = 1000;       // milliseconds
        var oneMinute = 60 * oneSecond;

        double pvTarget = 850;      // degrees C
        double pvLimit = 1000;      // degrees C

        double dCoMin = 0.01;       // minimum control output change
        double dCoMax = 1.0;        // maximum CO change
        double dCo = dCoMin;        // calculated CO change
        double dCo0 = dCo;          // previous change
        double kP = -0.001;         // proportional control constant: the negative of nominal dCO / dT
        double longEnough = 5;      // consecutive minutes with negligible error
        int minSecondsAtCo = 20;    // minimum hold time at CO
        double lagMinutes = 2;      // estimated seconds for small CO change to take full effect
        var tooHigh = 65;           // degrees above target
        var tooLow = -20;           // 

        // Variables
        double pv = tc.Temperature; // process variable
        var sw = new Stopwatch();   // time since last tc.Temperature check
        double dt = 0;              // .Elapsed.TotalMinutes for dPv/min and ddPv/min/min
        double pv0 = 0;             // previous pv
        double dPv = 0;             // change in pv
        double dPv_dt = 0;          // change in pv per minute
        double dPv0 = 0;            // previous change in pv
        double ddPv = 0;            // change in dPv
        double ddPv_dt = 0;         // change in dPv per minute
        double error = 0;           // positive error is overtemp
        double error0 = 0;          // previous error
        double expectedError = 0;   // expected long-term error
        bool timedOut = false;      // loop timed out with wait condition not met

        var state = "";
        var step = "";

        string getState()
        {
            pv = tc.Temperature;
            dt = sw.Elapsed.TotalMinutes;
            sw.Restart();
            dPv = pv - pv0;
            pv0 = pv;
            ddPv = dPv - dPv0;
            dPv0 = dPv;
            error0 = error;
            error = pv - pvTarget;

            if (dt > 1.5/60)    // need enough time to have received a new heater report
            {
                dPv_dt = dPv / dt;
                ddPv_dt = ddPv / dt;
                expectedError = error + lagMinutes * dPv_dt;
            }
            state = $"{step}CO={h.Config.PowerLevel:0.00}: {pv:0.0} °C (d={dPv_dt:0.0} dd={ddPv_dt:0.0}) e={expectedError:0.0}";
            return state;
        }

        if (!h.Config.ManualMode)
        {
            Tell($"{h.Name} is not in Manual mode.", 
                "Cannot calibrate it in this state.", NoticeType.Error);
            return;
        }

        if (tc == null)
        {
            Tell("Calibration thermocouple missing.", 
                "Cannot calibrate heater without one.", NoticeType.Error);
            return;
        }

        if (!OkCancel("Operator Needed",
            $"Move the calibration thermocouple to {h.Name}.\r\n" +
            $"Ok to continue or Cancel to skip {h.Name}").Ok())
        {
            return;
        }

        ProcessStep.Start($"Calibrating {h.Name} power level");
        TestLog.Record($"Calibrating {h.Name}'s PowerLevel");

        getState();
        WaitSeconds(5, "Initialize state");
        if (tc.Temperature > pvTarget - 50)
        {
            h.TurnOff();
            step = "Cooling: ";
            TestLog.Record($"{h.Name} calibration: {getState()}.");
            ProcessSubStep.Start(step);
            bool cooled()
            {   
                ProcessSubStep.CurrentStep.Description = getState();
                return pv < pvTarget - 50;
            }
            if (!WaitFor(cooled, oneMinute, oneSecond))
            {
                Announce("Calibration thermocouple is too hot.", 
                    "And it hasn't cooled sufficiently after over a minute.\r\n" +
                    $"Is it in {h.Name}?", NoticeType.Error);
                ProcessStep.End();
                return;
            }
            ProcessSubStep.End();
        }

        step = "Detect heating: ";
        ProcessSubStep.Start(step);
        var warmingTimeoutSeconds = 60;
        h.MaximumPowerLevel = 15;
        h.PowerLevel = 15;
        h.TurnOn();
        TestLog.Record($"{h.Name} calibration: {getState()}.");
        bool furnaceHeating()
        {
            ProcessSubStep.CurrentStep.Description = getState();
            return dPv_dt > 5 && ddPv_dt > 5;
        }
        while (!WaitFor(furnaceHeating, warmingTimeoutSeconds * oneSecond, 4 * oneSecond))
        {
            h.TurnOff();

            // Don't end the ProcessSubStep yet; retain the status in the UI.
            if (OkCancel($"{h.Name} doesn't seem to be heating.",
                $"Calibration Stopped\r\n" +
                $"Is the calibration thermocouple in {h.Name}?\r\n" +
                $"Ok to try again or Cancel calibrating {h.Name}.", NoticeType.Warning).Ok())
            {
                ProcessSubStep.End();       // to restart the timer
                ProcessSubStep.Start(step);
                h.TurnOn();
                TestLog.Record($"{h.Name} calibration: {getState()}");
            }
            else
            {
                ProcessSubStep.End();
                return;
            }
        }
        step = "Heating detected: ";
        TestLog.Record($"{h.Name} calibration: {getState()}");
        ProcessSubStep.End();

        step = $"Pre-heat: ";
        ProcessSubStep.Start(step);
        bool preheated()
        {            
            ProcessSubStep.CurrentStep.Description = getState();
            return pv > pvTarget - 10;
        }
        WaitFor(preheated, 5 * oneMinute, 4 * oneSecond);
        step = "Pre-Heat ended: ";
        TestLog.Record($"{h.Name} calibration: {getState()}");
        ProcessSubStep.End();

        step = $"Stabilize: ";
        ProcessSubStep.Start(step);
        double startingCO = 11.0;           // rough estimate
        h.PowerLevel = startingCO;
        TestLog.Record($"{h.Name} calibration: {getState()}");
        bool stabilize()
        {           
            ProcessSubStep.CurrentStep.Description = getState();
            return ProcessSubStep.Elapsed.TotalSeconds >= 60;
        }
        WaitFor(stabilize, -1, 4 * oneSecond);
        ProcessSubStep.End();
        step = "";

        while (!Stopping & !timedOut)
        {
            dCo0 = dCo;
            var adjust = expectedError < tooLow || expectedError > tooHigh;
            dCo = adjust ? kP * expectedError : 0;
            if (dCo < 0)
                dCo = Math.Clamp(dCo, -dCoMax, -dCoMin);
            else
                dCo = Math.Clamp(dCo, dCoMin, dCoMax);

            var powerLevel = Math.Round(Math.Min(h.MaximumPowerLevel, h.Config.PowerLevel + dCo), 2);
            h.PowerLevel = powerLevel;            

            bool configured() => h.Config.PowerLevel == powerLevel;
            WaitFor(configured, -1, 1);
            TestLog.Record($"{h.Name} calibration: {getState()}.");

            step = "";
            ProcessSubStep.Start(step);
            bool changeNeeded()
            {
                if (tc.Temperature > pvLimit) return true;                
                ProcessSubStep.CurrentStep.Description = getState();
                return ProcessSubStep.Elapsed.TotalSeconds >= minSecondsAtCo &&
                    (expectedError < tooLow || expectedError > tooHigh);
            }
            timedOut = !WaitFor(changeNeeded, (int)(longEnough * oneMinute), 5 * oneSecond);
            ProcessSubStep.End();

            if (tc.Temperature > pvLimit)
            {
                RecordAlert($"{h.Name} calibration stopped", $"Temperature exceeded 1000 °C.");
                break;
            }
        }
        TestLog.Record($"{h.Name} calibration: {getState()}");
        h.TurnOff();

        if (timedOut)
        {
            h.MaximumPowerLevel = h.Config.PowerLevel;
            RecordAlert($"{h.Name} calibration complete", $"{h.Name}'s PowerLevel is {h.Config.PowerLevel:0.00}.");
        }
        else
        {
            Announce($"{h.Name} Calibration Unsuccessful",
                $"Check {TestLog.Name} for details.", NoticeType.Error);
        }
        ProcessStep.End();

        void RecordAlert(string message, string details)
        {
            Alert(message, details);
            TestLog.Record(message + ". " + details);
        }
    }

    /// <summary>
    /// Calibrate all manual Inlet Port Quartz furnaces.
    /// </summary>
    protected void CalibrateIPQuartzHeaters()
    {
        var tc = Find<IThermocouple>("tCal");
        foreach (var inletPort in FindAll<IInletPort>())
        {
            if (inletPort.QuartzFurnace is Heater h && h.ManualMode)
                CalibrateManualHeater(h, tc);
        }
    }

    protected void PidStepTest(params IHeater[] heaters)
    {
        double initialCO = 1.0;
        double stepCO = 4.0;
        int warmup = 60; // seconds
        Array.ForEach(heaters, h => { h.Manual(stepCO); h.TurnOn(); });

        WaitSeconds(warmup); // Preheat furnace.

        Array.ForEach(heaters, h => h.PowerLevel = initialCO);

        WaitMinutes(15); // Wait for temperature to stabalize.
        var names = new List<string>();
        Array.ForEach(heaters, h => names.Add(h.Name));
        var log = new DataLog($"PID Step test{(heaters.Length > 1 ? "s" : "")} {string.Join(',', names)}.txt")
        {
            //Header = string.Join('\t', heaters as IEnumerable),
            ChangeTimeoutMilliseconds = 3 * 1000,
            OnlyLogWhenChanged = false,
            InsertLatestSkippedEntry = false
        };
        var columns = new ObservableList<DataLog.Column>();
        Array.ForEach(heaters, h =>
        {
            columns.Add(new DataLog.Column()
            {
                Name = h.Name,
                Resolution = -1.0,
                Format = "0.00"
            });
        });
        log.Columns = columns;
        HacsLog.List.Add(log);

        Array.ForEach(heaters, h => h.PowerLevel = stepCO);

        WaitMinutes(3);

        log.ChangeTimeoutMilliseconds = 30 * 1000;

        WaitMinutes(17);

        HacsLog.List.Remove(log);
        log.Close();
        log = null;

        Array.ForEach(heaters, h =>
        {
            h.TurnOff();
            h.Auto();
        });
    }

    protected void VttWarmStepTest()
    {
        if (Find<VTColdfinger>("VTC") is not VTColdfinger vtc)
            return;
        if (vtc.Heater is not IHeater h)
            return;

        // Replace VTC heater with dummy so we can manually control the real heater.
        vtc.Heater = new HC6Heater();

        double initialCO = 0.1;
        double stepCO = 0.3;

        h.Setpoint = 50;
        h.Manual(initialCO);
        h.TurnOn();

        WaitMinutes(60); // Wait for temperature to stabalize.

        var log = new DataLog($"VttWarm Step Test.txt")
        {
            Columns = new()
                {
                    new DataLog.Column()
                    {
                        Name = "tVTC",
                        Resolution = -1.0,
                        Format = "0.00"
                    },
                    new DataLog.Column()
                    {
                        Name = vtc.TopThermometer.Name,
                        Resolution = -1.0,
                        Format = "0.00"
                    },
                    new DataLog.Column()
                    {
                        Name = vtc.WireThermometer.Name,
                        Resolution = -1.0,
                        Format = "0.00"
                    }
                },
            ChangeTimeoutMilliseconds = 3 * 1000,
            OnlyLogWhenChanged = false,
            InsertLatestSkippedEntry = false
        };

        HacsLog.List.Add(log);

        h.PowerLevel = stepCO;

        WaitMinutes(40);

        HacsLog.List.Remove(log);
        log.Close();
        log = null;

        h.TurnOff();
        h.Auto();
        vtc.Heater = h;
    }

    /// <summary>
    /// Transfer CO2 from MC to IP,
    /// optionally add some carrier gas,
    /// then Collect(), Extract() and Measure()
    /// </summary>
    protected virtual void CO2LoopMC_IP_MC()
    {
        TransferCO2FromMCToIP();

        if (admitCarrier != null)
        {
            PrepareIPsForCollection([InletPort]);

            // if a transfer vessel is used... 
            WaitForOperator($"Put LN on the {InletPort.Name} coldfinger.\r\n" +
                $"Wait for it to freeze, then raise it a bit.");

            admitCarrier.Invoke();
        }

        WaitForOperator($"Remove LN from {InletPort.Name} and thaw the coldfinger.");

        Collect();
        Extract();
        Measure();
    }


    /// <summary>
    /// Transfers CO2 from MC to VTT, then performs extract() and measure()
    /// </summary>
    protected virtual void CleanupCO2InMC()
    {
        TransferCO2FromMCToVTT();
        VTT.VacuumSystem.Evacuate(CleanPressure);
        VTT.Isolate();
        Extract();
        Measure();
    }

    /// <summary>
    /// Admit maximum carrier gas to the IM+VM and join to the IP.
    /// </summary>
    protected virtual void AdmitIPCarrier()
    {
        IpIm(out ISection im);
        im.Evacuate(OkPressure);
        im.ClosePorts();

        var gs = GasSupply("O2", im) ?? InertGasSupply(im);
        var vm = im.VacuumSystem.VacuumManifold;

        gs.Admit();       // dunno, 1000-1500 Torr?
        WaitSeconds(1);
        gs.ShutOff();
        im.Isolate();
        vm.Evacuate();

        vm.Isolate();      // one cycle might keep ~10% in the IM
        im.JoinToVacuum();
        WaitSeconds(3);
        im.Isolate();
        vm.Evacuate();

        InletPort.Open();
        InletPort.Close();
        im.Evacuate();
    }

    /// <summary>
    /// Admit some carrier gas into the IM and join to the IP.
    /// </summary>
    protected virtual void AdmitIPPuffs()
    {
        IpIm(out ISection im);
        im.Evacuate(OkPressure);
        im.ClosePorts();

        var gs = Find<GasSupply>("O2." + im.Name);
        if (gs == null) gs = InertGasSupply(im);

        for (int i = 0; i < amountOfCarrier; ++i)
        {
            gs.Admit();       // dunno, 1000-1500 Torr?
            WaitSeconds(1);
            gs.ShutOff();
            im.VacuumSystem.Isolate();
            im.JoinToVacuum();      // one cycle might keep ~10% in the IM
            WaitSeconds(2);
            im.Isolate();
        }

        InletPort.Open();
    }

    protected virtual void MeasureExtractEfficiency()
    {
        TestLog.WriteLine("\r\n");
        TestLog.Record("Measure VTT extract efficiency");
        MeasureProcessEfficiency(CleanupCO2InMC);
    }


    protected Action admitCarrier;
    protected int amountOfCarrier;
    protected virtual void MeasureIpCollectionEfficiency()
    {
        TestLog.WriteLine("\r\n");
        TestLog.Record("IP collection efficiency test");
        if (IM_FirstTrap.FlowManager == null)
            admitCarrier = null;
        else
        {
            admitCarrier = AdmitIPCarrier;
            //amountOfCarrier = 3;    // puffs
        }
        MeasureProcessEfficiency(CO2LoopMC_IP_MC);
    }



    /// <summary>
    /// Simulates an organic extraction
    /// </summary>
    protected virtual void MeasureOrganicExtractEfficiency()
    {
        TestLog.WriteLine("\r\n");
        TestLog.Record("Organic bleed yield test");
        TestLog.Record($"Bleed target: {FirstTrapBleedPressure} Torr");
        admitCarrier = AdmitIPO2;
        MeasureProcessEfficiency(CO2LoopMC_IP_MC);
    }

    /// <summary>
    /// Set the Sample LabId to the desired number of loops
    /// Set the Sample mass to the desired starting quantity
    /// If there is at least 80% of the desired starting quantity
    /// already in the measurement chamber, that will be used
    /// instead of admitting fresh gas.
    /// </summary>
    /// <param name="transferLoop">method to move sample from MC to somewhere else and back</param>
    protected virtual void MeasureProcessEfficiency(Action transferLoop)
    {
        if (Sample == null)
        {
            Tell("A sample must be defined in order to test process efficiency.");
            return;
        }

        ProcessStep.Start("Measure transfer efficiency");
        if (ugCinMC < Sample.Micrograms * 0.8)
        {
            MC.VacuumSystem.OpenLine();
            MC.VacuumSystem.WaitForPressure(CleanPressure);
            AdmitDeadCO2();
        }

        int n; try { n = int.Parse(Sample.LabId); } catch { n = 1; }
        for (int repeats = 0; repeats < n; repeats++)
        {
            Sample.Micrograms = Sample.SelectedMicrogramsCarbon;
            transferLoop?.Invoke();
        }
        ProcessStep.End();
    }


    // Discards the MC contents soon after they reach the
    // temperature at which they were extracted.
    protected virtual void DiscardExtractedGases()
    {
        ProcessStep.Start("Discard extracted gases");
        var mcCF = MC.Coldfinger;
        mcCF.Thaw();
        ProcessSubStep.Start("Wait for MC coldfinger to thaw enough.");
        WaitFor(() => mcCF.Temperature > (VTT.Coldfinger as VTColdfinger).Setpoint + 10);
        ProcessSubStep.End();
        mcCF.Standby();    // stop thawing to save time

        // record pressure
        SampleLog.Record($"\tPressure of pre-CO2 discarded gases:\t{MC.Pressure:0.000}\tTorr");

        VTT_MC.OpenAndEvacuate(OkPressure);
        VTT_MC.IsolateFromVacuum();
        ProcessStep.End();
    }

    protected virtual void StepExtract()
    {
        // The equilibrium temperature of HCl at pressures from ~(1e-5..1e1)
        // is about 14 degC or more colder than CO2 at the same pressure.
        ExtractAt(-160);        // targets HCl
        DiscardExtractedGases();
        ExtractAt(-140);        // targets CO2
    }

    protected virtual void StepExtractionYieldTest()
    {
        Sample.LabId = "Step Extraction Yield Test";
        //admitDeadCO2(1000);
        Measure();

        TransferCO2FromMCToVTT();
        Extract();
        Measure();

        //transfer_CO2_MC_VTT();
        //step_extract();
        //VTT.Stop();
        //measure();
    }

    protected void ValvePositionDriftTest()
    {
        var v = FirstOrDefault<RS232Valve>();
        var pos = v.ClosedValue / 2;
        var op = new ActuatorOperation()
        {
            Name = "test",
            Value = pos,
            Incremental = false
        };
        v.ActuatorOperations.Add(op);

        v.DoWait(op);

        //op.Incremental = true;
        var rand = new Random();
        for (int i = 0; i < 100; i++)
        {
            op.Value = pos + rand.Next(-15, 16);
            v.DoWait(op);
        }
        op.Value = pos;
        op.Incremental = false;
        v.DoWait(op);

        v.ActuatorOperations.Remove(op);
    }

    protected void TestPort(IPort p)
    {
        for (int i = 0; i < 5; ++i)
        {
            p.Open();
            p.Close();
        }
        p.Open();
        WaitMinutes(5);
        p.Close();
    }

    // two minutes of moving the valve at a moderate pace
    protected void TestValve(IValve v)
    {
        TestLog.Record($"Operating {v.Name} for 2 minutes");
        for (int i = 0; i < 24; ++i)
        {
            v.CloseWait();
            WaitSeconds(2);
            v.OpenWait();
            WaitSeconds(2);
        }
    }

     protected void TestUpstream(IValve v)
    {
        TestLog.Record($"Checking {v.Name}'s 10-minute bump");
        v.OpenWait();
        WaitMinutes(5);     // empty the upstream side (assumes the downstream side is under vacuum)
        v.CloseWait();
        WaitMinutes(10);    // let the upstream pressure rise for 10 minutes
        v.OpenWait();       // how big is the pressure bump?
    }


    protected virtual void ExercisePorts(ISection s)
    {
        s.Isolate();
        s.Open();
        s.OpenPorts();
        WaitSeconds(5);
        s.ClosePorts();
        s.Evacuate(OkPressure);
    }

    protected virtual void TestAdmit(string gasSupply, double pressure)
    {
        var gs = Find<GasSupply>(gasSupply);
        gs?.Destination?.OpenAndEvacuate();
        gs?.Destination?.ClosePorts();
        gs?.Admit(pressure);
        WaitSeconds(10);

        TestLog.Record($"Admit test: {gasSupply}, target: {pressure:0.###}, stabilized: {gs.Meter.Value:0.###} in {ProcessStep.Elapsed:m':'ss}");
        gs?.Destination?.OpenAndEvacuate();
    }

    protected virtual void TestPressurize(string gasSupply, double pressure)
    {
        var gs = Find<GasSupply>(gasSupply);
        gs?.Destination?.OpenAndEvacuate(OkPressure);
        gs?.Destination?.ClosePorts();
        gs?.Pressurize(pressure);
        WaitSeconds(15);

        TestLog.Record($"Pressurize test: {gasSupply}, target: {pressure:0.###}, stabilized: {gs.Meter.Value:0.###} in {ProcessStep.Elapsed:m':'ss}");
        gs?.Destination?.OpenAndEvacuate();
    }



    protected virtual void Test() { }

    #endregion Test functions
}
