using System;

namespace AeonHacs
{
    public class NamedValue : NamedObject, INamedValue
    {
        #region static

        public static implicit operator double(NamedValue x)
        { return x?.Value ?? double.NaN; }

        public static IValue Find(string name) =>
            (IValue)FirstOrDefault<NamedObject>(x => x.Name == name && x is IValue);

        public static double Get(string name) => Find(name)?.Value ?? double.NaN;

        #endregion static

        public double Value => GetValue();
        public Func<double> GetValue { get; set; }
        public NamedValue(string name, Func<double> getValue)
        {
            Name = name;
            GetValue = getValue;
        }
    }
}
