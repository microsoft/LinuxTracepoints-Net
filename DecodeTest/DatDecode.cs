namespace DecodeTest
{
    using Microsoft.LinuxTracepoints;
    using Microsoft.LinuxTracepoints.Decode;
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
            bool comma;

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
                    this.writer.WriteStartObjectOnNewLine();
                    this.writer.WritePropertyNameOnNewLine("n");
                    e.AppendJsonEventIdentityTo(this.writer.WriteRawValueBuilder());
                    comma = false;

                    if (e.MoveNext())
                    {
                        while (true)
                        {
                            if (!e.AppendJsonItemToAndMoveNextSibling(this.writer.WriteRawValueBuilderOnNewLine(), ref comma))
                            {
                                break;
                            }
                        }
                    }

                    _ = e.GetEventInfo().ToString(); // Ensure ToString doesn't crash or assert.

                    this.writer.WritePropertyNameOnNewLine("meta");
                    this.writer.WriteStartObject();
                    comma = false;

                    e.AppendJsonEventMetaTo(this.writer.WriteRawValueBuilder(), ref comma,
                        EventHeaderMetaOptions.All & ~EventHeaderMetaOptions.Flags);

                    this.writer.WriteEndObject(); // meta
                    this.writer.WriteEndObjectOnNewLine(); // event

                    // Show the metadata as well.

                    e.Reset();

                    this.writer.WriteStartObjectOnNewLine();
                    while (e.MoveNextMetadata())
                    {
                        var item = e.GetItemInfo();
                        _ = item.ToString(); // Exercise ToString.
                        this.writer.WritePropertyNameOnNewLine(MakeName(item.NameAsString, item.Value.FieldTag));
                        this.writer.WriteStartObject();

                        this.writer.WriteString("Encoding", item.Value.Encoding.ToString());

                        if (item.Value.Format != 0)
                        {
                            if (item.Value.Encoding == EventHeaderFieldEncoding.Struct)
                            {
                                this.writer.WriteRaw("FieldCount", ((byte)item.Value.Format).ToString(CultureInfo.InvariantCulture));
                            }
                            else
                            {
                                this.writer.WriteString("Format", item.Value.Format.ToString());
                            }
                        }

                        if (item.Value.Bytes.Length != 0)
                        {
                           this.writer.WriteRaw("BadValueBytes", item.Value.Bytes.Length.ToString(CultureInfo.InvariantCulture));
                        }

                        if (item.Value.TypeSize != 0)
                        {
                            this.writer.WriteRaw("BadFixedSize", item.Value.TypeSize.ToString(CultureInfo.InvariantCulture));
                        }

                        if (item.Value.ArrayFlags != 0)
                        {
                            this.writer.WriteRaw("ElementCount", item.Value.ElementCount.ToString(CultureInfo.InvariantCulture));
                        }
                        else if (item.Value.ElementCount != 1)
                        {
                            this.writer.WriteRaw("BadElementCount", item.Value.ElementCount.ToString(CultureInfo.InvariantCulture));
                        }

                        this.writer.WriteEndObject();
                    }
                    if (e.LastError != EventHeaderEnumeratorError.Success)
                    {
                        this.writer.WriteCommentValue($"err: {e.LastError}");
                    }

                    this.writer.WriteEndObjectOnNewLine();
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
