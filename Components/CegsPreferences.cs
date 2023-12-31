﻿using Newtonsoft.Json;
using System.ComponentModel;
using AeonHacs.Utilities;

namespace AeonHacs.Components
{
    public partial class CegsPreferences : HacsComponent, ICegsPreferences
    {
        public static CegsPreferences Default = new CegsPreferences();

        #region System state & operations

        [JsonProperty, DefaultValue(true)] public static bool EnableWatchdogs { get => enableWatchdogs; set => Default.Ensure(ref enableWatchdogs, value); }
        static bool enableWatchdogs = true;
        [JsonProperty, DefaultValue(true)] public static bool EnableAutozero { get => enableAutozero; set => Default.Ensure(ref enableAutozero, value); }
        static bool enableAutozero = true;
        [JsonProperty, DefaultValue("6")] public static string PriorGR { get => lastGR; set => Default.Ensure(ref lastGR, value); }
        static string lastGR = "6";
        [JsonProperty, DefaultValue(1)] public static int NextGraphiteNumber { get => nextGraphiteNumber; set => Default.Ensure(ref nextGraphiteNumber, value); }
        static int nextGraphiteNumber = 1;
        [JsonProperty, DefaultValue(MassUnits.g)] public static MassUnits DefaultMassUnits { get => defaultMassUnits; set => Default.Ensure(ref defaultMassUnits, value); }
        static MassUnits defaultMassUnits = MassUnits.g;
        [JsonProperty, DefaultValue(false)] public static bool Take13CDefault { get => take13CDefault; set => Default.Ensure(ref take13CDefault, value); }
        static bool take13CDefault = false;

        #endregion System state & operations

        #region Pressure Constants

        /// <summary>
        /// Usually about 10 Torr over standard; should be slightly greater 
        /// than the atmospheric pressure at any lab that will handle 
        /// gaseous samples in septum-sealed vials (including external labs).
        /// </summary>
        [JsonProperty, DefaultValue(770.0)] public static double PressureOverAtm { get => pressureOverAtm; set => Default.Ensure(ref pressureOverAtm, value); }
        static double pressureOverAtm = 770.0;

        /// <summary>
        /// clean enough to join sections for drying 
        /// </summary>
        [JsonProperty, DefaultValue(0.005)] public static double OkPressure { get => okPressure; set => Default.Ensure(ref okPressure, value); }
        static double okPressure = 0.005;

        /// <summary>
        /// clean enough to start a new sample
        /// </summary>
		[JsonProperty, DefaultValue(0.0002)] public static double CleanPressure { get => cleanPressure; set => Default.Ensure(ref cleanPressure, value); }
        static double cleanPressure = 0.0002;

        // TODO: move to d13CPort
        /// <summary>
        /// An initial GM He pressure that will result in pressure_over_atm when expanded into an LN-frozen vial
        /// </summary>
        [JsonProperty, DefaultValue(555.0)] public static double VPInitialHePressure { get => vpInitialHePressure; set => Default.Ensure(ref vpInitialHePressure, value); }
        static double vpInitialHePressure = 555.0;

        // TODO: move to d13CPort
        /// <summary>
        /// The maximum value for abs(pVP - pressure_over_atm) that is considered nominal.
        /// </summary>
		[JsonProperty, DefaultValue(18.0)] public static double VPErrorPressure { get => vpErrorPressure; set => Default.Ensure(ref vpErrorPressure, value); }
        static double vpErrorPressure = 18.0;

        /// <summary>
        /// The maximum acceptable residual pressure for a completed graphite reaction,
        /// expressed as a multiple of the expected value.
        /// </summary>
		[JsonProperty, DefaultValue(1.1)] public static double MaximumResidual { get => maximumResidual; set => Default.Ensure(ref maximumResidual, value); }
        static double maximumResidual = 1.1;


        /// <summary>
        /// An initial IM O2 pressure that will expand sufficient oxygen into the inlet port to fully combust
        /// any sample. Typically, this means enough to fully oxidize about three mg C to CO2.
        /// </summary>
		[JsonProperty, DefaultValue(1350.0)] public static double IMO2Pressure { get => imO2Pressure; set => Default.Ensure(ref imO2Pressure, value); }
        static double imO2Pressure = 1350.0;

