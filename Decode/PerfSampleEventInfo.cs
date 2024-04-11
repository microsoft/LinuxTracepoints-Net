// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

#pragma warning disable CA1051 // Do not declare visible instance fields

namespace Microsoft.LinuxTracepoints.Decode
{
    using System;
    using System.Text;

    /// <summary>
    /// <para>
    /// Information about a sample event, typically returned by
    /// PerfDataFileReader.GetSampleEventInfo().
    /// </para><para>
    /// If the Format property is non-null, you can use it to access event
    /// information, including the event's fields.
    /// <code><![CDATA[
    /// var eventFormat = sampleEventInfo.Format;
    /// if (eventFormat == null)
    /// {
    ///     // Unexpected: Did not find TraceFS format metadata for this event.
    /// }
    /// else if (eventFormat.DecodingStyle != PerfEventDecodingStyle.EventHeader ||
    ///     !eventHeaderEnumerator.StartEvent(sampleEventInfo))
    /// {
    ///     // Decode using TraceFS format metadata.
    ///     // Typically the "common" fields are not interesting, so skip them.
    ///     var fieldsStart = eventFormat.CommonFieldCount;
    ///     var fieldsEnd = eventFormat.Fields.Count;
    ///     for (int i = fieldsStart; i < fieldsEnd; i += 1)
    ///     {
    ///         var fieldFormat = eventFormat.Fields[i];
    ///         var fieldValue = fieldFormat.GetFieldValue(sampleEventInfo);
    ///         // ... use fieldFormat and fieldValue to decode the field.
    ///     }
    /// }
    /// else
    /// {
    ///     while (eventHeaderEnumerator.MoveNext())
    ///     {
    ///         var itemInfo = eventHeaderEnumerator.GetItemInfo();
    ///         // ... use itemInfo to decode the field.
    ///     }
    /// }
    /// ]]></code>
    /// </para>
    /// </summary>
    public ref struct PerfSampleEventInfo
    {
        /// <summary>
        /// <para>
        /// The bytes of the event, including header and data, in event byte order.
        /// </para><para>
        /// The bytes consist of the 8-byte header followed by the data, both in event byte order.
        /// The format of the data depends on this.Header.Type.
        /// </para><para>
        /// This is the same as BytesMemory, i.e. this.BytesSpan == this.BytesMemory.Span.
        /// This field is provided as an optimization to avoid the overhead of redundant calls
        /// to BytesMemory.Span.
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
        /// Valid if GetSampleEventInfo() succeeded.
        /// Information about the session that collected the event, e.g. clock id and
        /// clock offset.
        /// </summary>
        public PerfEventSessionInfo SessionInfo;

        /// <summary>
        /// Valid if GetSampleEventInfo() succeeded.
        /// Information about the event (shared by all events with the same Id).
        /// </summary>
        public PerfEventDesc EventDesc;

        /// <summary>
        /// Valid if SampleType contains Identifier or Id.
        /// </summary>
        public UInt64 Id;

        /// <summary>
        /// Valid if SampleType contains IP.
        /// </summary>
        public UInt64 IP;

        /// <summary>
        /// Valid if SampleType contains Tid.
        /// </summary>
        public UInt32 Pid;

        /// <summary>
        /// Valid if SampleType contains Tid.
        /// </summary>
        public UInt32 Tid;

        /// <summary>
        /// Valid if SampleType contains Time.
        /// Use SessionInfo.TimeToRealTime() to convert to a TimeSpec.
        /// </summary>
        public UInt64 Time;

        /// <summary>
        /// Valid if SampleType contains Addr.
        /// </summary>
        public UInt64 Addr;

        /// <summary>
        /// Valid if SampleType contains StreamId.
        /// </summary>
        public UInt64 StreamId;

        /// <summary>
        /// Valid if SampleType contains Cpu.
        /// </summary>
        public UInt32 Cpu;

        /// <summary>
        /// Valid if SampleType contains Cpu.
        /// </summary>
        public UInt32 CpuReserved;

        /// <summary>
        /// Valid if SampleType contains Period.
        /// </summary>
        public UInt64 Period;

        /// <summary>
        /// Valid if SampleType contains Read.
        /// Offset into BytesMemory where ReadValues begins.
        /// </summary>
        public int ReadStart;

        /// <summary>
        /// Valid if SampleType contains Read.
        /// Length of ReadValues.
        /// </summary>
        public int ReadLength;

        /// <summary>
        /// Valid if SampleType contains Callchain.
        /// Offset into BytesMemory where Callchain begins.
        /// </summary>
        public int CallchainStart;

        /// <summary>
        /// Valid if SampleType contains Callchain.
        /// Length of Callchain.
        /// </summary>
        public int CallchainLength;

        /// <summary>
        /// Valid if SampleType contains Raw.
        /// Offset into BytesMemory where RawData begins.
        /// </summary>
        public int RawDataStart;

        /// <summary>
        /// Valid if SampleType contains Raw.
        /// Length of RawData.
        /// </summary>
        public int RawDataLength;

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
        /// Returns flags indicating which properties were present in the event.
        /// </summary>
        public readonly PerfEventAttrSampleType SampleType => this.EventDesc.Attr.SampleType;

        /// <summary>
        /// Event's full name (including the system name), e.g. "sched:sched_switch",
        /// or "" if not available.
        /// </summary>
        public readonly string Name => this.EventDesc.Name;

        /// <summary>
        /// Returns the event's tracefs format (decoding information), or null if not available.
        /// Use this to access this event's field values, i.e.
        /// <c>sampleEventInfo.Format.Fields[fieldIndex].GetFieldValue(sampleEventInfo)</c>.
        /// </summary>
        public readonly PerfEventFormat? Format => this.EventDesc.Format;

        /// <summary>
        /// Gets the Time as a PerfEventTimeSpec, using offset information from SessionInfo.
        /// </summary>
        public readonly PerfEventTimeSpec TimeSpec => this.SessionInfo.TimeToRealTime(this.Time);

        /// <summary>
        /// Gets the Time property as a DateTime, using offset information from SessionInfo.
        /// If the resulting DateTime is out of range (year before 1 or after 9999),
        /// returns DateTime.MinValue.
        /// </summary>
        public readonly DateTime DateTime
        {
            get
            {
                var ts = this.SessionInfo.TimeToRealTime(this.Time);
                DateTime result;
                if (PerfConvert.UnixTime64ToDateTime(ts.TvSec) is DateTime seconds)
                {
                    result = seconds.AddTicks(ts.TvNsec / 100);
                }
                else
                {
                    result = default;
                }
                return result;
            }
        }

        /// <summary>
        /// Gets the read_format data from the event in event-endian byte order.
        /// Valid if SampleType contains Read.
        /// </summary>
        public readonly ReadOnlyMemory<byte> ReadValues =>
            this.BytesMemory.Slice(this.ReadStart, this.ReadLength);

        /// <summary>
        /// Gets the read_format data from the event in event-endian byte order.
        /// Valid if SampleType contains Read.
        /// </summary>
        public readonly ReadOnlySpan<byte> ReadValuesSpan =>
            this.BytesSpan.Slice(this.ReadStart, this.ReadLength);

        /// <summary>
        /// Gets the callchain data from the event in event-endian byte order.
        /// Valid if SampleType contains Callchain.
        /// </summary>
        public readonly ReadOnlyMemory<byte> Callchain =>
            this.BytesMemory.Slice(this.CallchainStart, this.CallchainLength);

        /// <summary>
        /// Gets the callchain data from the event in event-endian byte order.
        /// Valid if SampleType contains Callchain.
        /// </summary>
        public readonly ReadOnlySpan<byte> CallchainSpan =>
            this.BytesSpan.Slice(this.CallchainStart, this.CallchainLength);

        /// <summary>
        /// Gets the raw field data from the event in event-endian byte order.
        /// This includes the data for the event's common fields, followed by user fields.
        /// Valid if SampleType contains Raw.
        /// </summary>
        public readonly ReadOnlyMemory<byte> RawData =>
            this.BytesMemory.Slice(this.RawDataStart, this.RawDataLength);

        /// <summary>
        /// Gets the raw field data from the event in event-endian byte order.
        /// This includes the data for the event's common fields, followed by user fields.
        /// Valid if SampleType contains Raw.
        /// </summary>
        public readonly ReadOnlySpan<byte> RawDataSpan =>
            this.BytesSpan.Slice(this.RawDataStart, this.RawDataLength);

        /// <summary>
        /// Gets the user field data from the event in event-endian byte order.
        /// This starts immediately after the event's common fields.
        /// Valid if SampleType contains Raw and format is available.
        /// </summary>
        public readonly ReadOnlyMemory<byte> UserData
        {
            get
            {
                var format = this.EventDesc.Format;
                return format == null || format.CommonFieldsSize > this.RawDataLength
                    ? default
                    : this.BytesMemory.Slice(
                        this.RawDataStart + format.CommonFieldsSize,
                        this.RawDataLength - format.CommonFieldsSize);

            }
        }

        /// <summary>
        /// Gets the user field data from the event in event-endian byte order.
        /// This starts immediately after the event's common fields.
        /// Valid if SampleType contains Raw and format is available.
        /// </summary>
        public readonly ReadOnlySpan<byte> UserDataSpan
        {
            get
            {
                var format = this.EventDesc.Format;
                return format == null || format.CommonFieldsSize > this.RawDataLength
                    ? default
                    : this.BytesSpan.Slice(
                        this.RawDataStart + format.CommonFieldsSize,
                        this.RawDataLength - format.CommonFieldsSize);
            }
        }

        /// <summary>
        /// Returns the full name of the event e.g. "sched:sched_switch",
        /// or "" if not available.
        /// </summary>
        public readonly override string ToString()
        {
            return this.EventDesc == null ? "" : this.EventDesc.Name;
        }

        /// <summary>
        /// <para>
        /// Appends the current event identity to the provided StringBuilder as a JSON string,
        /// e.g. <c>"MySystem:MyTracepointName"</c> (including the quotation marks).
        /// </para><para>
        /// The event identity includes the provider name and event name, e.g. "MySystem:MyTracepointName".
        /// This is commonly used as the value of the "n" property in the JSON rendering of the event.
        /// </para>
        /// </summary>
        public void AppendJsonEventIdentityTo(StringBuilder sb)
        {
            PerfConvert.StringAppendJson(sb, this.EventDesc.Name);
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
        /// by metaOptions and has a meaningful value available in the event info.
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
        public bool AppendJsonEventMetaTo(
            StringBuilder sb,
            bool addCommaBeforeNextItem = false,
            EventHeaderMetaOptions metaOptions = EventHeaderMetaOptions.Default,
            PerfJsonOptions jsonOptions = PerfJsonOptions.Default)
        {
            var w = new JsonWriter(sb, jsonOptions, addCommaBeforeNextItem);

            if (metaOptions.HasFlag(EventHeaderMetaOptions.Time) &&
                this.SampleType.HasFlag(PerfEventAttrSampleType.Time))
            {
                w.WriteValueNoEscapeName("time");
                if (SessionInfo.ClockOffsetKnown)
                {
                    sb.Append('"');
                    PerfConvert.DateTimeFullAppend(sb, this.DateTime);
                    sb.Append('"');
                }
                else
                {
                    PerfConvert.Float64gAppend(sb, this.Time / 1000000000.0);
                }
            }

            if (metaOptions.HasFlag(EventHeaderMetaOptions.Cpu) &&
                this.SampleType.HasFlag(PerfEventAttrSampleType.Cpu))
            {
                w.WriteValueNoEscapeName("cpu");
                PerfConvert.UInt32DecimalAppend(sb, this.Cpu);
            }
            
            if (this.SampleType.HasFlag(PerfEventAttrSampleType.Tid))
            {
                if (metaOptions.HasFlag(EventHeaderMetaOptions.Pid))
                {
                    w.WriteValueNoEscapeName("pid");
                    PerfConvert.UInt32DecimalAppend(sb, this.Pid);
                }

                if (metaOptions.HasFlag(EventHeaderMetaOptions.Tid))
                {
                    w.WriteValueNoEscapeName("tid");
                    PerfConvert.UInt32DecimalAppend(sb, this.Tid);
                }
            }

            if (0 != (metaOptions & (EventHeaderMetaOptions.Provider | EventHeaderMetaOptions.Event)))
            {
                var name = this.Name;
                if (!string.IsNullOrEmpty(name))
                {
                    var nameSpan = name.AsSpan();
                    var colonPos = nameSpan.IndexOf(':');
                    ReadOnlySpan<char> providerName, eventName;
                    if (colonPos < 0)
                    {
                        providerName = default;
                        eventName = nameSpan;
                    }
                    else
                    {
                        providerName = nameSpan.Slice(0, colonPos);
                        eventName = nameSpan.Slice(colonPos + 1);
                    }

                    if (metaOptions.HasFlag(EventHeaderMetaOptions.Provider) &&
                        !providerName.IsEmpty)
                    {
                        w.WriteValueNoEscapeName("provider");
                        PerfConvert.StringAppendJson(sb, providerName);
                    }

                    if (metaOptions.HasFlag(EventHeaderMetaOptions.Event) &&
                        !eventName.IsEmpty)
                    {
                        w.WriteValueNoEscapeName("event");
                        PerfConvert.StringAppendJson(sb, eventName);
                    }
                }
            }

            return w.Comma;
        }
    }
}
