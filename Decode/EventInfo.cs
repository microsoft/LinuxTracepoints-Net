// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Text;
using Debug = System.Diagnostics.Debug;

namespace Microsoft.LinuxTracepoints.Decode
{
    /// <summary>
    /// Event attributes returned by the GetEventInfo() method of EventEnumerator.
    /// </summary>
    public struct EventInfo
    {
        /// <summary>
        /// UTF-8 encoded "EventName" followed by 0 or more field attributes.
        /// Each attribute is ";AttribName=AttribValue".
        /// EventName should not contain ';'.
        /// AttribName should not contain ';' or '='.
        /// AttribValue may contain ";;" which should be unescaped to ";".
        /// </summary>
        public ReadOnlyMemory<byte> NameBytes;

        /// <summary>
        /// TracepointName, e.g. "ProviderName_LnKnnnOptions".
        /// </summary>
        public string TracepointName;

        /// <summary>
        /// Big-endian activity id bytes. 0 bytes for none,
        /// 16 bytes for activity id only, 32 bytes for activity id and related id.
        /// </summary>
        public ReadOnlyMemory<byte> ActivityIdBytes;

        /// <summary>
        /// Flags, Version, Id, Tag, Opcode, Level.
        /// </summary>
        public EventHeader Header;

        /// <summary>
        /// Event category bits.
        /// </summary>
        public ulong Keyword;

        /// <summary>
        /// Gets a new string (decoded from NameBytes) containing
        /// "EventName" followed by 0 or more field attributes.
        /// Each attribute is ";AttribName=AttribValue".
        /// EventName should not contain ';'.
        /// AttribName should not contain ';' or '='.
        /// AttribValue may contain ";;" which should be unescaped to ";".
        /// </summary>
        public string Name
        {
            get
            {
                return Encoding.UTF8.GetString(this.NameBytes.Span);
            }
        }

        /// <summary>
        /// Gets the chars of ProviderName, i.e. the part of TracepointName
        /// before level and keyword, e.g. if TracepointName is
        /// "ProviderName_LnKnnnOptions", returns "ProviderName".
        /// </summary>
        public ReadOnlyMemory<char> ProviderName
        {
            get
            {
                return this.TracepointName.AsMemory(0, this.TracepointName.LastIndexOf('_'));
            }
        }

        /// <summary>
        /// Gets the chars of Options, i.e. the part of TracepointName after
        /// level and keyword, e.g. if TracepointName is "ProviderName_LnKnnnOptions",
        /// returns "Options".
        /// </summary>
        public ReadOnlyMemory<char> Options
        {
            get
            {
                var n = this.TracepointName;
                for (var i = n.LastIndexOf('_') + 1; i < n.Length; i += 1)
                {
                    char ch = n[i];
                    if ('A' <= ch && ch <= 'Z' && ch != 'L' && ch != 'K')
                    {
                        return n.AsMemory(i);
                    }
                }

                return default;
            }
        }

        /// <summary>
        /// 128-bit activity id decoded from ActivityIdBytes, or NULL if no activity id.
        /// </summary>
        public Guid? ActivityId
        {
            get
            {
                var span = this.ActivityIdBytes.Span;
                Debug.Assert((span.Length & 0xF) == 0);
                return span.Length < 16
                    ? new Guid?()
                    : EventUtility.ReadGuidBigEndian(span);
            }
        }

        /// <summary>
        /// 128-bit related id decoded from ActivityIdBytes, or NULL if no related id.
        /// </summary>
        public Guid? RelatedActivityId
        {
            get
            {
                var span = this.ActivityIdBytes.Span;
                Debug.Assert((span.Length & 0xF) == 0);
                return span.Length < 32
                    ? new Guid?()
                    : EventUtility.ReadGuidBigEndian(span.Slice(16));
            }
        }
    }
}
