// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.LinuxTracepoints.Decode
{
    using System;
    using Debug = System.Diagnostics.Debug;
    using Encoding = System.Text.Encoding;
    using StringBuilder = System.Text.StringBuilder;

    /// <summary>
    /// Event attributes returned by the GetEventInfo() method of EventHeaderEnumerator.
    /// </summary>
    public readonly ref struct EventHeaderEventInfo
    {
        /// <summary>
        /// Initializes a new instance of the EventHeaderEventInfo struct.
        /// </summary>
        internal EventHeaderEventInfo(
            ReadOnlySpan<byte> eventData,
            int nameStart,
            int nameLength,
            int activityIdStart,
            int activityIdLength,
            string tracepointName,
            EventHeader header,
            ulong keyword)
        {
            this.EventData = eventData;
            this.NameStart = nameStart;
            this.NameLength = nameLength;
            this.ActivityIdStart = activityIdStart;
            this.ActivityIdLength = activityIdLength;
            this.TracepointName = tracepointName;
            this.Header = header;
            this.Keyword = keyword;
        }

        /// <summary>
        /// The Span corresponding to the EventData parameter passed to
        /// EventHeaderEnumerator.StartEvent(). For example, if you called
        /// enumerator.StartEvent(name, myData), this will be the same as myData.Span.
        /// The NameStart and ActivityIdStart fields are relative to this span.
        /// </summary>
        public ReadOnlySpan<byte> EventData { get; }

        /// <summary>
        /// Offset into EventData where NameBytes begins.
        /// </summary>
        public int NameStart { get; }

        /// <summary>
        /// Length of NameBytes.
        /// </summary>
        public int NameLength { get; }

        /// <summary>
        /// Offset into EventData where ActivityIdBytes begins.
        /// </summary>
        public int ActivityIdStart { get; }

        /// <summary>
        /// Length of ActivityIdBytes (may be 0, 16, or 32).
        /// </summary>
        public int ActivityIdLength { get; }

        /// <summary>
        /// TracepointName, e.g. "ProviderName_LnKnnnOptions".
        /// </summary>
        public string TracepointName { get; }

        /// <summary>
        /// Flags, Version, Id, Tag, Opcode, Level.
        /// </summary>
        public EventHeader Header { get; }

        /// <summary>
        /// Event category bits.
        /// </summary>
        public ulong Keyword { get; }

        /// <summary>
        /// UTF-8 encoded "EventName" followed by 0 or more field attributes.
        /// Each attribute is ";AttribName=AttribValue".
        /// EventName should not contain ';'.
        /// AttribName should not contain ';' or '='.
        /// AttribValue may contain ";;" which should be unescaped to ";".
        /// </summary>
        public ReadOnlySpan<byte> NameBytes =>
            this.EventData.Slice(this.NameStart, this.NameLength);

        /// <summary>
        /// Gets a new string (decoded from NameBytes) containing
        /// "EventName" followed by 0 or more field attributes.
        /// Each attribute is ";AttribName=AttribValue".
        /// EventName should not contain ';'.
        /// AttribName should not contain ';' or '='.
        /// AttribValue may contain ";;" which should be unescaped to ";".
        /// </summary>
        public string NameAsString =>
            Encoding.UTF8.GetString(this.EventData.Slice(this.NameStart, this.NameLength));

        /// <summary>
        /// Gets the chars of ProviderName, i.e. the part of TracepointName
        /// before level and keyword, e.g. if TracepointName is
        /// "ProviderName_LnKnnnOptions", returns "ProviderName".
        /// </summary>
        public ReadOnlySpan<char> ProviderName =>
            this.TracepointName.AsSpan(0, this.TracepointName.LastIndexOf('_'));

        /// <summary>
        /// Gets the chars of Options, i.e. the part of TracepointName after
        /// level and keyword, e.g. if TracepointName is "ProviderName_LnKnnnOptions",
        /// returns "Options".
        /// </summary>
        public ReadOnlySpan<char> Options
        {
            get
            {
                var n = this.TracepointName;
                for (var i = n.LastIndexOf('_') + 1; i < n.Length; i += 1)
                {
                    char ch = n[i];
                    if ('A' <= ch && ch <= 'Z' && ch != 'L' && ch != 'K')
                    {
                        return n.AsSpan(i);
                    }
                }

                return default;
            }
        }

        /// <summary>
        /// Big-endian activity id bytes. 0 bytes for none,
        /// 16 bytes for activity id only, 32 bytes for activity id and related id.
        /// </summary>
        public ReadOnlySpan<byte> ActivityIdBytes =>
            this.EventData.Slice(this.ActivityIdStart, this.ActivityIdLength);

        /// <summary>
        /// 128-bit activity id decoded from ActivityIdBytes, or NULL if no activity id.
        /// </summary>
        public Guid? ActivityId
        {
            get
            {
                Debug.Assert((this.ActivityIdLength & 0xF) == 0);
                return this.ActivityIdLength < 16
                    ? new Guid?()
                    : PerfConvert.ReadGuidBigEndian(this.EventData.Slice(this.ActivityIdStart));
            }
        }

        /// <summary>
        /// 128-bit related id decoded from ActivityIdBytes, or NULL if no related id.
        /// </summary>
        public Guid? RelatedActivityId
        {
            get
            {
                Debug.Assert((this.ActivityIdLength & 0xF) == 0);
                return this.ActivityIdLength < 32
                    ? new Guid?()
                    : PerfConvert.ReadGuidBigEndian(this.EventData.Slice(this.ActivityIdStart + 16));
            }
        }

        /// <summary>
        /// Returns TracepointName, or "" if none.
        /// </summary>
        public override string ToString()
        {
            return this.TracepointName ?? "";
        }

        /// <summary>
        /// <para>
        /// Appends the current event identity to the provided StringBuilder as a JSON string,
        /// e.g. <c>"MyProvider:MyEvent"</c> (including the quotation marks).
        /// </para><para>
        /// The event identity includes the provider name and event name, e.g. "MyProvider:MyEvent".
        /// This is commonly used as the value of the "n" property in the JSON rendering of the event.
        /// </para>
        /// </summary>
        public void AppendJsonEventIdentityTo(StringBuilder sb)
        {
            sb.Append('"');
            PerfConvert.StringAppendWithControlCharsJsonEscape(sb, this.ProviderName);
            sb.Append(':');
            PerfConvert.StringAppendWithControlCharsJsonEscape(sb, this.NameBytes, Encoding.UTF8);
            sb.Append('"');
        }

        /// <summary>
        /// <para>
        /// Appends event metadata to the provided StringBuilder as a comma-separated list
        /// of 0 or more JSON name-value pairs, e.g. <c>"level": 5, "keyword": 3</c>
        /// (including the quotation marks).
        /// </para><para>
        /// One name-value pair is appended for each metadata item that is both requested
        /// by infoOptions and has a meaningful value available in the event. For example,
        /// the "id" metadata item is only appended if the event has a non-zero Id value,
        /// even if the infoOptions parameter includes PerfInfoOptions.Id.
        /// </para><para>
        /// The following metadata items are supported:
        /// <list type="bullet"><item>
        /// "provider": "MyProviderName"
        /// </item><item>
        /// "event": "MyEventName"
        /// </item><item>
        /// "id": 123 (omitted if zero)
        /// </item><item>
        /// "version": 1 (omitted if zero)
        /// </item><item>
        /// "level": 5 (omitted if zero)
        /// </item><item>
        /// "keyword": "0x1" (omitted if zero)
        /// </item><item>
        /// "opcode": 1 (omitted if zero)
        /// </item><item>
        /// "tag": "0x123" (omitted if zero)
        /// </item><item>
        /// "activity": "12345678-1234-1234-1234-1234567890AB" (omitted if not present)
        /// </item><item>
        /// "relatedActivity": "12345678-1234-1234-1234-1234567890AB" (omitted if not present)
        /// </item><item>
        /// "options": "Gmygroup" (omitted if not present)
        /// </item><item>
        /// "flags": "0x7" (omitted if zero)
        /// </item></list>
        /// </para>
        /// </summary>
        /// <returns>
        /// Returns true if a comma would be needed before subsequent JSON output, i.e. if
        /// addCommaBeforeNextItem was true OR if any metadata items were appended.
        /// </returns>
        public bool AppendJsonEventInfoTo(
            StringBuilder sb,
            bool addCommaBeforeNextItem,
            PerfInfoOptions infoOptions = PerfInfoOptions.Default,
            PerfConvertOptions convertOptions = PerfConvertOptions.Default)
        {
            var w = new JsonWriter(sb, convertOptions, addCommaBeforeNextItem);

            int providerNameEnd =
                0 != (infoOptions & (PerfInfoOptions.Provider | PerfInfoOptions.Options))
                ? this.TracepointName.LastIndexOf('_')
                : 0;

            if (infoOptions.HasFlag(PerfInfoOptions.Provider))
            {
                PerfConvert.StringAppendJson(
                    w.WriteValueNoEscapeName("provider"),
                    this.TracepointName.AsSpan().Slice(0, providerNameEnd));
            }

            if (infoOptions.HasFlag(PerfInfoOptions.Event))
            {
                PerfConvert.StringAppendJson(
                    w.WriteValueNoEscapeName("event"),
                    this.NameBytes,
                    Encoding.UTF8);
            }

            if (infoOptions.HasFlag(PerfInfoOptions.Id) && this.Header.Id != 0)
            {
                PerfConvert.UInt32DecimalAppend(
                    w.WriteValueNoEscapeName("id"),
                    this.Header.Id);
            }

            if (infoOptions.HasFlag(PerfInfoOptions.Version) && this.Header.Version != 0)
            {
                PerfConvert.UInt32DecimalAppend(
                    w.WriteValueNoEscapeName("version"),
                    this.Header.Version);
            }

            if (infoOptions.HasFlag(PerfInfoOptions.Level) && this.Header.Level != 0)
            {
                PerfConvert.UInt32DecimalAppend(
                    w.WriteValueNoEscapeName("level"),
                    (byte)this.Header.Level);
            }

            if (infoOptions.HasFlag(PerfInfoOptions.Keyword) && this.Keyword != 0)
            {
                PerfConvert.UInt64HexAppendJson(
                    w.WriteValueNoEscapeName("keyword"),
                    this.Keyword,
                    convertOptions);
            }

            if (infoOptions.HasFlag(PerfInfoOptions.Opcode) && this.Header.Opcode != 0)
            {
                PerfConvert.UInt32DecimalAppend(
                    w.WriteValueNoEscapeName("opcode"),
                    (byte)this.Header.Opcode);
            }

            if (infoOptions.HasFlag(PerfInfoOptions.Tag) && this.Header.Tag != 0)
            {
                PerfConvert.UInt32HexAppendJson(
                    w.WriteValueNoEscapeName("tag"),
                    this.Header.Tag,
                    convertOptions);
            }

            if (infoOptions.HasFlag(PerfInfoOptions.Activity) && this.ActivityIdLength >= 16)
            {
                w.WriteValueNoEscapeName("activity");
                sb.Append('"');
                PerfConvert.GuidAppend(
                    sb,
                    PerfConvert.ReadGuidBigEndian(this.EventData.Slice(this.ActivityIdStart)));
                sb.Append('"');
            }

            if (infoOptions.HasFlag(PerfInfoOptions.RelatedActivity) && this.ActivityIdLength >= 32)
            {
                w.WriteValueNoEscapeName("relatedActivity");
                sb.Append('"');
                PerfConvert.GuidAppend(
                    sb,
                    PerfConvert.ReadGuidBigEndian(this.EventData.Slice(this.ActivityIdStart + 16)));
                sb.Append('"');
            }

            if (infoOptions.HasFlag(PerfInfoOptions.Options))
            {
                var n = this.TracepointName;
                for (int i = providerNameEnd + 1; i < n.Length; i += 1)
                {
                    var ch = n[i];
                    if ('A' <= ch && ch <= 'Z' && ch != 'L' && ch != 'K')
                    {
                        PerfConvert.StringAppendJson(
                            w.WriteValueNoEscapeName("options"),
                            n.AsSpan(i));
                        break;
                    }
                }
            }

            if (infoOptions.HasFlag(PerfInfoOptions.Flags))
            {
                PerfConvert.UInt32HexAppendJson(
                    w.WriteValueNoEscapeName("flags"),
                    (byte)this.Header.Flags,
                    convertOptions);
            }

            return w.Comma;
        }
    }
}
