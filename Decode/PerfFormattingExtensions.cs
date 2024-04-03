namespace Microsoft.LinuxTracepoints.Decode.Formatting
{
    using System;
    using System.Globalization;
    using System.Text;
    using static System.Net.Mime.MediaTypeNames;

    public static class PerfFormattingExtensions
    {
        /// <summary>
        /// Appends a Unix time_t (signed seconds since 1970) to a StringBuilder.
        /// If year is in range 0001..9999, appends a string like "2020-02-02T02:02:02".
        /// If year is outside of 0001..9999, appends a string like "TIME(-1234567890)".
        /// </summary>
        public static StringBuilder AppendUnixTime64(this StringBuilder sb, long secondsSince1970)
        {
            var maybe = PerfConvert.UnixTime64ToDateTime(secondsSince1970);
            if (maybe is DateTime value)
            {
                sb.AppendFormat(CultureInfo.InvariantCulture, "{0:s}", value);
            }
            else
            {
                sb.Append("TIME(");
                sb.Append(secondsSince1970);
                sb.Append(')');
            }

            return sb;
        }

        /// <summary>
        /// Appends the specified Linux errno value formatted as a string.
        /// If value is a known errno value, appends a string like "EPERM(1)".
        /// If value is not a known errno value, appends a string like "ERRNO(404)".
        /// </summary>
        public static StringBuilder AppendErrno(this StringBuilder sb, int linuxErrno)
        {
            var value = PerfConvert.ErrnoLookup(linuxErrno);
            if (value == null)
            {
                sb.Append("ERRNO(");
                sb.Append(linuxErrno);
                sb.Append(')');
            }
            else
            {
                sb.Append(value);
            }

            return sb;
        }

        /// <summary>
        /// Appends an integer value from a boolean field into a string.
        /// If value is 0/1, appends "false"/"true".
        /// Otherwise, appends value formatted as a signed integer.
        /// Note: input value is UInt32 because Bool8 and Bool16 should not be
        /// sign-extended, i.e. value should come from a call to TryGetUInt32,
        /// not a call to TryGetInt32.
        /// </summary>
        public static StringBuilder AppendBoolean(this StringBuilder sb, UInt32 integerBool)
        {
            switch (integerBool)
            {
                case 0: sb.Append("false"); break;
                case 1: sb.Append("true"); break;
                default: sb.Append(unchecked((int)integerBool)); break;
            }

            return sb;
        }

        /// <summary>
        /// Converts the encoded string to UTF-16 and appends it to the StringBuilder.
        /// </summary>
        public static StringBuilder AppendByteString(this StringBuilder sb, ReadOnlySpan<byte> bytes, Encoding encoding)
        {
            const int MaxStackChars = 256;
            var maxChars = encoding.GetMaxCharCount(bytes.Length);
            if (maxChars <= MaxStackChars)
            {
                Span<char> chars = stackalloc char[maxChars];
                var count = encoding.GetChars(bytes, chars);
                sb.Append(chars.Slice(0, count));
            }
            else
            {
                Span<char> chars = stackalloc char[MaxStackChars];
                var decoder = encoding.GetDecoder();
                var pos = 0;
                bool done;
                do
                {
                    int bytesUsed;
                    int charsUsed;
                    decoder.Convert(bytes.Slice(pos), chars, false, out bytesUsed, out charsUsed, out done);
                    sb.Append(chars.Slice(0, charsUsed));
                    pos += bytesUsed;
                }
                while (!done);
            }

            return sb;
        }

        /// <summary>
        /// Converts the specified UTF-8 string to UTF-16 and appends it to the StringBuilder.
        /// </summary>
        public static StringBuilder AppendUTF8(this StringBuilder sb, ReadOnlySpan<byte> bytes)
        {
            return AppendByteString(sb, bytes, Encoding.UTF8);
        }

        public static StringBuilder AppendHexBytes(this StringBuilder sb, ReadOnlySpan<byte> bytes)
        {
            Span<char> chars = stackalloc char[3];
            if (0 < bytes.Length)
            {
                sb.EnsureCapacity(sb.Length + bytes.Length * 3 - 1);

                var val = bytes[0];
                chars[0] = ' ';
                chars[1] = PerfConvert.ToHexChar(val >> 4);
                chars[2] = PerfConvert.ToHexChar(val);
                sb.Append(chars.Slice(1));

                for (int pos = 1; pos < bytes.Length; pos += 1)
                {
                    val = bytes[pos];
                    chars[1] = PerfConvert.ToHexChar(val >> 4);
                    chars[2] = PerfConvert.ToHexChar(val);
                    sb.Append(chars);
                }

                return sb;
            }

            return sb;
        }
    }
}