        /// <summary>
        /// The trap pressure to maintain when collecting CO2 out of a carrier gas
        /// by "bleeding" the mixture through the FirstTrap.
        /// </summary>
		[JsonProperty, DefaultValue(0.15)] public static double FirstTrapBleedPressure { get => firstTrapBleedPressure; set => Default.Ensure(ref firstTrapBleedPressure, value); }
        static double firstTrapBleedPressure = 0.15;

        /// <summary>
        /// The VTT pressure at which the process should continue once the bypass valve has been opened;
        /// </summary>
		[JsonProperty, DefaultValue(0.15)] public static double FirstTrapEndPressure { get => firstTrapEndPressure; set => Default.Ensure(ref firstTrapEndPressure, value); }
        static double firstTrapEndPressure = 0.15;

        /// <summary>
        /// The maximum pressure differential across the FirstTrap for opening its flow bypass valve. Choose this pressure so that 
        /// opening the bypass valve will not produce an excessive downstream pressure spike.
        /// </summary>
		[JsonProperty, DefaultValue(5.0)] public static double FirstTrapFlowBypassPressure { get => firstTrapFlowBypassPressure; set => Default.Ensure(ref firstTrapFlowBypassPressure, value); }
        static double firstTrapFlowBypassPressure = 5.0;

        /// <summary>
        /// The initial H2 pressure to be used when preparing Fe in the GRs for use as the graphitization catalyst.
        /// </summary>
        [JsonProperty, DefaultValue(700.0)] public static double IronPreconditionH2Pressure { get => ironPreconditionH2Pressure; set => Default.Ensure(ref ironPreconditionH2Pressure, value); }
        static double ironPreconditionH2Pressure = 700.0;

        #endregion Pressure Constants

        #region Rate of Change Constants
        /// <summary>
        /// The IM pressure rate of change, following a stable flow, used to detect 
        /// that something has been put onto the IP needle (a vial or stopper).
        /// </summary>
        [JsonProperty, DefaultValue(5.0)] public static double IMPluggedTorrPerSecond { get => imPluggedTorrPerSecond; set => Default.Ensure(ref imPluggedTorrPerSecond, value); }
        static double imPluggedTorrPerSecond = 5.0;

        /// <summary>
        /// An IM pressure rate of change, following "plugged" detection, that indicates the 
        /// delayed slowing of the pressure increase characteristic of a vial being filled, 
        /// as distinct from the rapid drop to stability that occurs when the needle is 
        /// plugged by a stopper.
        /// </summary>
		[JsonProperty, DefaultValue(4.0)] public static double IMLoadedTorrPerSecond { get => imLoadedTorrPerSecond; set => Default.Ensure(ref imLoadedTorrPerSecond, value); }
        static double imLoadedTorrPerSecond = 4.0;

        /// <summary>
        /// The graphitization reaction is considered complete when the declining pressure rate 
        /// of change falls below this value.
        /// </summary>
		[JsonProperty, DefaultValue(0.5)] public static double GRCompleteTorrPerMinute { get => grCompleteTorrPerMinute; set => Default.Ensure(ref grCompleteTorrPerMinute, value); }
        static double grCompleteTorrPerMinute = 0.5;


        #endregion Rate of Change Constants

        #region Temperature Constants
        /// <summary>
        /// Typical room temperature in the lab.
        /// </summary>
        [JsonProperty, DefaultValue(21.0)] public static double RoomTemperature { get => roomTemperature; set => Default.Ensure(ref roomTemperature, value); }
        static double roomTemperature = 21.0;


        // TODO: move these to the GR class
        /// <summary>
        /// Reaction temperature for trapping trace sulfur contamination in a CO2 sample onto iron powder.
        /// </summary>
		[JsonProperty, DefaultValue(400)] public static int SulfurTrapTemperature { get => sulfurTrapTemperature; set => Default.Ensure(ref sulfurTrapTemperature, value); }
        static int sulfurTrapTemperature = 400;

        /// <summary>
        /// Reaction temperature for preparing iron powder with H2 for use as a CO2 reduction catalyst.
        /// </summary>
        [JsonProperty, DefaultValue(400)] public static int IronPreconditioningTemperature { get => ironPreconditioningTemperature; set => Default.Ensure(ref ironPreconditioningTemperature, value); }
        static int ironPreconditioningTemperature = 400;

        /// <summary>
        /// Less than this error is near enough to the Fe Prep reaction temperature to begin the process.
        /// </summary>
		[JsonProperty, DefaultValue(10)] public static int IronPreconditioningTemperatureCushion { get => ironPreconditioningTemperatureCushion; set => Default.Ensure(ref ironPreconditioningTemperatureCushion, value); }
        static int ironPreconditioningTemperatureCushion = 10;

