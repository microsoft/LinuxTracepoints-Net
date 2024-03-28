// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

#pragma warning disable CA1051 // Do not declare visible instance fields

namespace Microsoft.LinuxTracepoints.Decode
{
    using System;
    using Debug = System.Diagnostics.Debug;

    /// <summary>
    /// Value returned by PerfDataFileReader.ReadEvent.
    /// </summary>
    public ref struct PerfEvent
    {
        /// <summary>
        /// <para>
        /// The header of the event in host-endian byte order.
        /// </para><para>
        /// This is the first 8 bytes of the event, byte-swapped if appropriate.
        /// </para>
        /// </summary>
        public readonly PerfEventAbi.PerfEventHeader Header;

        /// <summary>
        /// <para>
        /// The data of the event in event-endian byte order.
        /// </para><para>
        /// The data is the portion of the event bytes after the header. The format depends
        /// on this.Header.Type.
        /// </para><para>
        /// This field points into the PerfDataFileReader's data buffer. The referenced data
        /// is only valid until the next call to ReadEvent.
        /// </para>
        /// </summary>
        public readonly ReadOnlyMemory<byte> Data;

        /// <summary>
        /// <para>
        /// The data of the event in event-endian byte order.
        /// </para><para>
        /// The data is the portion of the event bytes after the header. The format depends
        /// on this.Header.Type.
        /// </para><para>
        /// This is the same as Data, i.e. this.DataSpan == this.Data.Span. This field
        /// is provided as an optimization to avoid the overhead of redundant calls to
        /// Memory.Span.
        /// </para><para>
        /// This field points into the PerfDataFileReader's data buffer. The referenced data
        /// is only valid until the next call to ReadEvent.
        /// </para>
        /// </summary>
        public readonly ReadOnlySpan<byte> DataSpan;

        /// <summary>
        /// Initializes a new instance of the PerfEvent struct.
        /// </summary>
        /// <param name="header">The header of the event in host-endian byte order.</param>
        /// <param name="data">The data of the event (the bytes after the header).</param>
        /// <param name="dataSpan">The data of the event (i.e. data.Span).</param>
        public PerfEvent(
            PerfEventAbi.PerfEventHeader header,
            ReadOnlyMemory<byte> data,
            ReadOnlySpan<byte> dataSpan)
        {
            Debug.Assert(dataSpan == data.Span);
            this.Header = header;
            this.Data = data;
            this.DataSpan = dataSpan;
        }
    }
}
