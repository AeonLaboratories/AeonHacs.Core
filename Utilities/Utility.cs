using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Numerics;
using System.Collections;
using System.Linq;
using System.Threading;
using System.ComponentModel.DataAnnotations;

namespace AeonHacs.Utilities
{
    // TODO: move as many as possible of these to extension classes
    public class Utility
    {
        public static string[] lineTerminators = new string[] { "\r\n", "\n" };

        public static string[] SplitIntoLines(string s)
        { return s.Split(lineTerminators, StringSplitOptions.None); }

        // conversions for MSB-first 2-byte sequences, often encountered in serial communications
        public static int toInt(string s, int msbIndex) => toInt(s[msbIndex], s[msbIndex + 1]);
        public static int toInt(string s, int msbIndex, int lsbIndex) => toInt(s[msbIndex], s[lsbIndex]);
        public static int toInt(char[] msbLsb) => toInt(msbLsb[0], msbLsb[1]);
        public static int toInt(char msb, char lsb) => (short)((msb << 8) | lsb);
        public static char[] MSBLSB(int i) => new char[] { MSB(i), LSB(i) };
        public static char LSB(int i) => (char)(i & 0xFF);
        public static char MSB(int i) => (char)((i >> 8) & 0xFF);

        /// <summary>
        /// Returns a string like "5.2 minutes" or "1 second".
        /// </summary>
        /// <param name="howmany"></param>
        /// <param name="singularUnit"></param>
        /// <returns></returns>
        public static string ToUnitsString(double howmany, string singularUnit) =>
             $"{howmany} {singularUnit.Plurality(howmany)}";

        public static string MinutesString(int minutes) =>
            ToUnitsString(minutes, "minute");
        public static string SecondsString(int seconds) =>
            ToUnitsString(seconds, "second");

        public static string IndentLines(string text, string indent)
        {
            string[] lines = SplitIntoLines(text);
            int last = lines.Length - 1;
            if (last < 0) return "";
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < last; i++)
            {
                sb.Append(indent);
                sb.Append(lines[i]);
                sb.Append("\r\n");
            }
            sb.Append(indent);
            sb.Append(lines[last]);
            return sb.ToString();
        }

        public static string IndentLines(string text)
        { return IndentLines(text, "   "); }

        public static string ToStringLine(string name, object value)
        {
            StringBuilder sb = new StringBuilder(name);
            sb.Append(": ");
            sb.Append(value.ToString());
            sb.Append("\r\n");
            return sb.ToString();
        }

        public static string ToStringLine(string name, bool value)
        { return ToStringLine(name, value ? "Yes" : "No"); }


        /// <summary>
        /// Rounds a number n rounded to s significant digits.
        /// </summary>
        /// <param name="n">The number to round</param>
        /// <param name="s">Significant digits to keep</param>
        /// <returns></returns>
        public static double Significant(double n, int s)
        {
            if (n == 0) return 0;
            double magnitude = Math.Pow(10, s - PowerOfTenCeiling(n));
            return Math.Round(n * magnitude) / magnitude;
        }

        /// <summary>
        /// Returns the ceiling of the base 10 logarithm of the absolute value of n, or 0 if n = 0.
        /// It's a useful indication of "order of magnitude".
        /// </summary>
        /// <param name="n"></param>
        /// <returns></returns>
        public static int PowerOfTenCeiling(double n)
        {
            if (n == 0) return 0;
            if (n < 0) n = -n;
            return (int)Math.Ceiling(Math.Log10(n));
        }

        // Returns a format string for representing a number n rounded to s significant digits.
        public static string sigDigitsString(double n, int s)
        {
            string pformat = "0." + new string('0', s - 1);
            string n1 = n.ToString(pformat + "e+00");
            int e = int.Parse(n1.Substring(n1.Length - 3)); // get the exponent

            if (e >= Math.Max(s, 3) || e <= -2)
                return n.ToString(pformat + "e-0");
            else
            {
                pformat += "0"; // extra zero in case needed left of decimal point
                int len = 1 + s - e; if (len == 2) len = 1;
                return n.ToString(pformat.Substring(0, len));
            }
        }

