// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.LinuxTracepoints.Decode
{
    using System;
    using Debug = System.Diagnostics.Debug;

    /// <summary>
    /// Value returned by PerfDataFileReader.ReadEvent.
    /// </summary>
    public readonly ref struct PerfEventBytes
    {
        /// <summary>
        /// Initializes a new instance of the PerfEventBytes struct.
        /// </summary>
        /// <param name="header">The header of the event, in host-endian byte order.</param>
        /// <param name="memory">The memory of the event, in event-endian byte order (including header).</param>
        /// <param name="span">The span of the memory parameter (i.e. memory.Span).</param>
        public PerfEventBytes(
            PerfEventHeader header,
            ReadOnlyMemory<byte> memory,
            ReadOnlySpan<byte> span)
        {
            Debug.Assert(memory.Length >= 8);
            Debug.Assert(memory.Length == span.Length);

            this.Header = header;
            this.Span = span;
            this.Memory = memory;
        }

        /// <summary>
        /// <para>
        /// The header of the event in host byte order.
        /// </para><para>
        /// This is a copy of the first 8 memory of the event, byte-swapped if event byte
        /// order is different from host byte order.
        /// </para>
        /// </summary>
        public PerfEventHeader Header { get; }

        /// <summary>
        /// <para>
        /// The memory of the event, including header and data, in event byte order.
        /// </para><para>
        /// The memory consist of the 8-byte header followed by the data, both in event byte order.
        /// The format of the data depends on this.Header.Type.
        /// </para><para>
        /// This is the same as Memory, i.e. this.Span == this.Memory.Span. This field
        /// is provided as an optimization to avoid the overhead of redundant calls to
        /// Memory.Span.
        /// </para><para>
        /// This field points into the PerfDataFileReader's data buffer. The referenced data
        /// is only valid until the next call to ReadEvent.
        /// </para>
        /// </summary>
        public ReadOnlySpan<byte> Span { get; }

        /// <summary>
        /// <para>
        /// The memory of the event, including header and data, in event byte order.
        /// </para><para>
        /// The memory consist of the 8-byte header followed by the data, both in event byte order.
        /// The format of the data depends on this.Header.Type.
        /// </para><para>
        /// This field points into the PerfDataFileReader's data buffer. The referenced data
        /// is only valid until the next call to ReadEvent.
        /// </para>
        /// </summary>
        public ReadOnlyMemory<byte> Memory { get; }
    }
}
