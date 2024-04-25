// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.LinuxTracepoints.Decode
{
    using System;
    using BinaryPrimitives = System.Buffers.Binary.BinaryPrimitives;
    using CultureInfo = System.Globalization.CultureInfo;
    using Debug = System.Diagnostics.Debug;
    using NumberStyles = System.Globalization.NumberStyles;

    internal static class Utility
    {
        private const NumberStyles BaseNumberStyle = NumberStyles.AllowTrailingWhite;

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
