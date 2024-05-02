// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.LinuxTracepoints.Decode
{
    using System;
    using CultureInfo = System.Globalization.CultureInfo;
    using Encoding = System.Text.Encoding;
    using StringBuilder = System.Text.StringBuilder;

    /// <summary>
    /// Provides access to the name and value of an EventHeader event item. An item is a
    /// field of the event or an element of an array field of the event. This struct is
    /// returned by the GetItemInfo() method of EventHeaderEnumerator.
    /// </summary>
    public readonly ref struct EventHeaderItemInfo
    {
        /// <summary>
        /// Initializes a new instance of the EventHeaderItemInfo struct.
        /// </summary>
        internal EventHeaderItemInfo(
            ReadOnlySpan<byte> eventData,
            int nameStart,
            int nameLength,
            PerfItemValue value)
        {
            this.EventData = eventData;
            this.NameStart = nameStart;
            this.NameLength = nameLength;
            this.Value = value;
        }

        /// <summary>
        /// The Span corresponding to the EventData parameter passed to
        /// EventHeaderEnumerator.StartEvent(). For example, if you called
        /// enumerator.StartEvent(name, myData), this will be the same as myData.Span.
        /// The NameStart field is relative to this span.
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
        /// Field value.
        /// </summary>
        public PerfItemValue Value { get; }

        /// <summary>
        /// Field type (same as Value.Type).
        /// </summary>
        public PerfItemType Type => this.Value.Type;

        /// <summary>
        /// UTF-8 encoded field name followed by 0 or more field attributes,
        /// e.g. "FieldName" or "FieldName;AttribName=AttribValue".
        /// Each attribute is ";AttribName=AttribValue".
        /// FieldName should not contain ';'.
        /// AttribName should not contain ';' or '='.
        /// AttribValue may contain ";;" which should be unescaped to ";".
        /// </summary>
        public ReadOnlySpan<byte> NameBytes => this.EventData.Slice(this.NameStart, this.NameLength);

        /// <summary>
        /// Gets a new string (decoded from NameBytes) containing
        /// field name followed by 0 or more field attributes, e.g.
        /// "FieldName" or "FieldName;AttribName=AttribValue".
        /// Each attribute is ";AttribName=AttribValue".
        /// FieldName should not contain ';'.
        /// AttribName should not contain ';' or '='.
        /// AttribValue may contain ";;" which should be unescaped to ";".
        /// </summary>
        public readonly string GetNameAsString()
        {
            return Encoding.UTF8.GetString(this.NameBytes);
        }

        /// <summary>
        /// Appends a string representation of this value like "Name = Type:Value" or "Name = Type:Value1, Value2".
        /// Returns sb.
        /// </summary>
        public StringBuilder AppendAsString(StringBuilder sb)
        {
            PerfConvert.StringAppendWithControlCharsJsonEscape(sb, this.NameBytes, Encoding.UTF8);

            var fieldTag = this.Value.Type.FieldTag;
            if (fieldTag == 0)
            {
                sb.Append(" = ");
            }
            else
            {
                sb.AppendFormat(CultureInfo.InvariantCulture, ";tag=0x{0:X} = ", fieldTag);
            }

            this.Value.AppendTo(sb);
            return sb;
        }

        /// <summary>
        /// Returns a string representation of this value like "Name = Type:Value" or "Name =Type:Value1, Value2".
        /// </summary>
        public override string ToString()
        {
            return this.AppendAsString(new StringBuilder()).ToString();
        }
    }
}
