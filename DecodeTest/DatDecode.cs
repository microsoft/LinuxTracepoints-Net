namespace DecodeTest
{
    using Microsoft.LinuxTracepoints;
    using Microsoft.LinuxTracepoints.Decode;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using System;
    using BinaryPrimitives = System.Buffers.Binary.BinaryPrimitives;
    using CultureInfo = System.Globalization.CultureInfo;
    using Encoding = System.Text.Encoding;
    using File = System.IO.File;

    internal sealed class DatDecode
    {
        private readonly EventHeaderEnumerator e = new EventHeaderEnumerator();
        private readonly JsonStringWriter writer;

        public DatDecode(JsonStringWriter writer)
        {
            this.writer = writer;
        }

        public void DecodeFile(string fileName)
        {
            this.DecodeBytes(File.ReadAllBytes(fileName));
        }

        public void DecodeBytes(ReadOnlyMemory<byte> bytes)
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
                    this.writer.WriteCommentValue(" " + e.GetEventInfo().ToString() + " ");
                    this.writer.WriteStartObjectOnNewLine();

                    // Metadata enumeration

                    this.writer.WritePropertyNameOnNewLine("MoveNextMetadata");
                    this.writer.WriteStartObject(); // MoveNextMetadata
                    while (e.MoveNextMetadata())
                    {
                        var item = e.GetItemInfo();
                        _ = item.ToString(); // Exercise ToString.
                        var itemType = item.Value.Type;
                        this.writer.WritePropertyNameOnNewLine(MakeName(item.GetNameAsString(), itemType.FieldTag));
                        this.writer.WriteStartObject();

                        this.writer.WritePropertyName("Encoding");
                        this.writer.WriteStringValue(itemType.Encoding.ToString());

                        if (itemType.Format != 0)
                        {
                            if (itemType.Encoding == EventHeaderFieldEncoding.Struct)
                            {
                                this.writer.WriteRaw("FieldCount", ((byte)itemType.Format).ToString(CultureInfo.InvariantCulture));
                            }
                            else
                            {
                                this.writer.WritePropertyName("Format");
                                this.writer.WriteStringValue(itemType.Format.ToString());
                            }
                        }

                        if (item.Value.Bytes.Length != 0)
                        {
                            this.writer.WriteRaw("BadValueBytes", item.Value.Bytes.Length.ToString(CultureInfo.InvariantCulture));
                        }

                        if (itemType.TypeSize != 0)
                        {
                            this.writer.WriteRaw("BadFixedSize", itemType.TypeSize.ToString(CultureInfo.InvariantCulture));
                        }

                        if (itemType.ArrayFlags != 0)
                        {
                            this.writer.WriteRaw("ElementCount", itemType.ElementCount.ToString(CultureInfo.InvariantCulture));
                        }
                        else if (itemType.ElementCount != 1)
                        {
                            this.writer.WriteRaw("BadElementCount", itemType.ElementCount.ToString(CultureInfo.InvariantCulture));
                        }

                        this.writer.WriteEndObject();
                    }
                    if (e.LastError != EventHeaderEnumeratorError.Success)
                    {
                        this.writer.WriteCommentValue($"err: {e.LastError}");
                    }

                    this.writer.WriteEndObjectOnNewLine(); // MoveNextMetadata

                    // AppendJsonItemToAndMoveNextSibling on values:

                    e.Reset();
                    this.writer.WritePropertyNameOnNewLine("AppendJsonItemN");
                    this.writer.WriteStartObject(); // AppendJsonItemN

                    this.writer.WritePropertyNameOnNewLine("n");
                    e.AppendJsonEventIdentityTo(this.writer.WriteRawValueBuilder());

                    e.MoveNext(); // Move past BeforeFirstItem.
                    while (e.State >= EventHeaderEnumeratorState.BeforeFirstItem)
                    {
                        var sb = this.writer.WriteRawValueBuilderOnNewLine();
                        e.AppendJsonItemToAndMoveNextSibling(sb, false, PerfConvertOptions.All);
                    }

                    if (e.State != EventHeaderEnumeratorState.AfterLastItem)
                    {
                        this.writer.WriteCommentValue($"Pos {pos}: Unexpected state {e.State}.");
                    }

                    this.writer.WritePropertyNameOnNewLine("info");
                    this.writer.WriteStartObject();

                    e.AppendJsonEventInfoTo(this.writer.WriteRawValueBuilder(), false, PerfInfoOptions.All, PerfConvertOptions.All);

                    this.writer.WriteEndObject(); // info
                    this.writer.WriteEndObjectOnNewLine(); // AppendJsonItemN

                    // AppendJsonItemToAndMoveNextSibling on BeforeFirstItem:

                    e.Reset();
                    this.writer.WritePropertyNameOnNewLine("AppendJsonItem1");
                    this.writer.WriteStartObject(); // AppendJsonItem1

                    while (e.State >= EventHeaderEnumeratorState.BeforeFirstItem)
                    {
                        var sb = this.writer.WriteRawValueBuilderOnNewLine();
                        if (!e.AppendJsonItemToAndMoveNextSibling(sb, false, PerfConvertOptions.None))
                        {
                            sb.Append("\"\": null"); // No fields.
                        }
                    }

                    if (e.State != EventHeaderEnumeratorState.AfterLastItem)
                    {
                        this.writer.WriteCommentValue($"Pos {pos}: Unexpected state {e.State}.");
                    }

                    this.writer.WritePropertyNameOnNewLine("info");
                    this.writer.WriteStartObject();

                    e.AppendJsonEventInfoTo(this.writer.WriteRawValueBuilder(), false,
                        PerfInfoOptions.Default & ~PerfInfoOptions.Level,
                        PerfConvertOptions.None);

                    this.writer.WriteEndObject(); // info
                    this.writer.WriteEndObjectOnNewLine(); // AppendJsonItem1

                    // MoveNext

                    int begin;

                    e.Reset();
                    this.writer.WritePropertyNameOnNewLine("MoveNext");
                    this.writer.WriteStartObject();
                    begin = this.writer.Builder.Length;
                    this.Enumerate(false);
                    var moveNextText = this.writer.Builder.ToString(begin, this.writer.Builder.Length - begin);
                    this.writer.WriteEndObjectOnNewLine();

                    // MoveNextSibling

                    e.Reset();
                    this.writer.WritePropertyNameOnNewLine("MoveNextSibling");
                    this.writer.WriteStartObject();
                    begin = this.writer.Builder.Length;
                    this.Enumerate(true);
                    var moveNextSiblingText = this.writer.Builder.ToString(begin, this.writer.Builder.Length - begin);
                    this.writer.WriteEndObjectOnNewLine();

                    Assert.AreEqual(moveNextText, moveNextSiblingText, "MoveNext != MoveNextSibling");

                    // ToString

                    e.Reset();
                    this.writer.WritePropertyNameOnNewLine("ToString");
                    this.writer.WriteStartObject();
                    this.EnumerateToString();
                    this.writer.WriteEndObjectOnNewLine();

                    // End event

                    this.writer.WriteEndObjectOnNewLine(); // event
                }
            }
        }

        private void Enumerate(bool moveNextSibling)
        {
            if (e.MoveNext())
            {
                while (true)
                {
                    var item = e.GetItemInfo();
                    var itemType = item.Value.Type;
                    switch (e.State)
                    {
                        case EventHeaderEnumeratorState.Value:
                            if (!itemType.IsArrayOrElement)
                            {
                                this.writer.WritePropertyNameOnNewLine(MakeName(item.GetNameAsString(), itemType.FieldTag));
                            }
                            item.Value.AppendJsonScalarTo(this.writer.WriteRawValueBuilder());
                            break;
                        case EventHeaderEnumeratorState.StructBegin:
                            if (!itemType.IsArrayOrElement)
                            {
                                this.writer.WritePropertyNameOnNewLine(MakeName(item.GetNameAsString(), itemType.FieldTag));
                            }
                            this.writer.WriteStartObject();
                            break;
                        case EventHeaderEnumeratorState.StructEnd:
                            this.writer.WriteEndObject();
                            break;
                        case EventHeaderEnumeratorState.ArrayBegin:
                            this.writer.WritePropertyNameOnNewLine(MakeName(item.GetNameAsString(), itemType.FieldTag));

                            if (moveNextSibling && itemType.TypeSize != 0)
                            {
                                item.Value.AppendJsonSimpleArrayTo(this.writer.WriteRawValueBuilder());

                                // Skip the entire array at once.
                                if (!e.MoveNextSibling()) // Instead of MoveNext().
                                {
                                    return; // End of event, or error.
                                }

                                continue; // Skip the MoveNext().
                            }

                            this.writer.WriteStartArray();
                            break;
                        case EventHeaderEnumeratorState.ArrayEnd:
                            this.writer.WriteEndArray();
                            break;
                    }

                    if (!e.MoveNext())
                    {
                        return; // End of event, or error.
                    }
                }
            }
        }

        private void EnumerateToString()
        {
            if (e.MoveNext())
            {
                while (true)
                {
                    var item = e.GetItemInfo();
                    var itemType = item.Value.Type;
                    switch (e.State)
                    {
                        case EventHeaderEnumeratorState.Value:
                            if (!itemType.IsArrayOrElement)
                            {
                                this.writer.WritePropertyNameOnNewLine(MakeName(item.GetNameAsString(), itemType.FieldTag));
                            }
                            this.writer.WriteStringValue(item.Value.ToString());
                            break;
                        case EventHeaderEnumeratorState.StructBegin:
                            if (!itemType.IsArrayOrElement)
                            {
                                this.writer.WritePropertyNameOnNewLine(MakeName(item.GetNameAsString(), itemType.FieldTag));
                            }
                            this.writer.WriteStartObject();
                            break;
                        case EventHeaderEnumeratorState.StructEnd:
                            this.writer.WriteEndObject();
                            break;
                        case EventHeaderEnumeratorState.ArrayBegin:
                            this.writer.WritePropertyNameOnNewLine(MakeName(item.GetNameAsString(), itemType.FieldTag));

                            if (itemType.TypeSize != 0)
                            {
                                this.writer.WriteStringValue(item.Value.ToString());

                                // Skip the entire array at once.
                                if (!e.MoveNextSibling()) // Instead of MoveNext().
                                {
                                    return; // End of event, or error.
                                }

                                continue; // Skip the MoveNext().
                            }

                            this.writer.WriteStartArray();
                            break;
                        case EventHeaderEnumeratorState.ArrayEnd:
                            this.writer.WriteEndArray();
                            break;
                    }

                    if (!e.MoveNext())
                    {
                        return; // End of event, or error.
                    }
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
