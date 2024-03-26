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
        /// The perfEventDataSpan parameter that was passed to
        /// PerfDataFileReader.GetSampleEventInfo(). The ReadStart, Callchain, and
        /// RawDataStart fields are relative to this span.
        /// </summary>
        public ReadOnlySpan<byte> EventData;

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
        /// Offset into EventData where ReadValues begins.
        /// </summary>
        public int ReadStart;

        /// <summary>
        /// Valid if SampleType contains Read.
        /// Length of ReadValues.
        /// </summary>
        public int ReadLength;

        /// <summary>
        /// Valid if SampleType contains Callchain.
        /// Offset into EventData where Callchain begins.
        /// </summary>
        public int CallchainStart;

        /// <summary>
        /// Valid if SampleType contains Callchain.
        /// Length of Callchain.
        /// </summary>
        public int CallchainLength;

        /// <summary>
        /// Valid if SampleType contains Raw.
        /// Offset into EventData where RawData begins.
        /// </summary>
        public int RawDataStart;

        /// <summary>
        /// Valid if SampleType contains Raw.
        /// Length of RawData.
        /// </summary>
        public int RawDataLength;

        /// <summary>
        /// Valid if SampleType contains Read.
        /// Points into the data buffer, so data may be overwritten
        /// by subsequent operations. Data is in file-endian order.
        /// </summary>
        public ReadOnlySpan<byte> ReadValues => this.EventData.Slice(this.ReadStart, this.ReadLength);

        /// <summary>
        /// Valid if SampleType contains Callchain.
        /// Points into the data buffer, so data may be overwritten
        /// by subsequent operations. Data is in file-endian order.
        /// </summary>
        public ReadOnlySpan<byte> Callchain => this.EventData.Slice(this.CallchainStart, this.CallchainLength);

        /// <summary>
        /// Valid if SampleType contains Raw.
        /// Points into the data buffer, so data may be overwritten
        /// by subsequent operations. Data is in file-endian order.
        /// </summary>
        public ReadOnlySpan<byte> RawData => this.EventData.Slice(this.RawDataStart, this.RawDataLength);

        /// <summary>
        /// Returns flags indicating which data was present in the event.
        /// </summary>
        public readonly PerfEventAbi.PerfEventSampleFormat SampleType
        {
            get
            {
                var eventDesc = this.EventDesc;
                return eventDesc != null ? eventDesc.Attr.SampleType : 0;
            }
        }

        /// <summary>
        /// Returns the name of the event, or "" if not available.
        /// </summary>
        public readonly string Name
        {
            get
            {
                var eventDesc = this.EventDesc;
                return eventDesc != null ? eventDesc.Name : "";
            }
        }

        /// <summary>
        /// Returns the event's tracefs format metadata, or null if not available.
        /// </summary>
        public readonly PerfEventMetadata? Metadata
        {
            get
            {
                var eventDesc = this.EventDesc;
                return eventDesc != null ? eventDesc.Metadata : null;
            }
        }
    }
}
