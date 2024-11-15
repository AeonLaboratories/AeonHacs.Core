using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using static AeonHacs.Components.CegsPreferences;

namespace AeonHacs.Components
{
    public class Sample : HacsComponent, ISample
    {
        public enum States { Unknown, Loaded, Prepared, InProcess, Complete }

        [JsonProperty]
        public virtual States State
        {
            get => state;
            set => Ensure(ref state, value);
        }
        States state;

        #region static
        /// <summary>
        /// Generates a new unique Sample Name.
        /// </summary>
        [Browsable(false)]
        public static string GenerateSampleName => $"Sample{SampleCounter++}";

        #endregion static

        #region HacsComponent
        [HacsConnect]
        public virtual void Connect()
        {
            InletPort = Find<IInletPort>(inletPortName);
            d13CPort = Find<Id13CPort>(_d13CPortName);
        }

        #endregion HacsComponent

        /// <summary>
        /// Typically assigned by the laboratory to identify and track the sample.
        /// </summary>
        [JsonProperty]
        public string LabId
        {
            get => labId;
            set => Ensure(ref labId, value);
        }
        string labId;

        [JsonProperty]
        public DateTime DateTime
        {
            get => dateTime;
            set => Ensure(ref dateTime, value);
        }
        DateTime dateTime;

        [JsonProperty("InletPort")]
        string InletPortName { get => InletPort?.Name ?? ""; set => inletPortName = value; }
        string inletPortName;
        public IInletPort InletPort
        {
            get => inletPort;
            set => Ensure(ref inletPort, value);
        }
        IInletPort inletPort;


        [JsonProperty("Traps")]
        public string Traps
        {
            get => traps;
            set => Ensure(ref traps, value);
        }   
        string traps;

        /// <summary>
        /// Appends a trap name to Traps.
        /// </summary>
        /// <param name="trapName"></param>
        public void AddTrap(string trapName)
        {
            if (!traps.IsBlank())
                traps += ", " + trapName;
            else
                traps = trapName;
        }


        [JsonProperty("d13CPort")]
        string d13CPortName { get => d13CPort?.Name ?? ""; set => _d13CPortName = value; }
        string _d13CPortName;
        public Id13CPort d13CPort
        {
            get => _d13CPort;
            set => Ensure(ref _d13CPort, value);
        }
        Id13CPort _d13CPort;


        [JsonProperty]
        public string Process
        {
            get => process;
            set => Ensure(ref process, value);
        }
        string process;

        [JsonProperty]
        public List<Parameter> Parameters
        {
            get => parameters;
            set => Ensure(ref parameters, value);
        }
        List<Parameter> parameters = [];

        private List<Parameter> CegsParameters => FirstOrDefault<Cegs>().CegsPreferences.DefaultParameters;

        private Parameter CegsParameter(string name) =>
            CegsParameters.Find(x => x.ParameterName == name);

        public virtual double Parameter(string name) =>
            Parameters?.Find(x => x.ParameterName == name)?.Value ??
                CegsParameter(name)?.Value ?? double.NaN;

        public void SetParameter(Parameter parameter)
        {
            RemoveParameter(parameter.ParameterName);
            if (CegsParameter(parameter.ParameterName) is not Parameter cegsParameter || parameter.Value != cegsParameter.Value)
                Parameters.Add(parameter);
        }

        public void RemoveParameter(string name) =>
            Parameters.RemoveAll(p => p.ParameterName == name);


        [JsonProperty]
        public bool SulfurSuspected
        {
            get => sulfurSuspected;
            set => Ensure(ref sulfurSuspected, value);
        }
        bool sulfurSuspected;

        [JsonProperty]
        public bool Take_d13C
        {
            get => take_d13C;
            set => Ensure(ref take_d13C, value);
        }
        bool take_d13C;

        /// <summary>
        /// Sample size
        /// </summary>
        [JsonProperty]
        public double Grams
        {
            get => grams;
            set => Ensure(ref grams, value, OnPropertyChanged);
        }
        double grams;

        public double Milligrams
        {
            get => Grams * 1000;
            set => Grams = value / 1000;
        }

        public double Micrograms
        {
            get => Grams * 1000000;
            set => Grams = value / 1000000;
        }

        /// <summary>
        /// The initial sample mass, expressed as micromoles;
        /// intended to be used with pure gas samples like CO2 or CH4 that
        /// have one carbon atom per particle.
        /// Perhaps this should be renamed to avoid confusion with
        /// the other similarly-named properties ("xxCarbon"), which refer
        /// to the extracted CO2.
        /// </summary>
        public double Micromoles
        {
            get => Micrograms / GramsCarbonPerMole;
            set => Micrograms = value * GramsCarbonPerMole;
        }

        /// <summary>
        /// micrograms of added dilution (dead) carbon
        /// </summary>
        [JsonProperty]
        public double MicrogramsDilutionCarbon
        {
            get => microgramsDilutionCarbon;
            set => Ensure(ref microgramsDilutionCarbon, value);
        }
        double microgramsDilutionCarbon;

