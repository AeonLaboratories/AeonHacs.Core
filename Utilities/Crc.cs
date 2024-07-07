using Newtonsoft.Json;
using System;
using System.ComponentModel;
using System.Text;

namespace AeonHacs.Utilities
{
    /// <summary>
    /// Encapsulates options for Cyclical Redundancy Checking.
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class CrcOptions
    {
        // To pre-Invert, start with 0xFFFF instead of 0
        [JsonProperty, DefaultValue(0xFFFF)]
        public UInt16 InitialValue { get; set; } = 0xFFFF;

        // see Aeon CRC notes
        [JsonProperty, DefaultValue(0xDAAE)]
        public UInt16 Polynomial { get; set; } = 0xDAAE;

        // Residue, residual, remainder: what we should get if the
        // CRC matches the message.
        [JsonProperty, DefaultValue(0x820)]
        public UInt16 ExpectedResidue { get; set; } = 0x820;


        [JsonProperty, DefaultValue(true)]
        public bool PostInvert { get; set; } = true;

        // RS-232 and IEEE-802 (Ethernet) communications specify lsb-first transmission.
        // Other protocols, such as hard-disk standards and XMODEM, are big-endian; data is
        // sent msb first;
        [JsonProperty, DefaultValue(false)]
        public bool MsBitFirst { get; set; }

        [JsonProperty, DefaultValue(false)]
        public bool MsByteFirst { get; set; }

        // termination character, or deliminter. 0x03 is ETX (end of transmission)
        [JsonProperty, DefaultValue(0x03)]
        public byte TermChar { get; set; } = 0x03;

        [JsonProperty, DefaultValue(false)]
        public bool OmitTermChar { get; set; }

        public CrcOptions() : this(0xFFFF, 0xDAAE, 0x82C0, true, false, false, 3, false) { }

        public CrcOptions(UInt16 init, UInt16 poly, UInt16 residue,
            bool postInvert, bool msBitFirst, bool msByteFirst, 
            byte termchar, bool omitTermChar)
        {
            InitialValue = init;
            Polynomial = poly;
            ExpectedResidue = residue;
            PostInvert = postInvert;
            MsBitFirst = msBitFirst;
            MsByteFirst = msByteFirst;
            TermChar = termchar;
            OmitTermChar = omitTermChar;
        }
    }


    public class Crc
    {
        public CrcOptions Options 
        {
            get { return options; }
            set { options = value; Init(); }
        }
        CrcOptions options;

        public UInt16 Code => remainder;
        UInt16 remainder;


        public Crc() { }
        public Crc(CrcOptions options) { this.options = options; }
        public Crc(string s) { Update(s); }


        public void Init()
        {
            if (Options == null) 
                Options = new CrcOptions();
            else
                remainder = Options.InitialValue; 
        }
        public bool Good() => Options != null && remainder == Options.ExpectedResidue;

        public UInt16 Update( byte b )
        {
            if (Options == null) Options = new CrcOptions();

            if (Options.MsBitFirst)
            {
                remainder ^= (UInt16)(b << 8);
                for (int bit = 0; bit < 8; bit++)
                {
                    int msb = remainder & 0x8000;
                    remainder <<= 1;
                    if (msb != 0) remainder ^= Options.Polynomial;
                }
            }
            else
            {
                remainder ^= b;
                for (int bit = 0; bit < 8; bit++)
                {
                    int lsb = remainder & 1;
                    remainder >>= 1;
                    if (lsb != 0) remainder ^= Options.Polynomial;
                }
            }
            return remainder;
        }

        public UInt16 Update(byte[] b, int n)
        {
            for (int i = 0; i < n; i++)
                Update(b[i]);
            return remainder;
        }

        public UInt16 Update(byte[] b)
        { return Update(b, b.Length); }

        public UInt16 Update(char[] c, int start, int n)
        {
            for (int i = start; i < n; i++)
                Update(c[i]);
            return remainder;
        }

        public UInt16 Update(char[] c, int n)
        { return Update(c, 0, n); }

        public UInt16 Update(char[] c)
        { return Update(c, c.Length); }

        public UInt16 Update(char c)
        { return Update((byte)c); }

        public UInt16 Update(string s)
        {
            for (int i = 0; i < s.Length; i++)
                Update(s[i]);
            return remainder;
        }

