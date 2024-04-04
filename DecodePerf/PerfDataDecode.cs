namespace DecodePerf
{
    using Microsoft.LinuxTracepoints;
    using Microsoft.LinuxTracepoints.Decode;
    using System;
    using System.Buffers;
    using CultureInfo = System.Globalization.CultureInfo;
    using Debug = System.Diagnostics.Debug;
    using Encoding = System.Text.Encoding;
    using IPAddress = System.Net.IPAddress;
    using MemoryMarshal = System.Runtime.InteropServices.MemoryMarshal;
    using Stream = System.IO.Stream;
    using Utf8JsonWriter = System.Text.Json.Utf8JsonWriter;

    internal sealed class PerfDataDecode : IDisposable
    {
        private readonly PerfDataFileReader reader = new PerfDataFileReader();
        private readonly EventHeaderEnumerator enumerator = new EventHeaderEnumerator();
        private readonly Utf8JsonWriter writer;
        private readonly ArrayBufferWriter<char> charBufStorage = new ArrayBufferWriter<char>();

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

            // We assume charBuf is large enough for all fixed-length fields.
            // The long ones are Value128.HexBytes (needs 47) and Value128.IPv6 (needs 45).
            var charBuf = this.charBufStorage.GetSpan(Math.Max(this.charBufStorage.FreeCapacity, 64));
            Debug.Assert(this.charBufStorage.WrittenCount == 0);

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
                                var fieldValue = fieldMeta.GetFieldValue(info.RawDataSpan, byteReader);
                                if (!fieldValue.IsArray)
                                {
                                    writer.WritePropertyName(fieldMeta.Name);
                                    this.WriteValue(fieldValue, ref charBuf);
                                }
                                else
                                {
                                    writer.WriteStartArray(fieldMeta.Name);
                                    WriteSimpleArrayValues(fieldValue, ref charBuf);
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
                                                writer.WritePropertyName(MakeName(item, ref charBuf));
                                            }
                                            this.WriteValue(item.Value, ref charBuf);
                                            break;
                                        case EventHeaderEnumeratorState.StructBegin:
                                            if (!item.Value.IsArray)
                                            {
                                                writer.WritePropertyName(MakeName(item, ref charBuf));
                                            }
                                            writer.WriteStartObject();
                                            break;
                                        case EventHeaderEnumeratorState.StructEnd:
                                            writer.WriteEndObject();
                                            break;
                                        case EventHeaderEnumeratorState.ArrayBegin:
                                            writer.WritePropertyName(MakeName(item, ref charBuf));
                                            writer.WriteStartArray();
                                            if (item.Value.ElementSize != 0)
                                            {
                                                // Process the entire array directly without using the enumerator.
                                                WriteSimpleArrayValues(item.Value, ref charBuf);
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

        private void WriteValue(in PerfValue item, ref Span<char> charBuf)
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
                            writer.WriteStringValue(PerfConvert.HexU32FormatAtEnd(charBuf, item.GetU8()));
                            return;
                        case EventFieldFormat.Boolean:
                            this.WriteBooleanValue(item.GetU8());
                            return;
                        case EventFieldFormat.HexBytes:
                            writer.WriteStringValue(PerfConvert.HexBytesFormat(charBuf, item.GetSpan8()));
                            return;
                        case EventFieldFormat.String8:
                            charBuf[0] = (char)item.GetU8();
                            writer.WriteStringValue(charBuf.Slice(0, 1));
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
                            writer.WriteStringValue(PerfConvert.HexU32FormatAtEnd(charBuf, item.GetU16()));
                            return;
                        case EventFieldFormat.Boolean:
                            this.WriteBooleanValue(item.GetU16());
                            return;
                        case EventFieldFormat.HexBytes:
                            writer.WriteStringValue(PerfConvert.HexBytesFormat(charBuf, item.GetSpan16()));
                            return;
                        case EventFieldFormat.StringUtf:
                            charBuf[0] = (char)item.GetU16();
                            writer.WriteStringValue(charBuf.Slice(0, 1));
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
                            writer.WriteStringValue(PerfConvert.HexU32FormatAtEnd(charBuf, item.GetU32()));
                            return;
                        case EventFieldFormat.Errno:
                            this.WriteErrnoValue(item.GetI32());
                            return;
                        case EventFieldFormat.Time:
                            writer.WriteStringValue(PerfConvert.UnixTime32ToDateTime(item.GetI32()));
                            return;
                        case EventFieldFormat.Boolean:
                            this.WriteBooleanValue(item.GetU32());
                            return;
                        case EventFieldFormat.Float:
                            this.WriteFloat32Value(charBuf, item.GetF32());
                            return;
                        case EventFieldFormat.HexBytes:
                            writer.WriteStringValue(PerfConvert.HexBytesFormat(charBuf, item.GetSpan32()));
                            return;
                        case EventFieldFormat.StringUtf:
                            writer.WriteStringValue(PerfConvert.Utf32Format(charBuf, item.GetU32()));
                            return;
                        case EventFieldFormat.IPv4:
                            writer.WriteStringValue(PerfConvert.IPv4Format(charBuf, item.GetIPv4()));
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
                            writer.WriteStringValue(PerfConvert.HexU64FormatAtEnd(charBuf, item.GetU64()));
                            return;
                        case EventFieldFormat.Time:
                            this.WriteUnixTime64Value(item.GetI64());
                            return;
                        case EventFieldFormat.Float:
                            this.WriteFloat64Value(charBuf, item.GetF64());
                            return;
                        case EventFieldFormat.HexBytes:
                            writer.WriteStringValue(PerfConvert.HexBytesFormat(charBuf, item.GetSpan64()));
                            return;
                    }
                case EventFieldEncoding.Value128:
                    switch (item.Format)
                    {
                        default:
                        case EventFieldFormat.HexBytes:
                            writer.WriteStringValue(PerfConvert.HexBytesFormat(charBuf, item.GetSpan128()));
                            return;
                        case EventFieldFormat.Uuid:
                            writer.WriteStringValue(item.GetGuid());
                            return;
                        case EventFieldFormat.IPv6:
                            this.WriteIPv6Value(charBuf, item.GetIPv6());
                            return;
                    }
                case EventFieldEncoding.ZStringChar8:
                case EventFieldEncoding.StringLength16Char8:
                    switch (item.Format)
                    {
                        case EventFieldFormat.HexBytes:
                            WriteHexBytesValue(item.Bytes, ref charBuf);
                            return;
                        case EventFieldFormat.String8:
                            WriteDecodedStringValue(item.Bytes, PerfConvert.EncodingLatin1, ref charBuf);
                            return;
                        case EventFieldFormat.StringUtfBom:
                        case EventFieldFormat.StringXml:
                        case EventFieldFormat.StringJson:
                            var encoding = PerfConvert.EncodingFromBom(item.Bytes);
                            if (encoding == null)
                            {
                                goto case EventFieldFormat.StringUtf;
                            }
                            WriteBomStringValue(item.Bytes, encoding, ref charBuf);
                            return;
                        default:
                        case EventFieldFormat.StringUtf:
                            writer.WriteStringValue(item.Bytes); // UTF-8
                            return;
                    }
                case EventFieldEncoding.ZStringChar16:
                case EventFieldEncoding.StringLength16Char16:
                    switch (item.Format)
                    {
                        case EventFieldFormat.HexBytes:
                            WriteHexBytesValue(item.Bytes, ref charBuf);
                            return;
                        case EventFieldFormat.StringUtfBom:
                        case EventFieldFormat.StringXml:
                        case EventFieldFormat.StringJson:
                            var encoding = PerfConvert.EncodingFromBom(item.Bytes);
                            if (encoding == null)
                            {
                                goto case EventFieldFormat.StringUtf;
                            }
                            WriteBomStringValue(item.Bytes, encoding, ref charBuf);
                            return;
                        default:
                        case EventFieldFormat.StringUtf:
                            if (item.ByteReader.ByteSwapNeeded)
                            {
                                WriteDecodedStringValue(
                                    item.Bytes,
                                    BitConverter.IsLittleEndian ? Encoding.BigEndianUnicode : Encoding.Unicode,
                                    ref charBuf);
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
                            WriteHexBytesValue(item.Bytes, ref charBuf);
                            return;
                        case EventFieldFormat.StringUtfBom:
                        case EventFieldFormat.StringXml:
                        case EventFieldFormat.StringJson:
                            var encoding = PerfConvert.EncodingFromBom(item.Bytes);
                            if (encoding == null)
                            {
                                goto case EventFieldFormat.StringUtf;
                            }
                            WriteBomStringValue(item.Bytes, encoding, ref charBuf);
                            return;
                        default:
                        case EventFieldFormat.StringUtf:
                            WriteDecodedStringValue(
                                item.Bytes,
                                item.FromBigEndian ? PerfConvert.EncodingUTF32BE : Encoding.UTF32,
                                ref charBuf);
                            return;
                    }
            }
        }

        /// <summary>
        /// Interprets the item as the BeginArray of a simple array (ElementSize != 0).
        /// Calls writer.WriteValue(...) for each element in the array.
        /// </summary>
        private void WriteSimpleArrayValues(in PerfValue item, ref Span<char> charBuf)
        {
            var arrayCount = item.ArrayCount;
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
                                writer.WriteStringValue(PerfConvert.HexU32FormatAtEnd(charBuf, item.GetU8(i)));
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
                                writer.WriteStringValue(PerfConvert.HexBytesFormat(charBuf, item.GetSpan8(i)));
                            }
                            return;
                        case EventFieldFormat.String8:
                            var chars1 = charBuf.Slice(0, 1);
                            for (int i = 0; i < arrayCount; i += 1)
                            {
                                chars1[0] = (char)item.GetU8(i);
                                writer.WriteStringValue(chars1);
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
                                writer.WriteStringValue(PerfConvert.HexU32FormatAtEnd(charBuf, item.GetU16(i)));
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
                                writer.WriteStringValue(PerfConvert.HexBytesFormat(charBuf, item.GetSpan16(i)));
                            }
                            return;
                        case EventFieldFormat.StringUtf:
                            var chars1 = charBuf.Slice(0, 1);
                            for (int i = 0; i < arrayCount; i += 1)
                            {
                                chars1[0] = (char)item.GetU16(i);
                                writer.WriteStringValue(chars1);
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
                                writer.WriteStringValue(PerfConvert.HexU32FormatAtEnd(charBuf, item.GetU32(i)));
                            }
                            return;
                        case EventFieldFormat.Errno:
                            for (int i = 0; i < arrayCount; i += 1)
                            {
                                this.WriteErrnoValue(item.GetI32(i));
                            }
                            return;
                        case EventFieldFormat.Time:
                            for (int i = 0; i < arrayCount; i += 1)
                            {
                                writer.WriteStringValue(PerfConvert.UnixTime32ToDateTime(item.GetI32(i)));
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
                                this.WriteFloat32Value(charBuf, item.GetF32(i));
                            }
                            return;
                        case EventFieldFormat.HexBytes:
                            for (int i = 0; i < arrayCount; i += 1)
                            {
                                writer.WriteStringValue(PerfConvert.HexBytesFormat(charBuf, item.GetSpan32(i)));
                            }
                            return;
                        case EventFieldFormat.StringUtf:
                            for (int i = 0; i < arrayCount; i += 1)
                            {
                                writer.WriteStringValue(PerfConvert.Utf32Format(charBuf, item.GetU32(i)));
                            }
                            return;
                        case EventFieldFormat.IPv4:
                            for (int i = 0; i < arrayCount; i += 1)
                            {
                                writer.WriteStringValue(PerfConvert.IPv4Format(charBuf, item.GetIPv4(i)));
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
                                writer.WriteStringValue(PerfConvert.HexU64FormatAtEnd(charBuf, item.GetU64(i)));
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
                                this.WriteFloat64Value(charBuf, item.GetF64(i));
                            }
                            return;
                        case EventFieldFormat.HexBytes:
                            for (int i = 0; i < arrayCount; i += 1)
                            {
                                writer.WriteStringValue(PerfConvert.HexBytesFormat(charBuf, item.GetSpan64(i)));
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
                                writer.WriteStringValue(PerfConvert.HexBytesFormat(charBuf, item.GetSpan128(i)));
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
                                this.WriteIPv6Value(charBuf, item.GetIPv6(i));
                            }
                            return;
                    }
            }
        }

        private void WriteBomStringValue(ReadOnlySpan<byte> bytes, Encoding encoding, ref Span<char> charBuf)
        {
            switch (encoding.CodePage)
            {
                case 65001: // UTF-8
                    writer.WriteStringValue(bytes.Slice(3));
                    break;
                case 1200: // UTF-16LE
                    if (BitConverter.IsLittleEndian)
                    {
                        writer.WriteStringValue(MemoryMarshal.Cast<byte, char>(bytes.Slice(2)));
                    }
                    else
                    {
                        WriteDecodedStringValue(bytes.Slice(2), encoding, ref charBuf);
                    }
                    break;
                case 1201: // UTF-16BE
                    if (BitConverter.IsLittleEndian)
                    {
                        WriteDecodedStringValue(bytes.Slice(2), encoding, ref charBuf);
                    }
                    else
                    {
                        writer.WriteStringValue(MemoryMarshal.Cast<byte, char>(bytes.Slice(2)));
                    }
                    break;
                case 12000: // UTF-32LE
                case 12001: // UTF-32BE
                    WriteDecodedStringValue(bytes.Slice(4), encoding, ref charBuf);
                    break;
                default:
                    Debug.Fail("Unexpected encoding.");
                    break;
            }
        }

        private void WriteDecodedStringValue(ReadOnlySpan<byte> bytes, Encoding encoding, ref Span<char> charBuf)
        {
            EnsureSpan(encoding.GetMaxCharCount(bytes.Length), ref charBuf);
            var charCount = encoding.GetChars(bytes, charBuf);
            writer.WriteStringValue(charBuf.Slice(0, charCount));
        }

        private void WriteHexBytesValue(ReadOnlySpan<byte> bytes, ref Span<char> charBuf)
        {
            EnsureSpan(PerfConvert.HexBytesFormatLength(bytes.Length), ref charBuf);
            writer.WriteStringValue(PerfConvert.HexBytesFormat(charBuf, bytes));
        }

        private void WriteFloat32Value(Span<char> charBuf, float value)
        {
            if (float.IsFinite(value))
            {
                writer.WriteNumberValue(value);
            }
            else
            {
                // Write Infinity, -Infinity, or NaN as a string.
                var ok = value.TryFormat(charBuf, out var count, default, CultureInfo.InvariantCulture);
                Debug.Assert(ok);
                writer.WriteStringValue(charBuf.Slice(0, count));
            }
        }

        private void WriteFloat64Value(Span<char> charBuf, double value)
        {
            if (double.IsFinite(value))
            {
                writer.WriteNumberValue(value);
            }
            else
            {
                // Write Infinity, -Infinity, or NaN as a string.
                var ok = value.TryFormat(charBuf, out var count, default, CultureInfo.InvariantCulture);
                Debug.Assert(ok);
                writer.WriteStringValue(charBuf.Slice(0, count));
            }
        }

        private void WriteIPv6Value(Span<char> charBuf, ReadOnlySpan<byte> value)
        {
            var addr = new IPAddress(value); // Garbage.
            var ok = addr.TryFormat(charBuf, out var count);
            Debug.Assert(ok);
            writer.WriteStringValue(charBuf.Slice(0, count));
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
                // Write out-of-range time_t as a signed integer.
                writer.WriteNumberValue(value);
            }
        }

        private void WriteErrnoValue(int errno)
        {
            var errnoString = PerfConvert.ErrnoLookup(errno);
            if (errnoString != null)
            {
                writer.WriteStringValue(errnoString);
            }
            else
            {
                // Write unrecognized errno as a signed integer.
                writer.WriteNumberValue(errno);
            }
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
                    // Write other values of true as signed integer.
                    writer.WriteNumberValue(unchecked((int)value));
                    break;
            }
        }

        private ReadOnlySpan<byte> MakeName(in EventHeaderItemInfo item, ref Span<char> charBuf)
        {
            var tag = item.Value.FieldTag;
            var nameBytes = item.NameBytes;
            if (tag == 0)
            {
                return nameBytes;
            }

            // ";tag=0xFFFF"
            const int TagMax = 11; // ";tag=0xFFFF"
            EnsureSpan((nameBytes.Length + TagMax + sizeof(char) - 1) / sizeof(char), ref charBuf);
            var byteBuf = MemoryMarshal.Cast<char, byte>(charBuf);
            nameBytes.CopyTo(byteBuf);

            var pos = nameBytes.Length;
            byteBuf[pos++] = (byte)';';
            byteBuf[pos++] = (byte)'t';
            byteBuf[pos++] = (byte)'a';
            byteBuf[pos++] = (byte)'g';
            byteBuf[pos++] = (byte)'=';
            byteBuf[pos++] = (byte)'0';
            byteBuf[pos++] = (byte)'x';
            if (0 != (tag & 0xF000)) byteBuf[pos++] = (byte)PerfConvert.ToHexChar(tag >> 12);
            if (0 != (tag & 0xFF00)) byteBuf[pos++] = (byte)PerfConvert.ToHexChar(tag >> 8);
            if (0 != (tag & 0xFFF0)) byteBuf[pos++] = (byte)PerfConvert.ToHexChar(tag >> 4);
            byteBuf[pos++] = (byte)PerfConvert.ToHexChar(tag);

            return byteBuf.Slice(0, pos);
        }

        private void EnsureSpan(int minLength, ref Span<char> charBuf)
        {
            if (charBuf.Length < minLength)
            {
                charBuf = charBufStorage.GetSpan(minLength);
            }
        }
    }
}
