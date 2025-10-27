using Newtonsoft.Json;
using System.Linq;
using System.Threading;
using static AeonHacs.Notify;
using static AeonHacs.Utilities.Utility;

namespace AeonHacs.Components;

/// <summary>
/// In situ extraction line: A CO2-liberator for quartz.
/// </summary>
public class ExtractionLine : ProcessManager, IExtractionLine
{
    #region HacsComponent

    [HacsConnect]
    protected virtual void Connect()
    {
        CEGS = Find<Cegs>(cegsName);
        TubeFurnace = Find<ITubeFurnace>(tubeFurnaceName);
        TFSection = Find<ISection>(tfSectionName);
        TFPort = Find<IInletPort>(tfPortName);
        O2GasSupply = Find<GasSupply>(o2GasSupplyName);
        HeGasSupply = Find<GasSupply>(heGasSupplyName);
        TFPressureManager = Find<FlowManager>(tfPressureManagerName);
        TFRateManager = Find<FlowManager>(tfRateManagerName);
    }

    [HacsPostConnect]
    protected void PostConnect()
    {
        VacuumSystem.ProcessStep = ProcessStep;
        O2GasSupply.ProcessStep = ProcessStep;
        HeGasSupply.ProcessStep = ProcessStep;

        PurgeFlowManager = new gasFlowManager()
        {
            GasSupply = HeGasSupply,
            Pressure = TFSection.Manometer,
            Reference = CEGS.Ambient.Manometer
        };
    }

    #endregion HacsComponent

    [JsonProperty("CEGS")]
    string CEGSName { get => CEGS?.Name; set => cegsName = value; }
    string cegsName;
    /// <summary>
    /// The CEGS that the ISEL connects to.
    /// </summary>
    public Cegs CEGS
    {
        get => cegs;
        set => Ensure(ref cegs, value);
    }
    Cegs cegs;


    [JsonProperty("TubeFurnace")]
    string TubeFurnaceName { get => TubeFurnace?.Name; set => tubeFurnaceName = value; }
    string tubeFurnaceName;
    /// <summary>
    /// The tube furnace.
    /// </summary>
    public ITubeFurnace TubeFurnace
    {
        get => tubeFurnace;
        set => Ensure(ref tubeFurnace, value);
    }
    ITubeFurnace tubeFurnace;

    [JsonProperty("O2GasSupply")]
    string O2GasSupplyName { get => O2GasSupply?.Name; set => o2GasSupplyName = value; }
    string o2GasSupplyName;
    /// <summary>
    /// The O2 gas supply for the tube furnace chamber.
    /// </summary>
    public GasSupply O2GasSupply
    {
        get => o2GasSupply;
        set => Ensure(ref o2GasSupply, value);
    }
    GasSupply o2GasSupply;


    [JsonProperty("HeGasSupply")]
    string HeGasSupplyName { get => HeGasSupply?.Name; set => heGasSupplyName = value; }
    string heGasSupplyName;
    /// <summary>
    /// The He gas supply for the tube furnace chamber.
    /// </summary>
    public GasSupply HeGasSupply
    {
        get => heGasSupply;
        set => Ensure(ref heGasSupply, value);
    }
    GasSupply heGasSupply;


    [JsonProperty("TFSection")]
    string TFSectionName { get => TFSection?.Name; set => tfSectionName = value; }
    string tfSectionName;
    /// <summary>
    /// The tube furnace Section.
    /// </summary>
    public ISection TFSection
    {
        get => tfSection;
        set => Ensure(ref tfSection, value);
    }
    ISection tfSection;


    [JsonProperty("TFPort")]
    string TFPortName { get => TFPort?.Name; set => tfPortName = value; }
    string tfPortName;
    /// <summary>
    /// The CEGS inlet port to which this ISEL is connected.
    /// </summary>
    public ILinePort TFPort
    {
        get => tfPort;
        set => Ensure(ref tfPort, value);
    }
    ILinePort tfPort;