        //calculates the binomial coefficient "n choose k"
        public static double binomCoeff(int n, int k)
        {
            if (n < 0 || k < 0) return 0;
            if (k > n) return 0;
            if (k > n / 2) k = n - k;
            double r = 1;
            for (int d = 1; d <= k; d++)
            {
                r *= n--;
                r /= d;
            }
            return r;
        }


        /// <summary>
        /// Waits a specified amount of time while optionally providing a StepTracker description of the wait operation.
        /// </summary>
        /// <param name="milliseconds">How long to wait, in milliseconds.</param>
        /// <param name="description">
        /// An optional description for the wait operation. If explicitly set to <c>null</c>, no StepTracker description is provided.
        /// If empty (the default), a generic description is created based on the amount of time.
        /// </param>

        public static void WaitMilliseconds(int milliseconds, string description = "")
        {
            if (description == "")
            {
                if (milliseconds >= 60000)
                    description = $"Wait {MinutesString(milliseconds / 60000)}.";
                else if (milliseconds >= 1000)
                    description = $"Wait {SecondsString(milliseconds / 1000)}.";
                else
                    description = $"Wait {ToUnitsString(milliseconds, "millisecond")}.";
            }

            if (description != null) StepTracker.Default.Start(description);

            Stopwatch sw = new Stopwatch();
            sw.Start();
            WaitFor(() => Hacs.Stopping || sw.Elapsed.TotalMilliseconds >= milliseconds);

            if (description != null) StepTracker.Default.End();
        }

        /// <summary>
        /// Waits a specified amount of time while optionally providing a StepTracker description of the wait operation.
        /// </summary>
        /// <param name="seconds">How long to wait, in seconds.</param>
        /// <param name="description">
        /// An optional description for the wait operation. If explicitly set to <c>null</c>, no StepTracker description is provided.
        /// If empty (the default), a generic description is created based on the amount of time.
        /// </param>
        public static void WaitSeconds(int seconds, string description = "") =>
            WaitMilliseconds(seconds * 1000, description);

        /// <summary>
        /// Waits a specified amount of time while optionally providing a StepTracker description of the wait operation.
        /// </summary>
        /// <param name="minutes">How long to wait, in minutes.</param>
        /// <param name="description">
        /// An optional description for the wait operation. If explicitly set to <c>null</c>, no StepTracker description is provided.
        /// If empty (the default), a generic description is created based on the amount of time.
        /// </param>
        public static void WaitMinutes(int minutes, string description = "") =>
            WaitMilliseconds(minutes * 60000, description);

        /// <summary>
        /// Wait until StepTracker.DefaultMajor.Elapsed has reached the specified number of minutes.
        /// </summary>
        /// <param name="minutes"></param>
        public static void WaitRemaining(int minutes)
        {
            int milliseconds = minutes * 60000 - (int)StepTracker.DefaultMajor.Elapsed.TotalMilliseconds;
            if (milliseconds > 0)
                WaitMilliseconds(milliseconds, $"Wait for remainder of {MinutesString(minutes)}.");
        }

        /// <summary>
        /// Waits a potentially limited time for a condition to be true.
        /// The default timeout value of -1 is infinite.
        /// The condition is tested every &lt;interval&gt; milliseconds.
        /// </summary>
        /// <param name="checkCondition">what to test</param>
        /// <param name="timeout">total milliseconds to wait before giving up</param>
        /// <param name="interval">milliseconds between tests</param>
        /// <returns>true if the condition is met, false if it timed out</returns>
        public static bool WaitFor(Func<bool> checkCondition, int timeout = -1, int interval = 20)
        {
            var startTime = DateTime.Now;
            bool conditionMet;
            while (!(conditionMet = checkCondition()) && (timeout < 0 || (DateTime.Now - startTime).TotalMilliseconds < timeout))
                Thread.Sleep(interval);
            return conditionMet;
        }


        public static string xmlAttribute(string attr, string value)
        {
            var sb = new StringBuilder(" ");
            sb.Append(attr);
            sb.Append("=\"");
            sb.Append(value);
            sb.Append("\"");
            return sb.ToString();
        }

