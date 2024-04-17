// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.LinuxTracepoints.Decode
{
    using System;
    using System.Runtime.CompilerServices;
    using System.Text;
    using CultureInfo = System.Globalization.CultureInfo;
    using Debug = System.Diagnostics.Debug;
    using IPAddress = System.Net.IPAddress;
    using MemoryMarshal = System.Runtime.InteropServices.MemoryMarshal;

    /// <summary>
    /// Helpers for converting raw event element data into .NET values such
    /// as string, DateTime.
    /// </summary>
    public static class PerfConvert
    {
        private const int StackAllocCharsMax =
#if DEBUG
            16; // Make small for testing.
#else
            512;
#endif

        private const long UnixEpochSeconds = 62135596800;
        private const long DaysToYear10000 = 3652059;
        private const long SecondsPerDay = 60 * 60 * 24;
        private const long MaxSeconds = DaysToYear10000 * SecondsPerDay - UnixEpochSeconds;

        /// <summary>
        /// ErrnoLookup(n) will return a non-null value if
        /// <![CDATA[0 <= n < ErrnoFirstUnknownValue]]>.
        /// </summary>
        private const int ErrnoFirstUnknownValue = 134;

        private static Encoding? encodingLatin1; // ISO-8859-1
        private static Encoding? encodingUTF32BE;
        private static string[]? errnoStrings;

        /// <summary>
        /// The maximum number of characters required by BooleanFormat is
        /// 17, e.g. "BOOL(-2147483648)".
        /// </summary>
        public const int BooleanMaxChars = 17;

        /// <summary>
        /// The maximum number of characters required by Char32Format is
        /// 2, i.e. the high and low surrogate pair.
        /// </summary>
        public const int Char32MaxChars = 2;

        /// <summary>
        /// The maximum number of characters required by DateTimeNoSubsecondsFormat is
        /// 20, e.g. "2020-02-02T02:02:02Z".
        /// </summary>
        public const int DateTimeNoSubsecondsMaxChars = 20;

        /// <summary>
        /// The maximum number of characters required by DateTimeFullFormat is
        /// 28, e.g. "2020-02-02T02:02:02.1234567Z".
        /// </summary>
        public const int DateTimeFullMaxChars = 28;

        /// <summary>
        /// Maximum number of characters required to convert an errno value,
        /// currently 20, e.g. "ENOTRECOVERABLE(131)".
        /// This is static readonly (not const) because it may be changed in the future.
        /// </summary>
        public static readonly int ErrnoMaxChars = 20;

        /// <summary>
        /// The maximum number of characters required by Float32Format is
        /// 15, e.g. "-3.40282347e+38".
        /// </summary>
        public const int Float32MaxChars = 15;

        /// <summary>
        /// The maximum number of characters required by Float64Format is
        /// 24, e.g. "-1.7976931348623157e+308".
        /// </summary>
        public const int Float64MaxChars = 24;

        /// <summary>
        /// The maximum number of characters required by GuidFormat is
        /// 36, e.g. "00000000-0000-0000-0000-000000000000".
        /// </summary>
        public const int GuidMaxChars = 36;

        /// <summary>
        /// The maximum number of characters required by Int32DecimalFormat is
        /// 11, e.g. "-2147483648".
        /// </summary>
        public const int Int32DecimalMaxChars = 11;

        /// <summary>
        /// The maximum number of characters required by Int64DecimalFormat is
        /// 20, e.g. "-9223372036854775808".
        /// </summary>
        public const int Int64DecimalMaxChars = 20;

        /// <summary>
        /// The maximum number of characters required by IPv4Format is
        /// 15, e.g. "255.255.255.255".
        /// </summary>
        public const int IPv4MaxChars = 15;

        /// <summary>
        /// The maximum number of characters required by IPv6Format is
        /// 45, e.g. "0000:0000:0000:0000:0000:ffff:192.168.100.228".
        /// </summary>
        public const int IPv6MaxChars = 45;

        /// <summary>
        /// The maximum number of characters required by UInt32DecimalFormat is
        /// 10, e.g. "4294967295".
        /// </summary>
        public const int UInt32DecimalMaxChars = 10;

        /// <summary>
        /// The maximum number of characters required by UInt32HexFormat is
        /// 10, e.g. "0xFFFFFFFF".
        /// </summary>
        public const int UInt32HexMaxChars = 10;

        /// <summary>
        /// The maximum number of characters required by UInt64DecimalFormat is
        /// 20, e.g. "18446744073709551615".
        /// </summary>
        public const int UInt64DecimalMaxChars = 20;

        /// <summary>
        /// The maximum number of characters required by UInt64HexFormat is
        /// 18, e.g. "0xFFFFFFFFFFFFFFFF".
        /// </summary>
        public const int UInt64HexMaxChars = 18;

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
        /// Converts the low 4 bits of the provided value to an uppercase hexadecimal character.
        /// </summary>
        public static char ToHexChar(int nibble)
        {
            const string HexChars = "0123456789ABCDEF";
            return HexChars[nibble & 0xF];
        }

        /// <summary>
        /// Formats the provided integer value as string, respecting convertOptions
        /// flag BoolOutOfRangeAsString: If value is 0/1, returns "false"/"true".
        /// Otherwise, if convertOptions has BoolOutOfRangeAsString, returns a string
        /// like "BOOL(5)" or "BOOL(-12)". Otherwise, returns value like 5 or -12.
        /// <br/>
        /// Note: input value is UInt32 because Bool8 and Bool16 should not be
        /// sign-extended, i.e. value should come from a call to GetU8 or GetU32,
        /// not a call to GetI8 or GetI32.
        /// Requires appropriately-sized destination buffer, Length >= BooleanMaxChars.
        /// Returns the formatted string (the filled portion of destination).
        /// </summary>
        public static Span<char> BooleanFormat(
            Span<char> destination,
            UInt32 boolVal,
            PerfConvertOptions convertOptions = PerfConvertOptions.Default)
        {
            string recognized;
            switch (boolVal)
            {
                case 0: recognized = "false"; break;
                case 1: recognized = "true"; break;
                default:
                    if (convertOptions.HasFlag(PerfConvertOptions.BoolOutOfRangeAsString))
                    {
                        var pos = 0;
                        destination[pos++] = 'B';
                        destination[pos++] = 'O';
                        destination[pos++] = 'O';
                        destination[pos++] = 'L';
                        destination[pos++] = '(';
                        var end = Int32DecimalFormatAtEnd(destination.Slice(pos), unchecked((int)boolVal));
                        end.CopyTo(destination.Slice(pos));
                        pos += end.Length;
                        destination[pos++] = ')';
                        return destination.Slice(0, pos);
                    }
                    else
                    {
                        var end = Int32DecimalFormatAtEnd(destination, unchecked((int)boolVal));
                        end.CopyTo(destination);
                        return destination.Slice(0, end.Length);
                    }
            }

            recognized.AsSpan().CopyTo(destination);
            return destination.Slice(0, recognized.Length);
        }

        /// <summary>
        /// Formats the provided integer value as string, respecting convertOptions
        /// flag BoolOutOfRangeAsString: If value is 0/1, returns "false"/"true".
        /// Otherwise, if convertOptions has BoolOutOfRangeAsString, returns a string
        /// like "BOOL(5)" or "BOOL(-12)". Otherwise, returns value like 5 or -12.
        /// <br/>
        /// Note: input value is UInt32 because Bool8 and Bool16 should not be
        /// sign-extended, i.e. value should come from a call to GetU8 or GetU32,
        /// not a call to GetI8 or GetI32.
        /// </summary>
        public static string BooleanToString(
            UInt32 value,
            PerfConvertOptions convertOptions = PerfConvertOptions.Default)
        {
            return new string(BooleanFormat(stackalloc char[BooleanMaxChars], value, convertOptions));
        }

        /// <summary>
        /// Formats the provided integer value as string, respecting convertOptions
        /// flag BoolOutOfRangeAsString: If value is 0/1, appends "false"/"true".
        /// Otherwise, if convertOptions has BoolOutOfRangeAsString, appends a string
        /// like "BOOL(5)" or "BOOL(-12)". Otherwise, appends value like 5 or -12.
        /// <br/>
        /// Note: input value is UInt32 because Bool8 and Bool16 should not be
        /// sign-extended, i.e. value should come from a call to GetU8 or GetU32,
        /// not a call to GetI8 or GetI32.
        /// </summary>
        public static StringBuilder BooleanAppend(
            StringBuilder sb,
            UInt32 value,
            PerfConvertOptions convertOptions = PerfConvertOptions.Default)
        {
            return sb.Append(BooleanFormat(stackalloc char[BooleanMaxChars], value, convertOptions));
        }

        /// <summary>
        /// Appends the provided value formatted as a JSON value, respecting
        /// convertOptions flag BoolOutOfRangeAsString. Returns sb.
        /// Note: input value is UInt32 because Bool8 and Bool16 should not be
        /// sign-extended, i.e. value should come from a call to GetU8 or GetU32,
        /// not a call to GetI8 or GetI32.
        /// </summary>
        public static StringBuilder BooleanAppendJson(
            StringBuilder sb,
            UInt32 value,
            PerfConvertOptions convertOptions = PerfConvertOptions.Default)
        {
            switch (value)
            {
                case 0: return sb.Append("false");
                case 1: return sb.Append("true");
                default:
                    if (convertOptions.HasFlag(PerfConvertOptions.BoolOutOfRangeAsString))
                    {
                        sb.Append("\"BOOL(");
                        Int32DecimalAppend(sb, unchecked((Int32)value));
                        return sb.Append(")\"");
                    }
                    break;
            }

            return Int32DecimalAppend(sb, unchecked((Int32)value));
        }

        /// <summary>
        /// Formats the provided integer value as a UTF-32 code point, or as '\uFFFD' if invalid.
        /// Requires appropriately-sized destination buffer, up to Char32MaxChars.
        /// Returns the formatted string (the filled portion of destination).
        /// </summary>
        public static Span<char> Char32Format(Span<char> destination, UInt32 utf32CodePoint)
        {
            if (utf32CodePoint <= 0xFFFF)
            {
                destination[0] = (char)utf32CodePoint;
                return destination.Slice(0, 1);
            }
            else if (utf32CodePoint <= 0x10FFFF)
            {
                var u = (utf32CodePoint - 0x10000) & 0xFFFFF;
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
        public static string Char32ToString(UInt32 utf32CodePoint)
        {
            return new string(Char32Format(stackalloc char[Char32MaxChars], utf32CodePoint));
        }

        /// <summary>
        /// Appends the provided UTF-32 code point. Returns sb.
        /// </summary>
        public static StringBuilder Char32Append(StringBuilder sb, UInt32 utf32CodePoint)
        {
            return sb.Append(Char32Format(stackalloc char[Char32MaxChars], utf32CodePoint));
        }

        /// <summary>
        /// Formats the provided DateTime value as a string like "2020-02-02T02:02:02Z".
        /// Requires appropriately-sized destination buffer, Length >= DateTimeNoSubsecondsMaxChars chars.
        /// Returns the formatted string (the filled portion of destination).
        /// </summary>
        public static Span<char> DateTimeNoSubsecondsFormat(Span<char> destination, DateTime value)
        {
            Debug.Assert(destination.Length >= DateTimeNoSubsecondsMaxChars);
            value.TryFormat(destination, out var pos, "s", null);
            destination[pos++] = 'Z';
            Debug.Assert(pos == DateTimeNoSubsecondsMaxChars);
            return destination.Slice(0, pos);
        }

        /// <summary>
        /// Formats the provided DateTime value as a string like "2020-02-02T02:02:02Z".
        /// </summary>
        public static string DateTimeNoSubsecondsToString(DateTime value)
        {
            return new string(DateTimeNoSubsecondsFormat(stackalloc char[DateTimeNoSubsecondsMaxChars], value));
        }

        /// <summary>
        /// Appends the provided DateTime value formatted as a string like "2020-02-02T02:02:02Z".
        /// </summary>
        public static StringBuilder DateTimeNoSubsecondsAppend(StringBuilder sb, DateTime value)
        {
            return sb.Append(DateTimeNoSubsecondsFormat(stackalloc char[DateTimeNoSubsecondsMaxChars], value));
        }

        /// <summary>
        /// Formats the provided DateTime value as a string like "2020-02-02T02:02:02.1234567Z".
        /// Requires appropriately-sized destination buffer, Length >= DateTimeFullMaxChars chars.
        /// Returns the formatted string (the filled portion of destination).
        /// </summary>
        public static Span<char> DateTimeFullFormat(Span<char> destination, DateTime value)
        {
            Debug.Assert(destination.Length >= DateTimeFullMaxChars);
            value.TryFormat(destination, out var pos, "s", null);
            destination[pos++] = '.';

            var ticks = unchecked((uint)(unchecked((ulong)value.Ticks) % 10000000u));
            pos += 7;
            for (var i = 1; i <= 7; i += 1)
            {
                var digit = ticks % 10;
                ticks /= 10;
                destination[pos - i] = (char)('0' + digit);
            }

            destination[pos++] = 'Z';
            Debug.Assert(pos == DateTimeFullMaxChars);
            return destination.Slice(0, pos);
        }

        /// <summary>
        /// Formats the provided DateTime value as a string like "2020-02-02T02:02:02.1234567Z".
        /// </summary>
        public static string DateTimeFullToString(DateTime value)
        {
            return new string(DateTimeFullFormat(stackalloc char[DateTimeFullMaxChars], value));
        }

        /// <summary>
        /// Appends the provided DateTime value formatted as a string like "2020-02-02T02:02:02.1234567Z".
        /// </summary>
        public static StringBuilder DateTimeFullAppend(StringBuilder sb, DateTime value)
        {
            return sb.Append(DateTimeFullFormat(stackalloc char[DateTimeFullMaxChars], value));
        }

        /// <summary>
        /// Returns true if the provided value is value that will return a non-null
        /// value from ErrnoLookup. Note that this returns true for 0, even though 0
        /// is not really a valid error number.
        /// </summary>
        public static bool ErrnoIsKnown(int linuxErrno)
        {
            return linuxErrno >= 0 && linuxErrno < ErrnoFirstUnknownValue;
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
                Debug.Assert(strings.Length == ErrnoFirstUnknownValue);
                errnoStrings = strings; // Cache the strings.
            }

            return linuxErrno >= 0 && linuxErrno < strings.Length
                ? strings[linuxErrno]
                : null;
        }

        /// <summary>
        /// If the specified value is a recognized Linux error number, returns a
        /// string like "ERRNO(0)" or "ENOENT(2)". Otherwise, if convertOptions has
        /// ErrnoUnknownAsString, returns a string like "ERRNO(404)". Otherwise,
        /// returns a string like "404".
        /// Requires appropriately-sized destination buffer, Length >= ErrnoMaxChars.
        /// Returns the formatted string (the filled portion of destination).
        /// </summary>
        public static Span<char> ErrnoFormat(
            Span<char> destination,
            Int32 errno,
            PerfConvertOptions convertOptions = PerfConvertOptions.Default)
        {
            var recognized = ErrnoLookup(errno);
            if (recognized != null)
            {
                recognized.AsSpan().CopyTo(destination);
                return destination.Slice(0, recognized.Length);
            }
            else if (convertOptions.HasFlag(PerfConvertOptions.ErrnoUnknownAsString))
            {
                var pos = 0;
                destination[pos++] = 'E';
                destination[pos++] = 'R';
                destination[pos++] = 'R';
                destination[pos++] = 'N';
                destination[pos++] = 'O';
                destination[pos++] = '(';
                var end = Int32DecimalFormatAtEnd(destination.Slice(pos), errno);
                end.CopyTo(destination.Slice(pos));
                pos += end.Length;
                destination[pos++] = ')';
                return destination.Slice(0, pos);
            }
            else
            {
                var end = Int32DecimalFormatAtEnd(destination, errno);
                end.CopyTo(destination);
                return destination.Slice(0, end.Length);
            }
        }

        /// <summary>
        /// If the specified value is a recognized Linux error number, returns a
        /// string like "ERRNO(0)" or "ENOENT(2)". Otherwise, if convertOptions has
        /// ErrnoUnknownAsString, returns a string like "ERRNO(404)". Otherwise,
        /// returns a string like "404".
        /// </summary>
        public static string ErrnoToString(
            Int32 linuxErrno,
            PerfConvertOptions convertOptions = PerfConvertOptions.Default)
        {
            return new string(ErrnoFormat(stackalloc char[ErrnoMaxChars], linuxErrno, convertOptions));
        }

        /// <summary>
        /// If the specified value is a recognized Linux error number, appends a
        /// string like "ERRNO(0)" or "ENOENT(2)". Otherwise, if convertOptions has
        /// ErrnoUnknownAsString, appends a string like "ERRNO(404)". Otherwise,
        /// returns a string like "404".
        /// </summary>
        public static StringBuilder ErrnoAppend(
            StringBuilder sb,
            Int32 linuxErrno,
            PerfConvertOptions convertOptions = PerfConvertOptions.Default)
        {
            return sb.Append(ErrnoFormat(stackalloc char[ErrnoMaxChars], linuxErrno, convertOptions));
        }

        /// <summary>
        /// Appends the provided value formatted as a JSON value, respecting convertOptions
        /// flags ErrnoKnownAsString and ErrnoUnknownAsString. Returns sb.
        /// </summary>
        public static StringBuilder ErrnoAppendJson(
            StringBuilder sb,
            Int32 value,
            PerfConvertOptions convertOptions = PerfConvertOptions.Default)
        {
            if (ErrnoIsKnown(value))
            {
                if (convertOptions.HasFlag(PerfConvertOptions.ErrnoKnownAsString))
                {
                    sb.Append('"');
                    sb.Append(ErrnoLookup(value));
                    return sb.Append('"');
                }
            }
            else
            {
                if (convertOptions.HasFlag(PerfConvertOptions.ErrnoUnknownAsString))
                {
                    sb.Append("\"ERRNO(");
                    Int32DecimalAppend(sb, value);
                    return sb.Append(")\"");
                }
            }

            return Int32DecimalAppend(sb, value);
        }

        /// <summary>
        /// Formats the provided value as a variable-length float string like
        /// "-3.4028235e+38" formatted using InvariantCulture and either "g" or "g9"
        /// (depending on convertOptions flag FloatExtraPrecision).
        /// Requires appropriately-sized destination buffer, up to Float32MaxChars.
        /// Returns the formatted string (the filled portion of destination).
        /// </summary>
        public static Span<char> Float32Format(
            Span<char> destination,
            Single value,
            PerfConvertOptions convertOptions = PerfConvertOptions.Default)
        {
            var format = convertOptions.HasFlag(PerfConvertOptions.FloatExtraPrecision) ? "g9" : "g";
            var ok = value.TryFormat(destination, out var len, format, CultureInfo.InvariantCulture);
            if (ok)
            {
                return destination.Slice(0, len);
            }
            else
            {
                Debug.Assert(destination.Length < Float32MaxChars);
                throw new ArgumentOutOfRangeException(nameof(destination), "Length < Float32MaxChars");
            }
        }

        /// <summary>
        /// Formats the provided value as a variable-length float string like
        /// "-3.4028235e+38" formatted using InvariantCulture and either "g" or "g9"
        /// (depending on convertOptions flag FloatExtraPrecision).
        /// </summary>
        public static string Float32ToString(
            Single value,
            PerfConvertOptions convertOptions = PerfConvertOptions.Default)
        {
            return new string(Float32Format(stackalloc char[Float32MaxChars], value, convertOptions));
        }

        /// <summary>
        /// Appends the provided value as a variable-length float string like
        /// "-3.4028235e+38" formatted using InvariantCulture and either "g" or "g9"
        /// (depending on convertOptions flag FloatExtraPrecision). Returns sb.
        /// </summary>
        public static StringBuilder Float32Append(
            StringBuilder sb,
            Single value,
            PerfConvertOptions convertOptions = PerfConvertOptions.Default)
        {
            return sb.Append(Float32Format(stackalloc char[Float32MaxChars], value, convertOptions));
        }

        /// <summary>
        /// Appends the provided value formatted as a JSON value, respecting convertOptions
        /// flags FloatExtraPrecision and FloatNonFiniteAsString. Returns sb.
        /// </summary>
        public static StringBuilder Float32AppendJson(
            StringBuilder sb,
            Single value,
            PerfConvertOptions convertOptions = PerfConvertOptions.Default)
        {
            if (float.IsFinite(value))
            {
                return Float32Append(sb, value, convertOptions);
            }
            else if (convertOptions.HasFlag(PerfConvertOptions.FloatNonFiniteAsString))
            {
                sb.Append('"');
                Float32Append(sb, value, convertOptions);
                return sb.Append('"');
            }
            else
            {
                return sb.Append("null");
            }
        }

        /// <summary>
        /// Formats the provided value as a variable-length double string like
        /// "-1.7976931348623157e+308" formatted using InvariantCulture and either "g" or "g17"
        /// (depending on convertOptions flag FloatExtraPrecision).
        /// Requires appropriately-sized destination buffer, up to Float64MaxChars.
        /// Returns the formatted string (the filled portion of destination).
        /// </summary>
        public static Span<char> Float64Format(
            Span<char> destination,
            Double value,
            PerfConvertOptions convertOptions = PerfConvertOptions.Default)
        {
            var format = convertOptions.HasFlag(PerfConvertOptions.FloatExtraPrecision) ? "g17" : "g";
            var ok = value.TryFormat(destination, out var len, format, CultureInfo.InvariantCulture);
            if (ok)
            {
                return destination.Slice(0, len);
            }
            else
            {
                Debug.Assert(destination.Length < Float64MaxChars);
                throw new ArgumentOutOfRangeException(nameof(destination), "Length < Float64MaxChars");
            }
        }

        /// <summary>
        /// Formats the provided value as a variable-length double string like
        /// "-1.7976931348623157e+308" formatted using InvariantCulture and either "g" or "g17"
        /// (depending on convertOptions flag FloatExtraPrecision).
        /// </summary>
        public static string Float64ToString(
            Double value,
            PerfConvertOptions convertOptions = PerfConvertOptions.Default)
        {
            return new string(Float64Format(stackalloc char[Float64MaxChars], value, convertOptions));
        }

        /// <summary>
        /// Appends the provided value as a variable-length double string like
        /// "-1.7976931348623157e+308" formatted using InvariantCulture and either "g" or "g17"
        /// (depending on convertOptions flag FloatExtraPrecision). Returns sb.
        /// </summary>
        public static StringBuilder Float64Append(
            StringBuilder sb,
            Double value,
            PerfConvertOptions convertOptions = PerfConvertOptions.Default)
        {
            return sb.Append(Float64Format(stackalloc char[Float64MaxChars], value, convertOptions));
        }

        /// <summary>
        /// Appends the provided value formatted as a JSON value, respecting convertOptions
        /// flags FloatExtraPrecision and FloatNonFiniteAsString. Returns sb.
        /// </summary>
        public static StringBuilder Float64AppendJson(
            StringBuilder sb,
            Double value,
            PerfConvertOptions convertOptions = PerfConvertOptions.Default)
        {
            if (double.IsFinite(value))
            {
                return Float64Append(sb, value, convertOptions);
            }
            else if (convertOptions.HasFlag(PerfConvertOptions.FloatNonFiniteAsString))
            {
                sb.Append('"');
                Float64Append(sb, value, convertOptions);
                return sb.Append('"');
            }
            else
            {
                return sb.Append("null");
            }
        }

        /// <summary>
        /// Formats the provided Guid value as a string like
        /// "12345678-1234-1234-1234-1234567890AB".
        /// Requires appropriately-sized destination buffer, up to GuidMaxChars.
        /// Returns the formatted string (the filled portion of destination).
        /// </summary>
        public static Span<char> GuidFormat(Span<char> destination, in Guid value)
        {
            var ok = value.TryFormat(destination, out var len, default);
            if (ok)
            {
                return destination.Slice(0, len);
            }
            else
            {
                Debug.Assert(destination.Length < GuidMaxChars);
                throw new ArgumentOutOfRangeException(nameof(destination), "Length < GuidMaxChars");
            }
        }

        /// <summary>
        /// Returns a new string like "12345678-1234-1234-1234-1234567890AB" for the provided value.
        /// </summary>
        public static string GuidToString(in Guid value)
        {
            return new string(GuidFormat(stackalloc char[GuidMaxChars], value));
        }

        /// <summary>
        /// Appends a string like "12345678-1234-1234-1234-1234567890AB" for the provided value.
        /// </summary>
        public static StringBuilder GuidAppend(StringBuilder sb, in Guid value)
        {
            return sb.Append(GuidFormat(stackalloc char[GuidMaxChars], value));
        }

        /// <summary>
        /// Returns the number of chars required to convertOptions the provided byte array as a string
        /// of hexadecimal bytes (e.g. "0D 0A").
        /// If bytesLength is 0, returns 0. Otherwise returns (3 * bytesLength - 1).
        /// </summary>
        public static int HexBytesLength(int bytesLength) =>
            bytesLength <= 0 ? 0 : 3 * bytesLength - 1;

        /// <summary>
        /// Formats the provided byte array as a string of hexadecimal bytes (e.g. "0D 0A").
        /// Requires appropriately-sized destination buffer, Length >= HexBytesLength(bytes.Length).
        /// Returns the formatted string (the filled portion of destination).
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">bytes.Length > 715827882</exception>"
        public static Span<char> HexBytesFormat(Span<char> destination, ReadOnlySpan<byte> bytes)
        {
            var bytesLength = bytes.Length;

            if (bytesLength > int.MaxValue / 3)
            {
                throw new ArgumentOutOfRangeException(nameof(bytes), "Length > 715827882");
            }

            if (0 < bytesLength)
            {
                var b = bytes[0];
                destination[0] = ToHexChar(b >> 4);
                destination[1] = ToHexChar(b);

                for (int pos = 1; pos < bytesLength; pos += 1)
                {
                    destination[(pos * 3) - 1] = ' ';
                    destination[(pos * 3) + 0] = ToHexChar(bytes[pos] >> 4);
                    destination[(pos * 3) + 1] = ToHexChar(bytes[pos]);
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
                chars[1] = ToHexChar(val >> 4);
                chars[2] = ToHexChar(val);
                sb.Append(chars.Slice(1));

                for (int pos = 1; pos < bytes.Length; pos += 1)
                {
                    val = bytes[pos];
                    chars[1] = ToHexChar(val >> 4);
                    chars[2] = ToHexChar(val);
                    sb.Append(chars);
                }

                return sb;
            }

            return sb;
        }

        /// <summary>
        /// Formats the provided integer value as a variable-length decimal string
        /// like "-1234" at the end of the destination buffer.
        /// Requires appropriately-sized destination buffer, up to Int32DecimalMaxChars.
        /// Returns the formatted string (the filled portion of destination).
        /// </summary>
        public static Span<char> Int32DecimalFormatAtEnd(Span<char> destination, Int32 value)
        {
            if (value < 0)
            {
                var len = UInt32DecimalFormatAtEnd(destination.Slice(1), unchecked((uint)-value)).Length;
                var start = destination.Length - len - 1;
                destination[start] = '-';
                return destination.Slice(start);
            }
            else
            {
                return UInt32DecimalFormatAtEnd(destination, unchecked((uint)value));
            }
        }

        /// <summary>
        /// Formats the provided integer value as a variable-length decimal string.
        /// </summary>
        public static string Int32DecimalToString(Int32 value)
        {
            return new string(Int32DecimalFormatAtEnd(stackalloc char[Int32DecimalMaxChars], value));
        }

        /// <summary>
        /// Appends the provided integer value as a variable-length decimal string.
        /// Returns sb.
        /// </summary>
        public static StringBuilder Int32DecimalAppend(StringBuilder sb, Int32 value)
        {
            return sb.Append(Int32DecimalFormatAtEnd(stackalloc char[Int32DecimalMaxChars], value));
        }

        /// <summary>
        /// Formats the provided integer value as a variable-length decimal string
        /// like "-1234" at the end of the destination buffer.
        /// Requires appropriately-sized destination buffer, up to Int64DecimalMaxChars.
        /// Returns the formatted string (the filled portion of destination).
        /// </summary>
        public static Span<char> Int64DecimalFormatAtEnd(Span<char> destination, Int64 value)
        {
            if (value < 0)
            {
                var len = UInt64DecimalFormatAtEnd(destination.Slice(1), unchecked((ulong)-value)).Length;
                var start = destination.Length - len - 1;
                destination[start] = '-';
                return destination.Slice(start);
            }
            else
            {
                return UInt64DecimalFormatAtEnd(destination, unchecked((ulong)value));
            }
        }

        /// <summary>
        /// Formats the provided integer value as a variable-length decimal string.
        /// </summary>
        public static string Int64DecimalToString(Int64 value)
        {
            return new string(Int64DecimalFormatAtEnd(stackalloc char[Int64DecimalMaxChars], value));
        }

        /// <summary>
        /// Appends the provided integer value as a variable-length decimal string.
        /// Returns sb.
        /// </summary>
        public static StringBuilder Int64DecimalAppend(StringBuilder sb, Int64 value)
        {
            return sb.Append(Int64DecimalFormatAtEnd(stackalloc char[Int64DecimalMaxChars], value));
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
            var end = UInt32DecimalFormatAtEnd(destination.Slice(pos), bytes[0]);
            end.CopyTo(destination);
            pos += end.Length;

            for (var i = 1; i < 4; i += 1)
            {
                destination[pos++] = '.';
                end = UInt32DecimalFormatAtEnd(destination.Slice(pos), bytes[i]);
                end.CopyTo(destination.Slice(pos));
                pos += end.Length;
            }

            return destination.Slice(0, pos);
        }

        /// <summary>
        /// Formats the provided value (big-endian) as an IPv4 address.
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
        /// Formats the provided 16-byte value as an IPv6 address.
        /// Requires appropriately-sized destination buffer, up to IPv6MaxChars.
        /// Returns the formatted string (the filled portion of destination).
        /// Note: Allocates an IPAddress object to convertOptions the address.
        /// </summary>
        public static Span<char> IPv6Format(Span<char> destination, ReadOnlySpan<byte> ipv6)
        {
            Debug.Assert(ipv6.Length == 16);
            var address = new IPAddress(ipv6); // Garbage
            var ok = address.TryFormat(destination, out var len);
            if (ok)
            {
                return destination.Slice(0, len);
            }
            else
            {
                Debug.Assert(destination.Length < IPv6MaxChars);
                throw new ArgumentOutOfRangeException(nameof(destination), "Length < IPv6MaxChars");
            }
        }

        /// <summary>
        /// Formats the provided 16-byte value as an IPv6 address.
        /// </summary>
        public static string IPv6ToString(ReadOnlySpan<byte> ipv6)
        {
            return new string(IPv6Format(stackalloc char[IPv6MaxChars], ipv6));
        }

        /// <summary>
        /// Appends the provided 16-byte value as an IPv6 address. Returns sb.
        /// </summary>
        public static StringBuilder IPv6Append(StringBuilder sb, ReadOnlySpan<byte> ipv6)
        {
            return sb.Append(IPv6Format(stackalloc char[IPv6MaxChars], ipv6));
        }

        /// <summary>
        /// Uses the provided encoding to convert a byte array to a string and appends it to the
        /// provided StringBuilder. Returns sb.
        /// Note that this conversion avoids generating garbage by using a stackalloc buffer.
        /// For short strings, there will be no garbage. For long strings, this will make one
        /// call to encoding.GetDecoder() which will allocate a decoder object to be used for
        /// the conversion.
        /// </summary>
        public static StringBuilder StringAppend(StringBuilder sb, ReadOnlySpan<byte> bytes, Encoding encoding)
        {
            var bytesLength = bytes.Length;
            var maxCharCount = encoding.GetMaxCharCount(bytesLength);
            if (maxCharCount <= StackAllocCharsMax)
            {
                Span<char> chars = stackalloc char[maxCharCount];
                var charCount = encoding.GetChars(bytes, chars);
                sb.Append(chars.Slice(0, charCount));
            }
            else
            {
                Span<char> chars = stackalloc char[StackAllocCharsMax];
                var decoder = encoding.GetDecoder();
                bool completed;
                do
                {
                    decoder.Convert(bytes, chars, true, out var bytesUsed, out var charsUsed, out completed);
                    bytes = bytes.Slice(bytesUsed);
                    sb.Append(chars.Slice(0, charsUsed));
                }
                while (!completed);
            }

            return sb;
        }

        /// <summary>
        /// Appends the string to sb as a JSON string. Returns sb.
        /// For example, if provided with input [Hello?World] where the ? is a NUL byte, this
        /// would append ["Hello\u0000World"].
        /// </summary>
        public static StringBuilder StringAppendJson(StringBuilder sb, ReadOnlySpan<char> chars)
        {
            sb.Append('"');
            AppendEscapedJson(sb, chars);
            return sb.Append('"');
        }

        /// <summary>
        /// Uses the specified encoding to convert the provided bytes into a string and appends
        /// it to sb as a JSON string. Returns sb.
        /// For example, if provided with input [Hello?World] where the ? is a NUL byte, this
        /// would append ["Hello\u0000World"].
        /// Note that this conversion avoids generating garbage by using a stackalloc buffer.
        /// For short strings, there will be no garbage. For long strings, this will make one
        /// call to encoding.GetDecoder() which will allocate a decoder object to be used for
        /// the conversion.
        /// </summary>
        public static StringBuilder StringAppendJson(StringBuilder sb, ReadOnlySpan<byte> bytes, Encoding encoding)
        {
            sb.Append('"');
            AppendEscapedJson(sb, bytes, encoding);
            return sb.Append('"');
        }


        /// <summary>
        /// Uses the Latin1 encoding to convert a byte array to a string and appends it to the
        /// provided StringBuilder. Returns sb.
        /// </summary>
        public static StringBuilder StringLatin1Append(StringBuilder sb, ReadOnlySpan<byte> bytes)
        {
            for (var i = 0; i < bytes.Length; i += 1)
            {
                sb.Append((char)bytes[i]);
            }

            return sb;
        }

        /// <summary>
        /// Uses the Latin1 encoding to convert the provided bytes into a string and appends
        /// it to sb as a JSON string. Returns sb.
        /// For example, if provided with input [Hello?World] where the ? is a NUL byte, this
        /// would append ["Hello\u0000World"].
        /// </summary>
        public static StringBuilder StringLatin1AppendJson(StringBuilder sb, ReadOnlySpan<byte> bytes)
        {
            sb.Append('"');

            foreach (var b in bytes)
            {
                AppendEscapedJson(sb, (char)b);
            }

            return sb.Append('"');
        }

        /// <summary>
        /// Formats the provided integer value as a variable-length decimal string
        /// like "1234" at the end of the destination buffer.
        /// Requires appropriately-sized destination buffer, up to UInt32DecimalMaxChars.
        /// Returns the formatted string (the filled portion of destination).
        /// </summary>
        public static Span<char> UInt32DecimalFormatAtEnd(Span<char> destination, UInt32 value)
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
        public static string UInt32DecimalToString(UInt32 value)
        {
            return new string(UInt32DecimalFormatAtEnd(stackalloc char[UInt32DecimalMaxChars], value));
        }

        /// <summary>
        /// Appends the provided integer value as a variable-length decimal string.
        /// Returns sb.
        /// </summary>
        public static StringBuilder UInt32DecimalAppend(StringBuilder sb, UInt32 value)
        {
            return sb.Append(UInt32DecimalFormatAtEnd(stackalloc char[UInt32DecimalMaxChars], value));
        }

        /// <summary>
        /// Formats the provided integer value as a variable-length decimal string
        /// like "1234" at the end of the destination buffer.
        /// Requires appropriately-sized destination buffer, up to UInt64DecimalMaxChars.
        /// Returns the formatted string (the filled portion of destination).
        /// </summary>
        public static Span<char> UInt64DecimalFormatAtEnd(Span<char> destination, UInt64 value)
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
        public static string UInt64DecimalToString(UInt64 value)
        {
            return new string(UInt64DecimalFormatAtEnd(stackalloc char[UInt64DecimalMaxChars], value));
        }

        /// <summary>
        /// Appends the provided integer value as a variable-length decimal string.
        /// Returns sb.
        /// </summary>
        public static StringBuilder UInt64DecimalAppend(StringBuilder sb, UInt64 value)
        {
            return sb.Append(UInt64DecimalFormatAtEnd(stackalloc char[UInt64DecimalMaxChars], value));
        }

        /// <summary>
        /// Formats the provided integer value as a variable-length hex string like
        /// "0x123ABC" at the end of the destination buffer.
        /// Requires appropriately-sized destination buffer, up to UInt32HexMaxChars.
        /// Returns the formatted string (the filled portion of destination).
        /// </summary>
        public static Span<char> UInt32HexFormatAtEnd(Span<char> destination, UInt32 value)
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
        public static string UInt32HexToString(UInt32 value)
        {
            return new string(UInt32HexFormatAtEnd(stackalloc char[UInt32HexMaxChars], value));
        }

        /// <summary>
        /// Appends a string like "0x123ABC". Returns sb.
        /// </summary>
        public static StringBuilder UInt32HexAppend(StringBuilder sb, UInt32 value)
        {
            return sb.Append(UInt32HexFormatAtEnd(stackalloc char[UInt32HexMaxChars], value));
        }

        /// <summary>
        /// Appends the provided value formatted as a JSON value, respecting convertOptions
        /// flag IntHexAsString. Returns sb.
        /// </summary>
        public static StringBuilder UInt32HexAppendJson(
            StringBuilder sb,
            UInt32 value,
            PerfConvertOptions convertOptions = PerfConvertOptions.Default)
        {
            if (convertOptions.HasFlag(PerfConvertOptions.IntHexAsString))
            {
                sb.Append('"');
                UInt32HexAppend(sb, value);
                return sb.Append('"');
            }
            else
            {
                return UInt32DecimalAppend(sb, value);
            }
        }

        /// <summary>
        /// Formats the provided integer value as a variable-length hex string like
        /// "0x123ABC" at the end of the destination buffer.
        /// Requires appropriately-sized destination buffer, up to UInt64HexMaxChars.
        /// Returns the formatted string (the filled portion of destination).
        /// </summary>
        public static Span<char> UInt64HexFormatAtEnd(Span<char> destination, UInt64 value)
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
        public static string UInt64HexToString(UInt64 value)
        {
            return new string(UInt64HexFormatAtEnd(stackalloc char[UInt64HexMaxChars], value));
        }

        /// <summary>
        /// Appends a string like "0x123ABC". Returns sb.
        /// </summary>
        public static StringBuilder UInt64HexAppend(StringBuilder sb, UInt64 value)
        {
            return sb.Append(UInt64HexFormatAtEnd(stackalloc char[UInt64HexMaxChars], value));
        }

        /// <summary>
        /// Appends the provided value formatted as a JSON value, respecting convertOptions
        /// flag IntHexAsString. Returns sb.
        /// </summary>
        public static StringBuilder UInt64HexAppendJson(
            StringBuilder sb,
            UInt64 value,
            PerfConvertOptions convertOptions = PerfConvertOptions.Default)
        {
            if (convertOptions.HasFlag(PerfConvertOptions.IntHexAsString))
            {
                sb.Append('"');
                UInt64HexAppend(sb, value);
                return sb.Append('"');
            }
            else
            {
                return UInt64DecimalAppend(sb, value);
            }
        }

        /// <summary>
        /// Converts a 32-bit Unix time_t (signed seconds since 1970) to a DateTime.
        /// </summary>
        public static DateTime UnixTime32ToDateTime(Int32 secondsSince1970)
        {
            return new DateTime((secondsSince1970 + UnixEpochSeconds) * 10000000, DateTimeKind.Utc);
        }

        /// <summary>
        /// Converts a 32-bit Unix time_t (signed seconds since 1970) to a string like
        /// "2020-02-02T02:02:02Z".
        /// Requires appropriately-sized destination buffer, Length >= UnixTime32MaxChars.
        /// Returns the formatted string (the filled portion of destination).
        /// </summary>
        public static Span<char> UnixTime32Format(Span<char> destination, Int32 secondsSince1970)
        {
            return DateTimeNoSubsecondsFormat(destination, UnixTime32ToDateTime(secondsSince1970));
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
        /// Appends a 32-bit Unix time_t (signed seconds since 1970) as a string like
        /// "2020-02-02T02:02:02Z". Returns sb.
        /// </summary>
        public static StringBuilder UnixTime32Append(StringBuilder sb, Int32 secondsSince1970)
        {
            return sb.Append(UnixTime32Format(stackalloc char[UnixTime32MaxChars], secondsSince1970));
        }

        /// <summary>
        /// Appends the provided value formatted as a JSON value, respecting convertOptions
        /// flag UnixTimeWithinRangeAsString. Returns sb.
        /// </summary>
        public static StringBuilder UnixTime32AppendJson(
            StringBuilder sb,
            Int32 value,
            PerfConvertOptions convertOptions = PerfConvertOptions.Default)
        {
            if (convertOptions.HasFlag(PerfConvertOptions.UnixTimeWithinRangeAsString))
            {
                sb.Append('"');
                UnixTime32Append(sb, value);
                return sb.Append('"');
            }

            return Int32DecimalAppend(sb, value);
        }

        /// <summary>
        /// Attempts to convert a 64-bit Unix time_t (signed seconds since 1970) to a DateTime.
        /// Returns null if the result is less than DateTime.MinValue or greater than
        /// DateTime.MaxValue.
        /// </summary>
        public static DateTime? UnixTime64ToDateTime(Int64 secondsSince1970)
        {
            if (UnixTime64IsInDateTimeRange(secondsSince1970))
            {
                return UnixTime64ToDateTimeUnchecked(secondsSince1970);
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Returns true if the provided 64-bit Unix time_t (signed seconds since 1970)
        /// can be converted to a DateTime.
        /// </summary>
        public static bool UnixTime64IsInDateTimeRange(Int64 secondsSince1970)
        {
            return secondsSince1970 >= -UnixEpochSeconds && secondsSince1970 < MaxSeconds;
        }

        /// <summary>
        /// Requires: UnixTime64IsInDateTimeRange(secondsSince1970) is true.
        /// Returns the DateTime for the provided 64-bit Unix time_t (signed seconds since 1970).
        /// </summary>
        public static DateTime UnixTime64ToDateTimeUnchecked(Int64 secondsSince1970)
        {
            Debug.Assert(UnixTime64IsInDateTimeRange(secondsSince1970));
            return new DateTime((secondsSince1970 + UnixEpochSeconds) * 10000000, DateTimeKind.Utc);
        }

        /// <summary>
        /// Converts a 64-bit Unix time_t (signed seconds since 1970) to a string.
        /// If year is in range 0001..9999, returns a string like "2020-02-02T02:02:02Z".
        /// Otherwise, if convertOptions has UnixTimeOutOfRangeAsString, returns a string like
        /// "TIME(-1234567890)". Otherwise, returns a string like -1234567890.
        /// Requires appropriately-sized destination buffer, Length >= UnixTime64MaxChars.
        /// Returns the formatted string (the filled portion of destination).
        /// </summary>
        public static Span<char> UnixTime64Format(
            Span<char> destination,
            Int64 secondsSince1970,
            PerfConvertOptions convertOptions = PerfConvertOptions.Default)
        {
            var maybe = UnixTime64ToDateTime(secondsSince1970);
            if (maybe is DateTime value)
            {
                return DateTimeNoSubsecondsFormat(destination, value);
            }
            else if (convertOptions.HasFlag(PerfConvertOptions.UnixTimeOutOfRangeAsString))
            {
                var pos = 0;
                destination[pos++] = 'T';
                destination[pos++] = 'I';
                destination[pos++] = 'M';
                destination[pos++] = 'E';
                destination[pos++] = '(';
                var end = Int64DecimalFormatAtEnd(destination.Slice(pos), secondsSince1970);
                end.CopyTo(destination.Slice(pos));
                pos += end.Length;
                destination[pos++] = ')';
                return destination.Slice(0, pos);
            }
            else
            {
                var end = Int64DecimalFormatAtEnd(destination, secondsSince1970);
                end.CopyTo(destination);
                return destination.Slice(0, end.Length);
            }
        }

        /// <summary>
        /// Converts a 64-bit Unix time_t (signed seconds since 1970) to a string.
        /// If year is in range 0001..9999, returns a string like "2020-02-02T02:02:02Z".
        /// Otherwise, if convertOptions has UnixTimeOutOfRangeAsString, returns a string like
        /// "TIME(-1234567890)". Otherwise, returns a string like -1234567890.
        /// </summary>
        public static string UnixTime64ToString(
            Int64 secondsSince1970,
            PerfConvertOptions convertOptions = PerfConvertOptions.Default)
        {
            return new string(UnixTime64Format(stackalloc char[UnixTime64MaxChars], secondsSince1970, convertOptions));
        }

        /// <summary>
        /// Converts a 64-bit Unix time_t (signed seconds since 1970) to a string.
        /// If year is in range 0001..9999, appends a string like "2020-02-02T02:02:02Z".
        /// Otherwise, if convertOptions has UnixTimeOutOfRangeAsString, appends a string like
        /// "TIME(-1234567890)". Otherwise, appends a string like -1234567890. Returns sb.
        /// </summary>
        public static StringBuilder UnixTime64Append(
            StringBuilder sb,
            Int64 secondsSince1970,
            PerfConvertOptions convertOptions = PerfConvertOptions.Default)
        {
            return sb.Append(UnixTime64Format(stackalloc char[UnixTime64MaxChars], secondsSince1970, convertOptions));
        }

        /// <summary>
        /// Appends the provided value formatted as a JSON value, respecting convertOptions
        /// flags UnixTimeWithinRangeAsString and UnixTimeOutOfRangeAsString. Returns sb.
        /// </summary>
        public static StringBuilder UnixTime64AppendJson(
            StringBuilder sb,
            Int64 value,
            PerfConvertOptions convertOptions = PerfConvertOptions.Default)
        {
            if (UnixTime64IsInDateTimeRange(value))
            {
                if (convertOptions.HasFlag(PerfConvertOptions.UnixTimeWithinRangeAsString))
                {
                    sb.Append('"');
                    DateTimeNoSubsecondsAppend(sb, UnixTime64ToDateTimeUnchecked(value));
                    return sb.Append('"');
                }
            }
            else
            {
                if (convertOptions.HasFlag(PerfConvertOptions.UnixTimeOutOfRangeAsString))
                {
                    sb.Append("\"TIME(");
                    Int64DecimalAppend(sb, value);
                    return sb.Append(")\"");
                }
            }

            return Int64DecimalAppend(sb, value);
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
            foreach (var s in strings)
            {
                Debug.Assert(s.Length <= ErrnoMaxChars);
            }
#endif

            return strings;
        }

        internal static void AppendEscapedJson(StringBuilder sb, ReadOnlySpan<byte> bytes, Encoding encoding)
        {
            if (encoding.CodePage == (BitConverter.IsLittleEndian ? 1200 : 1201))
            {
                AppendEscapedJson(sb, MemoryMarshal.Cast<byte, char>(bytes));
            }
            else
            {
                var bytesLength = bytes.Length;
                var maxCharCount = encoding.GetMaxCharCount(bytesLength);
                if (maxCharCount <= StackAllocCharsMax)
                {
                    Span<char> chars = stackalloc char[maxCharCount];
                    var charCount = encoding.GetChars(bytes, chars);
                    AppendEscapedJson(sb, chars.Slice(0, charCount));
                }
                else
                {
                    Span<char> chars = stackalloc char[StackAllocCharsMax];
                    var decoder = encoding.GetDecoder();
                    bool completed;
                    do
                    {
                        decoder.Convert(bytes, chars, true, out var bytesUsed, out var charsUsed, out completed);
                        bytes = bytes.Slice(bytesUsed);
                        AppendEscapedJson(sb, chars.Slice(0, charsUsed));
                    }
                    while (!completed);
                }
            }
        }

        internal static void AppendEscapedJson(StringBuilder sb, ReadOnlySpan<char> chars)
        {
            foreach (var c in chars)
            {
                AppendEscapedJson(sb, c);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void AppendEscapedJson(StringBuilder sb, char ch)
        {
            if (ch < 0x20)
            {
                switch (ch)
                {
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        sb.Append(stackalloc char[6] {
                            '\\',
                            'u',
                            ToHexChar(ch / 0x1000),
                            ToHexChar(ch / 0x100),
                            ToHexChar(ch / 0x10),
                            ToHexChar(ch),
                        });
                        break;
                }
            }
            else if (ch == '"' || ch == '\\')
            {
                sb.Append(stackalloc char[2] { '\\', ch });
            }
            else
            {
                sb.Append(ch);
            }
        }

        internal static void AppendEscapedJson(StringBuilder sb, UInt32 ch32)
        {
            AppendEscapedJson(sb, PerfConvert.Char32Format(stackalloc char[2], ch32));
        }

        internal static void AppendEscapedC(StringBuilder sb, ReadOnlySpan<byte> bytes, Encoding encoding)
        {
            if (encoding.CodePage == (BitConverter.IsLittleEndian ? 1200 : 1201))
            {
                AppendEscapedC(sb, MemoryMarshal.Cast<byte, char>(bytes));
            }
            else
            {
                var bytesLength = bytes.Length;
                var maxCharCount = encoding.GetMaxCharCount(bytesLength);
                if (maxCharCount <= StackAllocCharsMax)
                {
                    Span<char> chars = stackalloc char[maxCharCount];
                    var charCount = encoding.GetChars(bytes, chars);
                    AppendEscapedC(sb, chars.Slice(0, charCount));
                }
                else
                {
                    Span<char> chars = stackalloc char[StackAllocCharsMax];
                    var decoder = encoding.GetDecoder();
                    bool completed;
                    do
                    {
                        decoder.Convert(bytes, chars, true, out var bytesUsed, out var charsUsed, out completed);
                        bytes = bytes.Slice(bytesUsed);
                        AppendEscapedC(sb, chars.Slice(0, charsUsed));
                    }
                    while (!completed);
                }
            }
        }

        internal static void AppendEscapedC(StringBuilder sb, ReadOnlySpan<char> chars)
        {
            foreach (var c in chars)
            {
                AppendEscapedC(sb, c);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void AppendEscapedC(StringBuilder sb, char ch)
        {
            if (ch < 0x20)
            {
                switch (ch)
                {
                    case '\a': sb.Append("\\a"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\v': sb.Append("\\v"); break;
                    default:
                        sb.Append(stackalloc char[6] {
                            '\\',
                            'u',
                            ToHexChar(ch / 0x1000),
                            ToHexChar(ch / 0x100),
                            ToHexChar(ch / 0x10),
                            ToHexChar(ch),
                        });
                        break;
                }
            }
            else if (ch == '\\')
            {
                sb.Append("\\\\");
            }
            else
            {
                sb.Append(ch);
            }
        }

        internal static void AppendEscapedC(StringBuilder sb, UInt32 ch32)
        {
            AppendEscapedC(sb, PerfConvert.Char32Format(stackalloc char[2], ch32));
        }
    }
}
