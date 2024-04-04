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

    [Flags]
    internal enum PerfDataMeta
    {
        None = 0,           // disable the "meta" suffix.
        N = 0x1,            // "n":"provider:event" before the user fields (not in the suffix).
        Time = 0x2,         // timestamp (only for sample events).
        Cpu = 0x4,          // cpu index (only for sample events).
        Pid = 0x8,          // process id (only for sample events).
        Tid = 0x10,         // thread id (only for sample events).
        Id = 0x20,          // eventheader id (decimal integer, omitted if 0).
        Version = 0x40,     // eventheader version (decimal integer, omitted if 0).
        Level = 0x80,       // eventheader level (decimal integer, omitted if 0).
        Keyword = 0x100,    // eventheader keyword (hexadecimal string, omitted if 0).
        Opcode = 0x200,     // eventheader opcode (decimal integer, omitted if 0).
        Tag = 0x400,        // eventheader tag (hexadecimal string, omitted if 0).
        Activity = 0x800,   // eventheader activity ID (UUID string, omitted if 0).
        RelatedActivity = 0x1000,// eventheader related activity ID (UUID string, omitted if not set).
        Provider = 0x10000, // provider name or system name (string).
        Event = 0x20000,    // event name or tracepoint name (string).
        Options = 0x40000,  // eventheader provider options (string, omitted if none).
        Flags = 0x80000,    // eventheader flags (hexadecimal string).
        Common = 0x100000,  // Include the common_* fields before the user fields (only for sample events).
        Default = 0xffff,   // Include n..relatedActivity.
        All = ~0
    }

    internal sealed class PerfDataDecode : IDisposable
    {
        private readonly PerfDataFileReader reader = new PerfDataFileReader();
        private readonly EventHeaderEnumerator enumerator = new EventHeaderEnumerator();
        private readonly Utf8JsonWriter writer;

        // Scratch buffer for formatting strings.
        private readonly ArrayBufferWriter<char> charBufStorage = new ArrayBufferWriter<char>();

        public PerfDataDecode(Utf8JsonWriter writer, PerfDataMeta meta = PerfDataMeta.Default)
        {
            this.writer = writer;
            this.Meta = meta;
        }

        public PerfDataMeta Meta { get; set; }

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
                if (result != PerfDataFileResult.Ok)
                {
                    if (result != PerfDataFileResult.EndOfFile)
                    {
                        writer.WriteCommentValue($"Pos {reader.FilePos}: ReadEvent {result}"); // Garbage.
                    }
                    break;
                }

                if (e.Header.Type != PerfEventHeaderType.Sample)
                {
                    finishedInit |= e.Header.Type == PerfEventHeaderType.FinishedInit;

                    if (e.Header.Type >= PerfEventHeaderType.UserTypeStart)
                    {
                        // Synthetic events, injected by the trace collection tool.
                        writer.WriteStartObject();
                        writer.WriteString("nsSynth", e.Header.Type.ToString()); // Garbage.
                        writer.WriteNumber("size", e.Bytes.Length);
                        writer.WriteEndObject();
                    }
                    else if (!finishedInit)
                    {
                        // Non-sample events before FinishedInit.
                        // Since we haven't seen FinishedInit, we can't get attributes for them.
                        // Typically these are system-configuration events with well-known formats
                        // like Mmap, Ksymbol, Fork.
                        writer.WriteStartObject();
                        writer.WriteString("ns", e.Header.Type.ToString()); // Garbage.
                        writer.WriteNumber("size", e.Bytes.Length);
                        writer.WriteEndObject();
                    }
                    else
                    {
                        // Non-sample events after FinishedInit.
                        // Attributes are expected to be available for these events.
                        PerfNonSampleEventInfo info;
                        result = reader.GetNonSampleEventInfo(e, out info);
                        if (result != PerfDataFileResult.Ok)
                        {
                            writer.WriteCommentValue($"Pos {reader.FilePos}: GetNonSampleEventInfo {result}"); // Garbage.
                        }
                        else
                        {
                            writer.WriteStartObject();
                            writer.WriteString("ns", $"{e.Header.Type}/{info.Name}"); // Garbage.
                            writer.WriteNumber("size", e.Bytes.Length);

                            if (this.Meta.HasFlag(PerfDataMeta.Time))
                            {
                                writer.WriteString("time", info.DateTime);
                            }

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
                        writer.WriteCommentValue($"Pos {reader.FilePos}: GetSampleEventInfo {result}"); // Garbage.
                    }
                    else if (!(info.Format is PerfEventFormat infoFormat))
                    {
                        // No TraceFS format for this event. Unexpected.
                        writer.WriteStartObject();
                        writer.WriteNull("sNoMeta");
                        writer.WriteNumber("size", e.Bytes.Length);
                        writer.WriteEndObject();
                    }
                    else if (infoFormat.DecodingStyle != PerfEventDecodingStyle.EventHeader)
                    {
                        // TraceFS format present. Not an EventHeader event.
                        writer.WriteStartObject();

                        if (this.Meta.HasFlag(PerfDataMeta.N))
                        {
                            writer.WriteString("n", info.Name);
                        }

                        // Skip the common fields by default.
                        var firstField = this.Meta.HasFlag(PerfDataMeta.Common) ? 0 : infoFormat.CommonFieldCount;

                        for (int i = firstField; i < infoFormat.Fields.Length; i++)
                        {
                            var fieldMeta = infoFormat.Fields[i];
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

                        if (0 != (this.Meta & ~PerfDataMeta.N))
                        {
                            writer.WriteStartObject("meta");
                            WriteSampleMeta(info, true);
                            writer.WriteEndObject(); // meta
                        }

                        writer.WriteEndObject();
                    }
                    else if (!this.enumerator.StartEvent(infoFormat.Name, info.UserData))
                    {
                        writer.WriteStartObject();
                        writer.WriteString("nEventHeaderBad", info.Name);
                        writer.WriteNumber("size", e.Bytes.Length);

                        if (0 != (this.Meta & ~PerfDataMeta.N))
                        {
                            writer.WriteStartObject("meta");
                            WriteSampleMeta(info, true);
                            writer.WriteEndObject(); // meta
                        }

                        writer.WriteEndObject();
                    }
                    else
                    {
                        var ei = this.enumerator.GetEventInfo();
                        writer.WriteStartObject();

                        if (this.Meta.HasFlag(PerfDataMeta.N))
                        {
                            writer.WriteString("n", // Garbage
                                infoFormat.SystemName == "user_events"
                                ? $"{new string(ei.ProviderName)}:{ei.NameAsString}"
                                : $"{infoFormat.SystemName}:{ei.ProviderName.ToString()}:{ei.NameAsString}");
                        }

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
                                            // Process the simple array directly without using the enumerator.
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

                        if (0 != (this.Meta & ~PerfDataMeta.N))
                        {
                            writer.WriteStartObject("meta");

                            WriteSampleMeta(info, false);

                            if (this.Meta.HasFlag(PerfDataMeta.Provider))
                            {
                                writer.WriteString("provider", ei.ProviderName);
                            }

                            if (this.Meta.HasFlag(PerfDataMeta.Event))
                            {
                                writer.WriteString("event", ei.NameBytes);
                            }

                            if (this.Meta.HasFlag(PerfDataMeta.Id) && ei.Header.Id != 0)
                            {
                                this.writer.WriteNumber("id", ei.Header.Id);
                            }

                            if (this.Meta.HasFlag(PerfDataMeta.Version) && ei.Header.Version != 0)
                            {
                                this.writer.WriteNumber("version", ei.Header.Version);
                            }

                            if (this.Meta.HasFlag(PerfDataMeta.Level) && ei.Header.Level != 0)
                            {
                                this.writer.WriteNumber("level", (byte)ei.Header.Level);
                            }

                            if (this.Meta.HasFlag(PerfDataMeta.Keyword) && ei.Keyword != 0)
                            {
                                this.writer.WriteString("keyword", PerfConvert.HexU64FormatAtEnd(charBuf, ei.Keyword));
                            }

                            if (this.Meta.HasFlag(PerfDataMeta.Opcode) && ei.Header.Opcode != 0)
                            {
                                this.writer.WriteNumber("opcode", (byte)ei.Header.Opcode);
                            }

                            if (this.Meta.HasFlag(PerfDataMeta.Tag) && ei.Header.Tag != 0)
                            {
                                this.writer.WriteString("tag", PerfConvert.HexU32FormatAtEnd(charBuf, ei.Header.Tag));
                            }

                            if (this.Meta.HasFlag(PerfDataMeta.Activity) && ei.ActivityId is Guid aid)
                            {
                                this.writer.WriteString("activity", aid);
                            }
                            
                            if (this.Meta.HasFlag(PerfDataMeta.RelatedActivity) && ei.RelatedActivityId is Guid rid)
                            {
                                this.writer.WriteString("relatedActivity", rid);
                            }

                            if (this.Meta.HasFlag(PerfDataMeta.Options) && !ei.Options.IsEmpty)
                            {
                                this.writer.WriteString("options", ei.Options);
                            }

                            if (this.Meta.HasFlag(PerfDataMeta.Flags) && ei.Header.Flags != 0)
                            {
                                this.writer.WriteString("flags", PerfConvert.HexU32FormatAtEnd(charBuf, (uint)ei.Header.Flags));
                            }

                            this.writer.WriteEndObject(); // meta
                        }

                        this.writer.WriteEndObject(); // event
                    }
                }
            }
        }

        private void WriteSampleMeta(in PerfSampleEventInfo info, bool showProviderEvent)
        {
            var sampleType = info.SampleType;

            if (sampleType.HasFlag(PerfEventAttrSampleType.Time) && this.Meta.HasFlag(PerfDataMeta.Time))
            {
                if (info.SessionInfo.ClockOffsetKnown)
                {
                    writer.WriteString("time", info.DateTime);
                }
                else
                {
                    writer.WriteNumber("time", info.Time / 1000000000.0);
                }
            }

            if (sampleType.HasFlag(PerfEventAttrSampleType.Cpu) && this.Meta.HasFlag(PerfDataMeta.Cpu))
            {
                writer.WriteNumber("cpu", info.Cpu);
            }

            if (sampleType.HasFlag(PerfEventAttrSampleType.Tid))
            {
                if (this.Meta.HasFlag(PerfDataMeta.Pid))
                {
                    writer.WriteNumber("pid", info.Pid);
                }

                if (this.Meta.HasFlag(PerfDataMeta.Tid))
                {
                    writer.WriteNumber("tid", info.Tid);
                }
            }

            if (showProviderEvent && 0 != (this.Meta & (PerfDataMeta.Provider | PerfDataMeta.Event)))
            {
                var name = info.Name.AsSpan();
                var colonPos = name.IndexOf(':');
                ReadOnlySpan<char> providerName, eventName;
                if (colonPos < 0)
                {
                    providerName = default;
                    eventName = name;
                }
                else
                {
                    providerName = name.Slice(0, colonPos);
                    eventName = name.Slice(colonPos + 1);
                }

                if (this.Meta.HasFlag(PerfDataMeta.Provider) && !providerName.IsEmpty)
                {
                    writer.WriteString("provider", providerName);
                }

                if (this.Meta.HasFlag(PerfDataMeta.Event) && !eventName.IsEmpty)
                {
                    writer.WriteString("event", eventName);
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
