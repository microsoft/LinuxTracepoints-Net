// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.LinuxTracepoints.Decode
{
    using System;
    using Debug = System.Diagnostics.Debug;
    using NumberStyles = System.Globalization.NumberStyles;
    using CultureInfo = System.Globalization.CultureInfo;
    using BinaryPrimitives = System.Buffers.Binary.BinaryPrimitives;
    using Text = System.Text;

    internal static class Utility
    {
        private const NumberStyles BaseNumberStyle = NumberStyles.AllowTrailingWhite;
        private static Text.Encoding? encodingLatin1; // ISO-8859-1
        private static Text.Encoding? encodingUTF32BE;

        public static Text.Encoding EncodingLatin1
        {
            get
            {
                var encoding = encodingLatin1; // Get the cached encoding, if available.
                if (encoding == null)
                {
                    encoding = Text.Encoding.GetEncoding(28591); // Create a new encoding.
                    encodingLatin1 = encoding; // Cache the encoding.
                }

                return encoding;
            }
        }

        public static Text.Encoding EncodingUTF32BE
        {
            get
            {
                var encoding = encodingUTF32BE; // Get the cached encoding, if available.
                if (encoding == null)
                {
                    encoding = new Text.UTF32Encoding(true, true); // Create a new encoding.
                    encodingUTF32BE = encoding; // Cache the encoding.
                }

                return encoding;
            }
        }

        public static char ToHexChar(int nibble)
        {
            const string HexChars = "0123456789ABCDEF";
            return HexChars[nibble & 0xF];
        }

        public static string ToHexString(ReadOnlySpan<byte> bytes)
        {
            var pos = 0;
            if (pos < bytes.Length)
            {
                var str = new Text.StringBuilder(bytes.Length * 3 - 1);

                var val = bytes[pos];
                str.Append(ToHexChar(val >> 4));
                str.Append(ToHexChar(val));

                for (pos += 1; pos < bytes.Length; pos += 1)
                {
                    str.Append(' ');
                    val = bytes[pos];
                    str.Append(ToHexChar(val >> 4));
                    str.Append(ToHexChar(val & 0xF));
                }

                return str.ToString();
            }

            return "";
        }

        public static Guid ReadGuidBigEndian(ReadOnlySpan<byte> bytes)
        {
            unchecked
            {
                return new Guid(
                    BinaryPrimitives.ReadUInt32BigEndian(bytes),
                    BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(4)),
                    BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(6)),
                    bytes[8],
                    bytes[9],
                    bytes[10],
                    bytes[11],
                    bytes[12],
                    bytes[13],
                    bytes[14],
                    bytes[15]);
            }
        }

        public static bool IsSpaceOrTab(char ch)
        {
            return ch == ' ' || ch == '\t';
        }

        public static bool IsEolChar(char ch)
        {
            return ch == '\r' || ch == '\n';
        }

        public static bool IsDecimalDigit(char ch)
        {
            return '0' <= ch && ch <= '9';
        }

        public static bool IsHexDigit(char ch)
        {
            var chLower = (uint)ch | 0x20;
            return
                ('0' <= ch && ch <= '9') ||
                ('a' <= chLower && chLower <= 'f');
        }

        /// <summary>
        /// Given pos pointing after the opening quote, returns pos after the closing quote.
        /// </summary>
        public static int ConsumeString(int pos, ReadOnlySpan<char> str, char quote)
        {
            var i = pos;
            while (i < str.Length)
            {
                char consumed = str[i];
                i += 1;

                if (consumed == quote)
                {
                    break;
                }
                else if (consumed == '\\')
                {
                    if (i >= str.Length)
                    {
                        Debug.WriteLine("EOF within '\\' escape");
                        break; // Unexpected.
                    }

                    // Ignore whatever comes after the backslash, which
                    // is significant if it is quote or '\\'.
                    i += 1;
                }
            }

            return i;
        }

        // Given p pointing after the opening brance, returns position after the closing brace.
        public static int ConsumeBraced(int pos, ReadOnlySpan<char> str, char open, char close)
        {
            var i = pos;
            int nesting = 1;
            while (i < str.Length)
            {
                char consumed = str[i];
                i += 1;
                if (consumed == close)
                {
                    nesting -= 1;
                    if (nesting == 0)
                    {
                        break;
                    }
                }
                else if (consumed == open)
                {
                    nesting += 1;
                }
            }

            return i;
        }

        public static bool ParseUInt(ReadOnlySpan<char> str, out uint value)
        {
            if (str.Length > 2 && str[0] == '0' && (str[1] == 'x' || str[1] == 'X'))
            {
                return uint.TryParse(
                    str.Slice(2),
                    BaseNumberStyle | NumberStyles.AllowHexSpecifier,
                    CultureInfo.InvariantCulture,
                    out value);
            }
            else
            {
                return uint.TryParse(
                    str,
                    BaseNumberStyle,
                    CultureInfo.InvariantCulture,
                    out value);
            }
        }

        public static bool ParseUInt(ReadOnlySpan<char> str, out ushort value)
        {
            if (str.Length > 2 && str[0] == '0' && (str[1] == 'x' || str[1] == 'X'))
            {
                return ushort.TryParse(
                    str.Slice(2),
                    BaseNumberStyle | NumberStyles.AllowHexSpecifier,
                    CultureInfo.InvariantCulture,
                    out value);
            }
            else
            {
                return ushort.TryParse(
                    str,
                    BaseNumberStyle,
                    CultureInfo.InvariantCulture,
                    out value);
            }
        }
    }
}
