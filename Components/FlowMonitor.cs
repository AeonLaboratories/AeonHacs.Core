using AeonHacs.Utilities;
using Newtonsoft.Json;
using System.ComponentModel;
using System.Threading;

namespace AeonHacs.Components;

public class FlowMonitor : HacsComponent
{
    #region HacsComponent

    [HacsConnect]
    protected virtual void Connect()
    {
        FlowRateMeter = Find<IMeter>(flowRateMeterName);
        if (FlowRateMeter != null)
            FlowRateMeter.PropertyChanged += OnPropertyChanged;
    }

    [HacsInitialize]
    protected virtual void Initialize()
    {
        flowTrackingThread = new Thread(TrackFlow)
        {
            Name = $"{Name} TrackFlow",
            IsBackground = true
        };
        flowTrackingThread.Start();
    }

    #endregion HacsComponent

    [JsonProperty("FlowRateMeter")]
    string FlowRateMeterName { get => FlowRateMeter?.Name; set => flowRateMeterName = value; }
    string flowRateMeterName;
    public IMeter FlowRateMeter
    {
        get => flowRateMeter;
        set => Ensure(ref flowRateMeter, value, NotifyPropertyChanged);
    }
    IMeter flowRateMeter;

    public void ZeroNow() => FlowRateMeter.ZeroNow();

    public double FlowRate
    {
        get => flowRate;
        set => Ensure(ref flowRate, value);
    }
    double flowRate;

    object flowTrackingLock = new object();

    public double TrackedFlow
    {
        get => trackedFlow;
        set => Ensure(ref trackedFlow, value);
    }
    double trackedFlow;

    Stopwatch flowTrackingStopwatch = new Stopwatch();
    Thread flowTrackingThread;
    AutoResetEvent flowTrackingSignal = new AutoResetEvent(false);

    void TrackFlow()
    {
        flowTrackingStopwatch.Restart();
        while (true)
        {
            lock (flowTrackingLock)
            {
                TrackedFlow += FlowRate * flowTrackingStopwatch.Elapsed.TotalMinutes;
                flowTrackingStopwatch.Restart();
            }

            flowTrackingSignal.WaitOne(1000);
        }
    }

    /// <summary>
    /// Sets the TrackedFlow to 0.
    /// </summary>
    public void Reset()
    {
        lock (flowTrackingLock)
        {
            TrackedFlow = 0;
            flowTrackingStopwatch.Restart();
        }
    }

    public FlowMonitor() { }

    public void OnPropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        var propertyName = e?.PropertyName;
        if (sender == FlowRateMeter && propertyName == nameof(Meter.Value))
            FlowRate = FlowRateMeter.Value;
    }

    string TrackedUnit(string unit) =>
        unit.ToLower().EndsWith("m") ? 
            unit.Substring(0, unit.Length - 1) :     // a rash assumption
            unit + "/minute";
 
    public override string ToString()
    {
        var unit = FlowRateMeter.UnitSymbol;
        return $"{Name}: {FlowRateMeter.Value:0.00} {unit}" +
           (TrackedFlow > 0 ?
                Utility.IndentLines($"\r\nTracked Flow: {TrackedFlow:0.0} {TrackedUnit(unit)}") : 
                ""
           );
    }
}
