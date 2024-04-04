// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.LinuxTracepoints.Decode
{
    using System;
    using Debug = System.Diagnostics.Debug;

    /// <summary>
    /// Value returned by PerfDataFileReader.ReadEvent.
    /// </summary>
    public readonly ref struct PerfEvent
    {
        /// <summary>
        /// Initializes a new instance of the PerfEvent struct.
        /// </summary>
        /// <param name="header">The header of the event, in host-endian byte order.</param>
        /// <param name="bytes">The bytes of the event, in event-endian byte order (including header).</param>
        /// <param name="bytesSpan">The span of the bytes parameter (i.e. bytes.Span).</param>
        public PerfEvent(
            PerfEventHeader header,
            ReadOnlyMemory<byte> bytes,
            ReadOnlySpan<byte> bytesSpan)
        {
            Debug.Assert(bytes.Length >= 8);
            Debug.Assert(bytes.Length == bytesSpan.Length);

            this.Header = header;
            this.BytesSpan = bytesSpan;
            this.Bytes = bytes;
        }

        /// <summary>
        /// <para>
        /// The header of the event in host byte order.
        /// </para><para>
        /// This is a copy of the first 8 bytes of the event, byte-swapped if event byte
        /// order is different from host byte order.
        /// </para>
        /// </summary>
        public PerfEventHeader Header { get; }

        /// <summary>
        /// <para>
        /// The bytes of the event, including header and data, in event byte order.
        /// </para><para>
        /// The bytes consist of the 8-byte header followed by the data, both in event byte order.
        /// The format of the data depends on this.Header.Type.
        /// </para><para>
        /// This is the same as Bytes, i.e. this.BytesSpan == this.Bytes.Span. This field
        /// is provided as an optimization to avoid the overhead of redundant calls to
        /// Bytes.Span.
        /// </para><para>
        /// This field points into the PerfDataFileReader's data buffer. The referenced data
        /// is only valid until the next call to ReadEvent.
        /// </para>
        /// </summary>
        public ReadOnlySpan<byte> BytesSpan { get; }

        /// <summary>
        /// <para>
        /// The bytes of the event, including header and data, in event byte order.
        /// </para><para>
        /// The bytes consist of the 8-byte header followed by the data, both in event byte order.
        /// The format of the data depends on this.Header.Type.
        /// </para><para>
        /// This field points into the PerfDataFileReader's data buffer. The referenced data
        /// is only valid until the next call to ReadEvent.
        /// </para>
        /// </summary>
        public ReadOnlyMemory<byte> Bytes { get; }
    }
}
