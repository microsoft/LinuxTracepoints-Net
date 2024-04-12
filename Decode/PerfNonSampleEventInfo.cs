// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

#pragma warning disable CA1051 // Do not declare visible instance fields

namespace Microsoft.LinuxTracepoints.Decode
{
    using System;
    using StringBuilder = System.Text.StringBuilder;

    /// <summary>
    /// Information about a non-sample event, typically returned by
    /// PerfDataFileReader.GetNonSampleEventInfo().
    /// </summary>
    public ref struct PerfNonSampleEventInfo
    {
        /// <summary>
        /// <para>
        /// The bytes of the event, including header and data, in event byte order.
        /// </para><para>
        /// The bytes consist of the 8-byte header followed by the data, both in event byte order.
        /// The format of the data depends on this.Header.Type.
        /// </para><para>
        /// This is the same as BytesMemory, i.e. this.BytesSpan == this.BytesMemory.Span. This field
        /// is provided as an optimization to avoid the overhead of redundant calls to
        /// BytesMemory.Span.
        /// </para><para>
        /// This field points into the PerfDataFileReader's data buffer. The referenced data
        /// is only valid until the next call to ReadEvent.
        /// </para>
        /// </summary>
        public ReadOnlySpan<byte> BytesSpan;

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
        public ReadOnlyMemory<byte> BytesMemory;

        /// <summary>
        /// Valid if GetNonSampleEventInfo() succeeded.
        /// Information about the session that collected the event, e.g. clock id and
        /// clock offset.
        /// </summary>
        public PerfEventSessionInfo SessionInfo;

        /// <summary>
        /// Valid if GetNonSampleEventInfo() succeeded.
        /// Information about the event (shared by all events with the same Id).
        /// </summary>
        public PerfEventDesc EventDesc;

        /// <summary>
        /// Valid if SampleType contains Identifier or Id.
        /// </summary>
        public UInt64 Id;

        /// <summary>
        /// Valid if SampleType contains Cpu.
        /// </summary>
        public UInt32 Cpu;

        /// <summary>
        /// Valid if SampleType contains Cpu.
        /// </summary>
        public UInt32 CpuReserved;

        /// <summary>
        /// Valid if SampleType contains StreamId.
        /// </summary>
        public UInt64 StreamId;

        /// <summary>
        /// Valid if SampleType contains Time.
        /// Use SessionInfo.TimeToTimeSpec() to convert to a TimeSpec.
        /// </summary>
        public UInt64 Time;

        /// <summary>
        /// Valid if SampleType contains Tid.
        /// </summary>
        public UInt32 Pid;

        /// <summary>
        /// Valid if SampleType contains Tid.
        /// </summary>
        public UInt32 Tid;

        /// <summary>
        /// Returns true if the the session's event data is formatted in big-endian
        /// byte order. (Use ByteReader to do byte-swapping as appropriate.)
        /// </summary>
        public readonly bool FromBigEndian => this.SessionInfo.FromBigEndian;

        /// <summary>
        /// Returns a PerfByteReader configured for the byte order of the events
        /// in this session, i.e. PerfByteReader(FromBigEndian).
        /// </summary>
        public readonly PerfByteReader ByteReader => this.SessionInfo.ByteReader;

        /// <summary>
        /// Returns flags indicating which data was present in the event.
        /// </summary>
        public readonly PerfEventAttrSampleType SampleType => this.EventDesc.Attr.SampleType;

        /// <summary>
        /// Event's full name (including the system name), e.g. "dummy:HG",
        /// or "" if not available.
        /// </summary>
        public readonly string Name => this.EventDesc.Name;

        /// <summary>
        /// Gets the Time as a PerfEventTimeSpec, using offset information from SessionInfo.
        /// </summary>
        public readonly PerfEventTimeSpec TimeSpec => this.SessionInfo.TimeToTimeSpec(this.Time);

        /// <summary>
        /// Returns the full name of the event, e.g. "dummy:HG", or "" if not available.
        /// </summary>
        public readonly override string ToString()
        {
            return this.EventDesc == null ? "" : this.EventDesc.Name;
        }

        /// <summary>
        /// <para>
        /// Appends event metadata to the provided StringBuilder as a comma-separated list
        /// of 0 or more JSON name-value pairs, e.g. <c>"time": "...", "cpu": 3</c>
        /// (including the quotation marks).
        /// </para><para>
        /// PRECONDITION: Can be called after a successful call to reader.GetSampleEventInfo.
        /// </para><para>
        /// One name-value pair is appended for each metadata item that is both requested
        /// by infoOptions and has a meaningful value available in the event info.
        /// </para><para>
        /// The following metadata items are supported:
        /// <list type="bullet"><item>
        /// "time": "2024-01-01T23:59:59.123456789Z" if clock offset is known, or a float number of seconds
        /// (assumes the clock value is in nanoseconds), or omitted if not present.
        /// </item><item>
        /// "cpu": 3 (omitted if unavailable)
        /// </item><item>
        /// "pid": 123 (omitted if zero or unavailable)
        /// </item><item>
        /// "tid": 124 (omitted if zero or unavailable)
        /// </item><item>
        /// "provider": "SystemName" (omitted if unavailable)
        /// </item><item>
        /// "event": "TracepointName" (omitted if unavailable)
        /// </item></list>
        /// </para>
        /// </summary>
        /// <returns>
        /// Returns true if a comma would be needed before subsequent JSON output, i.e. if
        /// addCommaBeforeNextItem was true OR if any metadata items were appended.
        /// </returns>
        public readonly bool AppendJsonEventInfoTo(
            StringBuilder sb,
            bool addCommaBeforeNextItem = false,
            PerfInfoOptions infoOptions = PerfInfoOptions.Default,
            PerfJsonOptions jsonOptions = PerfJsonOptions.Default)
        {
            return this.SessionInfo.AppendJsonEventInfoTo(
                sb,
                addCommaBeforeNextItem,
                infoOptions,
                jsonOptions,
                this.SampleType,
                this.Time,
                this.Cpu,
                this.Pid,
                this.Tid,
                this.Name);
        }
    }
}