        /// <summary>
        /// Extract the CO2 from condensables at this temperature.
        /// </summary>
		[JsonProperty, DefaultValue(-140)] public static int CO2ExtractionTemperature { get => co2ExtractionTemperature; set => Default.Ensure(ref co2ExtractionTemperature, value); }
        static int co2ExtractionTemperature = -140;


        #endregion

        #region Time Constants

        /// <summary>
        /// Time to wait for CO2 to move from InletPort to the FirstTrap.
        /// </summary>
        [JsonProperty, DefaultValue(10)] public static int CollectionMinutes { get => collectionMinutes; set => Default.Ensure(ref collectionMinutes, value); }
        static int collectionMinutes = 10;
 
        /// <summary>
        /// Time to spend extracting CO2 from the VTT into the MC.
        /// </summary>
        [JsonProperty, DefaultValue(10)] public static int ExtractionMinutes { get => extractionMinutes; set => Default.Ensure(ref extractionMinutes, value); }
        static int extractionMinutes = 10;
 
        /// <summary>
        /// Time to wait for CO2 transfer between chambers.
        /// </summary>
        [JsonProperty, DefaultValue(4)] public static int CO2TransferMinutes { get => co2TransferMinutes; set => Default.Ensure(ref co2TransferMinutes, value); }
        static int co2TransferMinutes = 4;

        /// <summary>
        /// How long to average a measurement.
        /// </summary>
        [JsonProperty, DefaultValue(60)]
        public static int MeasurementSeconds { get => measurementSeconds; set => Default.Ensure(ref measurementSeconds, value); }
        static int measurementSeconds = 60;

        /// <summary>
        /// Duration of iron powder preparation process.
        /// </summary>
        [JsonProperty, DefaultValue(20)] public static int IronPreconditioningMinutes { get => ironPreconditioningMinutes; set => Default.Ensure(ref ironPreconditioningMinutes, value); }
        static int ironPreconditioningMinutes = 20;

        /// <summary>
        /// How long after turn-on the quartz bed takes to reach its functional temperature range.
        /// </summary>
		[JsonProperty, DefaultValue(10)] public static int QuartzFurnaceWarmupMinutes { get => quartzFurnaceWarmupMinutes; set => Default.Ensure(ref quartzFurnaceWarmupMinutes, value); }
        static int quartzFurnaceWarmupMinutes = 10;

        /// <summary>
        /// Duration of the sulfur trapping process.
        /// </summary>
		[JsonProperty, DefaultValue(20)] public static int SulfurTrapMinutes { get => sulfurTrapMinutes; set => Default.Ensure(ref sulfurTrapMinutes, value); }
        static int sulfurTrapMinutes = 20;

        /// <summary>
        /// System "tick" time. Determines how often the system checks polled conditions and updates passive devices.
        /// </summary>
		[JsonProperty, DefaultValue(50)] public static int UpdateIntervalMilliseconds { get => updateIntervalMilliseconds; set => Default.Ensure(ref updateIntervalMilliseconds, value); }
        static int updateIntervalMilliseconds = 50;

        #endregion

        #region Sample and Measurement Constants
        //
        // fundamental constants
        //
        /// <summary>
        /// Avogadro's number (particles/mol)
        /// </summary>
        [JsonProperty, DefaultValue(6.022140857E+23)] public static double AvogadrosNumber { get => avogadrosNumber; set => Default.Ensure(ref avogadrosNumber, value); }
        static double avogadrosNumber = 6.022140857E+23;

        /// <summary>
        /// Boltzmann constant (Pa * m^3 / K)
        /// </summary>
		[JsonProperty, DefaultValue(1.38064852E-23)] public static double BoltzmannConstant { get => boltzmannConstant; set => Default.Ensure(ref boltzmannConstant, value); }
        static double boltzmannConstant = 1.38064852E-23;

        /// <summary>
        /// Pascals per atm
        /// </summary>
        [JsonProperty, DefaultValue(101325.0)] public static double Pascal { get => pascal; set => Default.Ensure(ref pascal, value); }
        static double pascal = 101325.0;

        /// <summary>
        /// Torr per atm
        /// </summary>
		[JsonProperty, DefaultValue(760.0)] public static double Torr { get => torr; set => Default.Ensure(ref torr, value); }
        static double torr = 760.0;