    [JsonProperty("TFRateManager")]
    string TFRateManagerName { get => TFRateManager?.Name; set => tfRateManagerName = value; }
    string tfRateManagerName;
    /// <summary>
    /// The CEGS inlet port to which this ISEL is connected.
    /// </summary>
    public FlowManager TFRateManager
    {
        get => tfRateManager;
        set => Ensure(ref tfRateManager, value);
    }
    FlowManager tfRateManager;


    [JsonProperty("TFPressureManager")]
    string TFPressureManagerName { get => TFPressureManager?.Name; set => tfPressureManagerName = value; }
    string tfPressureManagerName;
    /// <summary>
    /// The CEGS inlet port to which this ISEL is connected.
    /// </summary>
    public FlowManager TFPressureManager
    {
        get => tfPressureManager;
        set => Ensure(ref tfPressureManager, value);
    }
    FlowManager tfPressureManager;




    gasFlowManager PurgeFlowManager = new gasFlowManager();
    public HacsLog SampleLog => CEGS.SampleLog;
    public double pAmbient => CEGS.Ambient.Pressure;
    public double OkPressure => CEGS.OkPressure;

    public VacuumSystem VacuumSystem => TFSection.VacuumSystem as VacuumSystem;
    public IManometer TFManometer => TFSection.Manometer;
    //public IValve vTFFlow => TFPressureManager.FlowValve;



    #region process manager

    #region ProcessDictionary
    protected override void BuildProcessDictionary()
    {
        ProcessDictionary["Open and evacuate line"] = () => VacuumSystem?.OpenLine();
        ProcessDictionary["Isolate tube furnace"] = () => TFSection.Isolate();
        ProcessDictionary["Pressurize tube furnace to 50 torr O2"] = () => PressurizeO2(50);
        ProcessDictionary["Evacuate tube furnace"] = EvacuateTF;
        ProcessDictionary["Evacuate tube furnace over 10 minutes"] = PacedEvacuate;
        ProcessDictionary["TurnOff tube furnace"] = () => TubeFurnace.TurnOff();
        ProcessDictionary["Prepare tube furnace for opening"] = PrepareForOpening;
        ProcessDictionary["Bake out tube furnace"] = Bakeout;
        ProcessDictionary["Degas LiBO2"] = Degas;
        ProcessDictionary["Begin extract"] = () => BeginExtract(50, 600, 10, 1100, 10);
        ProcessDictionary["Finish extract"] = FinishExtract;
        ProcessDictionary["Bleed"] = Bleed;
        ProcessDictionary["Remaining P in TF"] = Remaining_P;
        ProcessDictionary["Suspend IG"] = DisableVSManometer;
        ProcessDictionary["Restore IG"] = RestoreVSManometer;
    }
    #endregion ProcessDictionary


    #region TubeFurnace processes

    void IsolateTF()
    {
        TFSection.Isolate();
        O2GasSupply.ShutOff();
        HeGasSupply.ShutOff();
    }

    void PacedEvacuate()
    {
        var vTFFlow = TFRateManager.FlowValve;
        var vTFFlowShutoff = TFSection.PathToVacuum.LastOrDefault();

        IsolateTF();
        vTFFlow.CloseWait();
        vTFFlowShutoff.OpenWait();
        VacuumSystem.Evacuate();

        // evacuate TF over ~10 minutes
        //TFFlowManager.Start(-p_TF / (10 * 60));     // negative to target a falling pressure
        TFRateManager.Start(-1.5);     // 1.5 Torr / sec
        WaitForPressureBelow(50);
        TFRateManager.Stop();

        vTFFlow.OpenWait();

        WaitForPressureBelow(20);
        EvacuateTF();
        vTFFlow.CloseWait();
    }

    void TurnOffTF() => TubeFurnace.TurnOff();
    void EvacuateTF() => TFSection.OpenAndEvacuate();
    void EvacuateTF(double pressure)
    {
        ProcessStep.Start($"Evacuate to {pressure:0.0e0} Torr");
        TFSection.OpenAndEvacuate(pressure);
        ProcessStep.End();
    }

    void WaitForPressureBelow(double pressure)
    {
        ProcessStep.Start($"Wait for tube pressure < {pressure} Torr");
        WaitFor(() => TFManometer.Pressure < pressure);
        ProcessStep.End();
    }

