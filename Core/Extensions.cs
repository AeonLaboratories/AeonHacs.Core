using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace AeonHacs
{
    public static class IntExtensions
    {
        /// <summary>
        /// The least significant byte of an integer.
        /// </summary>
        /// <param name="i"></param>
        /// <returns></returns>
        public static byte Byte0(this int i) =>
            (byte)(i & 0xFF);

        /// <summary>
        /// The second-least significant byte of an integer. This is
        /// the most significant byte of a 16-bit value.
        /// </summary>
        /// <param name="i"></param>
        /// <returns></returns>
        public static byte Byte1(this int i) =>
            (byte)((i >> 8) & 0xFF);
    }

    public static class ByteExtensions
    {
        /// <summary>
        /// An eight character string representing the byte,
        /// e.g., "11001001" for the value 0xC9.
        /// </summary>
        /// <param name="b"></param>
        /// <returns></returns>
        public static string ToBinaryString(this byte b) =>
            Convert.ToString(b, 2).PadLeft(8, '0');
    }

    public static class BoolExtensions
    {
        /// <summary>
        /// Returns trueString if the bool is true, otherwise return falseString.
        /// </summary>
        public static string ToString(this bool value, string trueString, string falseString)
            => value ? trueString : falseString;

        /// <summary>
        /// The string "Yes" for true, "No" for false.
        /// </summary>
        public static string YesNo(this bool value) =>
            value.ToString("Yes", "No");

        /// <summary>
        /// The string "1" for true, "0" for false.
        /// </summary>
        public static string OneZero(this bool value) =>
            value.ToString("1", "0");

        /// <summary>
        /// The string "On" for true, "Off" for false.
        /// </summary>
        public static string OnOff(this bool value) =>
            value.ToString("On", "Off");

        /// <summary>
        /// OnOffState.On for true, OnOffState.Off for false.
        /// </summary>
        public static OnOffState ToOnOffState(this bool value) =>
            value ? OnOffState.On : OnOffState.Off;

        /// <summary>
        /// SwitchState.On for true, SwitchState.Off for false.
        /// </summary>
        public static SwitchState ToSwitchState(this bool value) =>
            value ? SwitchState.On : SwitchState.Off;
    }

    public static class DoubleExtensions
    {
        public static int ToInt(this double n) =>
            Convert.ToInt32(n);

        /// <summary>
        /// Whether x is a number (not NaN and not Infinity).
        /// </summary>
        public static bool IsANumber(this double x) => !(double.IsNaN(x) || double.IsInfinity(x));

        /// <summary>
        /// Whether x is NaN.
        /// </summary>
        public static bool IsNaN(this double x) => double.IsNaN(x);

    }

    public static class CharExtensions
    {
        public static bool IsVowel(this char c) => "aeiou".Contains(c);
    }

    public static partial class StringExtensions
    {
        /// <summary>
        /// Shorthand for string.IsNullOrWhiteSpace()
        /// </summary>
        /// <param name="s"></param>
        /// <returns></returns>
        public static bool IsBlank(this string s) =>
            string.IsNullOrWhiteSpace(s);

        public static bool Includes(this string s, string token) =>
            s?.Contains(token) ?? false;

        public static char[] LineDelimiters = { '\r', '\n' };

        public static string[] GetLines(this string s) =>
            s.Split(LineDelimiters, StringSplitOptions.RemoveEmptyEntries);

        public static string[] GetValues(this string s) =>
            s.Split(null as char[], StringSplitOptions.RemoveEmptyEntries);

        /// <summary>
        /// Produces a printable sequence of byte codes from
        /// a potentialy unprintable sequence of bytes. The result
        /// looks something like this: &quot;C3 02 54 4E&quot;...
        /// </summary>
        /// <param name="s"></param>
        /// <returns></returns>
        public static string ToByteString(this string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            StringBuilder sb = new StringBuilder();
            foreach (byte b in s)
                sb.Append($"{b:X2} ");
            sb.Remove(sb.Length - 1, 1);
            return sb.ToString();
        }

        public static string Plurality(this string singular, double n) =>
            n == 1 ? singular : singular.Plural();

        public static string Plural(this string singular)
        {
            if (string.IsNullOrEmpty(singular)) return string.Empty;
            singular.TrimEnd();
            if (string.IsNullOrEmpty(singular)) return string.Empty;

            int slen = singular.Length;
            char ultimate = singular[slen - 1];
            if (slen == 1)
            {
                if (char.IsUpper(ultimate)) return singular + "s";
                return singular + "'s";
            }
            ultimate = char.ToLower(ultimate);
            char penultimate = char.ToLower(singular[slen - 2]);

            if (ultimate == 'y')
            {
                if (penultimate.IsVowel()) return singular + "s";
                return singular.Substring(0, slen - 1) + "ies";
            }
            if (ultimate == 'f')
                return singular.Substring(0, slen - 1) + "ves";
            if (penultimate == 'f' && ultimate == 'e')
                return singular.Substring(0, slen - 2) + "ves";
            if ((penultimate == 'c' && ultimate == 'h') ||
                (penultimate == 's' && ultimate == 'h') ||
                (penultimate == 's' && ultimate == 's') ||
                (ultimate == 'x') ||
                (ultimate == 'o' && !penultimate.IsVowel()))
                return singular + "es";
            return singular + "s";
        }
    }
        
    public static class ArrayExtensions
    {
        public static Array RemoveAt(this Array source, int index)
        {
            Array dest = Array.CreateInstance(source.GetType().GetElementType(), source.Length - 1);

            if (index > 0)
                Array.Copy(source, 0, dest, 0, index);

            if (index < source.Length - 1)
                Array.Copy(source, index + 1, dest, index, source.Length - index - 1);

            return dest;
        }

        /// <summary>
        /// Convert the string into an ASCII8 byte array.
        /// </summary>
        /// <param name="s"></param>
        /// <returns></returns>
        public static byte[] ToASCII8ByteArray(this string s) =>
            EncodingType.ASCII8.GetBytes(s);

        /// <summary>
        /// Should be called ToString but extensions can't override base methods.
        /// </summary>
        /// <param name="byteArray"></param>
        /// <returns></returns>
        public static string ToStringToo(this byte[] byteArray) =>
            ToString(byteArray, 0, byteArray?.Length ?? 0);

        /// <summary>
        /// Converts a range of a byte array into a string.
        /// </summary>
        /// <param name="byteArray"></param>
        /// <param name="startIndex"></param>
        /// <param name="length"></param>
        /// <returns></returns>
        public static string ToString(this byte[] byteArray, int startIndex, int length)
        {
            if (byteArray == null || startIndex < 0 || length < 1 || startIndex >= byteArray.Length)
                return "";

            return EncodingType.ASCII8.GetString(byteArray, startIndex, length);
        }
    }

    public static class ListExtensions
    {
        public static List<string> Names<T>(this List<T> source) where T : INamedObject =>
            source?.Select(x => x?.Name)?.ToList();

        /// <summary>
        /// Treats null values as empty sets, to avoid ArgumentNullException.
        /// </summary>
        public static List<T> SafeUnion<T>(this IEnumerable<T> a, IEnumerable<T> b)
        {
            IEnumerable<T> s;
            if (a == null)
                s = b;
            else if (b == null)
                s = a;
            else
                s = a.Union(b);
            return s?.ToList();
        }

        /// <summary>
        /// Treats null values as empty sets, to avoid ArgumentNullException.
        /// </summary>
        public static List<T> SafeIntersect<T>(this IEnumerable<T> a, IEnumerable<T> b)
        {
            if (a == null || b == null) return null;
            return a.Intersect(b).ToList();
        }

        /// <summary>
        /// Treats null values as empty sets, to avoid ArgumentNullException.
        /// </summary>
        public static List<T> SafeExcept<T>(this IEnumerable<T> fromThese, IEnumerable<T> subtractThese)
        {
            if (fromThese == null) return null;

            IEnumerable<T> s;
            if (subtractThese == null)
                s = fromThese;
            else
                s = fromThese.Except(subtractThese);
            return s.ToList();
        }
    }

    public static partial class DictionaryExtensions
    {
        public static Dictionary<string, string> KeysNames<T>(this Dictionary<string, T> source) where T : INamedObject =>
            source?.ToDictionary(x => x.Key, x => x.Value.Name);
            
        /// <summary>
        /// Remove the first entry that contains the given value from the Dictionary
        /// </summary>
        public static bool RemoveValue<TKey, TValue>(this Dictionary<TKey, TValue> source, TValue value)
        {
            if (source.FirstOrDefault(x => x.Value.Equals(value)) is KeyValuePair<TKey, TValue> deleteMe)
                return source.Remove(deleteMe.Key);
            return false;
        }
    }

    public static class TypeExtensions
    {
        public static MemberInfo Default(this Type t) =>
            t?.GetMember("Default", MemberTypes.Field | MemberTypes.Property, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)?.FirstOrDefault();

        public static MemberInfo GetInstanceMember(this Type t, string propertyName) =>
            t?.GetMember(propertyName, MemberTypes.Field | MemberTypes.Property, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.FirstOrDefault();

        public static bool IsAtomic(this Type type) =>
            type.IsValueType || type == typeof(string);
            
    }

    public class EncodingType
    {
        /// <summary>
        /// ASCII uses only 7 bits; use this 8-bit "extended ASCII" encoding
        /// </summary>
        public static Encoding ASCII8 = Encoding.GetEncoding("iso-8859-1");
    }    

    public static class MemberInfoExtensions
    {
        public static void SetValue(this MemberInfo member, object property, object value)
        {
            if (member.MemberType == MemberTypes.Property)
                ((PropertyInfo)member).SetValue(property, value);
            else if (member.MemberType == MemberTypes.Field)
                ((FieldInfo)member).SetValue(property, value);
            else
                throw new Exception("Property must be of type FieldInfo or PropertyInfo");
        }

        public static object GetValue(this MemberInfo member, object property)
        {
            if (member.MemberType == MemberTypes.Property)
                return ((PropertyInfo)member).GetValue(property);
            else if (member.MemberType == MemberTypes.Field)
                return ((FieldInfo)member).GetValue(property);
            else
                throw new Exception("Property must be of type FieldInfo or PropertyInfo");
        }
    }
    
    /// <summary>
    /// Extension methods for PropertyInfo objects
    /// </summary>
    public static class PropertyInfoExtensions
    {
        public static bool JsonProperty(this PropertyInfo pi)
        {
            return pi.GetCustomAttributes(typeof(JsonPropertyAttribute), true).Length > 0;
        }

        public static bool IsSettable(this PropertyInfo pi)
        {
            if (pi == null || !pi.CanRead || !pi.CanWrite)
                return false;

            Type pType = pi.PropertyType;
            if (!(pType.IsPublic || pType.IsNestedPublic))
                return false;

            object[] CustomAttributes = pi.GetCustomAttributes(false);
            foreach (object attribute in CustomAttributes)
            {
                if (attribute is XmlIgnoreAttribute ||
                    attribute is XmlAttributeAttribute && pi.Name.Equals("Name"))
                    return false;
            }

            return true;
        }
    }

    public static class OnOffStateExtensions
    {
        public static bool IsOn(this OnOffState s) => s == OnOffState.On;
        public static bool IsOff(this OnOffState s) => s == OnOffState.Off;
        public static bool IsUnknown(this OnOffState s) => s == OnOffState.Unknown;
    }

    public static class SwitchStateExtensions
    {
        public static bool IsOn(this SwitchState s) => s == SwitchState.On;
        public static bool IsOff(this SwitchState s) => s == SwitchState.Off;
    }


    public static class ActionExtensions
    {
        public static void ParallelInvoke(this Action action)
        {
            var actions = action.GetInvocationList().Cast<Action>().ToArray();
            Parallel.Invoke(actions);
        }
    }

    public static class ColorExtensions
    {
        public static Color Blend(this Color color1, Color color2, double percent)
        {
            if (percent <= 0)
                return color1;
            else if (percent >= 1)
                return color2;

            double p1 = 1 - percent;

            int r1 = (int)(color1.R * p1);
            int g1 = (int)(color1.G * p1);
            int b1 = (int)(color1.B * p1);
            int r2 = (int)(color2.R * percent);
            int g2 = (int)(color2.G * percent);
            int b2 = (int)(color2.B * percent);

            return Color.FromArgb(r1 + r2, g1 + g2, b1 + b2);
        }
    }
    
}