        /// <summary>
        /// milliliters per liter
        /// </summary>
		[JsonProperty, DefaultValue(1000.0)] public static double MilliLiter { get => milliLiter; set => Default.Ensure(ref milliLiter, value); }
        static double milliLiter = 1000.0;

        /// <summary>
        /// cubic meters per liter
        /// </summary>
		[JsonProperty, DefaultValue(0.001)] public static double CubicMeter { get => cubicMeter; set => Default.Ensure(ref cubicMeter, value); }
        static double cubicMeter = 0.001;

        /// <summary>
        /// 0 °C in kelvins
        /// </summary>
		[JsonProperty, DefaultValue(273.15)] public static double ZeroDegreesC { get => zeroDegreesC; set => Default.Ensure(ref zeroDegreesC, value); }
        static double zeroDegreesC = 273.15;

        /// <summary>
        /// mass of carbon per mole, in micrograms, assuming standard isotopic composition
        /// </summary>
		public static double MicrogramsCarbonPerMole => GramsCarbonPerMole * 1000000;

        /// <summary>
        /// mass of carbon per mole, in grams, assuming standard isotopic composition
        /// </summary>
		[JsonProperty, DefaultValue(12.011)] public static double GramsCarbonPerMole { get => gramsCarbonPerMole; set => Default.Ensure(ref gramsCarbonPerMole, value); }
        public static double gramsCarbonPerMole = 12.011;

        /// <summary>
        ///  Boltzmann constant (Torr * mL / K)
        /// </summary>
        public static double BoltzmannConstantTorr_mL => BoltzmannConstant * Torr / Pascal * MilliLiter / CubicMeter;

        /// <summary>
        /// average number of carbon atoms per microgram,
        /// assuming standard isotopic composition
        /// </summary>
        public static double CarbonAtomsPerMicrogram => AvogadrosNumber / MicrogramsCarbonPerMole;

        /// <summary>
        /// Reference data for CO2 phase equilibrium temperature as a function of pressure.
        /// Available pressure range: 1e-5 to 8e+2 Torr.
        /// Available temperature range: -181 to -78.5 °C.
        /// Searching for pressures or temperatures outside these ranges will produce
        /// unreliable values.
        /// </summary>
        public static LookupTable CO2EqTable { get; private set; } = new LookupTable(@"CO2 eq.dat");

        /// <summary>
        /// A low enough value that sample pressures can easily be raised to
        /// exceed it by a large enough margin that quantitative transfer
        /// is readily achievable. At the same time, the value should be 
        /// high enough that its CO2 phase-equilibrium temperature can easily 
        /// be reached.
        /// At 6e-4 Torr, CO2 solid and gas reach equilibrium at -170 °C,
        /// readily achievable. Given equal chamber volumes, for example,
        /// a transfer of 99.9% could be achieved by raising pressure of the
        /// source to 1000 times the destination pressure, or > 6e-1 Torr,
        /// by increasing its CO2 temperature to about -140 °C.
        /// </summary>        
        [JsonProperty, DefaultValue(6e-4)] public static double CO2FreezePressure { get => co2FreezePressure; set => Default.Ensure(ref co2FreezePressure, value); }
        static double co2FreezePressure = 6e-4; // Torr

        /// <summary>
        /// When CO2 is at the specified pressure and this temperature, its 
        /// deposition and sublimation rates are equal.
        /// </summary>
        /// <param name="pressure"></param>
        /// <returns>The temperature at which CO2 at the given pressure sublimes and deposits at equal rates</returns>
        public static double CO2EquilibriumTemperature(double pressure) =>
            CO2EqTable.Interpolate(pressure);

        /// <summary>
        /// When CO2 is at the specified temperature and this pressure, its 
        /// deposition and sublimation rates are equal.
        /// </summary>
        /// <param name="temperature"></param>
        /// <returns>The pressure at which CO2 at the given temperature sublimes and deposits at equal rates</returns>
        public static double CO2EquilibriumPressure(double temperature) =>
            CO2EqTable.ReverseInterpolate(temperature);

        /// <summary>
        /// The CO2 phase equilibrium temperature at CO2FreezePressure.
        /// </summary>
        public static double CO2FreezeTemperature => CO2EquilibriumTemperature(CO2FreezePressure);

        /// <summary>
        /// During a CO2 transfer, the source temperature above which 
        /// the flow has substantially begun. E.g., the CO2 phase-equilibrium
        /// temperature of a pressure 10000 times that of the destination.
        /// </summary>
        public static double CO2TransferStartTemperature => CO2EquilibriumTemperature(10000 * CO2FreezePressure);

