using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AeonHacs;

public static class MoreEncoding
{
    public static readonly Encoding ASCII8 = Encoding.GetEncoding("iso-8859-1");
}

public static class Extensions
{
    extension(int i)
    {
        /// <summary>
        /// The least significant byte of an integer.
        /// </summary>
        public byte Byte0() =>
            (byte)(i & 0xFF);

        /// <summary>
        /// The second-least significant byte of an integer. This is
        /// the most significant byte of a 16-bit value.
        /// </summary>
        public byte Byte1() =>
            (byte)((i >> 8) & 0xFF);
    }

    extension(byte b)
    {
        /// <summary>
        /// An eight character string representing the byte,
        /// e.g., "11001001" for the value 0xC9.
        /// </summary>
        public string ToBinaryString() =>
            Convert.ToString(b, 2).PadLeft(8, '0');
    }

    extension(bool b)
    {
        /// <summary>
        /// Returns trueString if the bool is true, otherwise return falseString.
        /// </summary>
        public string ToString(string trueString, string falseString) =>
            b ? trueString : falseString;

        /// <summary>
        /// The string "Yes" for true, "No" for false.
        /// </summary>
        public string YesNo() =>
            b.ToString("Yes", "No");

        /// <summary>
        /// The string "1" for true, "0" for false.
        /// </summary>
        public string OneZero() =>
            b.ToString("1", "0");

        /// <summary>
        /// The string "On" for true, "Off" for false.
        /// </summary>
        public string OnOff() =>
            b.ToString("On", "Off");

        /// <summary>
        /// OnOffState.On for true, OnOffState.Off for false.
        /// </summary>
        public OnOffState ToOnOffState() =>
            b ? OnOffState.On : OnOffState.Off;

        /// <summary>
        /// SwitchState.On for true, SwitchState.Off for false.
        /// </summary>
        public SwitchState ToSwitchState() =>
            b ? SwitchState.On : SwitchState.Off;
    }

    // TODO: Do these really make the code better?
    extension(double d)
    {
        // TODO: Why not cast?
        public int ToInt() =>
            Convert.ToInt32(d);

        /// <summary>
        /// Whether x is a number (not NaN and not Infinity).
        /// </summary>
        public bool IsANumber() => double.IsFinite(d);

        /// <summary>
        /// Whether x is NaN.
        /// </summary>
        public bool IsNaN() => double.IsNaN(d);
    }

    extension(char c)
    {
        // TODO: Specify StringComparison
        public bool IsVowel() => "aeiou".Contains(c);
    }

    extension(Encoding)
    {
        /// <summary>
        /// ASCII uses only 7 bits; use this 8-bit "extended ASCII" encoding
        /// </summary>
        public static Encoding ASCII8 =>
            MoreEncoding.ASCII8;
    }

    extension(string str)
    {
        /// <summary>
        /// Shorthand for string.IsNullOrWhiteSpace()
        /// </summary>
        public bool IsBlank() =>
            string.IsNullOrWhiteSpace(str);

        /// <summary>
        /// Null safe version of <see cref="string.Contains(string)"/>
        /// </summary>
        /// <param name="token"></param>
        public bool Includes(string token) =>
            str?.Contains(token) ?? false;

        // TODO: Would it be better to change to
        // str.ReplaceLineEndings().Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        public string[] GetLines() =>
            str.ReplaceLineEndings().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        public string[] GetValues() =>
            str.Split(null as char[], StringSplitOptions.RemoveEmptyEntries);

        // TODO: Is this the correct encoding?
        /// <summary>
        /// Produces a printable sequence of byte codes from
        /// a potentialy unprintable sequence of bytes. The result
        /// looks something like this: &quot;C3 02 54 4E&quot;...
        /// </summary>
        public string ToByteString() =>
            BitConverter.ToString(Encoding.Default.GetBytes(str)).Replace('-', ' ');

