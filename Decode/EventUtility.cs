// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.LinuxTracepoints.Decode
{
    using System;
    using BinaryPrimitives = System.Buffers.Binary.BinaryPrimitives;
    using Text = System.Text;

    internal static class EventUtility
    {
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
    }
}
