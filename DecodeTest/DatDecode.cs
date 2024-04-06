namespace DecodeTest
{
    using Microsoft.LinuxTracepoints;
    using Microsoft.LinuxTracepoints.Decode;
    using System;
    using System.Buffers.Binary;
    using System.IO;
    using System.Text.Json;
    using CultureInfo = System.Globalization.CultureInfo;
    using Encoding = System.Text.Encoding;

    internal sealed class DatDecode
    {
        private readonly EventHeaderEnumerator e = new EventHeaderEnumerator();
        private readonly Utf8JsonWriter writer;

        public DatDecode(Utf8JsonWriter writer)
        {
            this.writer = writer;
        }

        public void DecodeFile(string fileName, bool moveNextSibling)
        {
            this.DecodeBytes(File.ReadAllBytes(fileName), moveNextSibling);
        }

        public void DecodeBytes(ReadOnlyMemory<byte> bytes, bool moveNextSibling)
        {
            var bytesSpan = bytes.Span;

            var pos = 0;
            while (pos < bytesSpan.Length)
            {
                if (pos >= bytesSpan.Length - 4)
                {
                    this.writer.WriteCommentValue($"Pos {pos}: Unexpected eof.");
                    break;
                }

                var size = BinaryPrimitives.ReadInt32LittleEndian(bytesSpan.Slice(pos));
                if (size < 4 || size > bytesSpan.Length - pos)
                {
                    this.writer.WriteCommentValue($"Pos {pos}: Bad size {size}.");
                    break;
                }

                var nameStart = pos + 4;
                pos += size;

                var nameLen = bytesSpan.Slice(nameStart, pos - nameStart).IndexOf((byte)0);
                if (nameLen < 0)
                {
                    this.writer.WriteCommentValue($"Pos {nameStart}: Unterminated event name.");
                    break;
                }

                var tracepointName = Encoding.UTF8.GetString(bytesSpan.Slice(nameStart, nameLen));
                var eventStart = nameStart + nameLen + 1;
                if (!e.StartEvent(tracepointName, bytes.Slice(eventStart, pos - eventStart)))
                {
                    this.writer.WriteCommentValue($"Pos {eventStart}: TryStartEvent error {e.LastError}.");
                }
                else
                {
                    this.writer.WriteStartObject();
                    if (e.MoveNext())
                    {
                        while (true)
                        {
                            var item = e.GetItemInfo();
                            _ = item.ToString(); // Exercise ToString.
                            switch (e.State)
                            {
                                case EventHeaderEnumeratorState.Value:
                                    if (!item.Value.IsArrayOrElement)
                                    {
                                        this.writer.WritePropertyName(MakeName(item.NameAsString, item.Value.FieldTag));
                                    }
                                    this.writer.WriteStringValue(item.Value.FormatScalar());
                                    break;
                                case EventHeaderEnumeratorState.StructBegin:
                                    if (!item.Value.IsArrayOrElement)
                                    {
                                        this.writer.WritePropertyName(MakeName(item.NameAsString, item.Value.FieldTag));
                                    }
                                    this.writer.WriteStartObject();
                                    break;
                                case EventHeaderEnumeratorState.StructEnd:
                                    this.writer.WriteEndObject();
                                    break;
                                case EventHeaderEnumeratorState.ArrayBegin:
                                    this.writer.WriteStartArray(MakeName(item.NameAsString, item.Value.FieldTag));

                                    if (moveNextSibling && item.Value.TypeSize != 0)
                                    {
                                        // Process the entire array directly without using the enumerator.
                                        // Adjust the item.ValueStart and item.ValueLength to point to each element.
                                        for (int i = 0; i != item.Value.ElementCount; i++)
                                        {
                                            this.writer.WriteStringValue(item.Value.FormatSimpleArrayElement(i));
                                        }

                                        this.writer.WriteEndArray();

                                        // Skip the entire array at once.
                                        if (!e.MoveNextSibling()) // Instead of MoveNext().
                                        {
                                            goto EventDone; // End of event, or error.
                                        }

                                        continue; // Skip the MoveNext().
                                    }
                                    break;
                                case EventHeaderEnumeratorState.ArrayEnd:
                                    this.writer.WriteEndArray();
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
                    _ = ei.ToString(); // Exercise ToString.
                    this.writer.WriteStartObject("meta");
                    this.writer.WriteString("provider", ei.ProviderName);
                    this.writer.WriteString("event", ei.NameAsString);

                    var options = ei.Options;
                    if (!options.IsEmpty)
                    {
                        this.writer.WriteString("options", options);
                    }

                    if (ei.Header.Id != 0)
                    {
                        this.writer.WriteNumber("id", ei.Header.Id);
                    }

                    if (ei.Header.Version != 0)
                    {
                        this.writer.WriteNumber("version", ei.Header.Version);
                    }

                    if (ei.Header.Level != 0)
                    {
                        this.writer.WriteNumber("level", (byte)ei.Header.Level);
                    }

                    if (ei.Keyword != 0)
                    {
                        this.writer.WriteString("keyword", $"0x{ei.Keyword:X}");
                    }

                    if (ei.Header.Opcode != 0)
                    {
                        this.writer.WriteNumber("opcode", (byte)ei.Header.Opcode);
                    }

                    if (ei.Header.Tag != 0)
                    {
                        this.writer.WriteString("tag", $"0x{ei.Header.Tag:X}");
                    }

                    Guid? g;

                    g = ei.ActivityId;
                    if (g.HasValue)
                    {
                        this.writer.WriteString("activity", g.Value);
                    }

                    g = ei.RelatedActivityId;
                    if (g.HasValue)
                    {
                        this.writer.WriteString("relatedActivity", g.Value);
                    }

                    /*
                    var options = ei.Options;
                    if (options.Length != 0)
                    {
                        WriteJsonItemBegin(comma, "options");
                        this.output.Write(options);
                    }
                    */

                    this.writer.WriteEndObject(); // meta
                    this.writer.WriteEndObject(); // event

                    // Show the metadata as well.

                    e.Reset();

                    this.writer.WriteStartObject();
                    while (e.MoveNextMetadata())
                    {
                        var item = e.GetItemInfo();
                        _ = item.ToString(); // Exercise ToString.
                        this.writer.WriteStartObject(MakeName(item.NameAsString, item.Value.FieldTag));

                        this.writer.WriteString("Encoding", item.Value.Encoding.ToString());

                        if (item.Value.Format != 0)
                        {
                            if (item.Value.Encoding == EventHeaderFieldEncoding.Struct)
                            {
                                this.writer.WriteNumber("FieldCount", (byte)item.Value.Format);
                            }
                            else
                            {
                                this.writer.WriteString("Format", item.Value.Format.ToString());
                            }
                        }

                        if (item.Value.Bytes.Length != 0)
                        {
                            this.writer.WriteNumber("BadValueBytes", item.Value.Bytes.Length);
                        }

                        if (item.Value.TypeSize != 0)
                        {
                            this.writer.WriteNumber("BadFixedSize", item.Value.TypeSize);
                        }

                        if (item.Value.ArrayFlags != 0)
                        {
                            this.writer.WriteNumber("ElementCount", item.Value.ElementCount);
                        }
                        else if (item.Value.ElementCount != 1)
                        {
                            this.writer.WriteNumber("BadElementCount", item.Value.ElementCount);
                        }

                        this.writer.WriteEndObject();
                    }
                    if (e.LastError != EventHeaderEnumeratorError.Success)
                    {
                        this.writer.WriteCommentValue($"err: {e.LastError}");
                    }

                    this.writer.WriteEndObject();
                }
            }
        }

        private static string MakeName(string baseName, int tag)
        {
            return tag == 0
                ? baseName
                : baseName + ";tag=0x" + tag.ToString("X", CultureInfo.InvariantCulture);
        }
    }
}
