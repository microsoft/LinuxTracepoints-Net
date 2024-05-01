// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.LinuxTracepoints.Decode
{
    using System;
    using Debug = System.Diagnostics.Debug;
    using Encoding = System.Text.Encoding;

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
    }
}
