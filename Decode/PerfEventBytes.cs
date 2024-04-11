// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.LinuxTracepoints.Decode
{
    using System;
    using CultureInfo = System.Globalization.CultureInfo;
    using Debug = System.Diagnostics.Debug;

    /// <summary>
    /// Value returned by PerfDataFileReader.ReadEvent.
    /// <list type="bullet"><item>
    /// If this is a sample event (header.Type == PerfEventHeaderType.Sample), you will
    /// usually need to get additional information about the event (timestamp, cpu,
    /// decoding information, etc.) by calling PerfDataFileReader.GetSampleEventInfo().
    /// </item><item>
    /// If this is a non-sample event (header.Type != PerfEventHeaderType.Sample), you may
    /// be able to get additional information about the event (timestamp, cpu, etc.)
    /// by calling PerfDataFileReader.GetNonSampleEventInfo. However, this is not always
    /// necessary. In addition, many non-sample events do not support this additional
    /// information, especially if header.Type >= UserTypeStart or if the event appears
    /// before the FinishedInit event has been processed.
    /// </item></list>
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

        /// <summary>
        /// Gets a string with Header.Type and Header.Size like "Sample(64)".
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            return this.Header.Type.ToString() + '(' + this.Header.Size.ToString(CultureInfo.InvariantCulture) + ')';
        }

        /// <summary>
        /// <para>
        /// Tries to get event information from the event's prefix. The prefix is
        /// usually present only for sample events. If the event prefix is not
        /// present, this function may return an error or it may succeed but return
        /// incorrect information. In general, only use this on events where
        /// this.Header.Type == PerfEventHeaderType.Sample.
        /// </para><para>
        /// Note that this is the same as
        /// <c>dataFileReader.GetSampleEventInfo(this, out sampleEventInfo)</c>.
        /// </para>
        /// </summary>
        /// <param name="dataFileReader">The dataFileReader that returned this PerfEventBytes.</param>
        /// <param name="sampleEventInfo">Receives the sample event information.</param>
        /// <returns>Ok on success, other value if information could not be found for this event.</returns>
        public PerfDataFileResult GetSampleEventInfo(PerfDataFileReader dataFileReader, out PerfSampleEventInfo sampleEventInfo)
        {
            return dataFileReader.GetSampleEventInfo(this, out sampleEventInfo);
        }

        /// <summary>
        /// Tries to get event information from the event's suffix. The suffix
        /// is usually present only for non-sample kernel-generated events.
        /// If the event suffix is not present, this function may return an error or
        /// it may succeed but return incorrect information. In general:
        /// <list type="bullet"><item>
        /// Only use this on events where eventBytes.Header.Type != PerfEventHeaderType.Sample
        /// and eventBytes.Header.Type &lt; PerfEventHeaderType.UserTypeStart.
        /// </item><item>
        /// Only use this on events that come after the FinishedInit event.
        /// </item></list>
        /// Note that this is the same as
        /// <c>dataFileReader.GetNonSampleEventInfo(this, out nonSampleEventInfo)</c>.
        /// </summary>
        /// <param name="dataFileReader">The dataFileReader that returned this PerfEventBytes.</param>
        /// <param name="nonSampleEventInfo">Receives the non-sample event information.</param>
        /// <returns>Ok on success, other value if information could not be found for this event.</returns>
        public PerfDataFileResult GetNonSampleEventInfo(PerfDataFileReader dataFileReader, out PerfNonSampleEventInfo nonSampleEventInfo)
        {
            return dataFileReader.GetNonSampleEventInfo(this, out nonSampleEventInfo);
        }
    }
}
