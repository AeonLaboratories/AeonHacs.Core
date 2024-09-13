using AeonHacs.Utilities;
using Newtonsoft.Json;
using System;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;
using static AeonHacs.Notify;
using static AeonHacs.Utilities.Utility;

namespace AeonHacs.Components
{
    public class Meter : HacsDevice, IMeter, Meter.IDevice, Meter.IConfig
    {
        #region static

        public static implicit operator double(Meter x)
        { return x?.Value ?? 0; }

        public static double RatiometricNominal = 5.0;
        public static double RatiometricValue
        {
            get => ratiometricValue;
            set
            {
                ratiometricValue = value;
                RatiometricCorrection = ratiometricValue > 0 ? Math.Pow(RatiometricNominal / ratiometricValue, 0.8) : 1.0;
            }
        }
        static double ratiometricValue = 5.0;
        public static double RatiometricCorrection { get; set; } = 1.0;

        #endregion static

        #region Device interfaces

        public new interface IDevice : HacsDevice.IDevice
        {
            double Value { get; set; }
        }
        public new interface IConfig : HacsDevice.IConfig { }
        public new IDevice Device => this;
        public new IConfig Config => this;

        #endregion Device interfaces

        public double RawValue { get; private set; }
        public double FilteredValue { get; private set; }
        public double PriorValue { get; private set; }
        public double ConvertedValue { get; private set; }
        public double ResolvedValue { get; private set; }

        /// <summary>
        /// The current output value.
        /// </summary>
        //[JsonProperty]
        public virtual double Value
        {
            get => value;
            protected set
            {
                Set(ref this.value, value);
                NotifyPropertyChanged(nameof(RawValue));
                NotifyPropertyChanged(nameof(FilteredValue));
                NotifyPropertyChanged(nameof(PriorValue));
                NotifyPropertyChanged(nameof(ConvertedValue));
                NotifyPropertyChanged(nameof(ResolvedValue));
            }
        }
        double value;

        double IDevice.Value
        {
            get => Value;
            set => Value = value;
        }

        /// <summary>
        /// A symbol that represents the units for Value.
        /// </summary>
        [JsonProperty]
        public virtual string UnitSymbol
        {
            get => unitSymbol;
            set => Ensure(ref unitSymbol, value);
        }
        string unitSymbol = "";

        [JsonProperty]
        public virtual bool Ratiometric
        {
            get => ratiometric;
            set => Ensure(ref ratiometric, value);
        }
        bool ratiometric;

        [JsonProperty]
        public virtual double MaxValue { get; set; } = double.PositiveInfinity;

        [JsonProperty]
        public virtual double MinValue { get; set; } = double.NegativeInfinity;

        /// <summary>
        ///  The smallest meaningful (detectable) difference from zero that Value can be.
        ///  If Sensitivity is set to zero, it is ignored, in which case Value can be
        ///  arbitrarily close to zero.
        /// </summary>
        [JsonProperty]
        public virtual double Sensitivity
        {
            get => sensitivity;
            set => Ensure(ref sensitivity, Math.Abs(value));
        }
        double sensitivity;


        int significantDigits = 1;  // used when ResolutionIsProportional
        void setSignificantDigits() => significantDigits = 1 + Utility.PowerOfTenCeiling(1 / Resolution);

        /// <summary>
        /// The resolvable unit size (smaller Value differences are indistinguishable).
        /// </summary>
        [JsonProperty]
        public virtual double Resolution
        {
            get => resolution;
            set
            {
                if (Ensure(ref resolution, Math.Abs(value)) && ResolutionIsProportional)
                    setSignificantDigits();
            }
        }
        double resolution;

        /// <summary>
        /// The resolution scales with the magnitude of Value. Set to true if a
        /// logarithmic scale provides a more appropriate view of the quantity.
        /// </summary>
        [JsonProperty]
        public virtual bool ResolutionIsProportional
        {
            get => resolutionIsProportional;
            set
            {
                if (Ensure(ref resolutionIsProportional, value) && value)
                    setSignificantDigits();
            }
        }
        bool resolutionIsProportional = false;

        /// <summary>
        /// A digital filter, designed to smooth irrelevant variations
        /// in Value over time.
        /// </summary>
        [JsonProperty]
        public virtual DigitalFilter Filter
        {
            get => filter;
            set => Ensure(ref filter, value);
        }
        DigitalFilter filter;

        /// <summary>
        /// The set of arithmetic operations that converts the input data
        /// provided via Update() into Value, the dynamic quantity of interest.
        /// </summary>
        [JsonProperty]
        public virtual OperationSet Conversion
        {
            get => conversion;
            set => Ensure(ref conversion, value);
        }
        OperationSet conversion;

