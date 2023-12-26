using Newtonsoft.Json;
using System;
using System.Windows.Markup;

namespace AeonHacs.Components
{
    public class Manometer : Meter, IManometer, Manometer.IDevice, Manometer.IConfig
    {
        #region static

        public static implicit operator double(Manometer x)
        { return x?.Pressure ?? 0; }

        // a significant change in pressure
        public static bool SignificantChange(double pFrom, double pTo)
        {
            if (pFrom <= 0 || pTo <= 0) return pFrom != pTo;
            var change = Math.Abs(pTo - pFrom);
            var scale = Math.Min(Math.Abs(pFrom), Math.Abs(pTo));
            double significant;

            if (scale >= 2)
                significant = 1;    // 1 Torr (50% at 2; 10% at 10; 1% at 100; 0.1% at 1000)
            else
                significant = Math.Pow(10, (int)Math.Log10(scale) - 2.0);
            return change >= significant;
        }

        #endregion static

        #region Device interfaces

        public new interface IDevice : Meter.IDevice
        {
            double Pressure { get; set; }
        }
        public new interface IConfig : Meter.IConfig { }
        public new IDevice Device => this;
        public new IConfig Config => this;

        #endregion Device interfaces

        public override double Value
        {
            get => base.Value;
            protected set
            {
                var vOld = base.Value;
                base.Value = value;
                if (base.Value != vOld)
                    NotifyPropertyChanged(nameof(Pressure));
            }
        }

        [JsonProperty]
        public virtual double Pressure
        {
            get => Value;
            protected set => Update(value);
        }
        double IDevice.Pressure
        {
            get => Pressure;
            set => Pressure = value;
        }
        //public double Voltage => (this as IVoltmeter)?.Voltage ?? 0;

        public Manometer(IHacsDevice d = null) : base(d) { }

    }
}
