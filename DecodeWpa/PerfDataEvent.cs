// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.Performance.Toolkit.Plugins.PerfDataExtension
{
    using Microsoft.LinuxTracepoints;
    using Microsoft.LinuxTracepoints.Decode;
    using Microsoft.Performance.SDK.Extensibility;
    using System;
    using System.Diagnostics.Tracing;
    using Debug = System.Diagnostics.Debug;
    using Timestamp = Microsoft.Performance.SDK.Timestamp;

    /// <summary>
    /// Stores and provides access to an event from a perf.data file.
    /// Stores the event's header, raw data bytes, and core offsets/lengths.
    /// Other information is extracted on-demand by properties.
    /// </summary>
    public sealed class PerfDataEvent : IKeyedDataType<PerfEventHeaderType>
    {
        private readonly byte[] contents;
        private readonly PerfEventDesc eventDesc;
        private readonly ulong fileRelativeTime;
        private readonly PerfEventHeader header;

        private readonly ushort topLevelFieldCount;
        private readonly PerfByteReader byteReader;

        // The following fields are zero for non-Sample events.

        private readonly byte activityIdLength; // EventHeader only. 0, 16, or 32.
        private readonly ushort activityIdStart; // EventHeader only. Offset into contents for activity ID + related ID.
        private readonly ushort rawDataLength;
        private readonly ushort rawDataStart;
        private readonly ushort eventHeaderNameLength; // EventHeader only. Length of the EventHeader event name.
        private readonly ushort eventHeaderNameStart; // EventHeader only. Offset into contents for the EventHeader event name.

        /// <summary>
        /// For raw events, i.e. non-Sample events with no event info.
        /// </summary>
        public PerfDataEvent(
            PerfByteReader byteReader,
            in PerfEventBytes bytes,
            ulong fileRelativeTime)
        {
            var bytesSpan = bytes.Span;
            var hasContent = bytesSpan.Length > 8;
            Debug.Assert(bytesSpan.Length >= 8);

            this.contents = hasContent ? bytesSpan.Slice(8).ToArray() : Array.Empty<byte>();
            this.eventDesc = PerfEventDesc.Empty;
            this.fileRelativeTime = fileRelativeTime;
            this.header = bytes.Header;
            this.topLevelFieldCount = (ushort)(hasContent ? 1 : 0); // If there is content, we'll make a "raw" field.
            this.byteReader = byteReader;
        }

        /// <summary>
        /// For non-Sample events with event info.
        /// </summary>
        public PerfDataEvent(
            PerfByteReader byteReader,
            PerfEventHeader header,
            in PerfNonSampleEventInfo info)
        {
            Debug.Assert(info.EventDesc.Format.IsEmpty);

            var bytesSpan = info.BytesSpan;
            var hasContent = bytesSpan.Length > 8;
            Debug.Assert(bytesSpan.Length >= 8);

            this.contents = hasContent ? bytesSpan.Slice(8).ToArray() : Array.Empty<byte>();
            this.eventDesc = info.EventDesc;
            this.fileRelativeTime = info.Time;
            this.header = header;
            this.topLevelFieldCount = (ushort)(hasContent ? 1 : 0); // If there is content, we'll make a "raw" field.
            this.byteReader = byteReader;
        }

        /// <summary>
        /// For Sample events with event info.
        /// </summary>
        public PerfDataEvent(
            PerfByteReader byteReader,
            PerfEventHeader header,
            in PerfSampleEventInfo info)
        {
            var format = info.Format;
            Debug.Assert(format.Fields.Count >= format.CommonFieldCount);

            var bytesSpan = info.BytesSpan;
            var hasContent = bytesSpan.Length > 8;
            Debug.Assert(bytesSpan.Length >= 8);

            this.contents = hasContent ? bytesSpan.Slice(8).ToArray() : Array.Empty<byte>();
            this.eventDesc = info.EventDesc;
            this.fileRelativeTime = info.Time;
            this.header = header;
            this.topLevelFieldCount = format.IsEmpty
                ? (ushort)(hasContent ? 1 : 0)
                : (ushort)(format.Fields.Count - format.CommonFieldCount);
            this.byteReader = byteReader;
            this.rawDataStart = (ushort)(info.RawDataStart >= 8 ? info.RawDataStart - 8 : 0);
            this.rawDataLength = (ushort)info.RawDataLength;
            Debug.Assert(this.rawDataStart + this.rawDataLength <= bytesSpan.Length);
        }

        /// <summary>
        /// For EventHeader Sample events with event info.
        /// Requires !info.EventDesc.Format.IsEmpty.
        /// </summary>
        public PerfDataEvent(
            PerfByteReader byteReader,
            PerfEventHeader header,
            in PerfSampleEventInfo info,
            in EventHeaderEventInfo ehEventInfo,
            ushort topLevelFieldCount)
            : this(byteReader, header, info)
        {
            Debug.Assert(!info.EventDesc.Format.IsEmpty);

            this.topLevelFieldCount = topLevelFieldCount;

            var userDataStart = (ushort)(this.rawDataStart + this.eventDesc.Format.CommonFieldsSize);
            Debug.Assert(userDataStart + ehEventInfo.NameStart + ehEventInfo.NameLength <= this.contents.Length);

            this.eventHeaderNameStart = (ushort)(userDataStart + ehEventInfo.NameStart);
            this.eventHeaderNameLength = (ushort)ehEventInfo.NameLength;

            this.activityIdLength = (byte)ehEventInfo.ActivityIdLength;
            if (this.activityIdLength != 0)
            {
                Debug.Assert(userDataStart + ehEventInfo.ActivityIdStart + this.activityIdLength <= this.contents.Length);
                this.activityIdStart = (ushort)(userDataStart + ehEventInfo.ActivityIdStart);
            }
        }

        /// <summary>
        /// Gets the size of the event data bytes, NOT including the 8-byte header.
        /// </summary>
        public int ContentsLength => this.contents.Length;

        /// <summary>
        /// Gets event data bytes as ReadOnlyMemory, NOT including the 8-byte header.
        /// </summary>
        public ReadOnlyMemory<byte> ContentsMemory => this.contents;

        /// <summary>
        /// Gets event data bytes as ReadOnlySpan, NOT including the 8-byte header.
        /// </summary>
        public ReadOnlySpan<byte> ContentsSpan => this.contents;

        /// <summary>
        /// Core event metadata.
        /// </summary>
        public PerfEventDesc EventDesc => this.eventDesc;

        /// <summary>
        /// Event's full name (including the system name), e.g. "sched:sched_switch",
        /// or "" if not available.
        /// </summary>
        public string TracepointId => this.eventDesc.Name;

        /// <summary>
        /// Returns the part of TracepointId before the first ':'.
        /// If no ':', returns the entire TracepointId (usually only empty TracepointId will lack a ':').
        /// Note that when !this.Format.IsEmpty, this.Format.SystemName will generally return the
        /// same string.
        /// </summary>
        public ReadOnlyMemory<char> SystemNameMemory
        {
            get
            {
                var tracepointId = this.eventDesc.Name;
                return tracepointId.AsMemory(0, GetSystemNameLength(tracepointId));
            }
        }

        /// <summary>
        /// Returns the part of TracepointId before the first ':'.
        /// If no ':', returns the entire TracepointId (usually only empty TracepointId will lack a ':').
        /// Note that when !this.Format.IsEmpty, this.Format.SystemName will generally return the
        /// same string.
        /// </summary>
        public ReadOnlySpan<char> SystemNameSpan
        {
            get
            {
                var tracepointId = this.eventDesc.Name;
                return tracepointId.AsSpan(0, GetSystemNameLength(tracepointId));
            }
        }

        /// <summary>
        /// Returns the part of TracepointId after the first ':'.
        /// If no ':', returns empty (usually only empty TracepointId will lack a ':').
        /// Note that when !this.Format.IsEmpty, this.Format.Name will generally return the
        /// same string.
        /// </summary>
        public ReadOnlyMemory<char> TracepointNameMemory
        {
            get
            {
                var tracepointId = this.eventDesc.Name;
                return tracepointId.AsMemory(GetTracepointNameStart(tracepointId));
            }
        }

        /// <summary>
        /// Returns the part of TracepointId after the first ':'.
        /// If no ':', returns empty (usually only empty TracepointId will lack a ':').
        /// Note that when !this.Format.IsEmpty, this.Format.Name will generally return the
        /// same string.
        /// </summary>
        public ReadOnlySpan<char> TracepointNameSpan
        {
            get
            {
                var tracepointId = this.eventDesc.Name;
                return tracepointId.AsSpan(GetTracepointNameStart(tracepointId));
            }
        }

        /// <summary>
        /// Returns the event's tracefs format information, or empty if not available.
        /// This is normally non-empty for Sample events and empty for non-Sample events.
        /// </summary>
        public PerfEventFormat Format => this.eventDesc.Format;

        /// <summary>
        /// Event timestamp in nanoseconds, relative to the ClockOffset of the associated
        /// PerfFileInfo.
        /// </summary>
        public ulong FileRelativeTime => this.fileRelativeTime;

        /// <summary>
        /// 8-byte header in host byte order. Contains event type and size.
        /// </summary>
        public PerfEventHeader Header => this.header;

        /// <summary>
        /// Gets the number of top-level fields.
        /// <list type="bullet"><item>
        /// For EventHeader Sample events, this is the number of EventHeader top-level fields
        /// (does not include fields nested within a struct).
        /// </item><item>
        /// For Sample events with tracefs format information, this is the number of tracefs
        /// fields, not including the Common fields.
        /// </item><item>
        /// For events without tracefs format information (i.e. non-Sample events), this is
        /// 1 if ContentsLength > 0, or 0 if ContentsLength == 0.
        /// </item></list>
        /// </summary>
        public ushort TopLevelFieldCount => this.topLevelFieldCount;

        /// <summary>
        /// Gets a byte reader configured for the byte order of the event's data.
        /// Same as new PerfByteReader(FromBigEndian).
        /// </summary>
        public PerfByteReader ByteReader => this.byteReader;

        /// <summary>
        /// Returns true if the event's data is in big-endian byte order, false if
        /// the event's data is in little-endian byte order. Same as ByteReader.FromBigEndian.
        /// </summary>
        public bool FromBigEndian => this.byteReader.FromBigEndian;

        /// <summary>
        /// For Sample events with raw data, returns the length of the raw data.
        /// Otherwise, returns 0.
        /// </summary>
        public int RawDataLength => this.rawDataLength;

        /// <summary>
        /// For Sample events with raw data, returns the raw data as ReadOnlyMemory.
        /// Otherwise, returns empty.
        /// </summary>
        public ReadOnlyMemory<byte> RawDataMemory => this.contents.AsMemory(this.rawDataStart, this.rawDataLength);

        /// <summary>
        /// For Sample events with raw data, returns the raw data as ReadOnlySpan.
        /// Otherwise, returns empty.
        /// </summary>
        public ReadOnlySpan<byte> RawDataSpan => this.contents.AsSpan(this.rawDataStart, this.rawDataLength);

        /// <summary>
        /// For events with CPU information in the header, returns the CPU number.
        /// Otherwise, returns null.
        /// </summary>
        public UInt32? Cpu
        {
            get
            {
                var sampleType = (UInt32)this.eventDesc.Attr.SampleType;
                if (0 == (sampleType & (UInt32)PerfEventAttrSampleType.Cpu))
                {
                    return null;
                }
                else if (this.header.Type == PerfEventHeaderType.Sample)
                {
                    var offset = sizeof(UInt64) * PopCnt(sampleType & (UInt32)(
                        PerfEventAttrSampleType.Identifier |
                        PerfEventAttrSampleType.IP |
                        PerfEventAttrSampleType.Tid |
                        PerfEventAttrSampleType.Time |
                        PerfEventAttrSampleType.Addr |
                        PerfEventAttrSampleType.Id |
                        PerfEventAttrSampleType.StreamId));
                    return this.byteReader.FixU32(BitConverter.ToUInt32(this.contents, offset));
                }
                else
                {
                    var offset = 0 == (sampleType & (UInt32)PerfEventAttrSampleType.Identifier) ? 8 : 16;
                    return this.byteReader.FixU32(BitConverter.ToUInt32(this.contents, this.contents.Length - offset));
                }
            }
        }

        /// <summary>
        /// For events with PID/TID information in the header, returns the PID.
        /// Otherwise, returns null.
        /// </summary>
        public UInt32? Pid
        {
            get
            {
                var sampleType = (UInt32)this.eventDesc.Attr.SampleType;
                if (0 == (sampleType & (UInt32)PerfEventAttrSampleType.Tid))
                {
                    return null;
                }
                else
                {
                    return this.byteReader.FixU32(BitConverter.ToUInt32(this.contents, PidOffset(sampleType)));
                }
            }
        }

        /// <summary>
        /// For events with PID/TID information in the header, returns the TID.
        /// Otherwise, returns null.
        /// </summary>
        public UInt32? Tid
        {
            get
            {
                var sampleType = (UInt32)this.eventDesc.Attr.SampleType;
                if (0 == (sampleType & (UInt32)PerfEventAttrSampleType.Tid))
                {
                    return null;
                }
                else
                {
                    return this.byteReader.FixU32(BitConverter.ToUInt32(this.contents, PidOffset(sampleType) + 4));
                }
            }
        }

        /// <summary>
        /// Returns true if this is an EventHeader event (i.e. a Sample event that
        /// uses the EventHeader decoding system). Returns false otherwise.
        /// </summary>
        public bool HasEventHeader => this.eventHeaderNameStart != 0;

        /// <summary>
        /// For EventHeader events, returns the part of the tracepoint name before the last '_',
        /// i.e. for "MyProvider_L1KffGgroup" returns "MyProvider".
        /// Otherwise, returns empty.
        /// </summary>
        public ReadOnlyMemory<char> ProviderNameMemory
        {
            get
            {
                if (this.HasEventHeader)
                {
                    var formatName = this.eventDesc.Format.Name;
                    return formatName.AsMemory(0, GetProviderNameLength(formatName));
                }
                else
                {
                    return default;
                }
            }
        }

        /// <summary>
        /// For EventHeader events, returns the part of the tracepoint name before the last '_',
        /// i.e. for "MyProvider_L1KffGgroup" returns "MyProvider".
        /// Otherwise, returns empty.
        /// </summary>
        public ReadOnlySpan<char> ProviderNameSpan
        {
            get
            {
                if (this.HasEventHeader)
                {
                    var formatName = this.eventDesc.Format.Name;
                    return formatName.AsSpan(0, GetProviderNameLength(formatName));
                }
                else
                {
                    return default;
                }
            }
        }

        /// <summary>
        /// For EventHeader events, returns the part of the tracepoint name after the "_LnKn"
        /// (if any), i.e. for "MyProvider_L1KffGgroup" returns "Ggroup".
        /// Otherwise, returns empty.
        /// </summary>
        public ReadOnlyMemory<char> ProviderOptionsMemory
        {
            get
            {
                if (this.HasEventHeader)
                {
                    var formatName = this.eventDesc.Format.Name;
                    return formatName.AsMemory(GetProviderOptionsStart(formatName));
                }
                else
                {
                    return default;
                }
            }
        }

        /// <summary>
        /// For EventHeader events, returns the part of the tracepoint name after the
        /// "_LnKn" (if any), i.e. for "MyProvider_L1KffGgroup" returns "Ggroup".
        /// Otherwise, returns empty.
        /// </summary>
        public ReadOnlySpan<char> ProviderOptionsSpan
        {
            get
            {
                if (this.HasEventHeader)
                {
                    var formatName = this.eventDesc.Format.Name;
                    return formatName.AsSpan(GetProviderOptionsStart(formatName));
                }
                else
                {
                    return default;
                }
            }
        }

        /// <summary>
        /// For EventHeader events, returns the EventHeader event name's length
        /// in UTF-8 bytes. Otherwise, returns 0.
        /// </summary>
        public int EventHeaderNameLength => this.eventHeaderNameLength;

        /// <summary>
        /// For EventHeader events, returns the EventHeader event name.
        /// Otherwise, returns empty.
        /// </summary>
        public ReadOnlyMemory<byte> EventHeaderNameMemory =>
            this.ContentsMemory.Slice(this.eventHeaderNameStart, this.eventHeaderNameLength);

        /// <summary>
        /// For EventHeader events, returns the EventHeader event name.
        /// Otherwise, returns empty.
        /// </summary>
        public ReadOnlySpan<byte> EventHeaderNameSpan =>
            this.ContentsSpan.Slice(this.eventHeaderNameStart, this.eventHeaderNameLength);

        /// <summary>
        /// For EventHeader events with an ActivityID, returns the ActivityID.
        /// Otherwise, returns null.
        /// </summary>
        public Guid? ActivityId => this.activityIdLength >= 16
            ? PerfConvert.ReadGuidBigEndian(this.ContentsSpan.Slice(this.activityIdStart))
            : default(Guid?);

        /// <summary>
        /// For EventHeader events with a related ActivityID (e.g. parent activity), returns
        /// the related ActivityID. Otherwise, returns null.
        /// </summary>
        public Guid? RelatedId => this.activityIdLength >= 32
            ? PerfConvert.ReadGuidBigEndian(this.ContentsSpan.Slice(this.activityIdStart + 16))
            : default(Guid?);

        /// <summary>
        /// For EventHeader events, returns the EventHeader (flags, version, ID, tag, opcode, level).
        /// Otherwise, returns null.
        /// </summary>
        public EventHeader? EventHeader => this.HasEventHeader
            ? this.ReadEventHeader()
            : default(EventHeader?);

        /// <summary>
        /// For EventHeader events, returns the EventHeader (flags, version, ID, tag, opcode, level).
        /// Otherwise, returns default(EventHeader).
        /// </summary>
        public EventHeader EventHeaderOrDefault => this.HasEventHeader
            ? this.ReadEventHeader()
            : default;

        /// <summary>
        /// For EventHeader events, returns the EventHeader flags.
        /// Otherwise, returns null.
        /// </summary>
        public EventHeaderFlags? EventHeaderFlags
        {
            get
            {
                if (!this.HasEventHeader)
                {
                    return null;
                }

                var pos = this.rawDataStart + this.eventDesc.Format.CommonFieldsSize + 0;
                return (EventHeaderFlags)this.contents[pos];
            }
        }

        /// <summary>
        /// For EventHeader events, returns the EventHeader stable event ID (0 if not assigned).
        /// Otherwise, returns null.
        /// </summary>
        public ushort? Id
        {
            get
            {
                if (!this.HasEventHeader)
                {
                    return null;
                }

                var pos = this.rawDataStart + this.eventDesc.Format.CommonFieldsSize + 2;
                return this.byteReader.FixU16(BitConverter.ToUInt16(this.contents, pos));
            }
        }

        /// <summary>
        /// For EventHeader events, returns the EventHeader event version (0 if stable ID not assigned).
        /// Otherwise, returns null.
        /// </summary>
        public byte? Version
        {
            get
            {
                if (!this.HasEventHeader)
                {
                    return null;
                }

                var pos = this.rawDataStart + this.eventDesc.Format.CommonFieldsSize + 1;
                return this.contents[pos];
            }
        }

        /// <summary>
        /// For EventHeader events, returns the EventHeader event tag (0 if none).
        /// Otherwise, returns null.
        /// </summary>
        public ushort? Tag
        {
            get
            {
                if (!this.HasEventHeader)
                {
                    return null;
                }

                var pos = this.rawDataStart + this.eventDesc.Format.CommonFieldsSize + 4;
                return this.byteReader.FixU16(BitConverter.ToUInt16(this.contents, pos));
            }
        }

        /// <summary>
        /// For EventHeader events, returns the EventHeader event opcode.
        /// Otherwise, returns null.
        /// </summary>
        public EventOpcode? Opcode
        {
            get
            {
                if (!this.HasEventHeader)
                {
                    return null;
                }

                var pos = this.rawDataStart + this.eventDesc.Format.CommonFieldsSize + 6;
                return (EventOpcode)this.contents[pos];
            }
        }

        /// <summary>
        /// For EventHeader events, returns the EventHeader event severity level.
        /// Otherwise, returns null.
        /// </summary>
        public EventLevel? Level
        {
            get
            {
                if (!this.HasEventHeader)
                {
                    return null;
                }

                var pos = this.rawDataStart + this.eventDesc.Format.CommonFieldsSize + 7;
                return (EventLevel)this.contents[pos];
            }
        }

        /// <summary>
        /// For EventHeader events, returns the EventHeader event category bitmask.
        /// Otherwise, returns null.
        /// </summary>
        public ulong? Keyword
        {
            get
            {
                if (!this.HasEventHeader)
                {
                    return default;
                }

                var tp = this.eventDesc.Format.Name;
                var pos = tp.LastIndexOf('_');
                if (pos < 0)
                {
                    return default;
                }

                pos += 1;
                while (pos < tp.Length)
                {
                    var ch = tp[pos];
                    pos += 1;
                    if (ch == 'K')
                    {
                        ulong k = 0;
                        while (pos < tp.Length)
                        {
                            ch = tp[pos];
                            pos += 1;
                            if ('0' <= ch && ch <= '9')
                            {
                                k = k * 16 + (uint)(ch - '0');
                            }
                            else if ('a' <= ch && ch <= 'f')
                            {
                                k = k * 16 + (uint)(ch - 'a' + 10);
                            }
                            else
                            {
                                break;
                            }
                        }

                        return k;
                    }
                }

                return default;
            }
        }

        /// <summary>
        /// Returns the part of TracepointId before the first ':'.
        /// If no ':', returns the entire TracepointId (usually only empty TracepointId will lack a ':').
        /// </summary>
        public static int GetSystemNameLength(string tracepointId)
        {
            var colonPos = tracepointId.IndexOf(':');
            return colonPos < 0 ? tracepointId.Length : colonPos;
        }

        /// <summary>
        /// Returns the part of TracepointId after the first ':'.
        /// If no ':', returns empty (usually only empty TracepointId will lack a ':').
        /// </summary>
        public static int GetTracepointNameStart(string tracepointId)
        {
            var colonPos = tracepointId.IndexOf(':');
            return colonPos < 0 ? tracepointId.Length : colonPos + 1;
        }

        /// <summary>
        /// Returns the part of the tracepoint name before the last '_', i.e. for
        /// "MyProvider_L1KffGgroup" returns "MyProvider". If no '_', returns empty.
        /// Since eventheader events always have a valid format, you can get the tracepoint
        /// name from this.Format.Name.
        /// </summary>
        public static int GetProviderNameLength(string eventheaderTracepointName)
        {
            var pos = eventheaderTracepointName.LastIndexOf('_');
            return pos < 0 ? eventheaderTracepointName.Length : pos;
        }

        /// <summary>
        /// Returns the part of the eventheader tracepoint name after the "_LnKn" (if any),
        /// i.e. for "MyProvider_L1KffGgroup" returns "Ggroup". Otherwise, returns empty.
        /// Since eventheader events always have a valid format, you can get the tracepoint
        /// name from this.Format.Name.
        /// </summary>
        public static int GetProviderOptionsStart(string eventheaderTracepointName)
        {
            var pos = eventheaderTracepointName.LastIndexOf('_');
            if (pos < 0)
            {
                return eventheaderTracepointName.Length;
            }

            pos += 1;
            while (pos < eventheaderTracepointName.Length)
            {
                var ch = eventheaderTracepointName[pos];
                if ('A' <= ch && ch <= 'Z' && ch != 'L' && ch != 'K')
                {
                    return pos;
                }

                pos += 1;
            }

            return eventheaderTracepointName.Length;
        }

        /// <summary>
        /// Returns the event type (same as this.Header.Type).
        /// </summary>
        public PerfEventHeaderType GetKey()
        {
            return this.Header.Type;
        }

        /// <summary>
        /// Given a session timestamp offset (from a PerfFileInfo), returns the event's
        /// session-relative timestamp, or 0 if the event does not have a valid timestamp.
        /// </summary>
        public Timestamp GetTimestamp(long sessionTimestampOffset)
        {
            return new Timestamp(Math.Max(0, unchecked((long)this.FileRelativeTime + sessionTimestampOffset)));
        }

        private static int PopCnt(UInt32 n)
        {
            n = n - ((n >> 1) & 0x55555555);
            n = (n & 0x33333333) + ((n >> 2) & 0x33333333);
            return (int)((((n + (n >> 4)) & 0xF0F0F0F) * 0x1010101) >> 24);
        }

        private int PidOffset(UInt32 sampleType)
        {
            if (this.header.Type == PerfEventHeaderType.Sample)
            {
                return sizeof(UInt64) * (
                    (0 != (sampleType & (UInt32)PerfEventAttrSampleType.Identifier) ? 1 : 0) +
                    (0 != (sampleType & (UInt32)PerfEventAttrSampleType.IP) ? 1 : 0));
            }
            else
            {
                return this.contents.Length - sizeof(UInt64) * PopCnt(sampleType & (UInt32)(
                    PerfEventAttrSampleType.Tid |
                    PerfEventAttrSampleType.Time |
                    PerfEventAttrSampleType.Id |
                    PerfEventAttrSampleType.StreamId |
                    PerfEventAttrSampleType.Cpu |
                    PerfEventAttrSampleType.Identifier));
            }
        }

        private EventHeader ReadEventHeader()
        {
            EventHeader eh;

            var pos = this.rawDataStart + this.eventDesc.Format.CommonFieldsSize;
            eh.Flags = (EventHeaderFlags)this.contents[pos];
            pos += 1;
            eh.Version = this.contents[pos];
            pos += 1;
            eh.Id = BitConverter.ToUInt16(this.contents, pos);
            pos += 2;
            eh.Tag = BitConverter.ToUInt16(this.contents, pos);
            pos += 2;
            eh.OpcodeByte = this.contents[pos];
            pos += 1;
            eh.LevelByte = this.contents[pos];

            var byteReader = this.byteReader;
            eh.Id = byteReader.FixU16(eh.Id);
            eh.Tag = byteReader.FixU16(eh.Tag);

            return eh;
        }

    }
}
