using Newtonsoft.Json;
using System;
using System.ComponentModel;
using System.Linq;
using System.Linq.Expressions;
using System.Text;

namespace AeonHacs.Components
{
    public class DataLog : HacsLog, IDataLog
    {
        #region static

        public static bool DefaultChanged(Column col) =>
            col.Resolution < 0 ?
                false :
            col.PriorValue is double p && col.Value is double v ?
                Math.Abs(v - p) >= col.Resolution :
            true;

        [JsonObject(MemberSerialization.OptIn)]
        public class Column : BindableObject
        {
            string name;
            /// <summary>
            /// The Column name; normally the name of a NamedObject that implments IValue
            /// </summary>
            [JsonProperty]
            public string Name { get => name; set => Ensure(ref name, value); }

            double resolution;
            /// <summary>
            /// When the magnitude of the difference between the current value and
            /// the prior recorded value exceeds Resolution, a new log entry is recorded.
            /// If Resolution is less than zero, this column will never trigger a log entry.
            /// </summary>
            [JsonProperty]
            public double Resolution { get => resolution; set => Ensure(ref resolution, value); }

            string format;
            /// <summary>
            /// The Column data is formatted according to this string.
            /// </summary>
            [JsonProperty]
            public string Format { get => format; set => Ensure(ref format, value); }

            /// <summary>
            /// The NamedObject that provides the Column's data.
            /// </summary>
            public NamedObject Source { get; set; }

            /// <summary>
            /// A reference to a function that returns the desired value from the Column's Source.
            /// </summary>
            public Func<double> Function { get; set; }

            /// <summary>
            /// A string containing the expression (&quot;source code&quot;) to be compiled into a Function that returns the Column's value.
            /// </summary>
            [JsonProperty("Path")]        // TODO: is there a better name?
            public string Expression { get; set; }

            /// <summary>
            /// Compiles the Source expression string into a function that returns a double.
            /// </summary>
            public Func<double> CompileSourceExpression(string expression)
            {
                if (expression.IndexOf('.') == -1)
                    expression += ".Value";

                var tokens = expression.Split('.');
                Source = Find<NamedObject>(tokens[0]);
                Expression expr = System.Linq.Expressions.Expression.Constant(Source);

                foreach (var token in tokens.Skip(1))
                {
                    expr = System.Linq.Expressions.Expression.PropertyOrField(expr, token);
                }

                return System.Linq.Expressions.Expression.Lambda<Func<double>>(expr).Compile();
            }


            double? priorValue;
            /// <summary>
            /// The most recent data value recorded for this Column
            /// </summary>
            public double? PriorValue { get => priorValue; set => Ensure(ref priorValue, value); }

            /// <summary>
            /// The current value of the Source object.
            /// </summary>
            public double? Value => Function?.Invoke();

            public override string ToString() => Name ?? "Column";
        }

        #endregion static

        #region HacsComponent
        [HacsConnect]
        protected virtual void Connect()
        {
            SetSources();
            SetHeader();
        }
        #endregion HacsComponent

        [JsonIgnore]
        public override string Header { get => base.Header; set => base.Header = value; }

        bool insertLatestSkippedEntry;
        /// <summary>
        /// If true, the most recently skipped log entry is inserted when a new log entry is recorded.
        /// </summary>
        [JsonProperty, DefaultValue(false)]
        public virtual bool InsertLatestSkippedEntry
        {
            get => insertLatestSkippedEntry;
            set => Ensure(ref insertLatestSkippedEntry, value);
        }

        [JsonProperty]
        public virtual ObservableList<Column> Columns
        {
            get => columns;
            set => Ensure(ref columns, value, OnColumnsChanged);
        }
        ObservableList<Column> columns;
        protected virtual void OnColumnsChanged(object sender = null, PropertyChangedEventArgs e = null)
        {
            if (sender == Columns && Connected)
                Connect();      // re-connect
        }

