// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.LinuxTracepoints.DecodeWpa
{
    using Microsoft.LinuxTracepoints.Decode;
    using Microsoft.Performance.SDK.Extensibility;
    using System;
    using System.Diagnostics.Tracing;
    using System.Text;
    using Debug = System.Diagnostics.Debug;
    using Timestamp = Microsoft.Performance.SDK.Timestamp;

    public sealed class PerfEventInfo : IKeyedDataType<PerfEventHeaderType>
    {
        private readonly ulong fileRelativeTime;
        private readonly PerfEventHeader header;

        private readonly byte[] contents; // event bytes, starting from immediately after header.
        private readonly PerfFileInfo fileInfo;
        private readonly PerfEventDesc desc;

        private readonly ushort rawDataStart;
        private readonly ushort rawDataLength;
        private readonly ushort eventHeaderNameStart;
        private readonly ushort eventHeaderNameLength;
        private readonly ushort activityIdStart;
        private readonly byte activityIdLength;

        /// <summary>
        /// For raw events, i.e. non-sample events without event info.
        /// </summary>
        internal PerfEventInfo(
            PerfFileInfo fileInfo,
            PerfEventBytes bytes)
        {
            this.fileRelativeTime = 0;
            this.header = bytes.Header;
            var bytesSpan = bytes.Span;
            this.contents = bytesSpan.Length > 8 ? bytesSpan.Slice(8).ToArray() : Array.Empty<byte>();
            this.fileInfo = fileInfo;
            this.desc = PerfEventDesc.Empty;
            this.rawDataStart = 0;
            this.rawDataLength = 0;
        }

        /// <summary>
        /// For non-sample events with info.
        /// </summary>
        internal PerfEventInfo(
            PerfFileInfo fileInfo,
            PerfEventHeader header,
            PerfNonSampleEventInfo info)
        {
            this.fileRelativeTime = info.Time;
            this.header = header;
            var bytesSpan = info.BytesSpan;
            this.contents = bytesSpan.Length > 8 ? bytesSpan.Slice(8).ToArray() : Array.Empty<byte>();
            this.fileInfo = fileInfo;
            this.desc = info.EventDesc;
            this.rawDataStart = 0;
            this.rawDataLength = 0;
        }

        /// <summary>
        /// For sample events.
        /// </summary>
        internal PerfEventInfo(
            PerfFileInfo fileInfo,
            PerfEventHeader header,
            PerfSampleEventInfo info)
        {
            this.fileRelativeTime = info.Time;
            this.header = header;
            var bytesSpan = info.BytesSpan;
            this.contents = bytesSpan.Length > 8 ? bytesSpan.Slice(8).ToArray() : Array.Empty<byte>();
            this.fileInfo = fileInfo;
            this.desc = info.EventDesc;
            this.rawDataStart = (ushort)(info.RawDataStart >= 8 ? info.RawDataStart - 8 : 0);
            this.rawDataLength = (ushort)info.RawDataLength;
        }

        /// <summary>
        /// For eventheader sample events.
        /// Requires info.EventDesc.Format != null.
        /// </summary>
        internal PerfEventInfo(
            PerfFileInfo fileInfo,
            PerfEventHeader header,
            PerfSampleEventInfo info,
            in EventHeaderEventInfo ehEventInfo)
            : this(fileInfo, header, info)
        {
            var userDataStart = (ushort)(this.rawDataStart + this.desc.Format!.CommonFieldsSize);

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

        public ulong FileRelativeTime => this.fileRelativeTime;

        public PerfEventHeader Header => this.header;

        public ReadOnlyMemory<byte> ContentsMemory => this.contents;

        public ReadOnlySpan<byte> ContentsSpan => this.contents;

        public PerfFileInfo FileInfo => this.fileInfo;

        public PerfEventDesc EventDesc => this.desc;

        public string TracepointId => this.desc.Name;

        public ReadOnlyMemory<char> SystemName
        {
            get
            {
                var tracepointId = this.desc.Name;
                var colonPos = tracepointId.IndexOf(':');
                if (colonPos < 0)
                {
                    return tracepointId.AsMemory();
                }
                else
                {
                    return tracepointId.AsMemory(0, colonPos);
                }
            }
        }

        public ReadOnlyMemory<char> TracepointName
        {
            get
            {
                var tracepointId = this.desc.Name;
                var colonPos = tracepointId.IndexOf(':');
                if (colonPos < 0)
                {
                    return tracepointId.AsMemory();
                }
                else
                {
                    return tracepointId.AsMemory(colonPos + 1);
                }
            }
        }

        public PerfEventFormat? Format => this.desc.Format;

        public int RawDataLength => this.rawDataLength;

        public ReadOnlyMemory<byte> RawDataMemory => this.contents.AsMemory(this.rawDataStart, this.rawDataLength);

        public ReadOnlySpan<byte> RawDataSpan => this.contents.AsSpan(this.rawDataStart, this.rawDataLength);

        public UInt32 Cpu { get; } // TODO

        public UInt32 Pid { get; } // TODO

        public UInt32 Tid { get; } // TODO

        public bool HasEventHeader => this.eventHeaderNameStart != 0;

        /// <summary>
        /// EventHeader only. Return the part of the tracepoint name before the last '_',
        /// i.e. for "MyProvider_L1K1" returns "MyProvider".
        /// </summary>
        public ReadOnlyMemory<char> ProviderName
        {
            get
            {
                var format = this.desc.Format;
                if (format == null || !this.HasEventHeader)
                {
                    return default;
                }

                var tp = format.Name;
                var pos = tp.LastIndexOf('_');
                if (pos < 0)
                {
                    return default;
                }

                return tp.AsMemory(0, pos);
            }
        }

        /// <summary>
        /// EventHeader only. Return the part of the tracepoint name after the "_LnKn",
        /// if any, i.e. for "MyProvider_L1K1Ggroup" returns "Ggroup".
        /// </summary>
        public ReadOnlyMemory<char> ProviderOptions
        {
            get
            {
                var format = this.desc.Format;
                if (format == null || !this.HasEventHeader)
                {
                    return default;
                }

                var tp = format.Name;
                var pos = tp.LastIndexOf('_');
                if (pos < 0)
                {
                    return default;
                }

                pos += 1;
                while (pos < tp.Length)
                {
                    var ch = tp[pos];
                    if ('A' <= ch && ch <= 'Z' && ch != 'L' && ch != 'K')
                    {
                        return tp.AsMemory(pos);
                    }

                    pos += 1;
                }

                return default;
            }
        }

        /// <summary>
        /// EventHeader only.
        /// </summary>
        public ReadOnlyMemory<byte> EventHeaderNameBytes =>
            this.ContentsMemory.Slice(this.eventHeaderNameStart, this.eventHeaderNameLength);

        /// <summary>
        /// EventHeader only.
        /// </summary>
        public Guid? ActivityId =>
            this.activityIdLength >= 16
            ? PerfConvert.ReadGuidBigEndian(this.ContentsSpan.Slice(this.activityIdStart))
            : new Guid?();

        /// <summary>
        /// EventHeader only.
        /// </summary>
        public Guid? RelatedId =>
            this.activityIdLength >= 32
            ? PerfConvert.ReadGuidBigEndian(this.ContentsSpan.Slice(this.activityIdStart + 16))
            : new Guid?();

        /// <summary>
        /// EventHeader only.
        /// </summary>
        public EventHeader? EventHeader
        {
            get
            {
                if (!this.HasEventHeader)
                {
                    return null;
                }

                var pos = this.rawDataStart + this.desc.Format!.CommonFieldsSize;
                EventHeader eh;
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

                var byteReader = this.fileInfo.ByteReader;
                eh.Id = byteReader.FixU16(eh.Id);
                eh.Tag = byteReader.FixU16(eh.Tag);

                return eh;
            }
        }

        /// <summary>
        /// EventHeader only.
        /// </summary>
        public EventHeaderFlags? EventHeaderFlags
        {
            get
            {
                if (!this.HasEventHeader)
                {
                    return null;
                }

                var pos = this.rawDataStart + this.desc.Format!.CommonFieldsSize + 0;
                return (EventHeaderFlags)this.contents[pos];
            }
        }

        /// <summary>
        /// EventHeader only.
        /// </summary>
        public ushort? Id
        {
            get
            {
                if (!this.HasEventHeader)
                {
                    return null;
                }

                var pos = this.rawDataStart + this.desc.Format!.CommonFieldsSize + 2;
                return this.fileInfo.ByteReader.FixU16(BitConverter.ToUInt16(this.contents, pos));
            }
        }

        /// <summary>
        /// EventHeader only.
        /// </summary>
        public byte? Version
        {
            get
            {
                if (!this.HasEventHeader)
                {
                    return null;
                }

                var pos = this.rawDataStart + this.desc.Format!.CommonFieldsSize + 1;
                return this.contents[pos];
            }
        }

        /// <summary>
        /// EventHeader only.
        /// </summary>
        public ushort? Tag
        {
            get
            {
                if (!this.HasEventHeader)
                {
                    return null;
                }

                var pos = this.rawDataStart + this.desc.Format!.CommonFieldsSize + 4;
                return this.fileInfo.ByteReader.FixU16(BitConverter.ToUInt16(this.contents, pos));
            }
        }

        /// <summary>
        /// EventHeader only.
        /// </summary>
        public EventOpcode? Opcode
        {
            get
            {
                if (!this.HasEventHeader)
                {
                    return null;
                }

                var pos = this.rawDataStart + this.desc.Format!.CommonFieldsSize + 6;
                return (EventOpcode)this.contents[pos];
            }
        }

        /// <summary>
        /// EventHeader only.
        /// </summary>
        public EventLevel? Level
        {
            get
            {
                if (!this.HasEventHeader)
                {
                    return null;
                }

                var pos = this.rawDataStart + this.desc.Format!.CommonFieldsSize + 7;
                return (EventLevel)this.contents[pos];
            }
        }

        /// <summary>
        /// EventHeader only.
        /// </summary>
        public ulong? Keyword
        {
            get
            {
                var format = this.desc.Format;
                if (format == null || !this.HasEventHeader)
                {
                    return default;
                }

                var tp = format.Name;
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

        public PerfEventHeaderType GetKey()
        {
            return this.header.Type;
        }

        /// <summary>
        /// Returns a new string with a human-friendly group for the event.
        /// For eventheader events, this is the provider name.
        /// Otherwise, this is the system name.
        /// </summary>
        public string GetGroupName()
        {
            if (this.HasEventHeader)
            {
                return this.ProviderName.ToString();
            }

            var format = this.desc.Format;
            if (format != null)
            {
                return format.SystemName;
            }

            return this.SystemName.ToString();
        }

        /// <summary>
        /// Returns a new string with a human-friendly name for the event.
        /// For eventheader events, this is the eventheader event name.
        /// For tracepoint events, this is the tracepoint name.
        /// Otherwise, this is the event type.
        /// </summary>
        public string GetEventName()
        {
            if (this.HasEventHeader)
            {
                return this.GetEventHeaderName();
            }

            var format = this.desc.Format;
            if (format != null)
            {
                return format.Name;
            }

            return this.header.Type.AsString();
        }

        /// <summary>
        /// EventHeader-only. Returns a new string with the eventheader event name.
        /// </summary>
        public string GetEventHeaderName()
        {
            return this.eventHeaderNameLength != 0
                ? Encoding.UTF8.GetString(this.ContentsSpan.Slice(this.eventHeaderNameStart, this.eventHeaderNameLength))
                : "";
        }

        public Timestamp GetTimestamp(long sessionTimestampOffset)
        {
            return new Timestamp(Math.Max(0, unchecked((long)this.fileRelativeTime + sessionTimestampOffset)));
        }

        public bool AppendValueAsJson(
            EventHeaderEnumerator enumerator,
            StringBuilder sb,
            bool addCommaBeforeNextItem = false,
            PerfConvertOptions convertOptions = PerfConvertOptions.Default)
        {
            bool needComma = addCommaBeforeNextItem;
            var comma = convertOptions.HasFlag(PerfConvertOptions.Space) ? ", " : "";

            var format = this.Format;
            if (format == null)
            {
                // No format - probably a non-sample event.
                if (needComma)
                {
                    sb.Append(comma);
                }

                needComma = true;

                sb.Append('"');
                var len = Math.Min(256, this.contents.Length); // Limit output to 256 bytes.
                PerfConvert.HexBytesAppend(sb, this.ContentsSpan.Slice(0, len));
                sb.Append('"');
            }
            else if (format.DecodingStyle == PerfEventDecodingStyle.EventHeader &&
                enumerator.StartEvent(format.Name, this.RawDataMemory.Slice(format.CommonFieldsSize)))
            {
                // EventHeader-style decoding.
                needComma = enumerator.AppendJsonItemToAndMoveNextSibling(sb, needComma, convertOptions);
            }
            else
            {
                // TraceFS format file decoding.
                var colon = convertOptions.HasFlag(PerfConvertOptions.Space) ? ": " : ":";
                var rawData = this.RawDataSpan;
                for (int fieldIndex = format.CommonFieldCount; fieldIndex < format.Fields.Count; fieldIndex += 1)
                {
                    if (needComma)
                    {
                        sb.Append(comma);
                    }

                    needComma = true;

                    var field = format.Fields[fieldIndex];
                    PerfConvert.StringAppendJson(sb, field.Name);
                    sb.Append(colon);

                    var fieldVal = field.GetFieldValue(rawData, this.FileInfo.ByteReader);
                    if (fieldVal.IsArrayOrElement)
                    {
                        fieldVal.AppendJsonSimpleArrayTo(sb, convertOptions);
                    }
                    else
                    {
                        fieldVal.AppendJsonScalarTo(sb, convertOptions);
                    }
                }
            }

            return needComma;
        }
    }
}