        // add CRC code and termination character to end of s
        public string sAppend(string s)
        {
            Update(s);
            UInt16 checkCode = Options.PostInvert ? (UInt16)~remainder : remainder;

            // Note: BitConverter.IsLittleEndian, so swap bytes iff MsByteFirst
            byte[] ba = BitConverter.GetBytes(checkCode);

            StringBuilder sb = new StringBuilder(s);
            if (Options.MsByteFirst)
            {
                sb.Append((char)ba[1]);
                sb.Append((char)ba[0]);
            }
            else
            {
                sb.Append((char)ba[0]);
                sb.Append((char)ba[1]);
            }

            if (!Options.OmitTermChar)
            {
                sb.Append((char)Options.TermChar);
            }

            return sb.ToString();
        }

        // create a byte array from s, with the CRC code and TermChar appended
        public byte[] Append(string s)
        {
            Update(s);
            UInt16 checkCode = Options.PostInvert ? (UInt16)~remainder : remainder;
            byte[] ba = new byte[s.Length + (Options.OmitTermChar ? 2 : 3)];
            int i;
            for (i = 0; i < s.Length; i++)
                ba[i] = (byte) s[i];

            // Note: BitConverter.IsLittleEndian, so swap bytes iff MsByteFirst
            byte[] crcba = BitConverter.GetBytes(checkCode);
            if (Options.MsByteFirst)
            {
                ba[i++] = crcba[1];
                ba[i++] = crcba[0];
            }
            else
            {
                ba[i++] = crcba[0];
                ba[i++] = crcba[1];
            }

            if (!Options.OmitTermChar)
                ba[i] = Options.TermChar;

            return ba;
        }

        public static string AppendTo(string s, CrcOptions options)
        {
            Crc crc = new Crc(options);
            return crc.sAppend(s);
        }

        public static string AppendTo(string s)
        {
            return AppendTo(s, new CrcOptions());
        }

        public static byte[] Codeword(string s, CrcOptions options)
        {
            Crc crc = new Crc(options);
            return crc.Append(s);
        }

        public static byte[] Codeword(string s)
        {
            return Codeword(s, new CrcOptions());
        }

    }

    //////////////////////////
    // NOTES
    //////////////////////////
    // Since RS232 is little-endian (transmitted lsb first), this CRC 
    // code works the data in that direction, i.e., it shifts to the
    // right instead of left, and accordingly uses the Reverse version
    // of the polynomial.
    //
    ////////////////
    // CRC-CCITT = x16 + x12 + x5 + 1
    // 
    // Normal:
    // (1)    1
    // (6) 5432 1098 7654 3210
    // (1) 0001 0000 0010 0001 = 0x1021
    //
    // Reverse of reciprocal (Koopman notation):
    // 1    1
    // 6543 2109 8765 4321 (0)
    // 1000 1000 0001 0000 (1) = 0x8810
    //
    // Reverse:
    // (1)    1
    // (6) 5432 1098 7654 3210
    // (1) 0001 0000 0010 0001 => 1000 0100 0000 1000 = 0x8408
    //    instead of 0, sometimes we get 339f = 11001110011111
    //
    //
    ////////////////
    // 16-bit CRC: 0xBAAD provides HD=4 protection for messages up to 2048 bits
    // ref: Koopman, Philip "Better Embedded System Software"
    //    see http://betterembsw.blogspot.com/2010/05/whats-best-crc-polynomial-to-use.html
    //
    // Normal:
    // (1)    1
    // (6) 5432 1098 7654 3210
    // (1) 0111 0101 0101 1011 = 0x755B
    //
    // Reverse of reciprocal (Koopman notation):
    // 1    1
    // 6543 2109 8765 4321 (0)
    // 1011 1010 1010 1101 (1) = 0xBAAD
    //
    // Reverse:
    // (1)    1
    // (6) 5432 1098 7654 3210
    // (1) 0111 0101 0101 1011 => 1101 1010 1010 1110 = 0xDAAE
    //
    //
    ////////////////
    // CRC-16/T10-DIF, aka "CRC-16Q" and "SCSI DIF"
    //        x16 + x15 + x11 + x9 + x8 + x7 + x5 + x4 + x2 + x1 + 1
    //
    // Normal:
    // (1)    1
    // (6) 5432 1098 7654 3210
    // (1) 1000 1011 1011 0111 = 0x8BB7
    //
    // Reverse of reciprocal (Koopman notation):
    // 1    1
    // 6543 2109 8765 4321 (0)
    // 1100 0101 1101 1011 (1) = 0xC5DB
    //
    // Reverse:
    // (1)    1
    // (6) 5432 1098 7654 3210
    // (1) 1000 1011 1011 0111 => 1110 1101 1101 0001 = 0xEDD1
    //
    //

}