        /// <summary>
        /// True if Value is currently at or above MaxValue, or if it is positive and infinite, or cannot be calculated.
        /// </summary>
        public virtual bool OverRange =>
            double.IsNaN(Value) ||
            Value >= MaxValue ||
            Value == double.PositiveInfinity;

        /// <summary>
        /// True if Value is at or below MinValue or less than Sensitivity,
        /// or if it is negative and infinite, or cannot be calculated.
        /// </summary>
        public virtual bool UnderRange =>
            double.IsNaN(Value) ||
            Value <= MinValue ||
            Value == double.NegativeInfinity ||
            Sensitivity > 0 && Value <= Sensitivity;

        [JsonProperty]
        public virtual RateOfChange RateOfChange
        {
            get => rateOfChange;
            set => Ensure(ref rateOfChange, value);
        }
        RateOfChange rateOfChange;


        [JsonProperty]
        public virtual double Stable
        {
            get => stable;
            set => Ensure(ref stable, value);
        }
        double stable;

        public virtual bool IsStable => RateOfChange != null &&
            Math.Abs(RateOfChange) <= Stable;

        [JsonProperty]
        public virtual double Falling
        {
            get => falling;
            set => Ensure(ref falling, value);
        }
        double falling;

        public virtual bool IsFalling => RateOfChange != null &&
            RateOfChange <= Falling;

        [JsonProperty]
        public virtual double Rising
        {
            get => rising;
            set => Ensure(ref rising, value);
        }
        double rising;

        public virtual bool IsRising => RateOfChange != null &&
            RateOfChange >= Rising;

        // add additional, similar RateOfChange conditions for VTT and IP?

        /// <summary>
        /// Update Value with a new input, based on the configuration and present
        /// state of the Meter. The value may be digitally filtered, scaled,
        /// converted to different units, and normalized for sensitivity and
        /// resolution.
        /// </summary>
        /// <param name="value">a new input value</param>
        /// <returns>the resultant output Value</returns>
        public virtual double Update(double value)
        {
            ++UpdatesReceived;
            //if (ratiometric) value *= RatiometricCorrection;
            RawValue = value;

            if (Filter != null)
                value = Filter.Update(value);

            FilteredValue = value;

            if (Zeroing)
            {
                double toomuch = 10 * Resolution;   // Note: Resolution can be 0, and that's ok
                if (0 < toomuch && toomuch < Math.Abs(value - PriorValue))
                    Zeroing = false;    // abort the zeroing opertion; do not restart (zeroing conditions may no longer be valid)
                else
                {
                    zero += value;
                    zerosNeeded--;
                    if (zerosNeeded <= 0)
                    {
                        offset(zero / ZerosToAverage);  // update Conversion
                        Zeroing = false;        // NotifyPropertyChanged
                    }
                }
            }
            PriorValue = FilteredValue; // save for next Zeroing "toomuch" check

            ConvertedValue = value = Conversion?.Execute(value) ?? value;

            if (Resolution > 0)
            {
                if (ResolutionIsProportional)
                    value = Utility.Significant(value, significantDigits);
                else
                    value = Math.Round(value / Resolution) * Resolution;
            }

            ResolvedValue = value;

            if (Sensitivity > 0 && Math.Abs(value) < Sensitivity)
                value = 0;

            var wasStable = IsStable;
            var wasFalling = IsFalling;
            var wasRising = IsRising;
            RateOfChange?.Update(value);
            if (IsStable != wasStable) NotifyPropertyChanged(nameof(IsStable));
            if (IsFalling != wasFalling) NotifyPropertyChanged(nameof(IsFalling));
            if (IsRising != wasRising) NotifyPropertyChanged(nameof(IsRising));

            //if (Name == "ugCinMC" || Name == "tMC")
            //    MetersLog.Record($"{Name}: raw: {rawValue:0.00000000}, filtered: {filteredValue:0.00000000}, final: {finalValue:0.00000000}, roc: {RateOfChange?.Value:0.00000000}");

            Value = value;

            return Value;
        }

        /// <summary>
        /// Wait until Value has remained stable for the given number of seconds (default 5).
        /// </summary>
        /// <param name="seconds"></param>
        public virtual void WaitForStable(int seconds = 5)
        {
            Stopwatch sw = new Stopwatch();
            sw.Restart();
            while (!WaitFor(() => { if (!IsStable) sw.Restart(); return sw.Elapsed.TotalSeconds >= seconds; }, 15 * 60000, 50))
            {
                var subject = $"{Name}";
                var message = $"{Name} is taking a long time to stabilize.\r\n" +
                              $"Maybe its Filter is misconfigured?";

                Announce(message, subject, NoticeType.Warning);
            }
        }

