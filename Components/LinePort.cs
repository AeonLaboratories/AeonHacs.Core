using Newtonsoft.Json;
using System.ComponentModel;

namespace AeonHacs.Components;

public class LinePort : Port, ILinePort
{
    #region HacsComponent
    [HacsConnect]
    protected override void Connect()
    {
        base.Connect();
        Sample = Find<Sample>(sampleName);
    }

    #endregion HacsComponent

    public enum States { Disabled, Empty, Loaded, Prepared, InProcess, Complete }

    [JsonProperty]
    public virtual States State
    {
        get => state;
        set
        {
            var priorState = state;
            if (Ensure(ref state, value) &&  state == States.Empty)
                ClearContents();
        }
    }
    States state;

    [JsonProperty("Sample")]
    string SampleName { get => Sample?.Name; set => sampleName = value; }
    string sampleName;
    public Sample Sample
    {
        get => sample;
        set => Ensure(ref sample, value, OnPropertyChanged);
    }
    Sample sample;

    [JsonProperty("Aliquot"), DefaultValue(0)]
    int AliquotIndex
    {
        get => aliquotIndex;
        set => Ensure(ref aliquotIndex, value, OnPropertyChanged);
    }
    int aliquotIndex = 0;

    public Aliquot Aliquot
    {
        get
        {
            if (Sample?.Aliquots != null && Sample.Aliquots.Count > AliquotIndex)
                return Sample.Aliquots[AliquotIndex];
            else
                return null;
        }
        set
        {
            Sample = value?.Sample;
            AliquotIndex = Sample?.AliquotIndex(value) ?? 0;
            if (Aliquot == null && State != States.Empty)
                State = States.Empty;
            NotifyPropertyChanged(nameof(Contents));
        }
    }

    public virtual string Contents => Aliquot?.Name ?? "";
    public virtual void ClearContents() => Aliquot = null;

    protected override void OnPropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e?.PropertyName == nameof(Sample) ||
            e?.PropertyName == nameof(AliquotIndex))
            NotifyPropertyChanged();
        else
            base.OnPropertyChanged(sender, e);
    }

    public override string ToString()
    {
        return $"{Name}: {Contents} ({State})";
    }
}