        public string Plural()
        {
            if (string.IsNullOrWhiteSpace(str)) return string.Empty;
            str = str.TrimEnd();

            //int slen = str.Length;
            char ultimate = str[^1];
            if (str.Length == 1)
            {
                if (char.IsUpper(ultimate)) return str + "s";
                return str + "'s";
            }
            ultimate = char.ToLower(ultimate);
            char penultimate = char.ToLower(str[^2]);

            if (ultimate == 'y')
            {
                if (penultimate.IsVowel()) return str + "s";
                return str[..^1] + "ies";
            }
            if (ultimate == 'f')
                return str[..^1] + "ves";
            if (penultimate == 'f' && ultimate == 'e')
                return str[..^2] + "ves";
            if ((penultimate == 'c' && ultimate == 'h') ||
                (penultimate == 's' && ultimate == 'h') ||
                (penultimate == 's' && ultimate == 's') ||
                (ultimate == 'x') ||
                (ultimate == 'o' && !penultimate.IsVowel()))
                return str + "es";
            return str + "s";
        }

        // TODO: Should this instead be a extension to int/double
        // that takes the singular string as an argument and replacing
        // Utility.ToUnitsString?
        public string Plurality(double n) =>
            n == 1 ? str : str.Plural();

        /// <summary>
        /// Convert the string into an ASCII8 byte array.
        /// </summary>
        public byte[] ToASCII8ByteArray() =>
            Encoding.ASCII8.GetBytes(str);
    }

    extension(byte[] bytes)
    {
        // TODO: Rename
        /// <summary>
        /// Should be called ToString but extensions can't override base methods.
        /// </summary>
        public string ToStringToo() =>
            bytes.ToString(0, bytes?.Length ?? 0);

        /// <summary>
        /// Converts a range of a byte array into a string.
        /// </summary>
        /// <param name="startIndex"></param>
        /// <param name="length"></param>
        /// <returns></returns>
        public string ToString(int startIndex, int length)
        {
            if (bytes == null || startIndex < 0 || length < 1 || startIndex >= bytes.Length)
                return "";

            return Encoding.ASCII8.GetString(bytes, startIndex, length);
        }
    }

    // TODO: Switch to IEnumerable.
    extension<T>(List<T> source) where T : INamedObject
    {
        public List<string> Names() =>
            source?.Select(x => x?.Name).ToList();
    }

    extension<T>(IEnumerable<T> source)
    {
        public List<T> SafeUnion(IEnumerable<T> second)
        {
            if (source is null)
                return second?.ToList();
            if (second is null)
                return [.. source];
            return [.. source.Union(second)];
        }

        public List<T> SafeIntersect(IEnumerable<T> second)
        {
            if (source is null || second is null)
                return null;
            return [.. source.Intersect(second)];
        }

        public List<T> SafeExcept(IEnumerable<T> second)
        {
            if (source is null)
                return null;
            if (second is null)
                return [.. source];
            return [.. source.Except(second)];
        }
    }

    extension(IEnumerable<Task<Notice>> tasks)
    {
        public async Task<Notice> FirstResponse(Predicate<Notice> condition = null)
        {
            if (tasks is null || !tasks.Any())
                return Notice.NoResponse;

            condition ??= _ => true;
            var tasklist = tasks.ToList();
            while (tasklist.Count > 0)
            {
                var task = await Task.WhenAny(tasklist);
                var result = await task;
                if (!result.Equals(Notice.NoResponse) && condition(result))
                    return result;
                tasklist.Remove(task);
            }

            return Notice.NoResponse;
        }
    }

    extension<T>(Dictionary<string, T> source) where T : INamedObject
    {
        public Dictionary<string, string> KeysNames() =>
            source?.ToDictionary(x => x.Key, x => x.Value.Name);
    }

    extension(Type type)
    {
        public bool IsAtomic() =>
            type.IsValueType || type == typeof(string);
    }

    extension(OnOffState state)
    {
        public bool IsOn() =>
            state == OnOffState.On;

        public bool IsOff() =>
            state == OnOffState.Off;

        public bool IsUnknown() =>
            state == OnOffState.Unknown;
    }

    extension(SwitchState state)
    {
        public bool IsOn() =>
            state == SwitchState.On;

        public bool IsOff() =>
            state == SwitchState.Off;
    }

    extension(Action action)
    {
        public void ParallelInvoke()
        {
            var actions = action.GetInvocationList().Cast<Action>().ToArray();
            Parallel.Invoke(actions);
        }
    }
}
