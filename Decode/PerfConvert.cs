namespace Microsoft.LinuxTracepoints.Decode
{
    using System;
    using System.Text;
    using Debug = System.Diagnostics.Debug;
    using MemoryMarshal = System.Runtime.InteropServices.MemoryMarshal;

    /// <summary>
    /// Helpers for converting raw event element data into types.
    /// </summary>
    public static class PerfConvert
    {
        private const long UnixEpochSeconds = 62135596800;
        private const long DaysToYear10000 = 3652059;
        private const long SecondsPerDay = 60 * 60 * 24;
        private const long MaxSeconds = DaysToYear10000 * SecondsPerDay - UnixEpochSeconds;

        private static Encoding? encodingLatin1; // ISO-8859-1
        private static Encoding? encodingUTF32BE;
        private static string[]? errnoStrings;

        /// <summary>
        /// The maximum number of characters required by Utf32Format is
        /// 2, i.e. the high and low surrogate pair.
        /// </summary>
        public const int Utf32MaxChars = 2;

        /// <summary>
        /// The maximum number of characters required by IPv4Format is
        /// 15, e.g. "255.255.255.255".
        /// </summary>
        public const int IPv4MaxChars = 15;

        /// <summary>
        /// The maximum number of characters required by DecimalU32Format is
        /// 10, e.g. "4294967295".
        /// </summary>
        public const int DecimalU32MaxChars = 10;

        /// <summary>
        /// The maximum number of characters required by DecimalU64Format is
        /// 20, e.g. "18446744073709551615".
        /// </summary>
        public const int DecimalU64MaxChars = 20;

        /// <summary>
        /// The maximum number of characters required by DecimalI32Format is
        /// 11, e.g. "-2147483648".
        /// </summary>
        public const int DecimalI32MaxChars = 11;

        /// <summary>
        /// The maximum number of characters required by DecimalI64Format is
        /// 20, e.g. "-9223372036854775808".
        /// </summary>
        public const int DecimalI64MaxChars = 20;

        /// <summary>
        /// The maximum number of characters required by HexU32Format is
        /// 10, e.g. "0xFFFFFFFF".
        /// </summary>
        public const int HexU32MaxChars = 10;

        /// <summary>
        /// The maximum number of characters required by HexU64Format is
        /// 18, e.g. "0xFFFFFFFFFFFFFFFF".
        /// </summary>
        public const int HexU64MaxChars = 18;

        /// <summary>
        /// The maximum number of characters required by DateTimeFormat is
        /// 20, e.g. "2020-02-02T02:02:02Z".
        /// </summary>
        public const int DateTimeMaxChars = 20;

        /// <summary>
        /// The maximum number of characters required by UnixTime32Format is
        /// 20, e.g. "2020-02-02T02:02:02Z".
        /// </summary>
        public const int UnixTime32MaxChars = 20;

        /// <summary>
        /// The maximum number of characters required by UnixTime64Format is
        /// 26, e.g. "TIME(-9223372036854775808)".
        /// </summary>
        public const int UnixTime64MaxChars = 26;

        /// <summary>
        /// The maximum number of characters required by BooleanFormat is
        /// 17, e.g. "BOOL(-2147483648)".
        /// </summary>
        public const int BooleanMaxChars = 17;

        /// <summary>
        /// Maximum number of characters required to format an errno value,
        /// currently 20, e.g. "ENOTRECOVERABLE(131)".
        /// This is static readonly (not const) because it may be changed in the future.
        /// </summary>
        public static readonly int ErrnoMaxChars = 20;

        /// <summary>
        /// Gets an encoding for ISO-8859-1 (Latin-1) characters, i.e. a cached
        /// value of Encoding.GetEncoding(28591).
        /// Provided because Encoding.Latin1 is not available in .NET Standard 2.1.
        /// </summary>
        public static Encoding EncodingLatin1
        {
            get
            {
                var encoding = encodingLatin1; // Get the cached encoding, if available.
                if (encoding == null)
                {
                    encoding = Encoding.GetEncoding(28591);
                    encodingLatin1 = encoding; // Cache the encoding.
                }

                return encoding;
            }
        }

        /// <summary>
        /// Gets an encoding for UTF-32 big-endian characters, i.e. a cached value
        /// of Encoding.GetEncoding(12001).
        /// Provided because Encoding.BigEndianUTF32 is not available in .NET Standard 2.1.
        /// </summary>
        public static Encoding EncodingUTF32BE
        {
            get
            {
                var encoding = encodingUTF32BE; // Get the cached encoding, if available.
                if (encoding == null)
                {
                    encoding = Encoding.GetEncoding(12001);
                    encodingUTF32BE = encoding; // Cache the encoding.
                }

                return encoding;
            }
        }

        /// <summary>
        /// If the provided byte array starts with a byte order mark (BOM),
        /// returns the corresponding encoding. Otherwise returns null.
        /// Returns one of the following: Encoding.UTF8, Encoding.Unicode,
        /// Encoding.BigEndianUnicode, Encoding.UTF32, PerfConvert.EncodingUTF32BE,
        /// or null.
        /// </summary>
        public static Encoding? EncodingFromBom(ReadOnlySpan<byte> str)
        {
            var cb = str.Length;
            if (cb >= 4 &&
                str[0] == 0xFF &&
                str[1] == 0xFE &&
                str[2] == 0x00 &&
                str[3] == 0x00)
            {
                return Encoding.UTF32;
            }
            else if (cb >= 4 &&
                str[0] == 0x00 &&
                str[1] == 0x00 &&
                str[2] == 0xFE &&
                str[3] == 0xFF)
            {
                return EncodingUTF32BE;
            }
            else if (cb >= 3 &&
                str[0] == 0xEF &&
                str[1] == 0xBB &&
                str[2] == 0xBF)
            {
                return Encoding.UTF8;
            }
            else if (cb >= 2 &&
               str[0] == 0xFF &&
               str[1] == 0xFE)
            {
                return Encoding.Unicode;
            }
            else if (cb >= 2 &&
                str[0] == 0xFE &&
                str[1] == 0xFF)
            {
                return Encoding.BigEndianUnicode;
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Formats the provided integer value as a UTF-32 code point, or as '\uFFFD' if invalid.
        /// Requires appropriately-sized destination buffer, up to Utf32MaxChars.
        /// Returns the formatted string (the filled portion of destination).
        /// </summary>
        public static Span<char> Utf32Format(Span<char> destination, UInt32 utf32codePoint)
        {
            if (utf32codePoint <= 0xFFFF)
            {
                destination[0] = (char)utf32codePoint;
                return destination.Slice(0, 1);
            }
            else if (utf32codePoint <= 0x10FFFF)
            {
                var u = (utf32codePoint - 0x10000) & 0xFFFFF;
                destination[1] = (char)(0xDC00 | (u & 0x3FF));
                destination[0] = (char)(0xD800 | (u >> 10));
                return destination.Slice(0, 2);
            }
            else
            {
                destination[0] = '\uFFFD'; // Replacement character
                return destination.Slice(0, 1);
            }
        }

        /// <summary>
        /// Returns a new string from the provided UTF-32 code point.
        /// </summary>
        public static string Utf32ToString(UInt32 utf32codePoint)
        {
            return new string(Utf32Format(stackalloc char[Utf32MaxChars], utf32codePoint));
        }

        /// <summary>
        /// Appends the provided UTF-32 code point. Returns sb.
        /// </summary>
        public static StringBuilder Utf32Append(StringBuilder sb, UInt32 utf32codePoint)
        {
            return sb.Append(Utf32Format(stackalloc char[Utf32MaxChars], utf32codePoint));
        }

        /// <summary>
        /// Formats the provided value (big-endian) as an IPv4 address.
        /// Requires appropriately-sized destination buffer, up to IPv4MaxChars.
        /// Returns the formatted string (the filled portion of destination).
        /// </summary>
        public static Span<char> IPv4Format(Span<char> destination, UInt32 ipv4)
        {
            var bytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref ipv4, 1));
            var pos = 0;
            var end = DecimalU32FormatAtEnd(destination.Slice(pos), bytes[0]);
            end.CopyTo(destination);
            pos += end.Length;

            for (var i = 1; i < 4; i += 1)
            {
                destination[pos++] = '.';
                end = DecimalU32FormatAtEnd(destination.Slice(pos), bytes[i]);
                end.CopyTo(destination.Slice(pos));
                pos += end.Length;
            }

            return destination.Slice(0, pos);
        }

        /// <summary>
        /// Appends the provided value (big-endian) as an IPv4 address. Returns sb.
        /// </summary>
        public static string IPv4ToString(UInt32 ipv4)
        {
            return new string(IPv4Format(stackalloc char[IPv4MaxChars], ipv4));
        }

        /// <summary>
        /// Appends the provided value (big-endian) as an IPv4 address. Returns sb.
        /// </summary>
        public static StringBuilder IPv4Append(StringBuilder sb, UInt32 ipv4)
        {
            return sb.Append(IPv4Format(stackalloc char[IPv4MaxChars], ipv4));
        }

        /// <summary>
        /// Formats the provided integer value as a variable-length decimal string
        /// like "1234" at the end of the destination buffer.
        /// Requires appropriately-sized destination buffer, up to DecimalU32MaxChars.
        /// Returns the formatted string (the filled portion of destination).
        /// </summary>
        public static Span<char> DecimalU32FormatAtEnd(Span<char> destination, UInt32 value)
        {
            var pos = destination.Length;
            do
            {
                destination[--pos] = (char)('0' + value % 10);
                value /= 10;
            }
            while (value != 0);
            return destination.Slice(pos);
        }

        /// <summary>
        /// Formats the provided integer value as a variable-length decimal string
        /// like "1234" at the end of the destination buffer.
        /// Requires appropriately-sized destination buffer, up to DecimalU64MaxChars.
        /// Returns the formatted string (the filled portion of destination).
        /// </summary>
        public static Span<char> DecimalU64FormatAtEnd(Span<char> destination, UInt64 value)
        {
            var pos = destination.Length;
            do
            {
                destination[--pos] = (char)('0' + value % 10);
                value /= 10;
            }
            while (value != 0);
            return destination.Slice(pos);
        }

        /// <summary>
        /// Formats the provided integer value as a variable-length decimal string.
        /// </summary>
        public static string DecimalU32ToString(UInt32 value)
        {
            return new string(DecimalU32FormatAtEnd(stackalloc char[DecimalU32MaxChars], value));
        }

        /// <summary>
        /// Formats the provided integer value as a variable-length decimal string.
        /// </summary>
        public static string DecimalU64ToString(UInt64 value)
        {
            return new string(DecimalU64FormatAtEnd(stackalloc char[DecimalU64MaxChars], value));
        }

        /// <summary>
        /// Appends the provided integer value as a variable-length decimal string.
        /// Returns sb.
        /// </summary>
        public static StringBuilder DecimalU32Append(StringBuilder sb, UInt32 value)
        {
            return sb.Append(DecimalU32FormatAtEnd(stackalloc char[DecimalU32MaxChars], value));
        }

        /// <summary>
        /// Appends the provided integer value as a variable-length decimal string.
        /// Returns sb.
        /// </summary>
        public static StringBuilder DecimalU64Append(StringBuilder sb, UInt64 value)
        {
            return sb.Append(DecimalU64FormatAtEnd(stackalloc char[DecimalU64MaxChars], value));
        }

        /// <summary>
        /// Formats the provided integer value as a variable-length decimal string
        /// like "-1234" at the end of the destination buffer.
        /// Requires appropriately-sized destination buffer, up to DecimalI32MaxChars.
        /// Returns the formatted string (the filled portion of destination).
        /// </summary>
        public static Span<char> DecimalI32FormatAtEnd(Span<char> destination, Int32 value)
        {
            if (value < 0)
            {
                var len = DecimalU32FormatAtEnd(destination.Slice(1), unchecked((uint)-value)).Length;
                var start = destination.Length - len - 1;
                destination[start] = '-';
                return destination.Slice(start);
            }
            else
            {
                return DecimalU32FormatAtEnd(destination, unchecked((uint)value));
            }
        }

        /// <summary>
        /// Formats the provided integer value as a variable-length decimal string
        /// like "-1234" at the end of the destination buffer.
        /// Requires appropriately-sized destination buffer, up to DecimalI64MaxChars.
        /// Returns the formatted string (the filled portion of destination).
        /// </summary>
        public static Span<char> DecimalI64FormatAtEnd(Span<char> destination, Int64 value)
        {
            if (value < 0)
            {
                var len = DecimalU64FormatAtEnd(destination.Slice(1), unchecked((ulong)-value)).Length;
                var start = destination.Length - len - 1;
                destination[start] = '-';
                return destination.Slice(start);
            }
            else
            {
                return DecimalU64FormatAtEnd(destination, unchecked((ulong)value));
            }
        }

        /// <summary>
        /// Formats the provided integer value as a variable-length decimal string.
        /// </summary>
        public static string DecimalI32ToString(Int32 value)
        {
            return new string(DecimalI32FormatAtEnd(stackalloc char[DecimalI32MaxChars], value));
        }

        /// <summary>
        /// Formats the provided integer value as a variable-length decimal string.
        /// </summary>
        public static string DecimalI64ToString(Int64 value)
        {
            return new string(DecimalI64FormatAtEnd(stackalloc char[DecimalI64MaxChars], value));
        }

        /// <summary>
        /// Appends the provided integer value as a variable-length decimal string.
        /// Returns sb.
        /// </summary>
        public static StringBuilder DecimalI32Append(StringBuilder sb, Int32 value)
        {
            return sb.Append(DecimalI32FormatAtEnd(stackalloc char[DecimalI32MaxChars], value));
        }

        /// <summary>
        /// Appends the provided integer value as a variable-length decimal string.
        /// Returns sb.
        /// </summary>
        public static StringBuilder DecimalI64Append(StringBuilder sb, Int64 value)
        {
            return sb.Append(DecimalI64FormatAtEnd(stackalloc char[DecimalI64MaxChars], value));
        }

        /// <summary>
        /// Formats the provided integer value as a variable-length hex string like
        /// "0x123ABC" at the end of the destination buffer.
        /// Requires appropriately-sized destination buffer, up to HexU32MaxChars.
        /// Returns the formatted string (the filled portion of destination).
        /// </summary>
        public static Span<char> HexU32FormatAtEnd(Span<char> destination, UInt32 value)
        {
            var pos = destination.Length;
            do
            {
                destination[--pos] = ToHexChar(unchecked((int)value));
                value >>= 4;
            }
            while (value != 0);
            destination[--pos] = 'x';
            destination[--pos] = '0';
            return destination.Slice(pos);
        }

        /// <summary>
        /// Formats the provided integer value as a variable-length hex string like
        /// "0x123ABC" at the end of the destination buffer.
        /// Requires appropriately-sized destination buffer, up to HexU64MaxChars.
        /// Returns the formatted string (the filled portion of destination).
        /// </summary>
        public static Span<char> HexU64FormatAtEnd(Span<char> destination, UInt64 value)
        {
            var pos = destination.Length;
            do
            {
                destination[--pos] = ToHexChar(unchecked((int)value));
                value >>= 4;
            }
            while (value != 0);
            destination[--pos] = 'x';
            destination[--pos] = '0';
            return destination.Slice(pos);
        }

        /// <summary>
        /// Returns a new string like "0x123ABC" for the provided value.
        /// </summary>
        public static string HexU32ToString(UInt32 value)
        {
            return new string(HexU32FormatAtEnd(stackalloc char[HexU32MaxChars], value));
        }

        /// <summary>
        /// Returns a new string like "0x123ABC" for the provided value.
        /// </summary>
        public static string HexU64ToString(UInt64 value)
        {
            return new string(HexU64FormatAtEnd(stackalloc char[HexU64MaxChars], value));
        }

        /// <summary>
        /// Appends a string like "0x123ABC". Returns sb.
        /// </summary>
        public static StringBuilder HexU32Append(StringBuilder sb, UInt32 value)
        {
            return sb.Append(HexU32FormatAtEnd(stackalloc char[HexU32MaxChars], value));
        }

        /// <summary>
        /// Appends a string like "0x123ABC". Returns sb.
        /// </summary>
        public static StringBuilder HexU64Append(StringBuilder sb, UInt64 value)
        {
            return sb.Append(HexU64FormatAtEnd(stackalloc char[HexU64MaxChars], value));
        }

        /// <summary>
        /// Returns the number of chars required to format the provided byte array as a string
        /// of hexadecimal bytes (e.g. "0D 0A").
        /// If bytesLength is 0, returns 0. Otherwise returns (3 * bytesLength - 1).
        /// </summary>
        public static int HexBytesFormatLength(int bytesLength) =>
            bytesLength <= 0 ? 0 : 3 * bytesLength - 1;

        /// <summary>
        /// Formats the provided byte array as a string of hexadecimal bytes (e.g. "0D 0A").
        /// Requires appropriately-sized destination buffer, Length >= HexBytesFormatLength(bytes.Length).
        /// Returns the formatted string (the filled portion of destination).
        /// </summary>
        public static Span<char> HexBytesFormat(Span<char> destination, ReadOnlySpan<byte> bytes)
        {
            var bytesLength = bytes.Length;
            if (0 < bytesLength)
            {
                var b = bytes[0];
                destination[0] = PerfConvert.ToHexChar(b >> 4);
                destination[1] = PerfConvert.ToHexChar(b);

                for (int pos = 1; pos < bytesLength; pos += 1)
                {
                    destination[(pos * 3) - 1] = ' ';
                    destination[(pos * 3) + 0] = PerfConvert.ToHexChar(bytes[pos] >> 4);
                    destination[(pos * 3) + 1] = PerfConvert.ToHexChar(bytes[pos]);
                }

                return destination.Slice(0, 3 * bytesLength - 1);
            }

            return destination.Slice(0, 0);
        }

        /// <summary>
        /// Converts a byte array to a new string of hexadecimal bytes (e.g. "0D 0A").
        /// </summary>
        public static string HexBytesToString(ReadOnlySpan<byte> bytes)
        {
            return bytes.Length > 0
                ? HexBytesAppend(new StringBuilder(bytes.Length * 3 - 1), bytes).ToString()
                : "";
        }

        /// <summary>
        /// Converts a byte array to a string of hexadecimal bytes (e.g. "0D 0A") and appends it
        /// to the provided StringBuilder. Returns sb.
        /// </summary>
        public static StringBuilder HexBytesAppend(StringBuilder sb, ReadOnlySpan<byte> bytes)
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

        /// <summary>
        /// Formats the provided DateTime value as a string like "2020-02-02T02:02:02Z".
        /// Requires appropriately-sized destination buffer, Length >= DateTimeMaxChars chars.
        /// Returns the formatted string (the filled portion of destination).
        /// </summary>
        public static Span<char> DateTimeFormat(Span<char> destination, DateTime value)
        {
            Debug.Assert(destination.Length >= 20);
            value.TryFormat(destination, out var pos, "s", null);
            destination[pos++] = 'Z';
            Debug.Assert(pos == DateTimeMaxChars);
            return destination.Slice(0, pos);
        }

        /// <summary>
        /// Formats the provided DateTime value as a string like "2020-02-02T02:02:02Z".
        /// </summary>
        public static string DateTimeToString(DateTime value)
        {
            return new string(DateTimeFormat(stackalloc char[DateTimeMaxChars], value));
        }

        /// <summary>
        /// Appends the provided DateTime value formatted as a string like "2020-02-02T02:02:02Z".
        /// </summary>
        public static StringBuilder DateTimeAppend(StringBuilder sb, DateTime value)
        {
            return sb.Append(DateTimeFormat(stackalloc char[DateTimeMaxChars], value));
        }

        /// <summary>
        /// Converts a 32-bit Unix time_t (signed seconds since 1970) to a DateTime.
        /// </summary>
        public static DateTime UnixTime32ToDateTime(Int32 secondsSince1970)
        {
            return new DateTime((secondsSince1970 + UnixEpochSeconds) * 10000000, DateTimeKind.Utc);
        }

        /// <summary>
        /// Attempts to convert a 64-bit Unix time_t (signed seconds since 1970) to a DateTime.
        /// Returns null if the result is less than DateTime.MinValue or greater than
        /// DateTime.MaxValue.
        /// </summary>
        public static DateTime? UnixTime64ToDateTime(Int64 secondsSince1970)
        {
            if (secondsSince1970 < -UnixEpochSeconds ||
                secondsSince1970 >= MaxSeconds)
            {
                return null;
            }
            else
            {
                return new DateTime((secondsSince1970 + UnixEpochSeconds) * 10000000, DateTimeKind.Utc);
            }
        }

        /// <summary>
        /// Converts a 32-bit Unix time_t (signed seconds since 1970) to a string like
        /// "2020-02-02T02:02:02Z".
        /// Requires appropriately-sized destination buffer, Length >= UnixTime32MaxChars.
        /// Returns the formatted string (the filled portion of destination).
        /// </summary>
        public static Span<char> UnixTime32Format(Span<char> destination, Int32 secondsSince1970)
        {
            return DateTimeFormat(destination, UnixTime32ToDateTime(secondsSince1970));
        }

        /// <summary>
        /// Converts a 64-bit Unix time_t (signed seconds since 1970) to a string.
        /// If year is in range 0001..9999, returns a value like   "2020-02-02T02:02:02Z".
        /// If year is outside of 0001..9999, returns a value like "TIME(-1234567890)".
        /// Requires appropriately-sized destination buffer, Length >= UnixTime64MaxChars.
        /// Returns the formatted string (the filled portion of destination).
        /// </summary>
        public static Span<char> UnixTime64Format(Span<char> destination, Int64 secondsSince1970)
        {
            var maybe = UnixTime64ToDateTime(secondsSince1970);
            if (maybe is DateTime value)
            {
                return DateTimeFormat(destination, value);
            }
            else
            {
                var pos = 0;
                destination[pos++] = 'T';
                destination[pos++] = 'I';
                destination[pos++] = 'M';
                destination[pos++] = 'E';
                destination[pos++] = '(';
                var end = DecimalI64FormatAtEnd(destination.Slice(pos), secondsSince1970);
                end.CopyTo(destination.Slice(pos));
                pos += end.Length;
                destination[pos++] = ')';
                return destination.Slice(0, pos);
            }
        }

        /// <summary>
        /// Converts a 32-bit Unix time_t (signed seconds since 1970) to a new string
        /// like "2020-02-02T02:02:02Z".
        /// </summary>
        public static string UnixTime32ToString(Int32 secondsSince1970)
        {
            return new string(UnixTime32Format(stackalloc char[UnixTime32MaxChars], secondsSince1970));
        }

        /// <summary>
        /// Converts a 64-bit Unix time_t (signed seconds since 1970) to a new string.
        /// If year is in range 0001..9999, returns a value like "2020-02-02T02:02:02".
        /// If year is outside of 0001..9999, returns a value like "TIME(-1234567890)".
        /// </summary>
        public static string UnixTime64ToString(Int64 secondsSince1970)
        {
            return new string(UnixTime64Format(stackalloc char[UnixTime64MaxChars], secondsSince1970));
        }

        /// <summary>
        /// Appends a 32-bit Unix time_t (signed seconds since 1970) as a string like
        /// "2020-02-02T02:02:02Z". Returns sb.
        /// </summary>
        public static StringBuilder UnixTime32Append(StringBuilder sb, Int32 secondsSince1970)
        {
            return sb.Append(UnixTime32Format(stackalloc char[UnixTime32MaxChars], secondsSince1970));
        }

        /// <summary>
        /// Appends a 64-bit Unix time_t (signed seconds since 1970).
        /// If year is in range 0001..9999, appends a value like "2020-02-02T02:02:02".
        /// If year is outside of 0001..9999, appends a value like "TIME(-1234567890)".
        /// Returns sb.
        /// </summary>
        public static StringBuilder UnixTime64Append(StringBuilder sb, Int64 secondsSince1970)
        {
            return sb.Append(UnixTime64Format(stackalloc char[UnixTime64MaxChars], secondsSince1970));
        }

        /// <summary>
        /// If the specified value is a recognized Linux error number, returns a
        /// string like "ERRNO(0)" or "ENOENT(2)". Otherwise returns null.
        /// </summary>
        public static string? ErrnoLookup(int linuxErrno)
        {
            var strings = errnoStrings; // Get the cached strings, if available.
            if (strings == null)
            {
                strings = NewErrnoStrings();
                errnoStrings = strings; // Cache the strings.
            }

            return linuxErrno >= 0 && linuxErrno < strings.Length
                ? strings[linuxErrno]
                : null;
        }

        /// <summary>
        /// If the specified value is a recognized Linux error number, returns a
        /// string like "ERRNO(0)" or "ENOENT(2)". Otherwise returns a new string like
        /// "ERRNO(404)".
        /// Requires appropriately-sized destination buffer, Length >= ErrnoMaxChars.
        /// Returns the formatted string (the filled portion of destination).
        /// </summary>
        public static Span<char> ErrnoFormat(Span<char> destination, Int32 errno)
        {
            var recognized = ErrnoLookup(errno);
            if (recognized != null)
            {
                recognized.AsSpan().CopyTo(destination);
                return destination.Slice(0, recognized.Length);
            }
            else
            {
                var pos = 0;
                destination[pos++] = 'E';
                destination[pos++] = 'R';
                destination[pos++] = 'R';
                destination[pos++] = 'N';
                destination[pos++] = 'O';
                destination[pos++] = '(';
                var end = DecimalI32FormatAtEnd(destination.Slice(pos), errno);
                end.CopyTo(destination.Slice(pos));
                pos += end.Length;
                destination[pos++] = ')';
                return destination.Slice(0, pos);
            }
        }

        /// <summary>
        /// If the specified value is a recognized Linux error number, returns a
        /// string like "ERRNO(0)" or "ENOENT(2)". Otherwise returns a new string like
        /// "ERRNO(404)".
        /// </summary>
        public static string ErrnoToString(Int32 linuxErrno)
        {
            return new string(ErrnoFormat(stackalloc char[ErrnoMaxChars], linuxErrno));
        }

        /// <summary>
        /// If the specified value is a recognized Linux error number, appends a
        /// string like "ERRNO(0)" or "ENOENT(2)". Otherwise appends a string like
        /// "ERRNO(404)". Returns sb.
        /// </summary>
        public static StringBuilder ErrnoAppend(StringBuilder sb, Int32 linuxErrno)
        {
            return sb.Append(ErrnoFormat(stackalloc char[ErrnoMaxChars], linuxErrno));
        }

        /// <summary>
        /// Formats the provided integer value as string.
        /// If value is 0/1, returns "false"/"true".
        /// Otherwise, returns value like "BOOL(5)" or "BOOL(-12)".
        /// Note: input value is UInt32 because Bool8 and Bool16 should not be
        /// sign-extended, i.e. value should come from a call to GetU8 or GetU32,
        /// not a call to GetI8 or GetI32.
        /// Requires appropriately-sized destination buffer, Length >= BooleanMaxChars.
        /// Returns the formatted string (the filled portion of destination).
        /// </summary>
        public static Span<char> BooleanFormat(Span<char> destination, UInt32 boolVal)
        {
            string recognized;
            switch (boolVal)
            {
                case 0: recognized = "false"; break;
                case 1: recognized = "true"; break;
                default:
                    var pos = 0;
                    destination[pos++] = 'B';
                    destination[pos++] = 'O';
                    destination[pos++] = 'O';
                    destination[pos++] = 'L';
                    destination[pos++] = '(';
                    var end = DecimalI32FormatAtEnd(destination.Slice(pos), unchecked((int)boolVal));
                    end.CopyTo(destination.Slice(pos));
                    pos += end.Length;
                    destination[pos++] = ')';
                    return destination.Slice(0, pos);
            }

            recognized.AsSpan().CopyTo(destination);
            return destination.Slice(0, recognized.Length);
        }

        /// <summary>
        /// Converts an integer value from a boolean field into a string.
        /// If value is 0/1, returns "false"/"true".
        /// Otherwise, returns value like "BOOL(5)" or "BOOL(-12)".
        /// Note: input value is UInt32 because Bool8 and Bool16 should not be
        /// sign-extended, i.e. value should come from a call to GetU8 or GetU32,
        /// not a call to GetI8 or GetI32.
        /// </summary>
        public static string BooleanToString(UInt32 value)
        {
            return new string(BooleanFormat(stackalloc char[BooleanMaxChars], value));
        }

        /// <summary>
        /// If value is 0/1, appends "false"/"true".
        /// Otherwise, appends a string like "BOOL(5)" or "BOOL(-12)".
        /// Note: input value is UInt32 because Bool8 and Bool16 should not be
        /// sign-extended, i.e. value should come from a call to GetU8 or GetU32,
        /// not a call to GetI8 or GetI32.
        /// </summary>
        public static StringBuilder BooleanAppend(StringBuilder sb, UInt32 value)
        {
            return sb.Append(BooleanFormat(stackalloc char[BooleanMaxChars], value));
        }

        /// <summary>
        /// Converts the low 4 bits of the provided value to an uppercase hexadecimal character.
        /// </summary>
        public static char ToHexChar(int nibble)
        {
            const string HexChars = "0123456789ABCDEF";
            return HexChars[nibble & 0xF];
        }

        private static string[] NewErrnoStrings()
        {
            var strings = new string[134] {
            "ERRNO(0)",
            "EPERM(1)",
            "ENOENT(2)",
            "ESRCH(3)",
            "EINTR(4)",
            "EIO(5)",
            "ENXIO(6)",
            "E2BIG(7)",
            "ENOEXEC(8)",
            "EBADF(9)",
            "ECHILD(10)",
            "EAGAIN(11)",
            "ENOMEM(12)",
            "EACCES(13)",
            "EFAULT(14)",
            "ENOTBLK(15)",
            "EBUSY(16)",
            "EEXIST(17)",
            "EXDEV(18)",
            "ENODEV(19)",
            "ENOTDIR(20)",
            "EISDIR(21)",
            "EINVAL(22)",
            "ENFILE(23)",
            "EMFILE(24)",
            "ENOTTY(25)",
            "ETXTBSY(26)",
            "EFBIG(27)",
            "ENOSPC(28)",
            "ESPIPE(29)",
            "EROFS(30)",
            "EMLINK(31)",
            "EPIPE(32)",
            "EDOM(33)",
            "ERANGE(34)",
            "EDEADLK(35)",
            "ENAMETOOLONG(36)",
            "ENOLCK(37)",
            "ENOSYS(38)",
            "ENOTEMPTY(39)",
            "ELOOP(40)",
            "ERRNO(41)",
            "ENOMSG(42)",
            "EIDRM(43)",
            "ECHRNG(44)",
            "EL2NSYNC(45)",
            "EL3HLT(46)",
            "EL3RST(47)",
            "ELNRNG(48)",
            "EUNATCH(49)",
            "ENOCSI(50)",
            "EL2HLT(51)",
            "EBADE(52)",
            "EBADR(53)",
            "EXFULL(54)",
            "ENOANO(55)",
            "EBADRQC(56)",
            "EBADSLT(57)",
            "ERRNO(58)",
            "EBFONT(59)",
            "ENOSTR(60)",
            "ENODATA(61)",
            "ETIME(62)",
            "ENOSR(63)",
            "ENONET(64)",
            "ENOPKG(65)",
            "EREMOTE(66)",
            "ENOLINK(67)",
            "EADV(68)",
            "ESRMNT(69)",
            "ECOMM(70)",
            "EPROTO(71)",
            "EMULTIHOP(72)",
            "EDOTDOT(73)",
            "EBADMSG(74)",
            "EOVERFLOW(75)",
            "ENOTUNIQ(76)",
            "EBADFD(77)",
            "EREMCHG(78)",
            "ELIBACC(79)",
            "ELIBBAD(80)",
            "ELIBSCN(81)",
            "ELIBMAX(82)",
            "ELIBEXEC(83)",
            "EILSEQ(84)",
            "ERESTART(85)",
            "ESTRPIPE(86)",
            "EUSERS(87)",
            "ENOTSOCK(88)",
            "EDESTADDRREQ(89)",
            "EMSGSIZE(90)",
            "EPROTOTYPE(91)",
            "ENOPROTOOPT(92)",
            "EPROTONOSUPPORT(93)",
            "ESOCKTNOSUPPORT(94)",
            "EOPNOTSUPP(95)",
            "EPFNOSUPPORT(96)",
            "EAFNOSUPPORT(97)",
            "EADDRINUSE(98)",
            "EADDRNOTAVAIL(99)",
            "ENETDOWN(100)",
            "ENETUNREACH(101)",
            "ENETRESET(102)",
            "ECONNABORTED(103)",
            "ECONNRESET(104)",
            "ENOBUFS(105)",
            "EISCONN(106)",
            "ENOTCONN(107)",
            "ESHUTDOWN(108)",
            "ETOOMANYREFS(109)",
            "ETIMEDOUT(110)",
            "ECONNREFUSED(111)",
            "EHOSTDOWN(112)",
            "EHOSTUNREACH(113)",
            "EALREADY(114)",
            "EINPROGRESS(115)",
            "ESTALE(116)",
            "EUCLEAN(117)",
            "ENOTNAM(118)",
            "ENAVAIL(119)",
            "EISNAM(120)",
            "EREMOTEIO(121)",
            "EDQUOT(122)",
            "ENOMEDIUM(123)",
            "EMEDIUMTYPE(124)",
            "ECANCELED(125)",
            "ENOKEY(126)",
            "EKEYEXPIRED(127)",
            "EKEYREVOKED(128)",
            "EKEYREJECTED(129)",
            "EOWNERDEAD(130)",
            "ENOTRECOVERABLE(131)",
            "ERFKILL(132)",
            "EHWPOISON(133)",
            };

#if DEBUG
            for (int i = 0; i < strings.Length; i += 1)
            {
                Debug.Assert(strings[i].Length <= ErrnoMaxChars);
            }
#endif

            return strings;
        }
    }
}
