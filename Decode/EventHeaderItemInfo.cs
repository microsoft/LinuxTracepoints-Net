// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.LinuxTracepoints.Decode
{
    using System;
    using CultureInfo = System.Globalization.CultureInfo;
    using Encoding = System.Text.Encoding;
    using StringBuilder = System.Text.StringBuilder;

    /// <summary>
    /// Event item attributes (attributes of a value, array, or structure within the event)
    /// returned by the GetItemInfo() method of EventHeaderEnumerator.
    /// </summary>
    public readonly ref struct EventHeaderItemInfo
    {
        /// <summary>
        /// Initializes a new instance of the EventHeaderItemInfo struct.
        /// </summary>
        internal EventHeaderItemInfo(
            ReadOnlySpan<byte> nameBytes,
            PerfValue value)
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
        public PerfValue Value { get; }

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

            var fieldTag = this.Value.FieldTag;
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