    void WaitForPressureAbove(double pressure)
    {
        ProcessStep.Start($"Wait for tube pressure > {pressure} Torr");
        WaitFor(() => TFManometer.Pressure > pressure);
        ProcessStep.End();
    }

    void WaitForTemperatureAbove(int temperature)
    {
        ProcessStep.Start($"Wait for tube temperature > {temperature} °C");
        WaitFor(() => TubeFurnace.Temperature > temperature);
        ProcessStep.End();
    }

    void WaitForTemperatureBelow(int temperature)
    {
        ProcessStep.Start($"Wait for tube temperature < {temperature} °C");
        WaitFor(() => TubeFurnace.Temperature < temperature);
        ProcessStep.End();
    }

    void PressurizeO2(double pressure)
    {
        ProcessStep.Start($"Pressurize tube to {pressure:0} Torr with {O2GasSupply.GasName}");
        O2GasSupply.Pressurize(pressure);
        //MFC?.TurnOn(MFC.MaximumSetpoint);
        //O2GasSupply.Admit(pressure);
        //MFC.TurnOff();
        ProcessStep.End();
    }

    void PressurizeHe(double pressure)
    {
        ProcessStep.Start($"Pressurize tube to {pressure:0} Torr with {HeGasSupply.GasName}");
        HeGasSupply.Pressurize(pressure);
        ProcessStep.End();
    }


    void PrepareForOpening()
    {
        TFPort.State = LinePort.States.InProcess;
        ProcessStep.Start("Prepare tube furnace for opening");

        PressurizeHe(pAmbient + 20);
        PurgeFlowManager.Start();

        ProcessStep.CurrentStep.Description = "Tube furnace ready to be opened";

        WaitForOperator(
            "Tube furnace ready to be opened." +
            "Purge flow is active.\r\n" +
            "Dismiss this window when furnace is closed again.");
        PurgeFlowManager.Stop();

        ProcessStep.End();
        TFPort.State = LinePort.States.Complete;
    }

    // Bake furnace tube
    void Bakeout()
    {
        ProcessStep.Start($"{Name}: Bakeout tube furnace");
        IsolateTF();

        if (!OkCancel("Confirm prior sample is removed",
            "Has the prior sample been removed?!\r\n" +
            "Ok to continue, or" +
            "Cancel to abort the process.").Ok())
        {
            return;
        }

        TFPort.State = LinePort.States.InProcess;

        SampleLog.WriteLine();
        SampleLog.Record($"{Name}: Start Process: Bakeout tube furnace");

        double O2Pressure = 50;
        int bakeTemperature = 1200;
        int bakeMinutes = 60;
        int bakeCycles = 4;

        EvacuateTF(0.01);
        PressurizeO2(O2Pressure);

        TubeFurnace.TurnOn(bakeTemperature);
        WaitForTemperatureAbove(bakeTemperature - 10);

        for (int i = 0; i < bakeCycles; ++i)
        {
            PressurizeO2(O2Pressure);

            ProcessStep.Start($"Bake at {bakeTemperature} °C for {MinutesString(bakeMinutes)} min, cycle {i + 1} of {bakeCycles}");
            WaitMinutes(bakeMinutes);
            ProcessStep.End();
            EvacuateTF(0.1);
        }

        TubeFurnace.TurnOff();
        EvacuateTF(OkPressure);
        var msg = $"{Name}: Tube bakeout process complete";
        SampleLog.Record(msg);
        Alert(msg);
        ProcessStep.End();
        TFPort.State = LinePort.States.Complete;
    }


