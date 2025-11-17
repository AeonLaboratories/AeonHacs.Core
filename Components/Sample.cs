using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using static AeonHacs.Components.CegsPreferences;

namespace AeonHacs.Components;

public class Sample : HacsComponent
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
    /// <remarks>
    /// Does nothing if trapName is null or blank, or already at the end of the list.
    /// </remarks>
    /// </summary>
    /// <param name="trapName"></param>
    public void AddTrap(string trapName)
    {
        if (trapName.IsBlank() || (traps?.EndsWith(trapName) ?? false)) return;
        traps = traps.IsBlank() ? trapName : $"{traps}, {trapName}";
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
    public string Protocol
    {
        get => protocol;
        set => Ensure(ref protocol, value);
    }
    string protocol;

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

    /// <summary>
    /// Returns the value of the named parameter.
    /// </summary>
    public virtual double Parameter(string name) =>
        Parameters?.Find(x => x.ParameterName == name)?.Value ??
            CegsParameter(name)?.Value ?? double.NaN;

    /// <summary>
    /// Checks for the parameter with the given name and returns true if it is 
    /// found and its value is a non-zero number; otherwise returns false.
    /// </summary>
    /// <param name="name"></param>
    public bool ParameterTrue(string name)
    {
        var p = Parameter(name);
        return p.IsANumber() && p != 0;
    }

    /// <summary>
    /// Keep a local parameter if and only if it's distinct from the default.
    /// </summary>
    public void SetParameter(Parameter p)
    {
        if (p == null) return;  // nothing to do.
        RemoveParameter(p.ParameterName);   // Remove existing

        // find the default
        var dp = CegsParameter(p.ParameterName);
        if (dp == null)     // Always add it if there's no default.
        {
            Parameters.Add(p);
            return;
        }

        // Comparing p and dp, they differ if
        // (A) only one of them is a number, or
        // (B) both are numbers, but their values differ.
        var dnum = dp.Value.IsANumber();
        var pnum = p.Value.IsANumber();
        if ((dnum != pnum) || (dnum && pnum && dp.Value != p.Value))
        {
            Parameters.Add(p);
        }
    }

    /// <summary>
    /// Remove all parameters with the given name.
    /// </summary>
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

    /// <summary>
    /// Sample size
    /// </summary>
    public double Milligrams
    {
        get => Grams * 1000;
        set => Grams = value / 1000;
    }

    /// <summary>
    /// Sample size
    /// </summary>
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
    /// A counter indicating which split of the sample this is.
    /// </summary>
    [JsonProperty]
    public int Split
        {
        get => split;
        set => Ensure(ref split, value);
    }
    int split;

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
    /// Total micrograms carbon extracted from the sample (as CO2)
    /// </summary>
    [JsonProperty]
    public double TotalMicrogramsCarbon
    {
        get => totalMicrogramsCarbon;
        set => Ensure(ref totalMicrogramsCarbon, value, OnPropertyChanged);
    }
    double totalMicrogramsCarbon;

    /// <summary>
    /// Total amount of carbon extracted from the sample, in micromoles.
    /// </summary>
    public double TotalMicromolesCarbon
    {
        get => TotalMicrogramsCarbon / GramsCarbonPerMole;
        set => TotalMicrogramsCarbon = value * GramsCarbonPerMole;
    }

    /// <summary>
    /// Number of splits (or "cuts") discarded to reduce sample size.
    /// </summary>
    [JsonProperty]
    public int Discards
    {
        get => discards;
        set => Ensure(ref discards, value);
    }
    int discards;

    /// <summary>
    /// Extracted carbon selected for analysis, in micrograms
    /// (ugC from the sample + ugDC)
    /// </summary>
    [JsonProperty]
    public double SelectedMicrogramsCarbon
    {
        get => selectedMicrogramsCarbon;
        set => Ensure(ref selectedMicrogramsCarbon, value, OnPropertyChanged);
    }
    double selectedMicrogramsCarbon;

    /// <summary>
    /// Extracted carbon selected for analysis
    /// </summary>
    public double SelectedMicromolesCarbon
    {
        get => SelectedMicrogramsCarbon / GramsCarbonPerMole;
        set => SelectedMicrogramsCarbon = value * GramsCarbonPerMole;
    }

    /// <summary>
    /// Extracted carbon selected for d13C analysis
    /// </summary>
    [JsonProperty]
    public double Micrograms_d13C
    {
        get => micrograms_d13C;
        set => Ensure(ref micrograms_d13C, value);
    }
    double micrograms_d13C;

    /// <summary>
    /// Carbon concentration in the d13C split after adding carrier gas.
    /// </summary>
    [JsonProperty]
    public double d13CPartsPerMillion
    {
        get => _d13CPartsPerMillion;
        set => Ensure(ref _d13CPartsPerMillion, value);
    }
    double _d13CPartsPerMillion;


    [JsonProperty]
    public List<Aliquot> Aliquots
    {
        get => aliquots;
        set => Ensure(ref aliquots, value);
    }
    List<Aliquot> aliquots = new List<Aliquot>(); // Aliquots is never null

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

    /// <summary>
    /// Creates a new split of the current sample.
    /// </summary>
    /// <remarks>The split is a pseudo-sample; its CO2 is obtained from the same material as the
    /// source sample, but under different conditions.
    /// The split's <see cref="Split"/> property is set to one more than its source's Split value, 
    /// so create each split from the prior one to maintain a sane value.
    /// The <see cref="DateTime"/> property is set to the split creation time; change it in the caller
    /// if a different value is desired.
    /// <see cref="State"/> is set to <see cref="States.Loaded"/> in anticipation of collection,
    /// and the <see cref="InletPort"/>'s <see cref="LinePort.State"/> is set to <see cref="LinePort.States.Loaded"/> as well.
    /// The <see cref="Parameters"/> are copied from the source
    /// sample, and a new set of <see cref="Aliquots"/> mirroring the source sample's is created
    /// </remarks>
    /// <returns>A new <see cref="Sample"/> instance representing the split.</returns>
    public Sample CreateSplit()
    {
        Sample splitSample = new()
        {
            LabId = LabId,
            Split = Split + 1,
            DateTime = DateTime.Now,
            State = States.Loaded,
            InletPort = InletPort,
            Protocol = Protocol,
            Parameters = Parameters.Select(p => p.Clone()).ToList(),
            SulfurSuspected = SulfurSuspected,
            Take_d13C = Take_d13C,
            Aliquots = Aliquots.Select(a => new Aliquot(a)).ToList()
        };
        splitSample.InletPort.State = LinePort.States.Loaded;
        return splitSample;
    }

    public Sample Clone()
    {
        var sample =  new Sample()
        {
            LabId = LabId,
            DateTime = DateTime,
            State = State,      // set to States.Unknown? - defer to caller
            InletPort = InletPort,
            Traps = Traps,      // set to empty? - defer to caller
            d13CPort = d13CPort,
            Protocol = Protocol,
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
            Aliquots = Aliquots.Select(a => new Aliquot(a) ).ToList()
        };
        sample.Aliquots.ForEach(a => a.Sample = sample);
        return sample;
    }

    public int AliquotIndex(Aliquot aliquot)
    {
        for (int i = 0; i < Aliquots.Count; ++i)
            if (Aliquots[i] == aliquot)
                return i;
        return -1;
    }

    string toString(Sample sample) =>
        $"{sample.LabId}{(sample.Split > 0 ? $" Split {sample.Split}" : "")} [{sample.InletPort?.Name ?? "---"}] {{{sample.Name}}}";

    public override string ToString()
    {
        var list = FindAll<Sample>().Where(s => s.LabId == LabId).OrderBy(s => s.Split).Select(toString);
        return string.Join("\r\n", list);
    }
}
