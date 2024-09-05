using AeonHacs;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using AeonHacs.Utilities;

namespace AeonHacs.Components
{
    public class Power : HacsComponent, IPower
    {
        #region HacsComponent
        [HacsConnect]
        protected virtual void Connect()
        {
            DC5V = Find<IVoltmeter>(dc5VName);
            MainsDetect = Find<IVoltmeter>(mainsDetectName);
        }

        #endregion HacsComponent

        [JsonProperty("DC5V")]
        string Dc5VName { get => DC5V?.Name; set => dc5VName = value; }
        string dc5VName;
        public IVoltmeter DC5V
        {
            get => dc5V;
            set => Ensure(ref dc5V, value, NotifyPropertyChanged);
        }
        IVoltmeter dc5V;

        [JsonProperty("MainsDetect")]
        string MainsDetectName { get => MainsDetect?.Name; set => mainsDetectName = value; }
        string mainsDetectName;
        public IVoltmeter MainsDetect
        {
            get => mainsDetect;
            set => Ensure(ref mainsDetect, value, NotifyPropertyChanged);
        }
        IVoltmeter mainsDetect;

        [JsonProperty, DefaultValue(4.0)]
        public double MainsDetectMinimumVoltage
        {
            get => mainsDetectMinimumVoltage;
            set => Ensure(ref mainsDetectMinimumVoltage, value);
        }
        double mainsDetectMinimumVoltage = 4.0;

        public bool MainsIsDown => MainsDetect.Voltage < MainsDetectMinimumVoltage;
        public Stopwatch MainsDownTimer = new Stopwatch();

        [JsonProperty]
        public int MilliSecondsMainsDownLimit
        {
            get => milliSecondsMainsDownLimit;
            set => Ensure(ref milliSecondsMainsDownLimit, value);
        }
        int milliSecondsMainsDownLimit = 60000;

        public bool MainsHasFailed => MainsDownTimer.ElapsedMilliseconds > MilliSecondsMainsDownLimit;

        // TODO: should these be NotifyPropertyChanged() instead?
        public Action MainsDown { get; set; }
        public Action MainsRestored { get; set; }
        public Action MainsFailed { get; set; }

        protected override void NotifyPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (sender == DC5V && e.PropertyName == nameof(Meter.Value))
                Meter.RatiometricValue = DC5V.Value;
            base.NotifyPropertyChanged(sender, e);
        }

        bool failureHandled = false;
        public void Update()
        {
            if (MainsIsDown)
            {
                if (!MainsDownTimer.IsRunning)
                {
                    MainsDownTimer.Restart();
                    MainsDown?.Invoke();
                    failureHandled = false;
                }
                else if (MainsHasFailed && !failureHandled)
                {
                    failureHandled = true;
                    MainsFailed?.Invoke();
                }
            }
            else if (MainsDownTimer.IsRunning)
            {
                MainsDownTimer.Stop();
                MainsDownTimer.Reset();
                MainsRestored?.Invoke();
            }
        }
    }
}