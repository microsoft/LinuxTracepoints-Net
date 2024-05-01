// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.Performance.Toolkit.Plugins.PerfDataExtension
{
    using Microsoft.LinuxTracepoints.Decode;
    using System.Collections.ObjectModel;
    using System;
    using Debug = System.Diagnostics.Debug;
    using System.Collections.Generic;

    /// <summary>
    /// Information about a perf.data file. Used as the context object for events.
    /// </summary>
    public class PerfDataFileInfo
    {
        private readonly ReadOnlyMemory<byte>[] headers = new ReadOnlyMemory<byte>[(int)PerfHeaderIndex.LastFeature];

        protected PerfDataFileInfo(string filename, PerfByteReader byteReader)
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

        /// <summary>
        /// Returns the LongSize parsed from a PERF_HEADER_TRACING_DATA header,
        /// or 0 if no PERF_HEADER_TRACING_DATA has been parsed.
        /// </summary>
        public byte TracingDataLongSize { get; private set; }

        /// <summary>
        /// Returns the PageSize parsed from a PERF_HEADER_TRACING_DATA header,
        /// or 0 if no PERF_HEADER_TRACING_DATA has been parsed.
        /// </summary>
        public int TracingDataPageSize { get; private set; }

        /// <summary>
        /// Returns the header_page parsed from a PERF_HEADER_TRACING_DATA header,
        /// or empty if no PERF_HEADER_TRACING_DATA has been parsed.
        /// </summary>
        public ReadOnlyMemory<byte> TracingDataHeaderPage { get; private set; }

        /// <summary>
        /// Returns the header_event parsed from a PERF_HEADER_TRACING_DATA header,
        /// or empty if no PERF_HEADER_TRACING_DATA has been parsed.
        /// </summary>
        public ReadOnlyMemory<byte> TracingDataHeaderEvent { get; private set; }

        /// <summary>
        /// Returns the ftraces parsed from a PERF_HEADER_TRACING_DATA header,
        /// or empty if no PERF_HEADER_TRACING_DATA has been parsed.
        /// </summary>
        public ReadOnlyCollection<ReadOnlyMemory<byte>> TracingDataFtraces { get; private set; } =
            new ReadOnlyCollection<ReadOnlyMemory<byte>>(Array.Empty<ReadOnlyMemory<byte>>());

        /// <summary>
        /// Returns the kallsyms parsed from a PERF_HEADER_TRACING_DATA header,
        /// or empty if no PERF_HEADER_TRACING_DATA has been parsed.
        /// </summary>
        public ReadOnlyMemory<byte> TracingDataKallsyms { get; private set; }

        /// <summary>
        /// Returns the printk parsed from a PERF_HEADER_TRACING_DATA header,
        /// or empty if no PERF_HEADER_TRACING_DATA has been parsed.
        /// </summary>
        public ReadOnlyMemory<byte> TracingDataPrintk { get; private set; }

        /// <summary>
        /// Returns the saved_cmdline parsed from a PERF_HEADER_TRACING_DATA header,
        /// or empty if no PERF_HEADER_TRACING_DATA has been parsed.
        /// </summary>
        public ReadOnlyMemory<byte> TracingDataSavedCmdLine { get; private set; }

        /// <summary>
        /// Returns the raw data from the specified header. Data is in file-endian
        /// byte order (use ByteReader to do byte-swapping as appropriate).
        /// Returns empty if the requested header was not loaded from the file.
        /// </summary>
        public ReadOnlyMemory<byte> Header(PerfHeaderIndex headerIndex)
        {
            return (uint)headerIndex < (uint)this.headers.Length
                ? this.headers[(uint)headerIndex]
                : default;
        }

        /// <summary>
        /// Assumes the specified header is a nul-terminated Latin1 string.
        /// Returns a new string with the value of the header.
        /// </summary>
        public string HeaderString(PerfHeaderIndex headerIndex)
        {
            var header = Header(headerIndex).Span;
            if (header.Length <= 4)
            {
                return "";
            }
            else
            {
                // Starts with a 4-byte length, followed by a nul-terminated string.
                header = header.Slice(4);
                var nul = header.IndexOf((byte)0);
                return PerfConvert.EncodingLatin1.GetString(header.Slice(0, nul >= 0 ? nul : header.Length));
            }
        }

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

            this.TracingDataLongSize = reader.TracingDataLongSize;
            this.TracingDataPageSize = reader.TracingDataPageSize;
            this.TracingDataHeaderPage = CloneMemory(reader.TracingDataHeaderPage);
            this.TracingDataHeaderEvent = CloneMemory(reader.TracingDataHeaderEvent);

            var readerFtraces = reader.TracingDataFtraces;
            if (readerFtraces.Count != 0)
            {
                var newFtraces = new ReadOnlyMemory<byte>[readerFtraces.Count];
                for (int i = 0; i < newFtraces.Length; i++)
                {
                    newFtraces[i] = CloneMemory(readerFtraces[i]);
                }
                this.TracingDataFtraces = new ReadOnlyCollection<ReadOnlyMemory<byte>>(newFtraces);
            }

            this.TracingDataKallsyms = CloneMemory(reader.TracingDataKallsyms);
            this.TracingDataPrintk = CloneMemory(reader.TracingDataPrintk);
            this.TracingDataSavedCmdLine = CloneMemory(reader.TracingDataSavedCmdLine);

            for (int i = 0; i < this.headers.Length; i += 1)
            {
                this.headers[i] = CloneMemory(reader.Header((PerfHeaderIndex)i));
            }
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

        private static ReadOnlyMemory<T> CloneMemory<T>(ReadOnlyMemory<T> memory)
        {
            return memory.Length == 0
                ? default
                : memory.ToArray();
        }
    }
}
