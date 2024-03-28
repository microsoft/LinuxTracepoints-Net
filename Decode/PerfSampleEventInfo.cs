// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

#pragma warning disable CA1051 // Do not declare visible instance fields

namespace Microsoft.LinuxTracepoints.Decode
{
    using System;

    /// <summary>
    /// Information about a sample event, typically returned by
    /// PerfDataFileReader.GetSampleEventInfo().
    /// </summary>
    public ref struct PerfSampleEventInfo
    {
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
        /// Valid if SampleType contains IP.
        /// </summary>
        public UInt64 IP;

        /// <summary>
        /// Valid if SampleType contains Addr.
        /// </summary>
        public UInt64 Addr;

        /// <summary>
        /// Valid if SampleType contains Period.
        /// </summary>
        public UInt64 Period;

        /// <summary>
        /// Valid if SampleType contains Read.
        /// Offset into event data where ReadValues begins.
        /// </summary>
        public int ReadStart;

        /// <summary>
        /// Valid if SampleType contains Read.
        /// Length of ReadValues.
        /// </summary>
        public int ReadLength;

        /// <summary>
        /// Valid if SampleType contains Callchain.
        /// Offset into event data where Callchain begins.
        /// </summary>
        public int CallchainStart;

        /// <summary>
        /// Valid if SampleType contains Callchain.
        /// Length of Callchain.
        /// </summary>
        public int CallchainLength;

        /// <summary>
        /// Valid if SampleType contains Raw.
        /// Offset into event data where RawData begins.
        /// </summary>
        public int RawDataStart;

        /// <summary>
        /// Valid if SampleType contains Raw.
        /// Length of RawData.
        /// </summary>
        public int RawDataLength;


        /// <summary>
        /// Returns flags indicating which data was present in the event.
        /// </summary>
        public readonly PerfEventAbi.PerfEventSampleFormat SampleType =>
            this.EventDesc.Attr.SampleType;

        /// <summary>
        /// Returns the name of the event, or "" if not available.
        /// </summary>
        public readonly string Name => this.EventDesc.Name;

        /// <summary>
        /// Returns the event's tracefs format metadata, or null if not available.
        /// </summary>
        public readonly PerfEventMetadata? Metadata => this.EventDesc.Metadata;

        /// <summary>
        /// Gets the Time as a PerfEventTimeSpec, using offset information from SessionInfo.
        /// </summary>
        public readonly PerfEventTimeSpec TimeSpec => this.SessionInfo.TimeToRealTime(this.Time);

        /// <summary>
        /// Gets the Time as a DateTime, using offset information from SessionInfo.
        /// </summary>
        public readonly DateTime DateTime
        {
            get
            {
                var ts = this.SessionInfo.TimeToRealTime(this.Time);
                return DateTime.UnixEpoch.AddSeconds(ts.TvSec).AddTicks(ts.TvNsec / 100);
            }
        }

        /// <summary>
        /// Gets the read_format data from the event in event-endian byte order.
        /// Valid if SampleType contains Read.
        /// </summary>
        public readonly ReadOnlyMemory<byte> GetReadValues(in PerfEvent perfEvent)
        {
            return perfEvent.Data.Slice(this.ReadStart, this.ReadLength);
        }

        /// <summary>
        /// Gets the read_format data from the event in event-endian byte order.
        /// Valid if SampleType contains Read.
        /// </summary>
        public readonly ReadOnlySpan<byte> GetReadValuesSpan(in PerfEvent perfEvent)
        {
            return perfEvent.DataSpan.Slice(this.ReadStart, this.ReadLength);
        }

        /// <summary>
        /// Gets the callchain data from the event in event-endian byte order.
        /// Valid if SampleType contains Callchain.
        /// </summary>
        public readonly ReadOnlyMemory<byte> GetCallchain(in PerfEvent perfEvent)
        {
            return perfEvent.Data.Slice(this.CallchainStart, this.CallchainLength);
        }

        /// <summary>
        /// Gets the callchain data from the event in event-endian byte order.
        /// Valid if SampleType contains Callchain.
        /// </summary>
        public readonly ReadOnlySpan<byte> GetCallchainSpan(in PerfEvent perfEvent)
        {
            return perfEvent.DataSpan.Slice(this.CallchainStart, this.CallchainLength);
        }

        /// <summary>
        /// Gets the raw field data from the event in event-endian byte order.
        /// This includes the data for the event's common fields, followed by user fields.
        /// Valid if SampleType contains Raw.
        /// </summary>
        public readonly ReadOnlyMemory<byte> GetRawData(in PerfEvent perfEvent)
        {
            return perfEvent.Data.Slice(this.RawDataStart, this.RawDataLength);
        }

        /// <summary>
        /// Gets the raw field data from the event in event-endian byte order.
        /// This includes the data for the event's common fields, followed by user fields.
        /// Valid if SampleType contains Raw.
        /// </summary>
        public readonly ReadOnlySpan<byte> GetRawDataSpan(in PerfEvent perfEvent)
        {
            return perfEvent.DataSpan.Slice(this.RawDataStart, this.RawDataLength);
        }

        /// <summary>
        /// Gets the user field data from the event in event-endian byte order.
        /// This starts immediately after the event's common fields.
        /// Valid if SampleType contains Raw and metadata is available.
        /// </summary>
        public readonly ReadOnlyMemory<byte> GetUserData(in PerfEvent perfEvent)
        {
            var metadata = this.EventDesc.Metadata;
            return metadata == null || metadata.CommonFieldsSize > this.RawDataLength
                ? default
                : perfEvent.Data.Slice(
                    this.RawDataStart + metadata.CommonFieldsSize,
                    this.RawDataLength - metadata.CommonFieldsSize);
        }

        /// <summary>
        /// Gets the user field data from the event in event-endian byte order.
        /// This starts immediately after the event's common fields.
        /// Valid if SampleType contains Raw and metadata is available.
        /// </summary>
        public readonly ReadOnlySpan<byte> GetUserDataSpan(in PerfEvent perfEvent)
        {
            var metadata = this.EventDesc.Metadata;
            return metadata == null || metadata.CommonFieldsSize > this.RawDataLength
                ? default
                : perfEvent.DataSpan.Slice(
                    this.RawDataStart + metadata.CommonFieldsSize,
                    this.RawDataLength - metadata.CommonFieldsSize);
        }
    }
}