        public void WaitSeconds(int seconds)
        {
            var now = DateTime.Now;
            while ((DateTime.Now - now).TotalSeconds < seconds)
                Thread.Sleep(50);
        }

        public double WaitForAverage(int seconds = 60)
        {
            var i = 1;
            var sum = Value;
            StepTracker.Default.Start($"Averaging {Name} over {seconds} seconds");
            void updateSum(object sender, PropertyChangedEventArgs e)
            {
                if (e.PropertyName == nameof(Value))
                {
                    ++i;
                    sum += Value;
                }
            };
            PropertyChanged += updateSum;
            WaitSeconds(seconds);
            PropertyChanged -= updateSum;
            StepTracker.Default.End();
            return sum / i;
        }


        #region Zeroing

        [JsonProperty, DefaultValue(300)]
        public int ZerosToAverage
        {
            get => zerosToAverage;
            set => Ensure(ref zerosToAverage, value);
        }
        int zerosToAverage;

        int zerosNeeded = 0;
        double zero;
        /// <summary>
        /// Value is currently being Zeroed (zero-offset compensation is being re-determined)
        /// </summary>
        public virtual bool Zeroing
        {
            get { return zerosNeeded > 0; }
            protected set
            {
                if (value)
                {
                    zero = 0;
                    zerosNeeded = ZerosToAverage;
                }
                else
                    zerosNeeded = 0;
                NotifyPropertyChanged();
            }
        }

        /// <summary>
        /// Start averaging the next &lt;ZerosToAverage&gt; readings, asynchronously. Returns immediately.
        /// (Later, once the required number of readings have been averaged, the meter's zero offset is
        /// updated automatically.)
        /// </summary>
        public virtual void ZeroNow() => ZeroNow(false);

        /// <summary>
        /// Reset the zero-offset value, based on the next &lt;ZerosToAverage&gt; readings.
        /// </summary>
        public virtual void ZeroNow(bool waitToFinish = false)
        {
            if (Conversion != null && !Zeroing)
            {
                Zeroing = true;
                if (waitToFinish)
                {
                    WaitFor(() => !Zeroing);
                    // Now, it's zeroed
                }
            }
        }

        void offset(double offset)
        {
            var firstOp = Conversion.Operations?.FirstOrDefault();
            while (firstOp is OperationSet os)
                firstOp = os?.Operations?.FirstOrDefault();

            bool insert = false;
            if (firstOp is Arithmetic firstArithmetic)
            {
                if (firstArithmetic.Operator == Arithmetic.Operators.Subtract)
                    firstArithmetic.Operand = offset;
                else if (firstArithmetic.Operator == Arithmetic.Operators.Add)
                    firstArithmetic.Operand = -offset;
                else
                    insert = true;
            }
            else
                insert = true;

            // TODO if offset is negative don't we need to do something else here?
            if (insert)
                Conversion.Operations.Insert(0, new Arithmetic("x-" + offset.ToString()));
        }

        #endregion Zeroing


        public Meter(IHacsDevice d = null) : base(d) { }

        public string DefaultFormat
        {
            get
            {
                var sb = new StringBuilder(Resolution.ToString());
                for (int i = 0; i < sb.Length; i++)
                    if (sb[i] != '.')
                        sb[i] = '0';
                if (ResolutionIsProportional)
                    sb.Append("e0");
                return sb.ToString();
            }
        }
        public virtual string ValueFormat
        {
            get => valueFormat.IsBlank() ? DefaultFormat : valueFormat;
            set => valueFormat = value;
        }
        string valueFormat;

        public override string ToString()
        {
            var sb = new StringBuilder($"{Name}: ");
            var format = ValueFormat;
            if (Sensitivity > 0 && Math.Abs(Value) <= Sensitivity)
                sb.Append("<" + Sensitivity.ToString(format));
            else
                sb.Append(Value.ToString(format));

            if (!UnitSymbol.IsBlank())
                sb.Append($" {UnitSymbol}");
            if (RateOfChange != null)
            {
                sb.Append(Utility.IndentLines("\r\n" + RateOfChange.Value.ToString(format)));
                if (!UnitSymbol.IsBlank())
                    sb.Append($" {UnitSymbol}/s");
                else
                    sb.Append($" units/s");
            }
            return sb.ToString();
        }
    }
}
