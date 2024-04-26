// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.LinuxTracepoints.DecodeWpa
{
    using Microsoft.LinuxTracepoints.Decode;
    using InvalidOperationException = System.InvalidOperationException;

    public sealed class PerfFileInfo
    {
        private long sessionTimestampOffset;

        internal PerfFileInfo(string filename)
        {
            this.FileName = filename;
        }

        /// <summary>
        /// The filename from which this trace data was loaded.
        /// </summary>
        public string FileName { get; }

        /// <summary>
        /// Gets the value of the PERF_HEADER_HOSTNAME header, or "" if not present.
        /// </summary>
        public string HostName { get; private set; } = "";

        /// <summary>
        /// Gets the value of the PERF_HEADER_OSRELEASE header, or "" if not present.
        /// </summary>
        public string OSRelease { get; private set; } = "";

        /// <summary>
        /// Gets the value of the PERF_HEADER_ARCH header, or "" if not present.
        /// </summary>
        public string Arch { get; private set; } = "";

        /// <summary>
        /// Gets the value of the PERF_HEADER_NRCPUS header "available" field, or 0 if not present.
        /// </summary>
        public uint CpusAvailable { get; private set; }

        /// <summary>
        /// Gets the value of the PERF_HEADER_NRCPUS header "online" field, or 0 if not present.
        /// </summary>
        public uint CpusOnline { get; private set; }

        /// <summary>
        /// Gets a byte reader configured for the byte order of the file.
        /// </summary>
        public PerfByteReader ByteReader { get; private set; }

        /// <summary>
        /// Returns true if the data sources have been completely processed.
        /// </summary>
        public bool SourceParserFinished { get; private set; }

        /// <summary>
        /// Number of events in this file.
        /// </summary>
        public uint EventCount { get; private set; }

        /// <summary>
        /// Returns the clockid of the file timestamp, e.g. CLOCK_MONOTONIC.
        /// Returns uint.MaxValue if the file timestamp clockid is unknown.
        /// </summary>
        public uint ClockId { get; private set; } = uint.MaxValue;

        /// <summary>
        /// Returns the CLOCK_REALTIME value that corresponds to an event timestamp
        /// of 0 for this file. Returns UnixEpoch (1970) if the file did not contain
        /// clock offset information.
        /// </summary>
        public PerfTimeSpec ClockOffset { get; private set; }

        /// <summary>
        /// File-relative timestamp of the first event in this file
        /// (nanoseconds since ClockOffset).
        /// FirstEventTime > LastEventTime means the file contained no time-stamped events.
        /// </summary>
        public ulong FirstEventTime { get; private set; } = ulong.MaxValue;

        /// <summary>
        /// File-relative timestamp of the last event in this file
        /// (nanoseconds since ClockOffset).
        /// FirstEventTime > LastEventTime means the file contained no time-stamped events.
        /// </summary>
        public ulong LastEventTime { get; private set; } = ulong.MinValue;

        /// <summary>
        /// Must not be called before source parsing completes (see ProcessingComplete property).
        /// SessionTimestampOffset = ClockOffset - Session start time.
        /// SessionTimestampOffset must be added to an event's file-relative
        /// timestamp to get the event's session-relative timestamp.
        /// </summary>
        public long SessionTimestampOffset => this.SourceParserFinished
            ? this.sessionTimestampOffset
            : throw new InvalidOperationException("SessionTimestampOffset is not valid until SourceParserFinished is true.");

        /// <summary>
        /// Returns ClockOffset + FirstEventTime.
        /// </summary>
        public PerfTimeSpec FirstEventTimeSpec => this.ClockOffset.AddNanoseconds(this.FirstEventTime);

        /// <summary>
        /// Returns ClockOffset + LastEventTime.
        /// </summary>
        public PerfTimeSpec LastEventTimeSpec => this.ClockOffset.AddNanoseconds(this.LastEventTime);

        internal void SetFromReader(
            PerfDataFileReader reader,
            ulong firstEventTime,
            ulong lastEventTime,
            uint eventCount)
        {
            this.ByteReader = reader.ByteReader;
            this.EventCount = eventCount;

            this.HostName = reader.HeaderString(PerfHeaderIndex.Hostname);
            this.OSRelease = reader.HeaderString(PerfHeaderIndex.OSRelease);
            this.Arch = reader.HeaderString(PerfHeaderIndex.Arch);

            var nrCpus = reader.Header(PerfHeaderIndex.NrCpus).Span;
            if (nrCpus.Length >= 8)
            {
                this.CpusAvailable = this.ByteReader.ReadU32(nrCpus);
                this.CpusOnline = this.ByteReader.ReadU32(nrCpus.Slice(4));
            }

            this.ClockId = reader.SessionInfo.ClockId;
            this.ClockOffset = reader.SessionInfo.ClockOffset;
            this.FirstEventTime = firstEventTime;
            this.LastEventTime = lastEventTime;
        }

        internal void SetSourceParserFinished(long sessionTimestampOffset)
        {
            this.sessionTimestampOffset = sessionTimestampOffset;
            this.SourceParserFinished = true;
        }
    }
}
