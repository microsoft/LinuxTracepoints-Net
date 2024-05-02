namespace DecodeTest
{
    using Microsoft.LinuxTracepoints.Decode;
    using System;
    using System.Text;

    internal sealed class JsonStringWriter
    {
        private readonly StringBuilder builder;
        private readonly int spacesPerIndent;
        private int indentLevel;
        private bool comma;

        public JsonStringWriter(int spacesPerIndent)
        {
            this.builder = new StringBuilder();
            this.spacesPerIndent = spacesPerIndent;
            this.indentLevel = 0;
            this.comma = false;
        }

        public StringBuilder Builder => this.builder;

        public void Reset()
        {
            this.builder.Clear();
            this.indentLevel = 0;
            this.comma = false;
        }

        public override string ToString()
        {
            return this.builder.ToString();
        }

        public void WriteCommentValue(ReadOnlySpan<char> value)
        {
            if (this.comma)
            {
                this.builder.Append(',');
            }

            this.builder.AppendLine();
            this.builder.Append(' ', this.indentLevel * this.spacesPerIndent);
            this.builder.Append("/*");
            this.builder.Append(value);
            this.builder.Append("*/");
            this.comma = false;
        }

        public void WritePropertyName(ReadOnlySpan<char> name)
        {
            if (this.comma)
            {
                this.builder.Append(',');
            }

            this.builder.Append(' ');
            StringValueNoComma(name);
            this.builder.Append(':');
            this.comma = false;
        }

        public void WritePropertyNameOnNewLine(ReadOnlySpan<char> name)
        {
            if (this.comma)
            {
                this.builder.Append(',');
            }

            this.builder.AppendLine();
            this.builder.Append(' ', this.indentLevel * this.spacesPerIndent);

            StringValueNoComma(name);
            this.builder.Append(':');
            this.comma = false;
        }

        public StringBuilder WriteRawValueBuilder()
        {
            if (this.comma)
            {
                this.builder.Append(',');
            }

            this.builder.Append(' ');
            this.comma = true;
            return this.builder;
        }

        public StringBuilder WriteRawValueBuilderOnNewLine()
        {
            if (this.comma)
            {
                this.builder.Append(',');
            }

            this.builder.AppendLine();
            this.builder.Append(' ', this.indentLevel * this.spacesPerIndent);

            this.comma = true;
            return this.builder;
        }

        public void WriteStartObject()
        {
            if (this.comma)
            {
                this.builder.Append(',');
            }

            this.indentLevel += 1;

            this.builder.Append(' ');
            this.builder.Append('{');
            this.comma = false;
        }

        public void WriteStartObjectOnNewLine()
        {
            if (this.comma)
            {
                this.builder.Append(',');
            }

            this.builder.AppendLine();
            this.builder.Append(' ', this.indentLevel * this.spacesPerIndent);

            this.indentLevel += 1;

            this.builder.Append('{');
            this.comma = false;
        }

        public void WriteEndObject()
        {
            this.indentLevel -= 1;

            this.builder.Append(' ');
            this.builder.Append('}');
            this.comma = true;
        }

        public void WriteEndObjectOnNewLine()
        {
            this.indentLevel -= 1;

            this.builder.AppendLine();
            this.builder.Append(' ', this.indentLevel * this.spacesPerIndent);

            this.builder.Append('}');
            this.comma = true;
        }

        public void WriteStartArray()
        {
            if (this.comma)
            {
                this.builder.Append(',');
            }

            this.indentLevel += 1;

            this.builder.Append(' ');
            this.builder.Append('[');
            this.comma = false;
        }

        public void WriteEndArray()
        {
            this.indentLevel -= 1;
            this.builder.Append(' ');
            this.builder.Append(']');
            this.comma = true;
        }

        public void WriteEndArrayOnNewLine()
        {
            this.indentLevel -= 1;

            this.builder.AppendLine();
            this.builder.Append(' ', this.indentLevel * this.spacesPerIndent);

            this.builder.Append(']');
            this.comma = true;
        }

        public void WriteStringValue(ReadOnlySpan<char> value)
        {
            this.builder.Append(' ');
            StringValueNoComma(value);
            this.comma = true;
        }

        public void WriteRaw(ReadOnlySpan<char> name, ReadOnlySpan<char> value)
        {
            WritePropertyName(name);
            this.builder.Append(' ');
            RawValueNoComma(value);
            this.comma = true;
        }

        private void StringValueNoComma(ReadOnlySpan<char> value)
        {
            PerfConvert.StringAppendJson(this.builder, value);
        }

        private void RawValueNoComma(ReadOnlySpan<char> value)
        {
            this.builder.Append(value);
        }
    }
}
