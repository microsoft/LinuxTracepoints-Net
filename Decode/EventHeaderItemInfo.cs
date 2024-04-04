// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

#pragma warning disable CA1051 // Do not declare visible instance fields

namespace Microsoft.LinuxTracepoints.Decode
{
    using System;
    using Encoding = System.Text.Encoding;

    /// <summary>
    /// Event item attributes (attributes of a value, array, or structure within the event)
    /// returned by the GetItemInfo() method of EventHeaderEnumerator.
    /// </summary>
    public readonly ref struct EventHeaderItemInfo
    {
        /// <summary>
        /// UTF-8 encoded field name followed by 0 or more field attributes,
        /// e.g. "FieldName" or "FieldName;AttribName=AttribValue".
        /// Each attribute is ";AttribName=AttribValue".
        /// FieldName should not contain ';'.
        /// AttribName should not contain ';' or '='.
        /// AttribValue may contain ";;" which should be unescaped to ";".
        /// </summary>
        public readonly ReadOnlySpan<byte> NameBytes;

        /// <summary>
        /// Field value.
        /// </summary>
        public readonly PerfValue Value;

        /// <summary>
        /// Initializes a new instance of the EventHeaderItemInfo struct.
        /// </summary>
        public EventHeaderItemInfo(
            ReadOnlySpan<byte> nameBytes,
            PerfValue value)
        {
            this.NameBytes = nameBytes;
            this.Value = value;
        }

        /// <summary>
        /// Gets a new string (decoded from NameBytes) containing
        /// field name followed by 0 or more field attributes, e.g.
        /// "FieldName" or "FieldName;AttribName=AttribValue".
        /// Each attribute is ";AttribName=AttribValue".
        /// FieldName should not contain ';'.
        /// AttribName should not contain ';' or '='.
        /// AttribValue may contain ";;" which should be unescaped to ";".
        /// </summary>
        public readonly string NameAsString => Encoding.UTF8.GetString(this.NameBytes);
    }
}
