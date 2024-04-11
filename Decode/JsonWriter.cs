// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.LinuxTracepoints.Decode
{
    using System;
    using StringBuilder = System.Text.StringBuilder;
    using Text = System.Text;

    internal struct JsonWriter
    {
        private StringBuilder builder;
        private bool comma;
        private bool currentSpace;
        private bool nextSpace;
        private bool wantFieldTag;

        public JsonWriter(StringBuilder builder, PerfJsonOptions options, bool comma)
        {
            this.builder = builder;
            this.comma = comma;
            this.currentSpace = false;
            this.nextSpace = options.HasFlag(PerfJsonOptions.Space);
            this.wantFieldTag = options.HasFlag(PerfJsonOptions.FieldTag);
        }

        public bool Comma => this.comma;

        public void WritePropertyNameNoEscape(ReadOnlySpan<char> name)
        {
            this.CommaSpace();
            this.builder.Append('"');
            this.builder.Append(name); // Assume no escaping needed.
            this.builder.Append("\":");
            this.comma = false;
        }

        public void WritePropertyName(ReadOnlySpan<byte> nameUtf8, ushort fieldTag)
        {
            this.CommaSpace();
            this.builder.Append('"');

            PerfConvert.AppendEscapedJson(this.builder, nameUtf8, Text.Encoding.UTF8);
            if (this.wantFieldTag && fieldTag != 0)
            {
                this.builder.Append(";tag=");
                PerfConvert.UInt32HexAppend(this.builder, fieldTag);
            }

            this.builder.Append("\":");
            this.comma = false;
        }

        public void WriteStartObject()
        {
            this.CommaSpace();
            this.builder.Append('{');
            this.comma = false;
        }

        public void WriteEndObject()
        {
            if (this.currentSpace) this.builder.Append(' ');
            this.builder.Append('}');
            this.comma = true;
        }

        public void WriteStartArray()
        {
            this.CommaSpace();
            this.builder.Append('[');
            this.comma = false;
        }

        public void WriteEndArray()
        {
            if (this.currentSpace) this.builder.Append(' ');
            this.builder.Append(']');
            this.comma = true;
        }

        public StringBuilder WriteValue()
        {
            this.CommaSpace();
            this.comma = true;
            return this.builder;
        }

        public StringBuilder WriteValueNoEscapeName(ReadOnlySpan<char> name)
        {
            WritePropertyNameNoEscape(name);
            if (this.currentSpace) this.builder.Append(' ');
            this.comma = true;
            return this.builder;
        }

        private void CommaSpace()
        {
            if (this.comma)
            {
                this.builder.Append(',');
            }

            if (this.currentSpace) this.builder.Append(' ');
            this.currentSpace = this.nextSpace;
        }
    }
}
