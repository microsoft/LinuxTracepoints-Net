// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

#pragma warning disable CA1051 // Do not declare visible instance fields

namespace Microsoft.LinuxTracepoints.Decode
{
    using System;

    /// <summary>
    /// Information about a non-sample event, typically returned by
    /// PerfDataFileReader.GetNonSampleEventInfo().
    /// </summary>
    public ref struct PerfNonSampleEventInfo
    {
        /// <summary>
        /// The perfEventDataSpan parameter that was passed to
        /// PerfDataFileReader.GetNonSampleEventInfo().
        /// </summary>
        public ReadOnlySpan<byte> EventData;

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
    }
}
