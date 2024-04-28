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
            ReadOnlySpan<byte> nameBytes,
            PerfItemValue value)
        {
            this.NameBytes = nameBytes;
            this.Value = value;
        }

        /// <summary>
        /// UTF-8 encoded field name followed by 0 or more field attributes,
        /// e.g. "FieldName" or "FieldName;AttribName=AttribValue".
        /// Each attribute is ";AttribName=AttribValue".
        /// FieldName should not contain ';'.
        /// AttribName should not contain ';' or '='.
        /// AttribValue may contain ";;" which should be unescaped to ";".
        /// </summary>
        public ReadOnlySpan<byte> NameBytes { get; }

        /// <summary>
        /// Field value.
        /// </summary>
        public PerfItemValue Value { get; }

        /// <summary>
        /// Field type (same as Value.Type).
        /// </summary>
        public PerfItemType Type => this.Value.Type;

        /// <summary>
        /// Gets a new string (decoded from NameBytes) containing
        /// field name followed by 0 or more field attributes, e.g.
        /// "FieldName" or "FieldName;AttribName=AttribValue".
        /// Each attribute is ";AttribName=AttribValue".
        /// FieldName should not contain ';'.
        /// AttribName should not contain ';' or '='.
        /// AttribValue may contain ";;" which should be unescaped to ";".
        /// </summary>
        public string NameAsString => Encoding.UTF8.GetString(this.NameBytes);

        /// <summary>
        /// Appends a string representation of this value like "Name = Type:Value" or "Name = Type:Value1, Value2".
        /// Returns sb.
        /// </summary>
        public StringBuilder AppendAsString(StringBuilder sb)
        {
            PerfConvert.StringAppend(sb, this.NameBytes, Encoding.UTF8);

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