        /// <summary>
        /// Stoichiometric ratio of hydrogen and carbon dioxide for CO2 reduction reactions.
        /// </summary>
        [JsonProperty, DefaultValue(2.0)] public static double H2_CO2StoichiometricRatio { get => h2_CO2StoichiometricRatio; set => Default.Ensure(ref h2_CO2StoichiometricRatio, value); }
        static double h2_CO2StoichiometricRatio = 2.0;

        /// <summary>
        /// Target H2:CO2 ratio for graphitization, providing excess hydogen to speed reaction.
        /// </summary>
		[JsonProperty, DefaultValue(2.3)] public static double H2_CO2GraphitizationRatio { get => h2_CO2GraphitizationRatio; set => Default.Ensure(ref h2_CO2GraphitizationRatio, value); }
        static double h2_CO2GraphitizationRatio = 2.3;

        /// <summary>
        /// Estimated appropriate initial GM H2 pressure reduction to compensate for 
        /// higher density of H2 in frozen standard GR coldfinger. The small coldfingers
        /// do not seem to significantly affect the average H2 density.
        /// </summary>
		[JsonProperty, DefaultValue(0.68)] public static double H2DensityAdjustment { get => h2DensityAdjustment; set => Default.Ensure(ref h2DensityAdjustment, value); }
        static double h2DensityAdjustment = 0.68;

        /// <summary>
        /// A sample with this mass of carbon or less is processed as "small."
        /// </summary>
		[JsonProperty, DefaultValue(50)] public static int SmallSampleMicrogramsCarbon { get => smallSampleMicrogramsCarbon; set => Default.Ensure(ref smallSampleMicrogramsCarbon, value); }
        static int smallSampleMicrogramsCarbon = 50;

        /// <summary>
        /// Use small graphite reactors for small samples.
        /// </summary>
		[JsonProperty, DefaultValue(false)] public static bool EnableSmallReactors { get => enableSmallReactors; set => Default.Ensure(ref enableSmallReactors, value); }
        static bool enableSmallReactors = false;

        /// <summary>
        /// The minimum Aliquot mass (in micrograms) sufficient to provide a
        /// d13C split.
        /// </summary>
		[JsonProperty, DefaultValue(36)] public static int MinimumUgCThatPermits_d13CSplit { get => minimumUgCThatPermits_d13CSplit; set => Default.Ensure(ref minimumUgCThatPermits_d13CSplit, value); }
        
        static int minimumUgCThatPermits_d13CSplit = 36;

        /// <summary>
        /// The minimum final mass for a diluted sample. Dilution is disabled unless 
        /// DilutedSampleMicrogramsCarbon > SmallSampleMicrogramsCarbon
        /// </summary>
        [JsonProperty, DefaultValue(200)] public static int DilutedSampleMicrogramsCarbon { get => dilutedSampleMicrogramsCarbon; set => Default.Ensure(ref dilutedSampleMicrogramsCarbon, value); }
        static int dilutedSampleMicrogramsCarbon = 200;

        /// <summary>
        /// If a sample has at least this mass of carbon, a fraction will be discarded
        /// before graphitizing.
        /// </summary>
        [JsonProperty, DefaultValue(1100)] public static int MaximumSampleMicrogramsCarbon { get => maximumSampleMicrogramsCarbon; set => Default.Ensure(ref maximumSampleMicrogramsCarbon, value); }
        static int maximumSampleMicrogramsCarbon = 1100;

        /// <summary>
        /// Next available Sample Number. Provides a unique Sample.Name for each sample processed.
		/// TODO: consider using UID?
		/// </summary>
        [JsonProperty, DefaultValue(0)] public static int SampleCounter { get => sampleCounter; set => Default.Ensure(ref sampleCounter, value); }
        static int sampleCounter = 0;

        /// <summary>
        /// The maximum number of approximate aliquots the sample can be divided into.
        /// This value is automatically determined from the number of ports on the 
        /// measurement chamber (MC.Ports.Count);
		/// </summary>
        [JsonProperty, DefaultValue(3)] public static int MaximumAliquotsPerSample { get => maximumAliquotsPerSample; set => Default.Ensure(ref maximumAliquotsPerSample, value); }
        static int maximumAliquotsPerSample = 3;

        #endregion Constants
    }
}
