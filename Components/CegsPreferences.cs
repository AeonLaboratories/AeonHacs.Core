using Newtonsoft.Json;
using System.ComponentModel;
using AeonHacs.Utilities;
using System.Collections.Generic;

namespace AeonHacs.Components
{
    public partial class CegsPreferences : HacsComponent, ICegsPreferences
    {
        public static CegsPreferences Default = new CegsPreferences();

        #region System state, configuration & operations

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

        /// <summary>
        /// Use small graphite reactors for small samples.
        /// </summary>
        [JsonProperty, DefaultValue(false)] public static bool EnableSmallReactors { get => enableSmallReactors; set => Default.Ensure(ref enableSmallReactors, value); }
        static bool enableSmallReactors = false;

        /// <summary>
        /// System "tick" time. Determines how often the system checks polled conditions and updates passive devices.
        /// </summary>
		[JsonProperty, DefaultValue(50)] public static int UpdateIntervalMilliseconds { get => updateIntervalMilliseconds; set => Default.Ensure(ref updateIntervalMilliseconds, value); }
        static int updateIntervalMilliseconds = 50;

        #endregion System state & operations

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
        /// Extract the CO2 from condensables at this temperature.
        /// </summary>
		[JsonProperty, DefaultValue(-140)] public static int CO2ExtractionTemperature { get => co2ExtractionTemperature; set => Default.Ensure(ref co2ExtractionTemperature, value); }
        static int co2ExtractionTemperature = -140;

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

         #endregion Constants


        /// <summary>
        /// Default process control parameters
        /// </summary>
        [JsonProperty]
        public List<Parameter> DefaultParameters
        {
            get => defaultParameters;
            set => Ensure(ref defaultParameters, value);
        }
        List<Parameter> defaultParameters = [];

        public void SetParameter(Parameter parameter)
        {
            RemoveParameter(parameter.ParameterName);
            DefaultParameters.Add(parameter);
        }

        public void RemoveParameter(string name) =>
            DefaultParameters.RemoveAll(p => p.ParameterName == name);

        public double Parameter(string name) =>
            DefaultParameters?.Find(x => x.ParameterName == name) is Parameter p && p.ParameterName == name ?
                p.Value : double.NaN;
    }
}