    // Degas LiBO2, boat, and quartz sleeve on Day 1 Process
    void Degas()
    {
        var vTFFlow = TFPressureManager.FlowValve;
        var vTFFlowShutoff = TFSection.PathToVacuum.LastOrDefault();

        TFPort.State = LinePort.States.InProcess;

        SampleLog.WriteLine();
        SampleLog.Record($"{Name}: Start Process: Degas LiBO2, boat, and sleeve");

        double ptarget = 50;
        int bleedTemperature = 1200;
        int bleedMinutes = 60;
        int t_LiBO2_frozen = 800;

        PacedEvacuate();
        EvacuateTF(0.01);
        IsolateTF();
        //MFC.ResetTrackedFlow();
        PressurizeO2(ptarget);

        TubeFurnace.TurnOn(bleedTemperature);
        WaitForTemperatureAbove(bleedTemperature - 10);

        vTFFlow.CloseWait();

        ProcessStep.Start($"Bleed O2 over sample for {MinutesString(bleedMinutes)}");
        //MFC.TurnOn(5);
        // Set FTG flow valve here and open the O2 gas supply valve
        O2GasSupply.Admit();

        VacuumSystem.Isolate();
        vTFFlowShutoff.OpenWait();
        VacuumSystem.Evacuate();

        TFPressureManager.Start(ptarget);

        WaitRemaining(bleedMinutes);
        ProcessStep.End();

        ProcessStep.Start($"Cool to below {t_LiBO2_frozen} °C");

        TubeFurnace.Setpoint = 100;
        WaitForTemperatureBelow(t_LiBO2_frozen);

        TFPressureManager.Stop();

        ProcessStep.End();

        TubeFurnace.TurnOff();
        //MFC.TurnOff();
        O2GasSupply.ShutOff();

        EvacuateTF();

        var msg = $"{Name}: Degas LiBO2, boat, and sleeve process complete";
        Alert(msg);
        SampleLog.Record(msg);
        //SampleLog.Record($"Degas O2 bleed volume\t{MFC.TrackedFlow}\tcc");

        TFPort.State = LinePort.States.Prepared;
    }

    /// <summary>
    /// </summary>
    /// <param name="targetPressure"></param>
    /// <param name="bleedTemperature"></param>
    /// <param name="bleedMinutes"></param>
    /// <param name="extractTemperature"></param>
    /// <param name="extractMinutes"></param>
    void BeginExtract(double targetPressure, int bleedTemperature, int bleedMinutes, int extractTemperature, int extractMinutes)
    {
        TFPort.State = LinePort.States.InProcess;

        SampleLog.WriteLine();
        SampleLog.Record($"{Name}: Start Process: Sample extraction");

        PacedEvacuate();
        EvacuateTF(OkPressure);
        IsolateTF();
        //MFC.ResetTrackedFlow();
        PressurizeO2(targetPressure);

        ProcessStep.Start("Low-T sample combustion");
        {
            var vTFFlow = TFPressureManager.FlowValve;
            var vTFFlowShutoff = TFSection.PathToVacuum.LastOrDefault();

            TubeFurnace.TurnOn(bleedTemperature);
            WaitForTemperatureAbove(bleedTemperature - 10);

            TFPressureManager.FlowValve.CloseWait();

            ProcessStep.Start($"Bleed O2 over sample for {MinutesString(bleedMinutes)}");
            {
                //MFC.TurnOn(5);
                O2GasSupply.Admit();

                VacuumSystem.Isolate();
                vTFFlowShutoff.OpenWait();
                VacuumSystem.Evacuate();

                TFPressureManager.Start(targetPressure);

                WaitRemaining(bleedMinutes);
                TFPressureManager.Stop();

                //MFC.TurnOff();
                O2GasSupply.ShutOff();

                EvacuateTF(OkPressure);
            }
            ProcessStep.End();

            ProcessStep.Start("Flush & evacuate TF");
            {
                PressurizeO2(targetPressure);
                EvacuateTF(1e-3);
            }
            ProcessStep.End();
        }
        ProcessStep.End();

        SampleLog.Record($"{Name}: Finish low-T sample combustion");
        // SampleLog.Record($"{Name}: Low-T O2 bleed volume\t{MFC.TrackedFlow}\tcc");

        IsolateTF();
        //MFC.ResetTrackedFlow();
        PressurizeO2(targetPressure);

        TubeFurnace.Setpoint = extractTemperature;

        ProcessStep.Start($"Combust sample at {extractTemperature} °C for {MinutesString(extractMinutes)}");
        {
            WaitForTemperatureAbove(extractTemperature - 10);
            WaitRemaining(extractMinutes);
        }
        ProcessStep.End();
    }

