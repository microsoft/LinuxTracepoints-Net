// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using Buffers = System.Buffers;
using Text = System.Text;

namespace Microsoft.LinuxTracepoints.Decode
{
    internal static class EventUtility
    {
        private static Buffers.SpanAction<char, ArraySegment<byte>>? actionCharsFromString8;
        private static Text.Encoding? encodingUTF32BE;

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

        public static Guid ReadGuidBigEndian(ReadOnlySpan<byte> bytes)
        {
            unchecked
            {
                var a = (int)(
                    bytes[0] << 24 |
                    bytes[1] << 16 |
                    bytes[2] << 8 |
                    bytes[3] << 0);
                var b = (short)(
                    bytes[4] << 8 |
                    bytes[5] << 0);
                var c = (short)(
                    bytes[6] << 8 |
                    bytes[7] << 0);
                return new Guid(a, b, c,
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

        /// <summary>
        /// Converts ISO-8859-1 bytes to a string.
        /// </summary>
        /// <param name="bytes">ISO-8859-1 bytes</param>
        /// <returns>New string.</returns>
        public static string ReadString8(ArraySegment<byte> bytes)
        {
            var action = actionCharsFromString8; // Get the cached delegate, if present.
            if (action == null)
            {
                action = CharsFromString8; // Create a new delegate.
                actionCharsFromString8 = action; // Cache the delegate.
            }

            return string.Create(bytes.Count, bytes, action);
        }

        private static void CharsFromString8(Span<char> chars, ArraySegment<byte> bytes)
        {
            var bytesArray = bytes.Array;
            var bytesIndex = bytes.Offset;
            for (int i = 0; i < chars.Length; i += 1, bytesIndex += 1)
            {
                chars[i] = (char)bytesArray[bytesIndex];
            }
        }
    }
}
