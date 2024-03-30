namespace DecodePerf
{
    using Microsoft.LinuxTracepoints;
    using Microsoft.LinuxTracepoints.Decode;
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Text.Json;
    using CultureInfo = System.Globalization.CultureInfo;

    internal sealed class PerfDataDecode : IDisposable
    {
        private readonly PerfDataFileReader reader = new PerfDataFileReader();
        private readonly EventHeaderEnumerator enumerator = new EventHeaderEnumerator();
        private readonly Utf8JsonWriter writer;

        public PerfDataDecode(Utf8JsonWriter writer)
        {
            this.writer = writer;
        }

        public void DecodeFile(string fileName)
        {
            reader.OpenFile(fileName);
            this.Decode();
        }

        public void DecodeStream(Stream stream, bool leaveOpen = false)
        {
            reader.OpenStream(stream, leaveOpen);
            this.Decode();
        }

        public void Dispose()
        {
            reader.Dispose();
            writer.Dispose();
        }

        private void Decode()
        {
            bool finishedInit = false;
            var byteReader = reader.ByteReader;
            PerfDataFileResult result;
            PerfEvent e;
            while (true)
            {
                result = reader.ReadEvent(out e);
                if (result == PerfDataFileResult.EndOfFile)
                {
                    break;
                }

                if (result != PerfDataFileResult.Ok)
                {
                    writer.WriteCommentValue($"Pos {reader.FilePos}: ReadEvent {result}");
                    break;
                }

                if (e.Header.Type != PerfEventHeaderType.Sample)
                {
                    if (e.Header.Type == PerfEventHeaderType.FinishedInit)
                    {
                        finishedInit = true;
                    }

                    if (e.Header.Type >= PerfEventHeaderType.UserTypeStart)
                    {
                        writer.WriteStartObject();
                        writer.WriteString("NonSampleSpecial", e.Header.Type.ToString());
                        writer.WriteNumber("Size", e.Bytes.Length);
                        writer.WriteEndObject();
                    }
                    else if (!finishedInit)
                    {
                        writer.WriteStartObject();
                        writer.WriteString("NonSampleEarly", e.Header.Type.ToString());
                        writer.WriteNumber("Size", e.Bytes.Length);
                        writer.WriteEndObject();
                    }
                    else
                    {
                        PerfNonSampleEventInfo info;
                        result = reader.GetNonSampleEventInfo(e, out info);
                        if (result != PerfDataFileResult.Ok)
                        {
                            writer.WriteCommentValue($"Pos {reader.FilePos}: GetNonSampleEventInfo {result}");
                        }
                        else
                        {
                            writer.WriteStartObject();
                            writer.WriteString("NonSample", $"{e.Header.Type}/{info.Name}");
                            writer.WriteNumber("Size", e.Bytes.Length);
                            writer.WriteString("Time", info.DateTime);
                            writer.WriteEndObject();
                        }
                    }
                }
                else
                {
                    PerfSampleEventInfo info;
                    result = reader.GetSampleEventInfo(e, out info);
                    if (result != PerfDataFileResult.Ok)
                    {
                        writer.WriteCommentValue($"Pos {reader.FilePos}: GetSampleEventInfo {result}");
                    }
                    else
                    {
                        var eventMeta = info.Metadata;
                        var eventData = info.RawDataSpan;
                        if (eventMeta == null)
                        {
                            writer.WriteStartObject();
                            writer.WriteString("SampleNoMeta", e.Header.Type.ToString());
                            writer.WriteNumber("Size", e.Bytes.Length);
                            writer.WriteEndObject();
                        }
                        else if (eventMeta.DecodingStyle != PerfEventDecodingStyle.EventHeader)
                        {
                            writer.WriteStartObject();
                            writer.WriteString("Sample", $"{e.Header.Type}/{info.Name}");
                            writer.WriteNumber("Size", e.Bytes.Length);
                            writer.WriteString("Time", info.DateTime);

                            for (int i = eventMeta.CommonFieldCount; i < eventMeta.Fields.Length; i++)
                            {
                                var fieldMeta = eventMeta.Fields[i];
                                var fieldData = fieldMeta.GetFieldBytes(eventData, byteReader.FromBigEndian);
                                writer.WriteString(fieldMeta.Name, fieldMeta.FormatField(fieldData, byteReader.FromBigEndian));
                            }

                            writer.WriteEndObject();
                        }
                        else if (!this.enumerator.StartEvent(eventMeta.Name, info.UserData))
                        {
                            writer.WriteStartObject();
                            writer.WriteString("SampleBadEH", $"{e.Header.Type}/{info.Name}");
                            writer.WriteNumber("Size", e.Bytes.Length);
                            writer.WriteString("Time", info.DateTime);
                            writer.WriteEndObject();
                        }
                        else
                        {
                            writer.WriteStartObject();
                            writer.WriteString("SampleEH", $"{e.Header.Type}/{info.Name}");
                            writer.WriteNumber("Size", e.Bytes.Length);
                            writer.WriteString("Time", info.DateTime);

                            if (this.enumerator.MoveNext())
                            {
                                while (true)
                                {
                                    var item = this.enumerator.GetItemInfo();
                                    switch (this.enumerator.State)
                                    {
                                        case EventHeaderEnumeratorState.Value:
                                            if (item.ArrayFlags == 0)
                                            {
                                                writer.WriteString(MakeName(item.NameAsString, item.FieldTag), item.FormatValue());
                                            }
                                            else
                                            {
                                                writer.WriteStringValue(item.FormatValue());
                                            }
                                            break;
                                        case EventHeaderEnumeratorState.StructBegin:
                                            if (item.ArrayFlags == 0)
                                            {
                                                writer.WriteStartObject(MakeName(item.NameAsString, item.FieldTag));
                                            }
                                            else
                                            {
                                                writer.WriteStartObject();
                                            }
                                            break;
                                        case EventHeaderEnumeratorState.StructEnd:
                                            writer.WriteEndObject();
                                            break;
                                        case EventHeaderEnumeratorState.ArrayBegin:
                                            writer.WriteStartArray(MakeName(item.NameAsString, item.FieldTag));
                                            if (item.ElementSize != 0)
                                            {
                                                // Process the entire array directly without using the enumerator.
                                                // Adjust the item.ValueStart and item.ValueLength to point to each element.
                                                Debug.Assert(item.ValueLength == item.ArrayCount * item.ElementSize);
                                                item.ValueLength = item.ElementSize;
                                                for (int i = 0; i != item.ArrayCount; i++)
                                                {
                                                    writer.WriteStringValue(item.FormatValue());
                                                    item.ValueStart += item.ElementSize;
                                                }

                                                writer.WriteEndArray();

                                                // Skip the entire array at once.
                                                if (!this.enumerator.MoveNextSibling()) // Instead of MoveNext().
                                                {
                                                    goto EventDone; // End of event, or error.
                                                }

                                                continue; // Skip the MoveNext().
                                            }
                                            break;
                                        case EventHeaderEnumeratorState.ArrayEnd:
                                            writer.WriteEndArray();
                                            break;
                                    }

                                    if (!this.enumerator.MoveNext())
                                    {
                                        goto EventDone; // End of event, or error.
                                    }
                                }
                            }

                        EventDone:

                            var ei = this.enumerator.GetEventInfo();
                            this.writer.WriteStartObject("meta");
                            this.writer.WriteString("provider", ei.ProviderName);
                            this.writer.WriteString("event", ei.Name);

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
                        }
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
