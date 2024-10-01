using AeonHacs.Utilities;
using Newtonsoft.Json;
using System;
using System.ComponentModel;
using System.Text;

namespace AeonHacs.Components
{
    public class Chamber : HacsComponent, IChamber
    {
        #region HacsComponent
        [HacsConnect]
        protected virtual void Connect()
        {
            Manometer = Find<IManometer>(manometerName);
            Thermometer = Find<IThermometer>(thermometerName);
            Heater = Find<IHeater>(heaterName);
            Coldfinger = Find<IColdfinger>(coldfingerName);
        }

        #endregion HacsComponent

        /// <summary>
        /// The chamber volume in milliliters / cubic centimeters
        /// </summary>
        [JsonProperty]
        public virtual double MilliLiters
        {
            get => milliLiters;
            set => Ensure(ref milliLiters, value);
        }
        double milliLiters;

        [JsonProperty("Manometer")]
        string ManometerName { get => Manometer?.Name; set => manometerName = value; }
        string manometerName;
        public virtual IManometer Manometer
        {
            get => manometer;
            set => Ensure(ref manometer, value, OnPropertyChanged);
        }
        IManometer manometer;
        public virtual double Pressure => Manometer?.Pressure ?? 0;


        [JsonProperty("Thermometer")]
        string ThermometerName { get => Thermometer?.Name; set => thermometerName = value; }
        string thermometerName;
        public virtual IThermometer Thermometer
        {
            get => thermometer;
            set => Ensure(ref thermometer, value, OnPropertyChanged);
        }
        IThermometer thermometer;
        public virtual double Temperature => Thermometer?.Temperature ?? 0;

        [JsonProperty("Heater")]
        string HeaterName { get => Heater?.Name; set => heaterName = value; }
        string heaterName;
        public virtual IHeater Heater
        {
            get => heater;
            set => Ensure(ref heater, value, OnPropertyChanged);
        }
        IHeater heater;

        [JsonProperty("Coldfinger")]
        string ColdfingerName { get => Coldfinger?.Name; set => coldfingerName = value; }
        string coldfingerName;
        // TODO: make IFTColdfinger (derive from a new Coldfinger class?)
        public virtual IColdfinger Coldfinger
        {
            get => coldfinger;
            set => Ensure(ref coldfinger, value, OnPropertyChanged);
        }
        IColdfinger coldfinger;

        //[JsonProperty]
        public virtual Action Clean { get; set; }

        // instead of using NotifyPropertyChanged() directly, so it can be overridden in derived classes
        protected virtual void OnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            NotifyPropertyChanged(sender, e);
        }

        public override string ToString()
        {
            //TODO flesh out
            var sb = new StringBuilder();
            sb.Append($"{Name}");
            if (Manometer != null)
                sb.Append(Environment.NewLine + Utility.IndentLines(Manometer.ToString()));
            if (Thermometer != null)
                sb.Append(Environment.NewLine + Utility.IndentLines(Thermometer.ToString()));

            return sb.ToString();
        }
    }
}