namespace DecodePerf
{
    using Microsoft.LinuxTracepoints;
    using Microsoft.LinuxTracepoints.Decode;
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Net;
    using System.Runtime.InteropServices;
    using System.Text.Json;
    using CultureInfo = System.Globalization.CultureInfo;
    using Encoding = System.Text.Encoding;

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
                                var fieldValue = fieldMeta.GetFieldValue(info.RawDataSpan, byteReader.FromBigEndian);
                                if (!fieldValue.IsArray)
                                {
                                    writer.WritePropertyName(fieldMeta.Name);
                                    this.WriteValue(fieldValue);
                                }
                                else
                                {
                                    writer.WriteStartArray(fieldMeta.Name);
                                    WriteSimpleArrayValues(fieldValue);
                                    writer.WriteEndArray();
                                }
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
                                            if (!item.Value.IsArray)
                                            {
                                                writer.WritePropertyName(MakeName(item.NameAsString, item.Value.FieldTag));
                                            }
                                            this.WriteValue(item.Value);
                                            break;
                                        case EventHeaderEnumeratorState.StructBegin:
                                            if (!item.Value.IsArray)
                                            {
                                                writer.WritePropertyName(MakeName(item.NameAsString, item.Value.FieldTag));
                                            }
                                            writer.WriteStartObject();
                                            break;
                                        case EventHeaderEnumeratorState.StructEnd:
                                            writer.WriteEndObject();
                                            break;
                                        case EventHeaderEnumeratorState.ArrayBegin:
                                            writer.WritePropertyName(MakeName(item.NameAsString, item.Value.FieldTag));
                                            writer.WriteStartArray();
                                            if (item.Value.ElementSize != 0)
                                            {
                                                // Process the entire array directly without using the enumerator.
                                                WriteSimpleArrayValues(item.Value);
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

        private void WriteValue(in PerfValue item)
        {
            switch (item.Encoding)
            {
                default:
                    throw new NotSupportedException("Unknown encoding.");
                case EventFieldEncoding.Invalid:
                    writer.WriteNullValue();
                    return;
                case EventFieldEncoding.Struct:
                    throw new InvalidOperationException("Invalid encoding for FormatValue.");
                case EventFieldEncoding.Value8:
                    switch (item.Format)
                    {
                        default:
                        case EventFieldFormat.UnsignedInt:
                            writer.WriteNumberValue(item.GetU8());
                            return;
                        case EventFieldFormat.SignedInt:
                            writer.WriteNumberValue(item.GetI8());
                            return;
                        case EventFieldFormat.HexInt:
                            this.WriteHexInt(item.GetU8(), stackalloc char[4]);
                            return;
                        case EventFieldFormat.Boolean:
                            this.WriteBooleanValue(item.GetU8());
                            return;
                        case EventFieldFormat.HexBytes:
                            this.WriteHexBytes(item.GetSpan1(), stackalloc char[2]);
                            return;
                        case EventFieldFormat.String8:
                            this.WriteChar16Value((char)item.GetU8());
                            return;
                    }
                case EventFieldEncoding.Value16:
                    switch (item.Format)
                    {
                        default:
                        case EventFieldFormat.UnsignedInt:
                            writer.WriteNumberValue(item.GetU16());
                            return;
                        case EventFieldFormat.SignedInt:
                            writer.WriteNumberValue(item.GetI16());
                            return;
                        case EventFieldFormat.HexInt:
                            this.WriteHexInt(item.GetU16(), stackalloc char[6]);
                            return;
                        case EventFieldFormat.Boolean:
                            this.WriteBooleanValue(item.GetU16());
                            return;
                        case EventFieldFormat.HexBytes:
                            this.WriteHexBytes(item.GetSpan2(), stackalloc char[5]);
                            return;
                        case EventFieldFormat.StringUtf:
                            this.WriteChar16Value((char)item.GetU16());
                            return;
                        case EventFieldFormat.Port:
                            writer.WriteNumberValue(item.GetPort());
                            return;
                    }
                case EventFieldEncoding.Value32:
                    switch (item.Format)
                    {
                        default:
                        case EventFieldFormat.UnsignedInt:
                            writer.WriteNumberValue(item.GetU32());
                            return;
                        case EventFieldFormat.SignedInt:
                        case EventFieldFormat.Pid:
                            writer.WriteNumberValue(item.GetI32());
                            return;
                        case EventFieldFormat.HexInt:
                            this.WriteHexInt(item.GetU32(), stackalloc char[10]);
                            return;
                        case EventFieldFormat.Errno:
                            this.WriteErrno(item.GetI32());
                            return;
                        case EventFieldFormat.Time:
                            this.WriteUnixTime32Value(item.GetI32());
                            return;
                        case EventFieldFormat.Boolean:
                            this.WriteBooleanValue(item.GetU32());
                            return;
                        case EventFieldFormat.Float:
                            this.WriteFloat32Value(item.GetF32());
                            return;
                        case EventFieldFormat.HexBytes:
                            this.WriteHexBytes(item.GetSpan4(), stackalloc char[11]);
                            return;
                        case EventFieldFormat.StringUtf:
                            this.WriteChar32Value(item.GetU32());
                            return;
                        case EventFieldFormat.IPv4:
                            this.WriteIPAddressValue(item.GetIPv4(), stackalloc char[16]);
                            return;
                    }
                case EventFieldEncoding.Value64:
                    switch (item.Format)
                    {
                        default:
                        case EventFieldFormat.UnsignedInt:
                            writer.WriteNumberValue(item.GetU64());
                            return;
                        case EventFieldFormat.SignedInt:
                            writer.WriteNumberValue(item.GetI64());
                            return;
                        case EventFieldFormat.HexInt:
                            this.WriteHexInt(item.GetU64(), stackalloc char[18]);
                            return;
                        case EventFieldFormat.Time:
                            this.WriteUnixTime64Value(item.GetI64());
                            return;
                        case EventFieldFormat.Float:
                            this.WriteFloat64Value(item.GetF64());
                            return;
                        case EventFieldFormat.HexBytes:
                            this.WriteHexBytes(item.GetSpan8(), stackalloc char[23]);
                            return;
                    }
                case EventFieldEncoding.Value128:
                    switch (item.Format)
                    {
                        default:
                        case EventFieldFormat.HexBytes:
                            this.WriteHexBytes(item.GetSpan16(), stackalloc char[47]);
                            return;
                        case EventFieldFormat.Uuid:
                            writer.WriteStringValue(item.GetGuid());
                            return;
                        case EventFieldFormat.IPv6:
                            this.WriteIPAddressValue(item.GetIPv6(), stackalloc char[46]);
                            return;
                    }
                case EventFieldEncoding.ZStringChar8:
                case EventFieldEncoding.StringLength16Char8:
                    switch (item.Format)
                    {
                        case EventFieldFormat.HexBytes:
                            writer.WriteStringValue(PerfConvert.ToHexString(item.Bytes)); // Garbage.
                            return;
                        case EventFieldFormat.String8:
                            writer.WriteStringValue(PerfConvert.EncodingLatin1.GetString(item.Bytes)); // Garbage.
                            return;
                        case EventFieldFormat.StringUtfBom:
                        case EventFieldFormat.StringXml:
                        case EventFieldFormat.StringJson:
                            this.WriteUtfBomValue(item);
                            return;
                        default:
                        case EventFieldFormat.StringUtf:
                            writer.WriteStringValue(item.Bytes); // Assume UTF-8
                            return;
                    }
                case EventFieldEncoding.ZStringChar16:
                case EventFieldEncoding.StringLength16Char16:
                    switch (item.Format)
                    {
                        case EventFieldFormat.HexBytes:
                            writer.WriteStringValue(PerfConvert.ToHexString(item.Bytes)); // Garbage.
                            return;
                        case EventFieldFormat.StringUtfBom:
                        case EventFieldFormat.StringXml:
                        case EventFieldFormat.StringJson:
                            this.WriteUtfBomValue(item); // Garbage
                            return;
                        default:
                        case EventFieldFormat.StringUtf:
                            if (item.ByteReader.ByteSwapNeeded)
                            {
                                this.WriteUtfBomValue(item); // Garbage
                            }
                            else
                            {
                                writer.WriteStringValue(MemoryMarshal.Cast<byte, char>(item.Bytes));
                            }
                            return;
                    }
                case EventFieldEncoding.ZStringChar32:
                case EventFieldEncoding.StringLength16Char32:
                    switch (item.Format)
                    {
                        case EventFieldFormat.HexBytes:
                            writer.WriteStringValue(PerfConvert.ToHexString(item.Bytes)); // Garbage.
                            return;
                        case EventFieldFormat.StringUtfBom:
                        case EventFieldFormat.StringXml:
                        case EventFieldFormat.StringJson:
                            this.WriteUtfBomValue(item); // Garbage
                            return;
                        default:
                        case EventFieldFormat.StringUtf:
                            this.WriteUtfBomValue(item); // Garbage
                            return;
                    }
            }
        }

        /// <summary>
        /// Interprets the item as the BeginArray of a simple array (ElementSize != 0).
        /// Calls writer.WriteValue(...) for each element in the array.
        /// </summary>
        private void WriteSimpleArrayValues(in PerfValue item)
        {
            var arrayCount = item.ArrayCount;

            // Room for 16 byte value converted to HexBytes.
            // Room for IPv6 address converted to string.
            Span<char> charBuf = stackalloc char[16 * 3];

            switch (item.Encoding)
            {
                default:
                    throw new NotSupportedException("Unknown encoding.");
                case EventFieldEncoding.Invalid:
                case EventFieldEncoding.Struct:
                case EventFieldEncoding.ZStringChar8:
                case EventFieldEncoding.StringLength16Char8:
                case EventFieldEncoding.ZStringChar16:
                case EventFieldEncoding.StringLength16Char16:
                case EventFieldEncoding.ZStringChar32:
                case EventFieldEncoding.StringLength16Char32:
                    throw new InvalidOperationException("Invalid encoding for WriteSimpleArray.");
                case EventFieldEncoding.Value8:
                    switch (item.Format)
                    {
                        default:
                        case EventFieldFormat.UnsignedInt:
                            for (int i = 0; i < arrayCount; i += 1)
                            {
                                writer.WriteNumberValue(item.GetU8(i));
                            }
                            return;
                        case EventFieldFormat.SignedInt:
                            for (int i = 0; i < arrayCount; i += 1)
                            {
                                writer.WriteNumberValue(item.GetI8(i));
                            }
                            return;
                        case EventFieldFormat.HexInt:
                            for (int i = 0; i < arrayCount; i += 1)
                            {
                                this.WriteHexInt(item.GetU8(i), charBuf);
                            }
                            return;
                        case EventFieldFormat.Boolean:
                            for (int i = 0; i < arrayCount; i += 1)
                            {
                                this.WriteBooleanValue(item.GetU8(i));
                            }
                            return;
                        case EventFieldFormat.HexBytes:
                            for (int i = 0; i < arrayCount; i += 1)
                            {
                                this.WriteHexBytes(item.GetSpan1(i), charBuf);
                            }
                            return;
                        case EventFieldFormat.String8:
                            for (int i = 0; i < arrayCount; i += 1)
                            {
                                this.WriteChar16Value((char)item.GetU8(i));
                            }
                            return;
                    }
                case EventFieldEncoding.Value16:
                    switch (item.Format)
                    {
                        default:
                        case EventFieldFormat.UnsignedInt:
                            for (int i = 0; i < arrayCount; i += 1)
                            {
                                writer.WriteNumberValue(item.GetU16(i));
                            }
                            return;
                        case EventFieldFormat.SignedInt:
                            for (int i = 0; i < arrayCount; i += 1)
                            {
                                writer.WriteNumberValue(item.GetI16(i));
                            }
                            return;
                        case EventFieldFormat.HexInt:
                            for (int i = 0; i < arrayCount; i += 1)
                            {
                                this.WriteHexInt(item.GetU16(i), charBuf);
                            }
                            return;
                        case EventFieldFormat.Boolean:
                            for (int i = 0; i < arrayCount; i += 1)
                            {
                                this.WriteBooleanValue(item.GetU16(i));
                            }
                            return;
                        case EventFieldFormat.HexBytes:
                            for (int i = 0; i < arrayCount; i += 1)
                            {
                                this.WriteHexBytes(item.GetSpan2(i), charBuf);
                            }
                            return;
                        case EventFieldFormat.StringUtf:
                            for (int i = 0; i < arrayCount; i += 1)
                            {
                                this.WriteChar16Value((char)item.GetU16(i));
                            }
                            return;
                        case EventFieldFormat.Port:
                            for (int i = 0; i < arrayCount; i += 1)
                            {
                                writer.WriteNumberValue(item.GetPort(i));
                            }
                            return;
                    }
                case EventFieldEncoding.Value32:
                    switch (item.Format)
                    {
                        default:
                        case EventFieldFormat.UnsignedInt:
                            for (int i = 0; i < arrayCount; i += 1)
                            {
                                writer.WriteNumberValue(item.GetU32(i));
                            }
                            return;
                        case EventFieldFormat.SignedInt:
                        case EventFieldFormat.Pid:
                            for (int i = 0; i < arrayCount; i += 1)
                            {
                                writer.WriteNumberValue(item.GetI32(i));
                            }
                            return;
                        case EventFieldFormat.HexInt:
                            for (int i = 0; i < arrayCount; i += 1)
                            {
                                this.WriteHexInt(item.GetU32(i), charBuf);
                            }
                            return;
                        case EventFieldFormat.Errno:
                            for (int i = 0; i < arrayCount; i += 1)
                            {
                                this.WriteErrno(item.GetI32(i));
                            }
                            return;
                        case EventFieldFormat.Time:
                            for (int i = 0; i < arrayCount; i += 1)
                            {
                                this.WriteUnixTime32Value(item.GetI32(i));
                            }
                            return;
                        case EventFieldFormat.Boolean:
                            for (int i = 0; i < arrayCount; i += 1)
                            {
                                this.WriteBooleanValue(item.GetU32(i));
                            }
                            return;
                        case EventFieldFormat.Float:
                            for (int i = 0; i < arrayCount; i += 1)
                            {
                                this.WriteFloat32Value(item.GetF32(i));
                            }
                            return;
                        case EventFieldFormat.HexBytes:
                            for (int i = 0; i < arrayCount; i += 1)
                            {
                                this.WriteHexBytes(item.GetSpan4(i), charBuf);
                            }
                            return;
                        case EventFieldFormat.StringUtf:
                            for (int i = 0; i < arrayCount; i += 1)
                            {
                                this.WriteChar32Value(item.GetU32(i));
                            }
                            return;
                        case EventFieldFormat.IPv4:
                            for (int i = 0; i < arrayCount; i += 1)
                            {
                                this.WriteIPAddressValue(item.GetIPv4(i), charBuf);
                            }
                            return;
                    }
                case EventFieldEncoding.Value64:
                    switch (item.Format)
                    {
                        default:
                        case EventFieldFormat.UnsignedInt:
                            for (int i = 0; i < arrayCount; i += 1)
                            {
                                writer.WriteNumberValue(item.GetU64(i));
                            }
                            return;
                        case EventFieldFormat.SignedInt:
                            for (int i = 0; i < arrayCount; i += 1)
                            {
                                writer.WriteNumberValue(item.GetI64(i));
                            }
                            return;
                        case EventFieldFormat.HexInt:
                            for (int i = 0; i < arrayCount; i += 1)
                            {
                                WriteHexInt(item.GetU64(i), charBuf);
                            }
                            return;
                        case EventFieldFormat.Time:
                            for (int i = 0; i < arrayCount; i += 1)
                            {
                                this.WriteUnixTime64Value(item.GetI64(i));
                            }
                            return;
                        case EventFieldFormat.Float:
                            for (int i = 0; i < arrayCount; i += 1)
                            {
                                this.WriteFloat64Value(item.GetF64(i));
                            }
                            return;
                        case EventFieldFormat.HexBytes:
                            for (int i = 0; i < arrayCount; i += 1)
                            {
                                WriteHexBytes(item.GetSpan8(i), charBuf);
                            }
                            return;
                    }
                case EventFieldEncoding.Value128:
                    switch (item.Format)
                    {
                        default:
                        case EventFieldFormat.HexBytes:
                            for (int i = 0; i < arrayCount; i += 1)
                            {
                                WriteHexBytes(item.GetSpan16(i), charBuf);
                            }
                            return;
                        case EventFieldFormat.Uuid:
                            for (int i = 0; i < arrayCount; i += 1)
                            {
                                writer.WriteStringValue(item.GetGuid(i));
                            }
                            return;
                        case EventFieldFormat.IPv6:
                            for (int i = 0; i < arrayCount; i += 1)
                            {
                                this.WriteIPAddressValue(item.GetIPv6(i), charBuf);
                            }
                            return;
                    }
            }
        }

        private void WriteUtfBomValue(in PerfValue item)
        {
            var bytes = item.GetStringBytes(out var encoding);
            if (encoding == Encoding.UTF8)
            {
                writer.WriteStringValue(bytes);
            }
            else if (encoding == Encoding.Unicode)
            {
                writer.WriteStringValue(MemoryMarshal.Cast<byte, char>(bytes));
            }
            else
            {
                writer.WriteStringValue(encoding.GetString(bytes)); // Garbage.
            }
        }

        private void WriteFloat32Value(float value)
        {
            if (float.IsFinite(value))
            {
                writer.WriteNumberValue(value);
            }
            else
            {
                writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture)); // Garbage
            }
        }

        private void WriteFloat64Value(double value)
        {
            if (double.IsFinite(value))
            {
                writer.WriteNumberValue(value);
            }
            else
            {
                writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture)); // Garbage
            }
        }

        private void WriteChar16Value(char ch)
        {
            writer.WriteStringValue(MemoryMarshal.CreateReadOnlySpan(ref ch, 1));
        }

        private void WriteChar32Value(uint ch32)
        {
            Span<char> chars = stackalloc char[2];
            var bytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref ch32, 1));
            var count = Encoding.UTF32.GetChars(bytes, chars);
            writer.WriteStringValue(chars.Slice(0, count));
        }

        private void WriteIPAddressValue(ReadOnlySpan<byte> value, Span<char> charBuf)
        {
            var addr = new IPAddress(value); // Garbage.
            int count;
            if (!addr.TryFormat(charBuf, out count))
            {
                Debug.Fail("TryFormat failed for IPAddress.");
                writer.WriteStringValue(addr.ToString()); // Garbage.
            }
            else
            {
                writer.WriteStringValue(charBuf.Slice(0, count));
            }
        }

        private void WriteUnixTime32Value(Int32 value)
        {
            writer.WriteStringValue(PerfConvert.UnixTime32ToDateTime(value));
        }

        private void WriteUnixTime64Value(Int64 value)
        {
            var dateTime = PerfConvert.UnixTime64ToDateTime(value);
            if (dateTime.HasValue)
            {
                writer.WriteStringValue(dateTime.Value);
            }
            else
            {
                writer.WriteNumberValue(value);
            }
        }

        private void WriteErrno(int errno)
        {
            var errnoString = PerfConvert.ErrnoLookup(errno);
            if (errnoString == null)
            {
                writer.WriteNumberValue(errno);
            }
            else
            {
                writer.WriteStringValue(errnoString);
            }
        }

        private void WriteHexBytes(ReadOnlySpan<byte> bytes, Span<char> charBuf)
        {
            var charPos = 0;
            if (bytes.Length > 0)
            {
                var b = bytes[0];
                charBuf[charPos++] = PerfConvert.ToHexChar(b >> 4);
                charBuf[charPos++] = PerfConvert.ToHexChar(b);
                for (int i = 1; i < bytes.Length; i += 1)
                {
                    b = bytes[i];
                    charBuf[charPos++] = ' ';
                    charBuf[charPos++] = PerfConvert.ToHexChar(b >> 4);
                    charBuf[charPos++] = PerfConvert.ToHexChar(b);
                }
            }

            writer.WriteStringValue(charBuf.Slice(0, charPos));
        }

        private void WriteHexInt(UInt32 value, Span<char> charBuf)
        {
            var charPos = charBuf.Length;
            do
            {
                charBuf[--charPos] = PerfConvert.ToHexChar(unchecked((int)value));
                value >>= 4;
            }
            while (value != 0);
            charBuf[--charPos] = 'x';
            charBuf[--charPos] = '0';
            writer.WriteStringValue(charBuf.Slice(charPos));
        }

        private void WriteHexInt(UInt64 value, Span<char> charBuf)
        {
            var bufPos = charBuf.Length;
            do
            {
                charBuf[--bufPos] = PerfConvert.ToHexChar(unchecked((int)value));
                value >>= 4;
            }
            while (value != 0);
            charBuf[--bufPos] = 'x';
            charBuf[--bufPos] = '0';
            writer.WriteStringValue(charBuf.Slice(bufPos));
        }

        private void WriteBooleanValue(UInt32 value)
        {
            switch (value)
            {
                case 0:
                    writer.WriteBooleanValue(false);
                    break;
                case 1:
                    writer.WriteBooleanValue(true);
                    break;
                default:
                    writer.WriteNumberValue(unchecked((int)value));
                    break;
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
