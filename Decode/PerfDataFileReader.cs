﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.LinuxTracepoints.Decode
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.IO;
    using System.Runtime.InteropServices;
    using BinaryPrimitives = System.Buffers.Binary.BinaryPrimitives;
    using Debug = System.Diagnostics.Debug;
    using Encoding = System.Text.Encoding;

    /// <summary>
    /// Status returned by ReadEvent, GetSampleEventInfo, and GetNonSampleEventInfo.
    /// </summary>
    public enum PerfDataFileResult
    {
        /// <summary>
        /// The operation succeeded.
        /// </summary>
        Ok,

        /// <summary>
        /// ReadEvent:
        /// No more events because the end of the file was reached.
        /// </summary>
        EndOfFile,

        /// <summary>
        /// ReadEvent:
        /// No more events because the file contains invalid data.
        /// 
        /// GetSampleEventInfo or GetNonSampleEventInfo:
        /// Failed to get event info because the event contains invalid data.
        /// </summary>
        InvalidData,

        /// <summary>
        /// GetSampleEventInfo or GetNonSampleEventInfo:
        /// The event's ID was not found in the event attr table.
        /// </summary>
        IdNotFound,

        /// <summary>
        /// GetSampleEventInfo:
        /// Failed to get event info because the event contains headers that this
        /// decoder cannot parse.
        /// </summary>
        NotSupported,

        /// <summary>
        /// GetSampleEventInfo or GetNonSampleEventInfo:
        /// Cannot get sample information because the event's ID was not collected in
        /// the trace (the event's sample_type did not include PERF_SAMPLE_ID or
        /// PERF_SAMPLE_IDENTIFIER).
        /// </summary>
        NoData,
    }

    /// <summary>
    /// Reads events from a perf.data file.
    /// </summary>
    public class PerfDataFileReader : IDisposable
    {
        private const sbyte OffsetUnset = -1;
        private const sbyte OffsetNoId = -2;

        /// <summary>
        /// "\x17\x08\x44tracing"
        /// </summary>
        private static readonly byte[] TracingSignature = new byte[] {
            0x17, 0x08, 0x44,
            0x74, 0x72, 0x61, 0x63, 0x69, 0x6E, 0x67, // "tracing"
        };

        /// <summary>
        /// "header_page\x00"
        /// </summary>
        private static readonly byte[] header_page0 = new byte[] {
            0x68, 0x65, 0x61, 0x64, 0x65, 0x72, 0x5F, 0x70,
            0x61, 0x67, 0x65, 0x00,
        };

        /// <summary>
        /// "header_event\x00"
        /// </summary>
        private static readonly byte[] header_event0 = new byte[] {
            0x68, 0x65, 0x61, 0x64, 0x65, 0x72, 0x5F, 0x65,
            0x76, 0x65, 0x6E, 0x74, 0x00,
        };

        /// <summary>
        /// "common_type"
        /// </summary>
        private static readonly byte[] common_type = new byte[] {
            0x63, 0x6F, 0x6D, 0x6D, 0x6F, 0x6E, 0x5F, 0x74,
            0x79, 0x70, 0x65,
        };

        private UInt64 m_filePos;
        private UInt64 m_fileLen;
        private UInt64 m_dataBeginFilePos;
        private UInt64 m_dataEndFilePos;
        private Memory<byte> m_buffer; // Scratch buffer
        private readonly ReadOnlyMemory<byte>[] m_headers = new ReadOnlyMemory<byte>[(byte)PerfHeaderIndex.LastFeature];
        private readonly List<PerfEventDesc> m_eventDescList = new List<PerfEventDesc>();
        private readonly ReadOnlyCollection<PerfEventDesc> m_eventDescListReadOnly;
        private readonly Dictionary<UInt64, PerfEventDesc> m_eventDescById = new Dictionary<UInt64, PerfEventDesc>();
        private readonly ReadOnlyDictionary<UInt64, PerfEventDesc> m_eventDescByIdReadOnly;
        private PerfEventSessionInfo m_sessionInfo = PerfEventSessionInfo.Empty;
        private Stream m_file = Stream.Null;
        private bool m_fileShouldBeClosed;
        private PerfByteReader m_byteReader;
        private sbyte m_sampleIdOffset = OffsetUnset; // -1 = unset, -2 = no id.
        private sbyte m_nonSampleIdOffset = OffsetUnset; // -1 = unset, -2 = no id.
        private sbyte m_commonTypeOffset = OffsetUnset; // -1 = unset.
        private byte m_commonTypeSize;
        private bool m_parsedHeaderEventDesc;

        // HEADER_TRACING_DATA
        private bool m_parsedTracingData;
        private byte m_tracingDataLongSize;
        private int m_tracingDataPageSize;
        private ReadOnlyMemory<byte> m_headerPage; // Points into m_headers.
        private ReadOnlyMemory<byte> m_headerEvent; // Points into m_headers.
        private readonly List<ReadOnlyMemory<byte>> m_ftraces = new List<ReadOnlyMemory<byte>>(); // Points into m_headers.
        private readonly ReadOnlyCollection<ReadOnlyMemory<byte>> m_ftracesReadOnly;
        private readonly Dictionary<UInt32, PerfEventMetadata> m_metadataById = new Dictionary<UInt32, PerfEventMetadata>();
        private ReadOnlyMemory<byte> m_kallsyms; // Points into m_headers.
        private ReadOnlyMemory<byte> m_printk; // Points into m_headers.
        private ReadOnlyMemory<byte> m_cmdline; // Points into m_headers.

        public PerfDataFileReader()
        {
            Debug.Assert(ClockData.SizeOfStruct == Marshal.SizeOf<ClockData>());
            Debug.Assert(perf_file_section.SizeOfStruct == Marshal.SizeOf<perf_file_section>());
            Debug.Assert(perf_pipe_header.SizeOfStruct == Marshal.SizeOf<perf_pipe_header>());
            Debug.Assert(perf_file_header.SizeOfStruct == Marshal.SizeOf<perf_file_header>());

            Debug.Assert(PerfEventAttr.SizeOfStruct == Marshal.SizeOf<PerfEventAttr>());
            Debug.Assert(PerfEventHeaderMisc.SizeOfStruct == Marshal.SizeOf<PerfEventHeaderMisc>());
            Debug.Assert(PerfEventHeader.SizeOfStruct == Marshal.SizeOf<PerfEventHeader>());

            Debug.Assert(EventHeader.SizeOfStruct == Marshal.SizeOf<EventHeader>());
            Debug.Assert(EventHeaderExtension.SizeOfStruct == Marshal.SizeOf<EventHeaderExtension>());

            m_eventDescListReadOnly = m_eventDescList.AsReadOnly();
            m_eventDescByIdReadOnly = new ReadOnlyDictionary<UInt64, PerfEventDesc>(m_eventDescById);
            m_ftracesReadOnly = m_ftraces.AsReadOnly();
        }

        /// <summary>
        /// Returns true if the currently-opened file is big-endian.
        /// </summary>
        public bool FileIsBigEndian => m_byteReader.FromBigEndian;

        /// <summary>
        /// Returns ByteReader(FileIsBigEndian).
        /// </summary>
        public PerfByteReader ByteReader => m_byteReader;

        /// <summary>
        /// Returns the position within the input file of the event that will be
        /// read by the next call to ReadEvent().
        /// Returns UINT64_MAX after end-of-file or file error.
        /// </summary>
        public UInt64 FilePos => m_filePos;

        /// <summary>
        /// Returns the position within the input file of the first event.
        /// </summary>
        public UInt64 DataBeginFilePos => m_dataBeginFilePos;

        /// <summary>
        /// If the input file was recorded in pipe mode, returns UINT64_MAX.
        /// Otherwise, returns the position within the input file immediately after
        /// the last event.
        /// </summary>
        public UInt64 DataEndFilePos => m_dataEndFilePos;

        /// <summary>
        /// Combined data from perf_file_header::attrs and PERF_RECORD_HEADER_ATTR.
        /// </summary>
        public ReadOnlyCollection<PerfEventDesc> EventDescList => m_eventDescListReadOnly;

        /// <summary>
        /// Combined data from perf_file_header::attrs, PERF_RECORD_HEADER_ATTR,
        /// and HEADER_EVENT_DESC, indexed by sample ID (from attr.sample_id).
        /// </summary>
        public ReadOnlyDictionary<UInt64, PerfEventDesc> EventDescById => m_eventDescByIdReadOnly;

        /// <summary>
        /// Returns the LongSize parsed from a PERF_HEADER_TRACING_DATA header,
        /// or 0 if no PERF_HEADER_TRACING_DATA has been parsed.
        /// </summary>
        public byte TracingDataLongSize => m_tracingDataLongSize;

        /// <summary>
        /// Returns the PageSize parsed from a PERF_HEADER_TRACING_DATA header,
        /// or 0 if no PERF_HEADER_TRACING_DATA has been parsed.
        /// </summary>
        public int TracingDataPageSize => m_tracingDataPageSize;

        /// <summary>
        /// Returns the header_page parsed from a PERF_HEADER_TRACING_DATA header,
        /// or empty if no PERF_HEADER_TRACING_DATA has been parsed.
        /// </summary>
        public ReadOnlyMemory<byte> TracingDataHeaderPage => m_headerPage;

        /// <summary>
        /// Returns the header_event parsed from a PERF_HEADER_TRACING_DATA header,
        /// or empty if no PERF_HEADER_TRACING_DATA has been parsed.
        /// </summary>
        public ReadOnlyMemory<byte> TracingDataHeaderEvent => m_headerEvent;

        /// <summary>
        /// Returns the ftraces parsed from a PERF_HEADER_TRACING_DATA header,
        /// or empty if no PERF_HEADER_TRACING_DATA has been parsed.
        /// </summary>
        public ReadOnlyCollection<ReadOnlyMemory<byte>> TracingDataFtraces => m_ftracesReadOnly;

        /// <summary>
        /// Returns the kallsyms parsed from a PERF_HEADER_TRACING_DATA header,
        /// or empty if no PERF_HEADER_TRACING_DATA has been parsed.
        /// </summary>
        public ReadOnlyMemory<byte> TracingDataKallsyms => m_kallsyms;

        /// <summary>
        /// Returns the printk parsed from a PERF_HEADER_TRACING_DATA header,
        /// or empty if no PERF_HEADER_TRACING_DATA has been parsed.
        /// </summary>
        public ReadOnlyMemory<byte> TracingDataPrintk => m_printk;

        /// <summary>
        /// Returns the saved_cmdline parsed from a PERF_HEADER_TRACING_DATA header,
        /// or empty if no PERF_HEADER_TRACING_DATA has been parsed.
        /// </summary>
        public ReadOnlyMemory<byte> TracingDataSavedCmdLine => m_cmdline;

        /// <summary>
        /// Returns the raw data from the specified header. Data is in file-endian
        /// byte order (use ByteReader to do byte-swapping as appropriate).
        /// Returns empty if the requested header was not loaded from the file.
        public ReadOnlyMemory<byte> Header(PerfHeaderIndex headerIndex)
        {
            return (int)headerIndex < m_headers.Length
                ? m_headers[(int)headerIndex]
                : default;
        }

        /// <summary>
        /// Closes the file (if any) and suppresses finalization of this object.
        /// </summary>
        public void Dispose()
        {
            GC.SuppressFinalize(this);
            FileClose();
        }

        /// <summary>
        /// Closes the file (if any) and resets the reader to its default-constructed state.
        /// </summary>
        public void Close()
        {
            m_filePos = default;
            m_fileLen = default;
            m_dataBeginFilePos = default;
            m_dataEndFilePos = default;

            // m_buffer: keep our scratch buffer allocation.

            for (int i = 0; i < m_headers.Length; i++)
            {
                m_headers[i] = default;
            }

            m_eventDescList.Clear();
            m_eventDescById.Clear();
            m_sessionInfo = PerfEventSessionInfo.Empty;
            FileClose();
            m_byteReader = default;
            m_sampleIdOffset = OffsetUnset;
            m_nonSampleIdOffset = OffsetUnset;
            m_commonTypeOffset = OffsetUnset;
            m_commonTypeSize = default;
            m_parsedHeaderEventDesc = default;

            // HEADER_TRACING_DATA
            m_parsedTracingData = default;
            m_tracingDataLongSize = default;
            m_tracingDataPageSize = default;
            m_headerPage = default;
            m_headerEvent = default;
            m_ftraces.Clear();
            m_metadataById.Clear();
            m_kallsyms = default;
            m_printk = default;
            m_cmdline = default;
        }

        /// <summary>
        /// Closes the current input file (if any), then opens the specified
        /// perf.data file (Mode = Open, Access = Read, Share = Read + Delete) and
        /// reads the file header.
        /// 
        /// If not a pipe-mode file, loads metadata.
        /// 
        /// If a pipe-mode file, metadata will be loaded as the metadata events are
        /// encountered by ReadEvent.
        /// 
        /// On successful return, the file will be positioned before the first event.
        /// </summary>
        /// <returns>true on success, false if the file is not a valid perf.data file.</returns>
        public bool OpenFile(string filePath)
        {
            return OpenStream(
                new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete),
                false);
        }

        /// <summary>
        /// Closes the current input file (if any), then opens the specified
        /// perf.data file (Mode = Open, Access = Read, Share = Read + Delete) and
        /// reads the file header.
        /// 
        /// If not a pipe-mode file, loads metadata.
        /// 
        /// If a pipe-mode file, metadata will be loaded as the metadata events are
        /// encountered by ReadEvent.
        /// 
        /// On successful return, the file will be positioned before the first event.
        /// </summary>
        /// <returns>true on success, false if the file is not a valid perf.data file.</returns>
        public bool OpenFile(string filePath, int bufferSize)
        {
            return OpenStream(
                new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, bufferSize),
                false);
        }

        /// <summary>
        /// Closes the current input file (if any), then opens the specified stream
        /// and reads the file header.
        /// 
        /// If reading a pipe-mode file, metadata will be loaded as the metadata
        /// events are encountered by ReadEvent. No seeking will occur.
        /// 
        /// If not a pipe-mode file, loads metadata. The stream must be seekable.
        /// 
        /// On successful return, the file will be positioned before the first event.
        /// </summary>
        /// <param name="stream">
        /// The stream to read from. If the stream is seekable, stream must be at
        /// position 0. If the stream is not seekable, the file must have been recorded
        /// in pipe mode.
        /// </param>
        /// <param name="leaveOpen">
        /// If false (default), the stream will be Disposed when the PerfDataFileReader
        /// is Disposed or Closed. If true, the stream will not be Disposed.
        /// </param>
        /// <returns>true on success, false if the file contains invalid data.</returns>
        public bool OpenStream(Stream stream, bool leaveOpen = false)
        {
            if (stream.CanSeek && stream.Position != 0)
            {
                throw new ArgumentException(
                    "PerfDataFileReader.OpenStream: If seekable, stream must start at position 0.",
                    nameof(stream));
            }

            Close();
            FileOpen(stream, leaveOpen);

            perf_file_header header = default;
            var headerSpan = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref header, 1));

            if (!FileRead(headerSpan.Slice(0, perf_pipe_header.SizeOfStruct)))
            {
                // EOF in the middle of expected data.
                FileClose();
                return false;
            }

            UInt64 headerSize;
            if (header.pipe_header.magic == perf_pipe_header.Magic2)
            {
                m_byteReader = PerfByteReader.HostEndian;
                headerSize = header.pipe_header.size;
            }
            else if (header.pipe_header.magic == BinaryPrimitives.ReverseEndianness(perf_pipe_header.Magic2))
            {
                m_byteReader = PerfByteReader.SwapEndian;
                headerSize = BinaryPrimitives.ReverseEndianness(header.pipe_header.size);
            }
            else
            {
                // Bad magic.
                FileClose();
                return false;
            }

            if (m_buffer.Length < 0x10000)
            {
                BufferResize(0x10000, 0);
            }

            if (headerSize == perf_pipe_header.SizeOfStruct)
            {
                // Pipe mode, no metadata section, no seeking allowed.
                Debug.Assert(m_filePos == perf_pipe_header.SizeOfStruct);
                m_dataBeginFilePos = perf_pipe_header.SizeOfStruct;
                m_dataEndFilePos = UInt64.MaxValue;
                return true;
            }
            else if (headerSize < perf_file_header.SizeOfStruct)
            {
                // Bad header size.
                FileClose();
                return false;
            }

            // Normal mode, file expected to be seekable.
            FileLenUpdate();

            if (!FileRead(headerSpan.Slice(perf_pipe_header.SizeOfStruct)))
            {
                // EOF in the middle of expected data.
                FileClose();
                return false;
            }

            if (m_byteReader.ByteSwapNeeded)
            {
                header.ByteSwap();
            }

            if (!SectionValid(header.attrs) ||
                !SectionValid(header.data) ||
                !SectionValid(header.event_types) ||
                !LoadAttrs(header.attrs, header.attr_size) ||
                !LoadHeaders(header.data, header.flags0) ||
                !FileSeek(header.data.offset))
            {
                FileClose();
                return false;
            }

            Debug.Assert(m_filePos == header.data.offset);
            m_dataBeginFilePos = header.data.offset;
            m_dataEndFilePos = header.data.offset + header.data.size;

            return true;
        }

        /// <summary>
        /// <para>
        /// Reads the next event from the input file.
        /// </para><para>
        /// On success, returns Ok and sets perfEvent to the event data,
        /// which can be used with GetSampleEventInfo or GetNonSampleEventInfo,
        /// depending on the event type.
        /// </para><para>
        /// The event data is valid until the next call to ReadEvent.
        /// </para>
        /// </summary>
        /// <param name="perfEvent">
        /// Receives the event header and data. Use the perfEvent.Header.Type field
        /// to determine the event type. As appropriate, call GetSampleEventInfo or
        /// GetNonSampleEventInfo to decode the event.
        /// </param>
        /// <returns>Ok on success; EndOfFile or InvalidData if no more events.</returns>
        public PerfDataFileResult ReadEvent(out PerfEvent perfEvent)
        {
            PerfDataFileResult result;

            var eventStartFilePos = m_filePos;

            if (eventStartFilePos >= m_dataEndFilePos)
            {
                if (m_filePos == m_dataEndFilePos && m_filePos != UInt64.MaxValue)
                {
                    result = PerfDataFileResult.EndOfFile; // normal-mode has reached EOF.
                    goto ErrorOrEof;
                }

                throw new InvalidOperationException("PerfDataFileReader.ReadEvent: called after error or EOF.");
            }

            if (PerfEventHeader.SizeOfStruct > m_dataEndFilePos - eventStartFilePos)
            {
                result = PerfDataFileResult.InvalidData;
                goto ErrorOrEof;
            }

            Debug.Assert(m_buffer.Length >= 0x10000); // Should be resized during Open().
            var bufferSpan = m_buffer.Span;

            if (!FileRead(bufferSpan.Slice(0, PerfEventHeader.SizeOfStruct)))
            {
                if (m_filePos == eventStartFilePos &&
                    m_filePos != UInt64.MaxValue &&
                    m_dataEndFilePos == UInt64.MaxValue)
                {
                    result = PerfDataFileResult.EndOfFile; // pipe-mode has reached EOF.
                }
                else
                {
                    result = PerfDataFileResult.InvalidData; // EOF in the middle of expected data.
                }

                goto ErrorOrEof;
            }

            var eventHeader = MemoryMarshal.Read<PerfEventHeader>(bufferSpan);
            if (m_byteReader.ByteSwapNeeded)
            {
                eventHeader.ByteSwap();
            }

            if (eventHeader.Size < PerfEventHeader.SizeOfStruct)
            {
                result = PerfDataFileResult.InvalidData;
                goto ErrorOrEof;
            }

            var eventData = new PosLength(PerfEventHeader.SizeOfStruct, eventHeader.Size - PerfEventHeader.SizeOfStruct);

            if ((uint)eventData.Length > m_dataEndFilePos - m_filePos)
            {
                result = PerfDataFileResult.InvalidData;
                goto ErrorOrEof;
            }

            if (!FileRead(eventData.Slice(bufferSpan)))
            {
                result = PerfDataFileResult.InvalidData; // EOF in the middle of expected data.
                goto ErrorOrEof;
            }

            // Successfully read the basic event data.
            // Check for any special cases based on the type.
            switch (eventHeader.Type)
            {
                case PerfEventHeaderType.HeaderAttr:

                    if (eventData.Length >= (int)PerfEventAttrSize.Ver0)
                    {
                        var attrSize = (int)m_byteReader.ReadU32(bufferSpan.Slice(eventData.StartPos + PerfEventAttr.OffsetOfSize));
                        if (attrSize < (int)PerfEventAttrSize.Ver0 || attrSize > eventData.Length)
                        {
                            result = PerfDataFileResult.InvalidData;
                            goto ErrorOrEof;
                        }

                        var attrSizeCapped = Math.Min(attrSize, PerfEventAttr.SizeOfStruct);
                        if (!AddAttr(
                            bufferSpan.Slice(eventData.StartPos, attrSizeCapped),
                            default,
                            bufferSpan.Slice(eventData.StartPos + attrSize, eventData.Length - attrSize)))
                        {
                            result = PerfDataFileResult.InvalidData;
                            goto ErrorOrEof;
                        }
                    }
                    break;

                case PerfEventHeaderType.HeaderTracingData:

                    var oldEndPos = eventData.EndPos;
                    if (eventHeader.Size != 0x0C ||
                        !ReadPostEventData(4, ref eventData, ref bufferSpan))
                    {
                        result = PerfDataFileResult.InvalidData;
                        goto ErrorOrEof;
                    }

                    if (!m_parsedTracingData)
                    {
                        var tracingDataHeader = SetHeader(
                            PerfHeaderIndex.TracingData,
                            bufferSpan.Slice(oldEndPos, eventData.EndPos - oldEndPos));
                        ParseTracingData(tracingDataHeader);
                    }
                    break;

                case PerfEventHeaderType.HeaderBuildId:

                    SetHeader(PerfHeaderIndex.BuildId, eventData.Slice(bufferSpan));
                    break;

                case PerfEventHeaderType.Auxtrace:

                    if (ReadPostEventData(8, ref eventData, ref bufferSpan))
                    {
                        result = PerfDataFileResult.InvalidData;
                        goto ErrorOrEof;
                    }
                    break;

                case PerfEventHeaderType.HeaderFeature:

                    if (eventData.Length >= sizeof(UInt64))
                    {
                        var index64 = m_byteReader.ReadU64(bufferSpan.Slice(eventData.StartPos));
                        if (index64 < (uint)m_headers.Length)
                        {
                            var index = (PerfHeaderIndex)index64;
                            var featureHeader = SetHeader(
                                index,
                                bufferSpan.Slice(eventData.StartPos + sizeof(UInt64), eventData.Length - sizeof(UInt64)));
                            switch (index)
                            {
                                case PerfHeaderIndex.ClockId:
                                    ParseHeaderClockid(featureHeader);
                                    break;
                                case PerfHeaderIndex.ClockData:
                                    ParseHeaderClockData(featureHeader);
                                    break;
                            }
                        }
                    }
                    break;

                case PerfEventHeaderType.FinishedInit:

                    var eventDescHeader = m_headers[(int)PerfHeaderIndex.EventDesc];
                    if (!eventDescHeader.IsEmpty)
                    {
                        ParseHeaderEventDesc(eventDescHeader.Span);
                    }
                    break;
            }

            if (m_filePos > m_dataEndFilePos)
            {
                result = PerfDataFileResult.InvalidData;
                goto ErrorOrEof;
            }

            Debug.Assert(m_buffer.Length == bufferSpan.Length); // m_buffer and bufferSpan must be kept in sync.
            perfEvent = new PerfEvent(
                eventHeader,
                m_buffer.Slice(eventData.StartPos, eventData.Length),
                bufferSpan.Slice(eventData.StartPos, eventData.Length));
            return PerfDataFileResult.Ok;

        ErrorOrEof:

            FileClose();
            m_filePos = UInt64.MaxValue; // Subsequent ReadEvent should get EndOfFile.

            perfEvent = default;
            return result;
        }

        /// <summary>
        /// Tries to get event information from the event's prefix. The prefix is
        /// usually present only for sample events. If the event prefix is not
        /// present, this function may return an error or it may succeed but return
        /// incorrect information. In general, only use this on events where
        /// perfEvent.Header.Type == PERF_RECORD_SAMPLE.
        /// </summary>
        /// <param name="perfEventDataSpan">
        /// The data of the event to decode (e.g. the perfEvent.DataSpan from a
        /// perfEvent returned from a call to ReadEvent).
        /// </param>
        /// <param name="info">Receives event information.</param>
        /// <returns>Ok on success, other value if this event could not be decoded.</returns>
        public PerfDataFileResult GetSampleEventInfo(
            ReadOnlySpan<byte> perfEventDataSpan,
            out PerfSampleEventInfo info)
        {
            PerfDataFileResult result;
            UInt64 id;

            if (m_sampleIdOffset < 0)
            {
                result = PerfDataFileResult.NoData;
                goto Error;
            }
            else if (m_sampleIdOffset + sizeof(UInt64) > perfEventDataSpan.Length)
            {
                result = PerfDataFileResult.InvalidData;
                goto Error;
            }

            id = m_byteReader.ReadU64(perfEventDataSpan.Slice(m_sampleIdOffset));

            PerfEventDesc eventDesc;
            if (!m_eventDescById.TryGetValue(id, out eventDesc))
            {
                result = PerfDataFileResult.IdNotFound;
                goto Error;
            }

            var infoSampleTypes = eventDesc.Attr.SampleType;

            Debug.Assert(perfEventDataSpan.Length >= sizeof(UInt64)); // Otherwise id lookup would have failed.
            var pos = 0; // Skip PerfEventHeader.
            var endPos = perfEventDataSpan.Length & -sizeof(UInt64);

            result = PerfDataFileResult.InvalidData;

            if (0 != (infoSampleTypes & PerfEventAttrSampleType.Identifier))
            {
                Debug.Assert(pos != endPos); // Otherwise id lookup would have failed.
                pos += sizeof(UInt64); // Was read in id lookup.
            }

            if (0 == (infoSampleTypes & PerfEventAttrSampleType.IP))
            {
                info.IP = default;
            }
            else
            {
                if (pos == endPos)
                {
                    goto Error;
                }

                info.IP = m_byteReader.ReadU64(perfEventDataSpan.Slice(pos));
                pos += sizeof(UInt64);
            }

            if (0 == (infoSampleTypes & PerfEventAttrSampleType.Tid))
            {
                info.Pid = default;
                info.Tid = default;
            }
            else
            {
                if (pos == endPos)
                {
                    goto Error;
                }

                info.Pid = m_byteReader.ReadU32(perfEventDataSpan.Slice(pos));
                pos += sizeof(UInt32);
                info.Tid = m_byteReader.ReadU32(perfEventDataSpan.Slice(pos));
                pos += sizeof(UInt32);
            }

            if (0 == (infoSampleTypes & PerfEventAttrSampleType.Time))
            {
                info.Time = default;
            }
            else
            {
                if (pos == endPos)
                {
                    goto Error;
                }

                info.Time = m_byteReader.ReadU64(perfEventDataSpan.Slice(pos));
                pos += sizeof(UInt64);
            }

            if (0 == (infoSampleTypes & PerfEventAttrSampleType.Addr))
            {
                info.Addr = default;
            }
            else
            {
                if (pos == endPos)
                {
                    goto Error;
                }

                info.Addr = m_byteReader.ReadU64(perfEventDataSpan.Slice(pos));
                pos += sizeof(UInt64);
            }

            if (0 == (infoSampleTypes & PerfEventAttrSampleType.Id))
            {
                // Nothing to do.
            }
            else
            {
                if (pos == endPos)
                {
                    goto Error;
                }

                pos += sizeof(UInt64); // Was read in id lookup.
            }

            if (0 == (infoSampleTypes & PerfEventAttrSampleType.StreamId))
            {
                info.StreamId = default;
            }
            else
            {
                if (pos == endPos)
                {
                    goto Error;
                }

                info.StreamId = m_byteReader.ReadU64(perfEventDataSpan.Slice(pos));
                pos += sizeof(UInt64);
            }

            if (0 == (infoSampleTypes & PerfEventAttrSampleType.Cpu))
            {
                info.Cpu = default;
                info.CpuReserved = default;
            }
            else
            {
                if (pos == endPos)
                {
                    goto Error;
                }

                info.Cpu = m_byteReader.ReadU32(perfEventDataSpan.Slice(pos));
                pos += sizeof(UInt32);
                info.CpuReserved = m_byteReader.ReadU32(perfEventDataSpan.Slice(pos));
                pos += sizeof(UInt32);
            }

            if (0 == (infoSampleTypes & PerfEventAttrSampleType.Period))
            {
                info.Period = default;
            }
            else
            {
                if (pos == endPos)
                {
                    goto Error;
                }

                info.Period = m_byteReader.ReadU64(perfEventDataSpan.Slice(pos));
                pos += sizeof(UInt64);
            }

            if (0 == (infoSampleTypes & PerfEventAttrSampleType.Read))
            {
                info.ReadStart = default;
                info.ReadLength = default;
            }
            else
            {
                info.ReadStart = pos;

                const PerfEventAttrReadFormat SupportedReadFormats =
                    PerfEventAttrReadFormat.TotalTimeEnabled |
                    PerfEventAttrReadFormat.TotalTimeRunning |
                    PerfEventAttrReadFormat.Id |
                    PerfEventAttrReadFormat.Group |
                    PerfEventAttrReadFormat.Lost;

                var attrReadFormat = eventDesc.Attr.ReadFormat;
                if (0 != (attrReadFormat & ~SupportedReadFormats))
                {
                    result = PerfDataFileResult.NotSupported;
                    goto Error;
                }
                else if (0 == (attrReadFormat & PerfEventAttrReadFormat.Group))
                {
                    var itemsCount = 1 // value
                        + ((0 != (attrReadFormat & PerfEventAttrReadFormat.TotalTimeEnabled)) ? 1 : 0)
                        + ((0 != (attrReadFormat & PerfEventAttrReadFormat.TotalTimeRunning)) ? 1 : 0)
                        + ((0 != (attrReadFormat & PerfEventAttrReadFormat.Id)) ? 1 : 0)
                        + ((0 != (attrReadFormat & PerfEventAttrReadFormat.Lost)) ? 1 : 0);
                    var size = itemsCount * sizeof(UInt64);
                    if (endPos - pos < size)
                    {
                        goto Error;
                    }

                    pos += size;
                }
                else
                {
                    if (pos == endPos)
                    {
                        goto Error;
                    }

                    var nr = m_byteReader.ReadU64(perfEventDataSpan.Slice(pos));
                    if (nr >= 0x10000 / sizeof(UInt64))
                    {
                        goto Error;
                    }

                    var staticCount = 1 // nr
                        + ((0 != (attrReadFormat & PerfEventAttrReadFormat.TotalTimeEnabled)) ? 1 : 0)
                        + ((0 != (attrReadFormat & PerfEventAttrReadFormat.TotalTimeRunning)) ? 1 : 0);
                    var dynCount = 1 // value
                        + ((0 != (attrReadFormat & PerfEventAttrReadFormat.Id)) ? 1 : 0)
                        + ((0 != (attrReadFormat & PerfEventAttrReadFormat.Lost)) ? 1 : 0);
                    var size = sizeof(UInt64) * (staticCount + (int)nr * dynCount);
                    if (endPos - pos < size)
                    {
                        goto Error;
                    }

                    pos += size;
                }

                info.ReadLength = pos - info.ReadStart;
            }

            if (0 == (infoSampleTypes & PerfEventAttrSampleType.Callchain))
            {
                info.CallchainStart = default;
                info.CallchainLength = default;
            }
            else
            {
                if (pos == endPos)
                {
                    goto Error;
                }

                var nr = m_byteReader.ReadU64(perfEventDataSpan.Slice(pos));
                if (nr >= 0x10000 / sizeof(UInt64))
                {
                    goto Error;
                }

                var size = sizeof(UInt64) * (1 + (int)nr);
                if (endPos - pos < size)
                {
                    goto Error;
                }

                info.CallchainStart = pos;
                info.CallchainLength = size;

                pos += size;
            }

            if (0 == (infoSampleTypes & PerfEventAttrSampleType.Raw))
            {
                info.RawDataStart = default;
                info.RawDataLength = default;
            }
            else
            {
                if (pos == endPos)
                {
                    goto Error;
                }

                var rawSize = m_byteReader.ReadU32(perfEventDataSpan.Slice(pos));

                if ((uint)(endPos - pos - sizeof(UInt32)) < rawSize)
                {
                    goto Error;
                }

                info.RawDataStart = pos + sizeof(UInt32);
                info.RawDataLength = (int)rawSize;

                pos += (sizeof(UInt32) + (int)rawSize + sizeof(UInt64) - 1) & -sizeof(UInt64);
            }

            Debug.Assert(pos <= endPos);
            info.SessionInfo = m_sessionInfo;
            info.EventDesc = eventDesc;
            info.Id = id;
            return PerfDataFileResult.Ok;

        Error:

            info = default;
            return result;
        }

        /// <summary>
        /// Tries to get event information from the event's suffix. The suffix
        /// is usually present only for non-sample kernel-generated events.
        /// If the event suffix is not present, this function may return an error or
        /// it may succeed but return incorrect information. In general:
        /// <list type="bullet"><item>
        /// Only use this on events where perfEventData.Type != PERF_RECORD_SAMPLE
        /// and perfEventData.Type < PERF_RECORD_USER_TYPE_START.
        /// </item><item>
        /// Only use this on events that come after the PERF_RECORD_FINISHED_INIT
        /// event.
        /// </item></list>
        /// </summary>
        /// <param name="perfEventDataSpan">
        /// The data of the event to decode (e.g. the perfEvent.DataSpan from a
        /// perfEvent returned from a call to ReadEvent).
        /// </param>
        /// <param name="info">Receives event information.</param>
        /// <returns>Ok on success, other value if this event could not be decoded.</returns>
        public PerfDataFileResult GetNonSampleEventInfo(
            ReadOnlySpan<byte> perfEventDataSpan,
            out PerfNonSampleEventInfo info)
        {
            PerfDataFileResult result;
            UInt64 id;

            if (m_nonSampleIdOffset < 0)
            {
                result = PerfDataFileResult.NoData;
                goto Error;
            }
            else if (m_nonSampleIdOffset > perfEventDataSpan.Length)
            {
                result = PerfDataFileResult.InvalidData;
                goto Error;
            }

            id = m_byteReader.ReadU64(perfEventDataSpan.Slice(perfEventDataSpan.Length - m_nonSampleIdOffset));

            PerfEventDesc eventDesc;
            if (!m_eventDescById.TryGetValue(id, out eventDesc))
            {
                result = PerfDataFileResult.IdNotFound;
                goto Error;
            }

            var infoSampleTypes = eventDesc.Attr.SampleType;

            Debug.Assert(perfEventDataSpan.Length >= sizeof(UInt64) * 2); // Otherwise id lookup would have failed.
            var pos = perfEventDataSpan.Length & -sizeof(UInt64); // Read backwards.

            result = PerfDataFileResult.InvalidData;

            if (0 != (infoSampleTypes & PerfEventAttrSampleType.Identifier))
            {
                pos -= sizeof(UInt64); // Was read in id lookup.
                Debug.Assert(pos != 0); // Otherwise id lookup would have failed.
            }

            if (0 == (infoSampleTypes & PerfEventAttrSampleType.Cpu))
            {
                info.CpuReserved = default;
                info.Cpu = default;
            }
            else
            {
                pos -= sizeof(UInt32);
                info.CpuReserved = m_byteReader.ReadU32(perfEventDataSpan.Slice(pos));
                pos -= sizeof(UInt32);
                info.Cpu = m_byteReader.ReadU32(perfEventDataSpan.Slice(pos));

                if (pos == 0)
                {
                    goto Error;
                }
            }

            if (0 == (infoSampleTypes & PerfEventAttrSampleType.StreamId))
            {
                info.StreamId = default;
            }
            else
            {
                pos -= sizeof(UInt64);
                info.StreamId = m_byteReader.ReadU64(perfEventDataSpan.Slice(pos));

                if (pos == 0)
                {
                    goto Error;
                }
            }

            if (0 == (infoSampleTypes & PerfEventAttrSampleType.Id))
            {
                // Nothing to do.
            }
            else
            {
                pos -= sizeof(UInt64); // Was read in id lookup.

                if (pos == 0)
                {
                    goto Error;
                }
            }

            if (0 == (infoSampleTypes & PerfEventAttrSampleType.Time))
            {
                info.Time = default;
            }
            else
            {
                pos -= sizeof(UInt64);
                info.Time = m_byteReader.ReadU64(perfEventDataSpan.Slice(pos));

                if (pos == 0)
                {
                    goto Error;
                }
            }

            if (0 == (infoSampleTypes & PerfEventAttrSampleType.Tid))
            {
                info.Tid = default;
                info.Pid = default;
            }
            else
            {
                pos -= sizeof(UInt32);
                info.Tid = m_byteReader.ReadU32(perfEventDataSpan.Slice(pos));
                pos -= sizeof(UInt32);
                info.Pid = m_byteReader.ReadU32(perfEventDataSpan.Slice(pos));

                if (pos == 0)
                {
                    goto Error;
                }
            }

            Debug.Assert(pos >= sizeof(UInt64));
            Debug.Assert(pos < 0x10000 / sizeof(UInt64));
            info.SessionInfo = m_sessionInfo;
            info.EventDesc = eventDesc;
            info.Id = id;
            return PerfDataFileResult.Ok;

        Error:

            info = default;
            return result;
        }

        /// <summary>
        /// Expects data.Slice(pos) starts with: byte[] szValue.
        /// If value is nul-terminated, sets value and returns position after the nul.
        /// Otherwise, sets value to empty and returns -1.
        /// </summary>
        private static int ReadSz(ReadOnlySpan<byte> data, int pos, out PosLength value)
        {
            Debug.Assert(pos <= data.Length);
            var len = data.Slice(pos).IndexOf((byte)0);
            if (len < 0)
            {
                value = default;
                return -1;
            }
            else
            {
                value = new PosLength(pos, len);
                return pos + len + 1;
            }
        }

        /// <summary>
        /// Expects data.Slice(pos) starts with: sectionSize + byte[sectionSize] value.
        /// sizeOfSectionSize must be 4 (sectionSize is UInt32) or 8 (sectionSize is UInt64).
        /// On success, sets value and returns position after the value.
        /// On failure, sets value to empty and returns -1.
        /// </summary>
        private static int ReadSection(
            int sizeOfSectionSize,
            PerfByteReader dataByteReader,
            ReadOnlySpan<byte> data,
            int pos,
            out PosLength value)
        {
            Debug.Assert(sizeOfSectionSize == sizeof(UInt32) || sizeOfSectionSize == sizeof(UInt64));
            Debug.Assert(pos <= data.Length);

            if (data.Length - pos < sizeOfSectionSize)
            {
                value = default;
                return -1;
            }

            var sectionSize = sizeOfSectionSize == sizeof(UInt64)
                ? dataByteReader.ReadU64(data.Slice(pos))
                : dataByteReader.ReadU32(data.Slice(pos));
            pos += sizeOfSectionSize;

            if ((UInt32)(data.Length - pos) < sectionSize)
            {
                value = default;
                return -1;
            }

            value = new PosLength(pos, (int)sectionSize);
            return pos + (int)sectionSize;
        }

        /// <summary>
        /// Expects data.Slice(pos) starts with: byte[] szName + sectionSize + byte[sectionSize] value.
        /// expectedName must include the nul terminator.
        /// Sets value and returns position after the value.
        /// If name does not match, sets value to empty and returns pos.
        /// On failure, sets value to empty and returns -1.
        /// </summary>
        private static int ReadNamedSection64(
            PerfByteReader dataByteReader,
            ReadOnlySpan<byte> data,
            int pos,
            ReadOnlySpan<byte> expectedName,
            out PosLength value)
        {
            Debug.Assert(pos <= data.Length);

            if (data.Length - pos < expectedName.Length ||
                !expectedName.SequenceEqual(data.Slice(pos, expectedName.Length)))
            {
                value = default;
                return pos;
            }

            return ReadSection(8, dataByteReader, data, pos + expectedName.Length, out value);
        }

        private bool ReadPostEventData(int sizeOfDataSize, ref PosLength eventData, ref Span<byte> buffer)
        {
            Debug.Assert(sizeOfDataSize == sizeof(UInt32) || sizeOfDataSize == sizeof(UInt64));

            if (eventData.Length < sizeOfDataSize)
            {
                return false;
            }

            var dataSize64 = sizeOfDataSize == sizeof(UInt64)
                ? m_byteReader.ReadU64(buffer.Slice(eventData.StartPos))
                : m_byteReader.ReadU32(buffer.Slice(eventData.StartPos));
            if (dataSize64 >= 0x80000000 || 0 != (dataSize64 & 7))
            {
                return false;
            }

            var dataSize = (UInt32)dataSize64;

            if ((UInt32)eventData.EndPos + dataSize > (UInt32)m_buffer.Length)
            {
                if ((UInt32)eventData.EndPos + dataSize >= 0x80000000)
                {
                    return false;
                }

                BufferResize(eventData.EndPos + (int)dataSize, eventData.EndPos);
                buffer = m_buffer.Span;
            }

            if (!FileRead(buffer.Slice(eventData.EndPos, (int)dataSize)))
            {
                return false; // EOF in the middle of expected data.
            }

            eventData = new PosLength(eventData.StartPos, eventData.Length + (int)dataSize);
            return true;
        }

        private ReadOnlySpan<byte> SetHeader(PerfHeaderIndex headerIndex, ReadOnlySpan<byte> value)
        {
            var header = value.Length > 0
                ? new byte[value.Length].AsMemory()
                : default;
            var headerSpan = header.Span;
            value.CopyTo(headerSpan);
            m_headers[(byte)headerIndex] = header;
            return headerSpan;
        }

        private bool LoadAttrs(in perf_file_section attrsSection, UInt64 attrAndIdSectionSize64)
        {
            bool ok;

            if (attrsSection.size >= 0x80000000 ||
                attrAndIdSectionSize64 < (int)PerfEventAttrSize.Ver0 + perf_file_section.SizeOfStruct ||
                attrAndIdSectionSize64 > 0x10000)
            {
                ok = false;
            }
            else
            {
                var attrAndIdSectionSize = (uint)attrAndIdSectionSize64; // <= 0x10000
                var attrSizeInFile = attrAndIdSectionSize - perf_file_section.SizeOfStruct;

                PerfEventAttr attr = default;
                var attrBytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref attr, 1))
                    .Slice(0, Math.Min((int)attrSizeInFile, PerfEventAttr.SizeOfStruct));

                perf_file_section section = default;
                var sectionBytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref section, 1));

                var newEventDescListCount = m_eventDescList.Count + (int)((uint)attrsSection.size / attrAndIdSectionSize);
                if (newEventDescListCount > m_eventDescList.Capacity)
                {
                    m_eventDescList.Capacity = newEventDescListCount;
                }

                ok = true;

                var attrFilePosEnd = attrsSection.offset + attrsSection.size;
                for (var attrFilePos = attrsSection.offset; attrFilePos < attrFilePosEnd;)
                {
                    if (!FileSeekAndRead(attrFilePos, attrBytes))
                    {
                        ok = false; // EOF in the middle of expected data.
                        break;
                    }

                    attrFilePos += attrSizeInFile;

                    if (!FileSeekAndRead(attrFilePos, sectionBytes))
                    {
                        ok = false; // EOF in the middle of expected data.
                        break;
                    }

                    attrFilePos += perf_file_section.SizeOfStruct;

                    if (!SectionValid(section) ||
                        0 != (section.size & 7) ||
                        section.size >= 0x80000000)
                    {
                        ok = false;
                        break;
                    }

                    var sectionSize = (int)section.size;
                    if (m_buffer.Length < sectionSize)
                    {
                        BufferResize(sectionSize, 0);
                    }

                    var sectionData = m_buffer.Span.Slice(0, sectionSize);
                    if (!FileSeekAndRead(section.offset, sectionData))
                    {
                        ok = false; // EOF in the middle of expected data.
                        break;
                    }

                    if (!AddAttr(attrBytes, default, sectionData))
                    {
                        ok = false;
                        break;
                    }
                }
            }

            return ok;
        }
        private bool LoadHeaders(in perf_file_section dataSection, UInt64 flags)
        {
            bool ok = true;

            perf_file_section section = default;
            var sectionBytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref section, 1));

            var filePos = dataSection.offset + dataSection.size;
            UInt64 mask = 1;
            for (int headerIndex = 0; headerIndex < m_headers.Length; headerIndex += 1)
            {
                if (0 != (flags & mask))
                {
                    if (!FileSeekAndRead(filePos, sectionBytes))
                    {
                        ok = false; // EOF in the middle of expected data.
                        break;
                    }

                    filePos += perf_file_section.SizeOfStruct;

                    if (!SectionValid(section) ||
                        section.size >= 0x80000000)
                    {
                        ok = false;
                        break;
                    }

                    var header = new byte[(int)section.size].AsMemory();
                    var headerSpan = header.Span;
                    if (!FileSeekAndRead(section.offset, headerSpan))
                    {
                        ok = false; // EOF in the middle of expected data.
                        break;
                    }

                    m_headers[headerIndex] = header;

                    switch (headerIndex)
                    {
                        case (int)PerfHeaderIndex.ClockId:
                            ParseHeaderClockid(headerSpan);
                            break;
                        case (int)PerfHeaderIndex.ClockData:
                            ParseHeaderClockData(headerSpan);
                            break;
                        case (int)PerfHeaderIndex.EventDesc:
                            ParseHeaderEventDesc(headerSpan);
                            break;
                        case (int)PerfHeaderIndex.TracingData:
                            ParseTracingData(headerSpan);
                            break;
                    }
                }

                mask <<= 1;
            }

            return ok;
        }

        private void ParseTracingData(ReadOnlySpan<byte> data)
        {
            var dataMemory = m_headers[(int)PerfHeaderIndex.TracingData];
            Debug.Assert(data == dataMemory.Span); // These should be the same thing.

            if (TracingSignature.Length <= data.Length &&
                TracingSignature.AsSpan().SequenceEqual(data.Slice(0, TracingSignature.Length)))
            {
                m_parsedTracingData = true;

                PosLength sectionValue;
                var pos = TracingSignature.Length;

                // Version

                pos = ReadSz(data, pos, out sectionValue);
                if (pos < 0)
                {
                    return; // Unexpected.
                }

                double tracingDataVersion;
                _ = double.TryParse(Encoding.UTF8.GetString(sectionValue.Slice(data)), out tracingDataVersion);

                // Big Endian, LongSize, PageSize

                if (data.Length - pos < 1 + 1 + sizeof(UInt32))
                {
                    return; // Unexpected.
                }

                var dataIsBigEndian = data[pos] != 0;
                var dataByteReader = new PerfByteReader(dataIsBigEndian);
                pos += 1;

                m_tracingDataLongSize = data[pos];
                pos += 1;

                m_tracingDataPageSize = (int)dataByteReader.ReadU32(data.Slice(pos));
                pos += sizeof(UInt32);

                // header_page

                pos = ReadNamedSection64(dataByteReader, data, pos, header_page0.AsSpan(), out sectionValue);
                if (pos < 0)
                {
                    return; // Unexpected.
                }

                m_headerPage = sectionValue.Slice(dataMemory);

                // header_event (not really used anymore)

                pos = ReadNamedSection64(dataByteReader, data, pos, header_event0.AsSpan(), out sectionValue);
                if (pos < 0)
                {
                    return; // Unexpected.
                }

                m_headerEvent = sectionValue.Slice(dataMemory);

                // ftraces

                if (data.Length - pos < sizeof(UInt32))
                {
                    return; // Unexpected.
                }

                var ftraceCount = dataByteReader.ReadU32(data.Slice(pos));
                pos += sizeof(UInt32);
                if (ftraceCount > (data.Length - pos) / sizeof(UInt64))
                {
                    return; // Unexpected.
                }

                m_ftraces.Capacity = m_ftraces.Count + (int)ftraceCount;
                for (uint ftraceIndex = 0; ftraceIndex != ftraceCount; ftraceIndex += 1)
                {
                    pos = ReadSection(8, dataByteReader, data, pos, out sectionValue);
                    if (pos < 0)
                    {
                        return; // Unexpected.
                    }

                    m_ftraces.Add(sectionValue.Slice(dataMemory));
                }

                // systems (and events)

                if (data.Length - pos < sizeof(UInt32))
                {
                    return; // Unexpected.
                }

                var systemCount = dataByteReader.ReadU32(data.Slice(pos));
                pos += sizeof(UInt32);
                for (uint systemIndex = 0; systemIndex != systemCount; systemIndex += 1)
                {
                    pos = ReadSz(data, pos, out sectionValue);
                    if (pos < 0)
                    {
                        return; // Unexpected.
                    }

                    var systemName = Utility.EncodingLatin1.GetString(sectionValue.Slice(data));

                    if (data.Length - pos < sizeof(UInt32))
                    {
                        return; // Unexpected.
                    }

                    var eventCount = dataByteReader.ReadU32(data.Slice(pos));
                    pos += sizeof(UInt32);
                    for (uint eventIndex = 0; eventIndex != eventCount; eventIndex += 1)
                    {
                        pos = ReadSection(8, dataByteReader, data, pos, out sectionValue);
                        if (pos < 0)
                        {
                            return; // Unexpected.
                        }

                        var formatFileContents = Utility.EncodingLatin1.GetString(sectionValue.Slice(data));
                        var longSize64 = m_tracingDataLongSize == 0
                            ? IntPtr.Size == sizeof(UInt64)
                            : m_tracingDataLongSize == sizeof(UInt64);
                        var eventMetadata = PerfEventMetadata.Parse(longSize64, systemName, formatFileContents);
                        if (eventMetadata != null)
                        {
                            sbyte commonTypeOffset = OffsetUnset;
                            byte commonTypeSize = 0;
                            for (ushort i = 0; i != eventMetadata.CommonFieldCount; i += 1)
                            {
                                var field = eventMetadata.Fields[i];
                                if (field.Name == "common_type")
                                {
                                    if (field.Offset <= sbyte.MaxValue &&
                                        (field.Size == 1 || field.Size == 2 || field.Size == 4) &&
                                        field.Array == PerfFieldArray.None)
                                    {
                                        commonTypeOffset = (sbyte)field.Offset;
                                        commonTypeSize = (byte)field.Size;
                                    }
                                    break;
                                }
                            }

                            if (commonTypeOffset == OffsetUnset)
                            {
                                // Unexpected: did not find a usable "common_type" field.
                                continue;
                            }
                            else if (m_commonTypeOffset == OffsetUnset)
                            {
                                // First event to be parsed. Use its "common_type" field.
                                m_commonTypeOffset = commonTypeOffset;
                                m_commonTypeSize = commonTypeSize;
                            }
                            else if (
                                m_commonTypeOffset != commonTypeOffset ||
                                m_commonTypeSize != commonTypeSize)
                            {
                                // Unexpected: found a different "common_type" field.
                                continue;
                            }

                            m_metadataById.TryAdd(eventMetadata.Id, eventMetadata);
                        }
                    }
                }

                // Update EventDesc with the new metadata.
                for (var i = 0; i < m_eventDescList.Count; i += 1)
                {
                    var desc = m_eventDescList[i];
                    if (desc.Metadata == null &&
                        desc.Attr.Type == PerfEventAttrType.Tracepoint &&
                        m_metadataById.TryGetValue((uint)desc.Attr.Config, out var metadata))
                    {
                        desc.SetMetadata(metadata);
                    }
                }

                // kallsyms

                pos = ReadSection(4, dataByteReader, data, pos, out sectionValue);
                if (pos < 0)
                {
                    return; // Unexpected.
                }

                m_kallsyms = sectionValue.Slice(dataMemory);

                // printk

                pos = ReadSection(4, dataByteReader, data, pos, out sectionValue);
                if (pos < 0)
                {
                    return; // Unexpected.
                }

                m_printk = sectionValue.Slice(dataMemory);

                // saved_cmdline

                if (tracingDataVersion >= 0.6)
                {
                    pos = ReadSection(8, dataByteReader, data, pos, out sectionValue);
                    if (pos < 0)
                    {
                        return; // Unexpected.
                    }

                    m_cmdline = sectionValue.Slice(dataMemory);
                }
            }
        }

        private void ParseHeaderClockid(ReadOnlySpan<byte> data)
        {
            Debug.Assert(data == m_headers[(int)PerfHeaderIndex.ClockId].Span); // These should be the same thing.

            if (data.Length >= sizeof(UInt64))
            {
                m_sessionInfo.SetClockId((uint)m_byteReader.ReadU64(data));
            }
        }

        private void ParseHeaderClockData(ReadOnlySpan<byte> data)
        {
            Debug.Assert(data == m_headers[(int)PerfHeaderIndex.ClockData].Span); // These should be the same thing.

            if (data.Length >= ClockData.SizeOfStruct)
            {
                var clockData = MemoryMarshal.Read<ClockData>(data);
                if (1 <= m_byteReader.FixU32(clockData.version))
                {
                    m_sessionInfo.SetClockData(
                        m_byteReader.FixU32(clockData.clockid),
                        m_byteReader.FixU64(clockData.wall_clock_ns),
                        m_byteReader.FixU64(clockData.clockid_time_ns));
                }
            }
        }

        private void ParseHeaderEventDesc(ReadOnlySpan<byte> data)
        {
            Debug.Assert(data == m_headers[(int)PerfHeaderIndex.EventDesc].Span); // These should be the same thing.

            if (m_parsedHeaderEventDesc)
            {
                return;
            }

            if (data.Length >= sizeof(UInt32) + sizeof(UInt32))
            {
                m_parsedHeaderEventDesc = true;

                var pos = 0;

                var eventCount = m_byteReader.ReadU32(data);
                pos += sizeof(UInt32);

                var attrSize = m_byteReader.ReadI32(data.Slice(pos));
                pos += sizeof(UInt32);
                if (attrSize < (int)PerfEventAttrSize.Ver0 || attrSize > 0x10000)
                {
                    return; // Unexpected.
                }

                for (uint eventIndex = 0; eventIndex != eventCount; eventIndex += 1)
                {
                    if (data.Length - pos < attrSize + sizeof(UInt32) + sizeof(UInt32))
                    {
                        return; // Unexpected.
                    }

                    var attrPos = pos;
                    pos += attrSize;

                    var idsCount = m_byteReader.ReadU32(data.Slice(pos));
                    pos += sizeof(UInt32);

                    var stringSize = m_byteReader.ReadU32(data.Slice(pos));
                    pos += sizeof(UInt32);

                    if (attrSize != m_byteReader.ReadI32(data.Slice(attrPos + PerfEventAttr.OffsetOfSize)) ||
                        idsCount > 0x10000 ||
                        stringSize > 0x10000 ||
                        data.Length - pos < stringSize + idsCount * sizeof(UInt64))
                    {
                        return; // Unexpected.
                    }

                    var stringPos = pos;
                    pos += (int)stringSize;

                    var stringLen = data.Slice(stringPos, (int)stringSize).IndexOf((byte)0);
                    if (stringLen < 0)
                    {
                        return; // Unexpected.
                    }

                    var ids = data.Slice(pos, (int)idsCount * sizeof(UInt64));
                    pos += ids.Length;

                    var attrSizeCapped = Math.Min(attrSize, PerfEventAttr.SizeOfStruct);
                    AddAttr(data.Slice(attrPos, attrSizeCapped), data.Slice(stringPos, stringLen), ids);
                }
            }
        }

        private bool AddAttr(
            ReadOnlySpan<byte> attrBytes,
            ReadOnlySpan<byte> name,
            ReadOnlySpan<byte> idsBytes)
        {
            Debug.Assert(attrBytes.Length <= PerfEventAttr.SizeOfStruct);

            PerfEventAttr attr = default;
            attrBytes.CopyTo(MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref attr, 1)));

            if (m_byteReader.ByteSwapNeeded)
            {
                attr.ByteSwap();
            }

            attr.Size = (PerfEventAttrSize)attrBytes.Length;

            var sampleType = attr.SampleType;

            sbyte sampleIdOffset;
            sbyte nonSampleIdOffset;
            if (0 != (sampleType & PerfEventAttrSampleType.Identifier))
            {
                // ID is at a fixed offset.
                sampleIdOffset = 0;
                nonSampleIdOffset = sizeof(UInt64);
            }
            else if (0 == (sampleType & PerfEventAttrSampleType.Id))
            {
                // ID is not available.
                sampleIdOffset = OffsetNoId;
                nonSampleIdOffset = OffsetNoId;
            }
            else
            {
                // ID is at a sampleType-dependent offset.
                sampleIdOffset = (sbyte)(sizeof(UInt64) * (
                    (0 != (sampleType & PerfEventAttrSampleType.IP) ? 1 : 0) +
                    (0 != (sampleType & PerfEventAttrSampleType.Tid) ? 1 : 0) +
                    (0 != (sampleType & PerfEventAttrSampleType.Time) ? 1 : 0) +
                    (0 != (sampleType & PerfEventAttrSampleType.Addr) ? 1 : 0)));
                nonSampleIdOffset = (sbyte)(sizeof(UInt64) * (1 +
                    (0 != (sampleType & PerfEventAttrSampleType.Cpu) ? 1 : 0) +
                    (0 != (sampleType & PerfEventAttrSampleType.StreamId) ? 1 : 0)));
            }

            if (0 == (attr.Options & PerfEventAttrOptions.SampleIdAll))
            {
                // ID is not available for non-sample events.
                nonSampleIdOffset = OffsetNoId;
            }

            if (sampleIdOffset != m_sampleIdOffset)
            {
                if (m_sampleIdOffset != OffsetUnset)
                {
                    // Unexpected: Inconsistent sampleIdOffset across the attrs in the trace.
                    return false;
                }

                m_sampleIdOffset = sampleIdOffset;
            }

            if (nonSampleIdOffset != m_nonSampleIdOffset)
            {
                if (m_nonSampleIdOffset != OffsetUnset)
                {
                    // Unexpected: Inconsistent nonSampleIdOffset across the attrs in the trace.
                    return false;
                }

                m_nonSampleIdOffset = nonSampleIdOffset;
            }

            var ids = new ulong[idsBytes.Length / sizeof(UInt64)];
            for (int i = 0; i != ids.Length; i += 1)
            {
                ids[i] = m_byteReader.ReadU64(idsBytes.Slice(i * sizeof(UInt64)));
            }

            PerfEventMetadata? metadata = null;
            if (attr.Type == PerfEventAttrType.Tracepoint)
            {
                m_metadataById.TryGetValue((UInt32)attr.Config, out metadata);
            }

            var eventDesc = new PerfEventDesc(
                attr,
                Utility.EncodingLatin1.GetString(name),
                metadata,
                new ReadOnlyCollection<ulong>(ids));
            m_eventDescList.Add(eventDesc);

            foreach (var id in ids)
            {
                m_eventDescById[id] = eventDesc;
            }

            return true;
        }

        private void BufferResize(int minSize, int preserveSize)
        {
            Debug.Assert(minSize > m_buffer.Length);
            Debug.Assert(preserveSize <= m_buffer.Length);

            var oldBuffer = m_buffer;
            m_buffer = new byte[minSize];

            if (preserveSize > 0)
            {
                oldBuffer.Slice(0, preserveSize).CopyTo(m_buffer);
            }
        }

        private bool SectionValid(in perf_file_section section)
        {
            var endOffset = section.offset + section.size;
            return section.offset <= endOffset && endOffset <= m_fileLen;
        }

        private void FileClose()
        {
            var file = m_file;
            var fileShouldBeClosed = m_fileShouldBeClosed;

            m_file = Stream.Null;
            m_fileShouldBeClosed = false;

            if (fileShouldBeClosed)
            {
                file.Dispose();
            }
        }

        private void FileOpen(Stream stream, bool leaveOpen)
        {
            Debug.Assert(m_file == Stream.Null);
            Debug.Assert(m_fileShouldBeClosed == false);
            m_file = stream;
            m_fileShouldBeClosed = !leaveOpen;
        }

        private void FileLenUpdate()
        {
            m_fileLen = (UInt64)m_file.Length;
        }

        private bool FileRead(Span<byte> buffer)
        {
            var remaining = buffer;
            while (true)
            {
                var readCount = m_file.Read(remaining);
                m_filePos += (uint)readCount;

                if (readCount == remaining.Length)
                {
                    return true;
                }
                else if (readCount == 0)
                {
                    return false;
                }

                remaining = remaining.Slice(readCount);
            }
        }

        private bool FileSeek(UInt64 newFilePos)
        {
            if (m_filePos == newFilePos)
            {
                return true;
            }
            else if (newFilePos > m_fileLen)
            {
                return false;
            }
            else
            {
                m_file.Position = (long)newFilePos;
                m_filePos = newFilePos;
                return true;
            }
        }

        private bool FileSeekAndRead(UInt64 offset, Span<byte> buffer)
        {
            return FileSeek(offset) && FileRead(buffer);
        }

        private struct PosLength
        {
            public readonly int StartPos;
            public readonly int Length;

            public PosLength(int startPos, int length)
            {
                StartPos = startPos;
                Length = length;
            }

            public int EndPos => this.StartPos + this.Length;

            public Span<T> Slice<T>(Span<T> data)
            {
                return data.Slice(this.StartPos, this.Length);
            }

            public ReadOnlySpan<T> Slice<T>(ReadOnlySpan<T> data)
            {
                return data.Slice(this.StartPos, this.Length);
            }

            public ReadOnlyMemory<T> Slice<T>(ReadOnlyMemory<T> data)
            {
                return data.Slice(this.StartPos, this.Length);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ClockData
        {
            public const int SizeOfStruct = 24;
            public UInt32 version;
            public UInt32 clockid;
            public UInt64 wall_clock_ns;
            public UInt64 clockid_time_ns;
        };

        [StructLayout(LayoutKind.Sequential)]
        private struct perf_file_section
        {
            public const int SizeOfStruct = 16;
            public UInt64 offset; // offset from start of file
            public UInt64 size;   // size of the section

            public void ByteSwap()
            {
                this.offset = BinaryPrimitives.ReverseEndianness(this.offset);
                this.size = BinaryPrimitives.ReverseEndianness(this.size);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct perf_pipe_header
        {
            public const int SizeOfStruct = 16;
            public const UInt64 Magic2 = 0x32454C4946524550; // "PERFILE2"

            public UInt64 magic; // If correctly byte-swapped, this will be equal to Magic2.
            public UInt64 size;  // Size of the header.

            public void ByteSwap()
            {
                this.magic = BinaryPrimitives.ReverseEndianness(this.magic);
                this.size = BinaryPrimitives.ReverseEndianness(this.size);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct perf_file_header
        {
            public const int SizeOfStruct = 104;
            public perf_pipe_header pipe_header;
            public UInt64 attr_size;    // size of (perf_event_attrs + perf_file_section) in attrs.
            public perf_file_section attrs;
            public perf_file_section data;
            public perf_file_section event_types; // Not used.

            // 256-bit bitmap based on HEADER_BITS
            public UInt64 flags0;
            public UInt64 flags1;
            public UInt64 flags2;
            public UInt64 flags3;

            // Reverse the endian order of all fields in this struct.
            public void ByteSwap()
            {
                this.pipe_header.ByteSwap();
                this.attr_size = BinaryPrimitives.ReverseEndianness(this.attr_size);
                this.attrs.ByteSwap();
                this.data.ByteSwap();
                this.event_types.ByteSwap();
                this.flags0 = BinaryPrimitives.ReverseEndianness(this.flags0);
                this.flags1 = BinaryPrimitives.ReverseEndianness(this.flags1);
                this.flags2 = BinaryPrimitives.ReverseEndianness(this.flags2);
                this.flags3 = BinaryPrimitives.ReverseEndianness(this.flags3);
            }
        }
    }
}
