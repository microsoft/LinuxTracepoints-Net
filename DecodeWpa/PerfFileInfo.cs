// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.LinuxTracepoints.DecodeWpa
{
    using Microsoft.LinuxTracepoints.Decode;
    using Debug = System.Diagnostics.Debug;

    /// <summary>
    /// Information about a perf.data file. Used as the context object for events.
    /// </summary>
    public class PerfFileInfo
    {
        protected PerfFileInfo(string filename, PerfByteReader byteReader)
        {
            this.FileName = filename;
            this.ByteReader = byteReader;
        }

        /// <summary>
        /// The filename from which this trace data was loaded.
        /// </summary>
        public string FileName { get; }

        /// <summary>
        /// Gets a byte reader configured for the byte order of the file's events.
        /// Same as new PerfByteReader(FromBigEndian).
        /// </summary>
        public PerfByteReader ByteReader { get; }

        /// <summary>
        /// Returns true if the file's events are in big-endian byte order, false if
        /// the events are in little-endian byte order. Same as ByteReader.FromBigEndian.
        /// </summary>
        public bool FromBigEndian => this.ByteReader.FromBigEndian;

        /// <summary>
        /// True if we've finished parsing the trace file's headers.
        /// This becomes true when we see a FinishedInit event or a Sample event.
        /// </summary>
        public bool HeaderAttributesAvailable { get; private set; }

        /// <summary>
        /// True if we've finished parsing the trace file.
        /// </summary>
        public bool FileAttributesAvailable { get; private set; }

        /// <summary>
        /// True if we've finished parsing all trace files in the session.
        /// </summary>
        public bool SessionAttributesAvailable { get; private set; }

        /// <summary>
        /// Gets the value of the PERF_HEADER_HOSTNAME header, or "" if not present.
        /// <br/>
        /// Not available until trace's headers are parsed (HeaderAttributesAvailable).
        /// </summary>
        public string HostName { get; private set; } = "";

        /// <summary>
        /// Gets the value of the PERF_HEADER_OSRELEASE header, or "" if not present.
        /// <br/>
        /// Not available until trace's headers are parsed (HeaderAttributesAvailable).
        /// </summary>
        public string OSRelease { get; private set; } = "";

        /// <summary>
        /// Gets the value of the PERF_HEADER_ARCH header, or "" if not present.
        /// <br/>
        /// Not available until trace's headers are parsed (HeaderAttributesAvailable).
        /// </summary>
        public string Arch { get; private set; } = "";

        /// <summary>
        /// Gets the value of the PERF_HEADER_NRCPUS header "available" field, or 0 if not present.
        /// <br/>
        /// Not available until trace's headers are parsed (HeaderAttributesAvailable).
        /// </summary>
        public uint CpusAvailable { get; private set; }

        /// <summary>
        /// Gets the value of the PERF_HEADER_NRCPUS header "online" field, or 0 if not present.
        /// <br/>
        /// Not available until trace's headers are parsed (HeaderAttributesAvailable).
        /// </summary>
        public uint CpusOnline { get; private set; }

        /// <summary>
        /// Returns the clockid of the file timestamp, e.g. CLOCK_MONOTONIC.
        /// Returns uint.MaxValue if the file timestamp clockid is unknown.
        /// <br/>
        /// Not available until we finish parsing the trace's headers (HeaderAttributesAvailable).
        /// </summary>
        public uint ClockId { get; private set; } = uint.MaxValue;

        /// <summary>
        /// Returns the CLOCK_REALTIME value that corresponds to an event timestamp
        /// of 0 for this file. Returns UnixEpoch (1970) if the file did not contain
        /// clock offset information.
        /// <br/>
        /// Not available until we finish parsing the trace's headers (HeaderAttributesAvailable).
        /// </summary>
        public PerfTimeSpec ClockOffset { get; private set; }

        /// <summary>
        /// Number of events in this file.
        /// <br/>
        /// Not available until trace's file is parsed (FileAttributesAvailable).
        /// </summary>
        public uint EventCount { get; private set; }

        /// <summary>
        /// File-relative timestamp of the first event in this file
        /// (nanoseconds since ClockOffset).
        /// FirstEventTime > LastEventTime means the file contained no time-stamped events.
        /// <br/>
        /// Not available until trace's file is parsed (FileAttributesAvailable).
        /// </summary>
        public ulong FirstEventTime { get; private set; } = ulong.MaxValue;

        /// <summary>
        /// File-relative timestamp of the last event in this file
        /// (nanoseconds since ClockOffset).
        /// FirstEventTime > LastEventTime means the file contained no time-stamped events.
        /// <br/>
        /// Not available until trace's file is parsed (FileAttributesAvailable).
        /// </summary>
        public ulong LastEventTime { get; private set; } = ulong.MinValue;

        /// <summary>
        /// Returns ClockOffset + FirstEventTime.
        /// <br/>
        /// Not available until trace's file is parsed (FileAttributesAvailable).
        /// </summary>
        public PerfTimeSpec FirstEventTimeSpec => this.ClockOffset.AddNanoseconds(this.FirstEventTime);

        /// <summary>
        /// Returns ClockOffset + LastEventTime.
        /// <br/>
        /// Not available until trace's file is parsed (FileAttributesAvailable).
        /// </summary>
        public PerfTimeSpec LastEventTimeSpec => this.ClockOffset.AddNanoseconds(this.LastEventTime);

        /// <summary>
        /// SessionTimestampOffset = ClockOffset - Session start time.
        /// SessionTimestampOffset must be added to an event's file-relative
        /// timestamp to get the event's session-relative timestamp.
        /// <br/>
        /// Not available until all trace files are parsed (SessionAttributesAvailable).
        /// </summary>
        public long SessionTimestampOffset { get; private set; }

        protected void SetHeaderAttributes(PerfDataFileReader reader)
        {
            Debug.Assert(!this.HeaderAttributesAvailable, "Header attributes already set");
            this.HeaderAttributesAvailable = true;

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
        }

        protected void SetFileAttributes(ulong firstEventTime, ulong lastEventTime, uint eventCount)
        {
            Debug.Assert(!this.FileAttributesAvailable, "File attributes already set");
            this.FileAttributesAvailable = true;

            this.EventCount = eventCount;
            this.FirstEventTime = firstEventTime;
            this.LastEventTime = lastEventTime;
        }

        protected void SetSessionAttributes(long sessionTimestampOffset)
        {
            Debug.Assert(!this.SessionAttributesAvailable, "Session attributes already set");
            this.SessionAttributesAvailable = true;

            this.SessionTimestampOffset = sessionTimestampOffset;
        }
    }
}