        /// <summary>
        /// total micrograms carbon from the sample
        /// </summary>
        [JsonProperty]
        public double TotalMicrogramsCarbon
        {
            get => totalMicrogramsCarbon;
            set => Ensure(ref totalMicrogramsCarbon, value, OnPropertyChanged);
        }
        double totalMicrogramsCarbon;

        public double TotalMicromolesCarbon
        {
            get => TotalMicrogramsCarbon / GramsCarbonPerMole;
            set => TotalMicrogramsCarbon = value * GramsCarbonPerMole;
        }

        [JsonProperty]
        public int Discards
        {
            get => discards;
            set => Ensure(ref discards, value);
        }
        int discards;

        /// <summary>
        /// micrograms carbon (C from the sample + ugDC) selected for analysis
        /// </summary>
        [JsonProperty]
        public double SelectedMicrogramsCarbon
        {
            get => selectedMicrogramsCarbon;
            set => Ensure(ref selectedMicrogramsCarbon, value, OnPropertyChanged);
        }
        double selectedMicrogramsCarbon;

        public double SelectedMicromolesCarbon
        {
            get => SelectedMicrogramsCarbon / GramsCarbonPerMole;
            set => SelectedMicrogramsCarbon = value * GramsCarbonPerMole;
        }

        [JsonProperty]
        public double Micrograms_d13C
        {
            get => micrograms_d13C;
            set => Ensure(ref micrograms_d13C, value);
        }
        double micrograms_d13C;

        [JsonProperty]
        public double d13CPartsPerMillion
        {
            get => _d13CPartsPerMillion;
            set => Ensure(ref _d13CPartsPerMillion, value);
        }
        double _d13CPartsPerMillion;


        [JsonProperty]
        public List<IAliquot> Aliquots
        {
            get => aliquots;
            set => Ensure(ref aliquots, value);
        }
        List<IAliquot> aliquots = new List<IAliquot>(); // Aliquots is never null

        public int AliquotsCount
        {
            get => Aliquots.Count;      // It is an error for Aliquots to be null
            set
            {
                if (value < 0) value = 0;
                if (value > MaximumAliquotsPerSample)
                    value = MaximumAliquotsPerSample;
                if (Aliquots.Count < value)
                {
                    for (int i = Aliquots.Count; i < value; ++i)
                        Aliquots.Add(new Aliquot() { Sample = this });
                }
                else if (Aliquots.Count > value)
                {
                    while (Aliquots.Count > value)
                        Aliquots.RemoveAt(value);
                }
            }
        }

        public bool ShouldSerializeAliquotIds() => false;
        [JsonProperty]        // deserialize only
        public List<string> AliquotIds
        {
            get => Aliquots.Names();
            set
            {
                if (value == null) return;
                // allow blank Aliquot IDs; automatically generate them later
                // silently delete extraneous values
                AliquotsCount = Math.Min(value.Count, MaximumAliquotsPerSample);

                for (int i = 0; i < AliquotsCount; ++i)
                    Aliquots[i].Name = value[i];
            }
        }

        protected void OnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            var property = e?.PropertyName;
            if (property == nameof(Grams))
            {
                NotifyPropertyChanged(nameof(Milligrams));
                NotifyPropertyChanged(nameof(Micrograms));
                NotifyPropertyChanged(nameof(Micromoles));
            }
            else if (property == nameof(TotalMicrogramsCarbon))
            {
                NotifyPropertyChanged(nameof(TotalMicromolesCarbon));
            }
            else if (property == nameof(SelectedMicrogramsCarbon))
            {
                NotifyPropertyChanged(nameof(SelectedMicromolesCarbon));
            }
        }


        public Sample()
        {
            Name = GenerateSampleName;
        }

        public Sample Clone()
        {
            return new Sample()
            {
                LabId = LabId,
                DateTime = DateTime,
                State = State,      // set to States.Unknown? - defer to caller
                InletPort = InletPort,
                Traps = Traps,      // set to empty? - defer to caller
                d13CPort = d13CPort,
                Process = Process,
                Parameters = Parameters.Select(p => p.Clone()).ToList(),
                SulfurSuspected = SulfurSuspected,
                Take_d13C = Take_d13C,
                Grams = Grams,
                MicrogramsDilutionCarbon = MicrogramsDilutionCarbon,
                TotalMicrogramsCarbon = TotalMicrogramsCarbon,
                Discards = Discards,
                SelectedMicrogramsCarbon = SelectedMicrogramsCarbon,
                Micrograms_d13C = Micrograms_d13C,
                d13CPartsPerMillion = d13CPartsPerMillion,
                Aliquots = Aliquots.Select(a => (new Aliquot(a as Aliquot) as IAliquot)).ToList()
            };
        }

        public int AliquotIndex(IAliquot aliquot)
        {
            for (int i = 0; i < Aliquots.Count; ++i)
                if (Aliquots[i] == aliquot)
                    return i;
            return -1;
        }

        public override string ToString()
        {
            return $"{LabId} [{InletPort?.Name ?? "---"}] {{{Name}}}";
        }
    }
}