        public static string xmlTag(string element, string value, List<string> attributes)
        {
            var sb = new StringBuilder();
            sb.Append("<"); sb.Append(element);
            if (attributes != null)
                foreach (string s in attributes)
                    sb.Append(s);
            if (string.IsNullOrWhiteSpace(value))
                sb.Append(" />");
            else
            {
                sb.Append(">");

                // *hack* *cough*
                // Microsoft's implementation of ToString() for booleans
                // outputs "True" or "False", but XmlSerialization
                // is case-sensitive and requires either "true" or "false"
                if (value == "False") value = "false";
                else if (value == "True") value = "true";

                sb.Append(value);
                sb.Append("</"); sb.Append(element); sb.Append(">");
            }
            sb.Append("\r\n");
            return sb.ToString();
        }

        public static string xmlTag(string element, string value)
        {
            return xmlTag(element, value, "");
        }

        public static string xmlTag(string element, string value, string attribute)
        {
            var attributes = new List<string>();
            if (!string.IsNullOrWhiteSpace(attribute))
                attributes.Add(attribute);
            return xmlTag(element, value, attributes);
        }

        public static double[] Multiply(double[] a, double k)
        { for (int i = 0; i < a.Length; ++i) a[i] = k * a[i]; return a; }

        public static double[] Negate(double[] a) { Multiply(a, -1); return a; }

        // evaluate polynomial in x
        public static double EvaluatePolynomial(double[] coeffs, double x)
        {
            int i = coeffs.Length;
            double sum = coeffs[--i];
            while (i > 0)
                sum = x * sum + coeffs[--i];
            return sum;
        }

        // evaluate polynomial in z
        public static Complex EvaluatePolynomial(Complex[] coeffs, Complex z)
        {
            int i = coeffs.Length;
            Complex sum = coeffs[--i];
            while (i > 0)
                sum = z * sum + coeffs[--i];
            return sum;
        }

        public static bool IsList(object o)
        {
            return typeof(IList).IsAssignableFrom(o.GetType());
        }
    }

    public struct ObjectPair
    {
        public object x;
        public object y;

        public ObjectPair(object x, object y)
        {
            this.x = x;
            this.y = y;
        }
    }

    public enum Services { MessageBox, TitledMessageBox, PlaySound }

    public class Request : EventArgs
    {
        public Services Service;
        public object Args;

        public Request(Services service, object args)
        {
            Service = service;
            Args = args;
        }
    }

    public class LookupTable
    {
        public string filename;

        public double[] key;
        public double[] value;
        public int count { get; set; }
        public bool outOfRange { get; set; }

        public LookupTable(string filename)
        { load(filename); }

        void load(string filename)
        {
            StreamReader fin;
            try { fin = new StreamReader(filename); }
            catch (Exception e)
            {
                string message = e.ToString();

                Notify.Announce(message, type: NoticeType.Error);
                throw new Exception("Couldn't open '" + filename + "'");
            }
            this.filename = filename;

            int nLines = 0;
            string line = fin.ReadLine();
            while (line != null)
            {
                ++nLines;
                line = fin.ReadLine();
            }

            count = nLines;
            key = new double[count];
            value = new double[count];

            fin.DiscardBufferedData();
            fin.BaseStream.Seek(0, SeekOrigin.Begin);

            char[] delimiters = { '\t' };
            string[] token;
            int i = 0;
            line = fin.ReadLine();
            while (line != null)
            {
                token = line.Split(delimiters);
                key[i] = double.Parse(token[0]);
                value[i] = double.Parse(token[1]);
                ++i;
                line = fin.ReadLine();
            }
            fin.Close();
        }

        // only works for monotonically increasing functions
        public double Interpolate(double d) =>
            Interpolate(d, key, value);

        // only works for monotonically increasing functions
        public double ReverseInterpolate(double d) =>
            Interpolate(d, value, key);

        public double Interpolate(double d, double[] key, double[] value)
        {
            if (d < key[0] || d > key[count - 1])
            {
                outOfRange = true;
                return d;
            }
            outOfRange = false;
            int i = Array.BinarySearch(key, d);
            if (i >= 0) return value[i];
            i = ~i;            // index of lowest key that is greater than d
            var h = i - 1;  // i's predecessor
            var m = (value[i] - value[h]) / (key[i] - key[h]);
            var x = d - key[h];
            var b = value[h];
            return m * x + b;
        }
    }
}