    void FinishExtract()
    {
        var vTFFlow = TFPressureManager.FlowValve;
        var vTFFlowShutoff = TFSection.PathToVacuum.LastOrDefault();

        int bakeMinutes = 10;

        ProcessStep.Start($"Continue bake for {MinutesString(bakeMinutes)} more");
        WaitMinutes(bakeMinutes);
        ProcessStep.End();

        //MFC.TurnOn(5);
        O2GasSupply.Admit();

        VacuumSystem.Isolate();
        vTFFlow.CloseWait();
        vTFFlowShutoff.OpenWait(); //doesn't proceed until in state requested

    }

    bool vsManometerWasAuto;
    void DisableVSManometer()
    {
        vsManometerWasAuto = VacuumSystem.AutoManometer;
        VacuumSystem.AutoManometer = false;
        VacuumSystem.DisableManometer();
    }

    void RestoreVSManometer()
    {
        VacuumSystem.AutoManometer = vsManometerWasAuto;
    }

    // Bleed O2 through tube furnace to CEGS
    void Bleed()
    {
        // handled by caller?
        //var vTFFlow = TFPressureManager.FlowValve;
        //var vTFFlowShutoff = TFSection.PathToVacuum.LastOrDefault();

        int t_LiBO2_frozen = 800;
        int bleedMinutes = 10; //60;
        int targetPressure = 50;

        // disable ion gauge while low vacuum flow is expected
        DisableVSManometer();

        ProcessStep.Start($"Bleed O2 over sample for {MinutesString(bleedMinutes)} + cool down");
        {

            TFPressureManager.Start(targetPressure);

            WaitRemaining(bleedMinutes);

            TubeFurnace.Setpoint = 100;
            WaitForTemperatureBelow(t_LiBO2_frozen);

            TFPressureManager.Stop();

            TubeFurnace.TurnOff();
            //MFC?.TurnOff();
            O2GasSupply.ShutOff();
            // v_TF_VM.OpenWait();     // WHY???
        }
        ProcessStep.End();

        RestoreVSManometer();

        SampleLog.Record($"{Name}: Finish high-T sample combustion and bleed");
        //if (MFC != null)
        //    SampleLog.Record($"{Name}: High T O2 bleed volume\t{MFC.TrackedFlow}\tcc");

        TFPort.State = LinePort.States.Complete;

    }

    void Remaining_P()
    {
        SampleLog.Record($"{Name}: Pressure remaining after bleed\t{TFManometer.Value}\ttorr");
    }


    #endregion TubeFurnace processes

    #endregion process manager



    protected class gasFlowManager
    {
        public GasSupply GasSupply { get; set; }
        public IManometer Pressure { get; set; }
        public IManometer Reference { get; set; }
        public double Overpressure { get; set; } = 20;

        double pressure_min => Reference.Value + Overpressure / 2;
        double pressure_max => Reference.Value + Overpressure;

        Thread managerThread;
        AutoResetEvent stopSignal = new AutoResetEvent(false);

        public bool Busy => managerThread != null && managerThread.IsAlive;

        public void Start()
        {
            if (managerThread != null && managerThread.IsAlive)
                return;

            managerThread = new Thread(manageFlow)
            {
                Name = $"{Pressure.Name} purgeFlowManager",
                IsBackground = true
            };
            managerThread.Start();
        }

        public void Stop() { stopSignal.Set(); }

        void manageFlow()
        {
            int timeout = 100;
            bool stopRequested = false;
            while (!stopRequested)
            {
                if (Pressure.Value < pressure_min && !GasSupply.SourceValve.IsOpened)
                    GasSupply.SourceValve.OpenWait();
                else if (Pressure.Value > pressure_max && GasSupply.SourceValve.IsOpened)
                    GasSupply.SourceValve.CloseWait();
                stopRequested = stopSignal.WaitOne(timeout);
            }
            GasSupply.SourceValve.CloseWait();
        }
    }
}