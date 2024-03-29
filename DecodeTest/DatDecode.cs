namespace DecodeTest
{
    using Microsoft.LinuxTracepoints;
    using Microsoft.LinuxTracepoints.Decode;
    using System;
    using System.Buffers.Binary;
    using System.Diagnostics;
    using System.IO;
    using CultureInfo = System.Globalization.CultureInfo;
    using Encoding = System.Text.Encoding;

    internal sealed class DatDecode
    {
        private readonly EventHeaderEnumerator e = new EventHeaderEnumerator();
        private readonly TextWriter output;

        public DatDecode(TextWriter output)
        {
            this.output = output;
        }

        public TextWriter Output
        {
            get { return this.output; }
        }

        public void DecodeFile(string fileName)
        {
            this.DecodeBytes(File.ReadAllBytes(fileName));
        }

        public void DecodeBytes(ReadOnlyMemory<byte> bytes)
        {
            var bytesSpan = bytes.Span;

            bool comma = false;
            var pos = 0;
            while (pos < bytesSpan.Length)
            {
                this.output.WriteLine(comma ? "," : "");
                if (pos >= bytesSpan.Length - 4)
                {
                    this.output.Write("Pos {0}: Unexpected eof.", pos);
                    break;
                }

                var size = BinaryPrimitives.ReadInt32LittleEndian(bytesSpan.Slice(pos));
                if (size < 4 || size > bytesSpan.Length - pos)
                {
                    this.output.Write("Pos {0}: Bad size {1}.", pos, size);
                    break;
                }

                var nameStart = pos + 4;
                pos += size;

                var nameLen = bytesSpan.Slice(nameStart, pos - nameStart).IndexOf((byte)0);
                if (nameLen < 0)
                {
                    this.output.Write("Pos {0}: Unterminated event name.", nameStart);
                    break;
                }

                var tracepointName = Encoding.UTF8.GetString(bytesSpan.Slice(nameStart, nameLen));
                var eventStart = nameStart + nameLen + 1;
                if (!e.StartEvent(tracepointName, bytes.Slice(eventStart, pos - eventStart)))
                {
                    this.output.Write("Pos {0}: TryStartEvent error {1}.", eventStart, e.LastError);
                }
                else
                {
                    this.output.Write("  {");
                    comma = false;
                    if (e.MoveNext())
                    {
                        while (true)
                        {
                            var item = e.GetItemInfo();
                            switch (e.State)
                            {
                                case EventHeaderEnumeratorState.Value:
                                    WriteJsonItemBegin(comma, item.Name, item.FieldTag, item.ArrayFlags != 0);
                                    this.WriteJsonValue(item.FormatValue());
                                    comma = true;
                                    break;
                                case EventHeaderEnumeratorState.StructBegin:
                                    WriteJsonItemBegin(comma, item.Name, item.FieldTag, item.ArrayFlags != 0);
                                    this.output.Write('{');
                                    comma = false;
                                    break;
                                case EventHeaderEnumeratorState.StructEnd:
                                    this.output.Write(" }");
                                    comma = true;
                                    break;
                                case EventHeaderEnumeratorState.ArrayBegin:
                                    WriteJsonItemBegin(comma, item.Name, item.FieldTag);
                                    this.output.Write('[');
                                    comma = false;
                                    if (item.ElementSize != 0)
                                    {
                                        // Process the entire array directly without using the enumerator.
                                        // Adjust the item.ValueStart and item.ValueLength to point to each element.
                                        Debug.Assert(item.ValueLength == item.ArrayCount * item.ElementSize);
                                        item.ValueLength = item.ElementSize;
                                        for (int i = 0; i != item.ArrayCount; i++)
                                        {
                                            if (comma)
                                            {
                                                this.output.Write(',');
                                            }

                                            this.output.Write(' ');
                                            this.WriteJsonValue(item.FormatValue());
                                            item.ValueStart += item.ElementSize;
                                            comma = true;
                                        }

                                        this.output.Write(" ]");
                                        comma = true;

                                        // Skip the entire array at once.
                                        if (!e.MoveNextSibling()) // Instead of MoveNext().
                                        {
                                            goto EventDone; // End of event, or error.
                                        }

                                        continue; // Skip the MoveNext().
                                    }
                                    break;
                                case EventHeaderEnumeratorState.ArrayEnd:
                                    this.output.Write(" ]");
                                    comma = true;
                                    break;
                            }

                            if (!e.MoveNext())
                            {
                                goto EventDone; // End of event, or error.
                            }
                        }
                    }

                EventDone:

                    var ei = e.GetEventInfo();
                    WriteJsonItemBegin(comma, "meta");
                    this.output.Write('{');
                    comma = false;

                    WriteJsonItemBegin(comma, "provider");
                    this.output.Write("\"{0}\"", ei.ProviderName.ToString());
                    comma = true;

                    WriteJsonItemBegin(comma, "event");
                    this.output.Write("\"{0}\"", ei.Name);

                    var options = ei.Options;
                    if (!options.IsEmpty)
                    {
                        WriteJsonItemBegin(comma, "options");
                        this.output.Write("\"{0}\"", options.ToString());
                    }

                    if (ei.Header.Id != 0)
                    {
                        WriteJsonItemBegin(comma, "id");
                        this.output.Write("{0}", ei.Header.Id);
                    }

                    if (ei.Header.Version != 0)
                    {
                        WriteJsonItemBegin(comma, "version");
                        this.output.Write("{0}", ei.Header.Version);
                    }

                    if (ei.Header.Level != 0)
                    {
                        WriteJsonItemBegin(comma, "level");
                        this.output.Write("{0}", (byte)ei.Header.Level);
                    }

                    if (ei.Keyword != 0)
                    {
                        WriteJsonItemBegin(comma, "keyword");
                        this.output.Write("\"0x{0:X}\"", ei.Keyword);
                    }

                    if (ei.Header.Opcode != 0)
                    {
                        WriteJsonItemBegin(comma, "opcode");
                        this.output.Write("{0}", (byte)ei.Header.Opcode);
                    }

                    if (ei.Header.Tag != 0)
                    {
                        WriteJsonItemBegin(comma, "tag");
                        this.output.Write("\"0x{0:X}\"", ei.Header.Tag);
                    }

                    Guid? g;

                    g = ei.ActivityId;
                    if (g.HasValue)
                    {
                        WriteJsonItemBegin(comma, "activity");
                        this.output.Write("\"{0}\"", g.Value.ToString());
                    }

                    g = ei.RelatedActivityId;
                    if (g.HasValue)
                    {
                        WriteJsonItemBegin(comma, "relatedActivity");
                        this.output.Write("\"{0}\"", g.Value.ToString());
                    }

                    /*
                    var options = ei.Options;
                    if (options.Length != 0)
                    {
                        WriteJsonItemBegin(comma, "options");
                        this.output.Write(options);
                    }
                    */

                    // Show the metadata as well.

                    this.output.WriteLine(" } },");

                    e.Reset();

                    this.output.Write("  {");
                    comma = false;
                    while (e.MoveNextMetadata())
                    {
                        var item = e.GetItemInfo();
                        WriteJsonItemBegin(comma, item.Name, item.FieldTag);
                        this.output.Write('{');
                        comma = false;

                        WriteJsonItemBegin(comma, "Encoding");
                        this.output.Write("\"{0}\"", item.Encoding.ToString());
                        comma = true;

                        if (item.Format != 0)
                        {
                            if (item.Encoding == EventHeaderFieldEncoding.Struct)
                            {
                                WriteJsonItemBegin(comma, "FieldCount");
                                this.output.Write((byte)item.Format);
                                comma = true;
                            }
                            else
                            {
                                WriteJsonItemBegin(comma, "Format");
                                this.output.Write("\"{0}\"", item.Format.ToString());
                                comma = true;
                            }
                        }

                        if (item.ValueBytes.Length != 0)
                        {
                            WriteJsonItemBegin(comma, "BadValueBytes");
                            this.output.Write(item.ValueBytes.Length);
                            comma = true;
                        }

                        if (item.ElementSize != 0)
                        {
                            WriteJsonItemBegin(comma, "BadElementSize");
                            this.output.Write(item.ElementSize);
                            comma = true;
                        }

                        if (item.ArrayIndex != 0)
                        {
                            WriteJsonItemBegin(comma, "BadArrayIndex");
                            this.output.Write(item.ArrayIndex);
                            comma = true;
                        }

                        if (item.ArrayFlags != 0)
                        {
                            WriteJsonItemBegin(comma, "ArrayCount");
                            this.output.Write(item.ArrayCount);
                            comma = true;
                        }
                        else if (item.ArrayCount != 1)
                        {
                            this.output.Write("BadArrayCount {0} ", item.ArrayCount);
                        }

                        this.output.Write(" }");
                        comma = true;
                    }
                    if (e.LastError != EventHeaderEnumeratorError.Success)
                    {
                        WriteJsonItemBegin(comma, "err");
                        this.output.Write("\"{0}\"", e.LastError.ToString());
                    }

                    this.output.Write(" }");
                    comma = true;
                }
            }
        }

        private void WriteJsonItemBegin(bool comma, string name, int tag = 0, bool noname = false)
        {
            if (noname)
            {
                this.output.Write(comma ? ", " : " ");
            }
            else
            {
                this.output.Write(comma ? ", \"" : " \"");
                this.output.Write(name);

                if (tag != 0)
                {
                    this.output.Write(";tag=0x");
                    this.output.Write(tag.ToString("X", CultureInfo.InvariantCulture));
                }

                this.output.Write("\": ");
            }
        }

        private void WriteJsonValue(string value)
        {
            this.output.Write('"');

            foreach (var c in value)
            {
                if (c == '\0')
                {
                    this.output.Write("\\0");
                }
                else
                {
                    this.output.Write(c);
                }
            }

            this.output.Write('"');
        }
    }
}