        void SetSources()
        {
            foreach (var col in Columns)
            {
                // Compile the Column's expression if provided
                col.Function = col.CompileSourceExpression(col.Expression.IsBlank() ? col.Name : col.Expression);
            }
        }

        void SetHeader()
        {
            if (columns != null && columns.Count > 0)
            {
                var sb = new StringBuilder(columns[0].Name);
                for (int i = 1; i < columns.Count; ++i)
                    sb.Append("\t" + columns[i].Name);
                Header = sb.ToString();
            }
        }

        // Constructor for DataLog class
        public DataLog(string fileName, bool archiveDaily = true) : base(fileName, archiveDaily)
        {
            Update = Report;
        }

        /// <summary>
        /// Compiled expression for Changed, evaluated at runtime.
        /// </summary>
        public virtual Func<Column, bool> Changed { get; set; } = DefaultChanged;

        /// <summary>
        /// Determines if any column has changed according to the Changed function.
        /// </summary>
        bool AnyChanged() => Columns.Any(c => Changed(c));

        double[] currentValues;
        /// <summary>
        /// Retrieves the current value for a column at the given index.
        /// </summary>
        string value(int index)
        {
            var col = Columns[index];
            var v = col.Value ?? double.NaN;
            currentValues[index] = v;
            return v.ToString(col.Format);
        }

        long changeTimeoutMilliseconds = 30000;
        /// <summary>
        /// The timeout in milliseconds after which a new log entry is recorded even if no column has changed.
        /// </summary>
        [JsonProperty, DefaultValue(30000)]
        public virtual long ChangeTimeoutMilliseconds
        {
            get => changeTimeoutMilliseconds;
            set => Ensure(ref changeTimeoutMilliseconds, value);
        }

        bool onlyLogWhenChanged = false;
        /// <summary>
        /// If true, logs are only recorded when a column has changed.
        /// </summary>
        [JsonProperty, DefaultValue(false)]
        public virtual bool OnlyLogWhenChanged
        {
            get => onlyLogWhenChanged;
            set => Ensure(ref onlyLogWhenChanged, value);
        }

        StringBuilder entryBuilder = new StringBuilder();
        /// <summary>
        /// Generates a log entry string from the current column values.
        /// </summary>
        string GenerateEntry()
        {
            entryBuilder.Clear();
            currentValues = new double[Columns.Count];
            entryBuilder.Append(value(0));
            for (int i = 1; i < Columns.Count; ++i)
                entryBuilder.Append("\t" + value(i));
            return entryBuilder.ToString();
        }

        string skippedTimeStamp;
        string skippedEntry = "";
        /// <summary>
        /// Skips a log entry by storing it for potential later insertion.
        /// </summary>
        void Skip(string entry)
        {
            skippedTimeStamp = TimeStamp();
            skippedEntry = entry;
        }

        /// <summary>
        /// Writes the most recently skipped log entry to the log.
        /// </summary>
        void WriteSkippedEntry()
        {
            WriteLine(skippedTimeStamp + skippedEntry);
            skippedEntry = "";
        }

        /// <summary>
        /// Writes a log entry, optionally including the most recently skipped entry.
        /// </summary>
        void WriteLog(string entry)
        {
            if (OnlyLogWhenChanged)
                LogParsimoniously(entry);
            else
            {
                if (InsertLatestSkippedEntry && !skippedEntry.IsBlank() && skippedEntry != entry)
                    WriteSkippedEntry();
                Record(entry);
            }
            for (int i = 0; i < Columns.Count; ++i)
                Columns[i].PriorValue = currentValues[i];
        }

        /// <summary>
        /// Generates a log report, writing the current values if any column has changed or the timeout has been reached.
        /// </summary>
        protected virtual void Report()
        {
            if (Columns == null || Columns.Count == 0) return;

            var entry = GenerateEntry();
            if (AnyChanged() || ElapsedMilliseconds >= ChangeTimeoutMilliseconds)
                WriteLog(entry);
            else
                Skip(entry);
        }

        public override string ToString() => Name ?? "DataLog";
    }
}