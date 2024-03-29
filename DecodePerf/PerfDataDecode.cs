namespace DecodePerf
{
    using Microsoft.LinuxTracepoints.Decode;
    using System;
    using System.Diagnostics;
    using System.IO;
    using CultureInfo = System.Globalization.CultureInfo;

    internal sealed class PerfDataDecode : IDisposable
    {
        private readonly PerfDataFileReader reader = new PerfDataFileReader();
        private readonly EventHeaderEnumerator enumerator = new EventHeaderEnumerator();
        private readonly TextWriter output;

        public PerfDataDecode(TextWriter output)
        {
            this.output = output;
        }

        public TextWriter Output
        {
            get { return this.output; }
        }

        public void DecodeFile(string fileName)
        {
            this.reader.OpenFile(fileName);
            this.Decode();
        }

        public void DecodeStream(Stream stream, bool leaveOpen = false)
        {
            this.reader.OpenStream(stream, leaveOpen);
            this.Decode();
        }

        public void Dispose()
        {
            this.reader.Dispose();
        }

        private void Decode()
        {
            bool comma = false;
            bool finishedInit = false;
            var byteReader = this.reader.ByteReader;
            PerfDataFileResult result;
            PerfEvent e;
            while (true)
            {
                result = this.reader.ReadEvent(out e);
                if (result == PerfDataFileResult.EndOfFile)
                {
                    break;
                }

                this.output.WriteLine(comma ? "," : "");
                comma = true;

                if (result != PerfDataFileResult.Ok)
                {
                    this.output.Write("Pos {0}: ReadEvent {1}", this.reader.FilePos, result);
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
                        this.output.Write(
                            $@"  {{ ""NonSampleSpecial"":""{e.Header.Type}"",""Size"":{e.Data.Length} }}");
                    }
                    else if (!finishedInit)
                    {
                        this.output.Write(
                            $@"  {{ ""NonSampleEarly"":""{e.Header.Type}"",""Size"":{e.Data.Length} }}");
                    }
                    else
                    {
                        PerfNonSampleEventInfo info;
                        result = this.reader.GetNonSampleEventInfo(e.DataSpan, out info);
                        if (result != PerfDataFileResult.Ok)
                        {
                            this.output.Write("Pos {0}: GetNonSampleEventInfo {1}", this.reader.FilePos, result);
                        }
                        else
                        {
                            this.output.Write(
                                $@"  {{ ""NonSample"":""{e.Header.Type}/{info.Name}"",""Size"":{e.Data.Length},""Time"":""{info.DateTime:o}"" }}");
                        }
                    }
                }
                else
                {
                    PerfSampleEventInfo info;
                    result = this.reader.GetSampleEventInfo(e.DataSpan, out info);
                    if (result != PerfDataFileResult.Ok)
                    {
                        this.output.Write("Pos {0}: GetSampleEventInfo {1}", this.reader.FilePos, result);
                    }
                    else
                    {
                        var eventMeta = info.Metadata;
                        var eventData = info.GetRawDataSpan(e);
                        if (eventMeta == null)
                        {
                            this.output.Write(
                                $@"  {{ ""SampleNoMeta"":""{e.Header.Type}"",""Size"":{e.Data.Length} }}");
                        }
                        else if (eventMeta.DecodingStyle != PerfEventDecodingStyle.EventHeader)
                        {
                            this.output.Write(
                                $@"  {{ ""Sample"":""{e.Header.Type}/{info.Name}"",""Size"":{e.Data.Length},""Time"":""{info.DateTime:o}""");

                            for (int i = eventMeta.CommonFieldCount; i < eventMeta.Fields.Length; i++)
                            {
                                var fieldMeta = eventMeta.Fields[i];
                                var fieldData = fieldMeta.GetFieldBytes(eventData, byteReader.FromBigEndian);
                                this.output.Write($@",""{fieldMeta.Name}"":{fieldMeta.FormatField(fieldData, byteReader.FromBigEndian, true)}");
                            }

                            this.output.Write(" }");
                        }
                        else if (!this.enumerator.StartEvent(eventMeta.Name, info.GetUserData(e)))
                        {
                            this.output.Write(
                                $@"  {{ ""SampleBadEH"":""{e.Header.Type}/{info.Name}"",""Size"":{e.Data.Length},""Time"":""{info.DateTime:o}"" }}");
                        }
                        else
                        {
                            this.output.Write(
                                $@"  {{ ""SampleEH"":""{e.Header.Type}/{info.Name}"",""Size"":{e.Data.Length},""Time"":""{info.DateTime:o}""");

                            comma = true;
                            if (this.enumerator.MoveNext())
                            {
                                while (true)
                                {
                                    var item = this.enumerator.GetItemInfo();
                                    switch (this.enumerator.State)
                                    {
                                        case EventHeaderEnumeratorState.Value:
                                            this.WriteJsonItemBegin(comma, item.Name, item.FieldTag, item.ArrayFlags != 0);
                                            this.WriteJsonValue(item.FormatValue());
                                            comma = true;
                                            break;
                                        case EventHeaderEnumeratorState.StructBegin:
                                            this.WriteJsonItemBegin(comma, item.Name, item.FieldTag, item.ArrayFlags != 0);
                                            this.output.Write('{');
                                            comma = false;
                                            break;
                                        case EventHeaderEnumeratorState.StructEnd:
                                            this.output.Write(" }");
                                            comma = true;
                                            break;
                                        case EventHeaderEnumeratorState.ArrayBegin:
                                            this.WriteJsonItemBegin(comma, item.Name, item.FieldTag);
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
                                                if (!this.enumerator.MoveNextSibling()) // Instead of MoveNext().
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

                                    if (!this.enumerator.MoveNext())
                                    {
                                        goto EventDone; // End of event, or error.
                                    }
                                }
                            }

                        EventDone:

                            var ei = this.enumerator.GetEventInfo();
                            this.WriteJsonItemBegin(comma, "meta");
                            this.output.Write('{');
                            comma = false;

                            this.WriteJsonItemBegin(comma, "provider");
                            this.output.Write("\"{0}\"", ei.ProviderName.ToString());
                            comma = true;

                            this.WriteJsonItemBegin(comma, "event");
                            this.output.Write("\"{0}\"", ei.Name);

                            var options = ei.Options;
                            if (!options.IsEmpty)
                            {
                                this.WriteJsonItemBegin(comma, "options");
                                this.output.Write("\"{0}\"", options.ToString());
                            }

                            if (ei.Header.Id != 0)
                            {
                                this.WriteJsonItemBegin(comma, "id");
                                this.output.Write("{0}", ei.Header.Id);
                            }

                            if (ei.Header.Version != 0)
                            {
                                this.WriteJsonItemBegin(comma, "version");
                                this.output.Write("{0}", ei.Header.Version);
                            }

                            if (ei.Header.Level != 0)
                            {
                                this.WriteJsonItemBegin(comma, "level");
                                this.output.Write("{0}", (byte)ei.Header.Level);
                            }

                            if (ei.Keyword != 0)
                            {
                                this.WriteJsonItemBegin(comma, "keyword");
                                this.output.Write("\"0x{0:X}\"", ei.Keyword);
                            }

                            if (ei.Header.Opcode != 0)
                            {
                                this.WriteJsonItemBegin(comma, "opcode");
                                this.output.Write("{0}", (byte)ei.Header.Opcode);
                            }

                            if (ei.Header.Tag != 0)
                            {
                                this.WriteJsonItemBegin(comma, "tag");
                                this.output.Write("\"0x{0:X}\"", ei.Header.Tag);
                            }

                            Guid? g;

                            g = ei.ActivityId;
                            if (g.HasValue)
                            {
                                this.WriteJsonItemBegin(comma, "activity");
                                this.output.Write("\"{0}\"", g.Value.ToString());
                            }

                            g = ei.RelatedActivityId;
                            if (g.HasValue)
                            {
                                this.WriteJsonItemBegin(comma, "relatedActivity");
                                this.output.Write("\"{0}\"", g.Value.ToString());
                            }

                            this.output.Write("} }");
                            comma = true;
                        }
                    }
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
                    this.output.Write("\\u0000");
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
