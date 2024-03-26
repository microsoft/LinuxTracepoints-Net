// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

#pragma warning disable CA1051 // Do not declare visible instance fields

namespace Microsoft.LinuxTracepoints.Decode
{
    using System;

    /// <summary>
    /// Value returned by PerfDataFileReader.ReadEvent.
    /// </summary>
    public ref struct PerfEventData
    {
        /// <summary>
        /// The header of the event. This is the first 8 bytes of the event,
        /// byte-swapped if necessary.
        /// </summary>
        public PerfEventAbi.PerfEventHeader Header;

        /// <summary>
        /// The bytes of the event, starting with the header, in file-endian order.
        /// This is the same as Memory.Span.
        /// </summary>
        public ReadOnlySpan<byte> Span;

        /// <summary>
        /// The bytes of the event, starting with the header, in file-endian order.
        /// </summary>
        public ReadOnlyMemory<byte> Memory;
    }
}
