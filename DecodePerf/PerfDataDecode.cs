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

        public void DecodeFile(string fileName, PerfDataFileEventOrder eventOrder)
        {
            reader.OpenFile(fileName, eventOrder);
            this.Decode();
        }

        public void DecodeStream(Stream stream, PerfDataFileEventOrder eventOrder, bool leaveOpen = false)
        {
            reader.OpenStream(stream, eventOrder, leaveOpen);
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
            PerfEventBytes eventBytes;

            // We assume charBuf is large enough for all fixed-length fields.
            // The long ones are Value128.HexBytes (needs 47) and Value128.IPv6 (needs 45).
            var charBuf = this.charBufStorage.GetSpan(Math.Max(this.charBufStorage.FreeCapacity, 64));
            Debug.Assert(this.charBufStorage.WrittenCount == 0);

            while (true)
            {
                result = reader.ReadEvent(out eventBytes);
                if (result != PerfDataFileResult.Ok)
                {
                    if (result != PerfDataFileResult.EndOfFile)
                    {
                        writer.WriteCommentValue($"Pos {reader.FilePos}: ReadEvent {result}"); // Garbage.
                    }
                    break;
                }

                writer.WriteStartObject(); // event

                if (eventBytes.Header.Type != PerfEventHeaderType.Sample)
                {
                    finishedInit |= eventBytes.Header.Type == PerfEventHeaderType.FinishedInit;

                    PerfNonSampleEventInfo info;
                    if (eventBytes.Header.Type >= PerfEventHeaderType.UserTypeStart)
                    {
                        // Synthetic events, no attributes.
                        info = default;
                        result = PerfDataFileResult.NoData;
                    }
                    else
                    {
                        // Attributes are expected to be available for these events.
                        result = reader.GetNonSampleEventInfo(eventBytes, out info);
                        if (result != PerfDataFileResult.Ok)
                        {
                            // Attributes not available.
                            // If we haven't seen FinishedInit, IdNotFound is expected.
                            if (finishedInit || result != PerfDataFileResult.IdNotFound)
                            {
                                writer.WriteCommentValue($"Pos {reader.FilePos}: GetNonSampleEventInfo {result}"); // Garbage.
                            }
                        }
                    }

                    writer.WriteString("ns", // Garbage.
                        result != PerfDataFileResult.Ok
                        ? eventBytes.Header.Type.ToString()
                        : $"{eventBytes.Header.Type}/{info.Name}");
                    writer.WriteNumber("size", eventBytes.Memory.Length);

                    if (result == PerfDataFileResult.Ok)
                    {
                        WriteNonSampleMeta(info);
                    }
                }
                else
                {
                    PerfSampleEventInfo info;
                    result = reader.GetSampleEventInfo(eventBytes, out info);
                    if (result != PerfDataFileResult.Ok)
                    {
                        // Unable to lookup attributes for event. Unexpected.
                        writer.WriteCommentValue($"Pos {reader.FilePos}: GetSampleEventInfo {result}"); // Garbage.
                        writer.WriteNull("n");
                        writer.WriteNumber("size", eventBytes.Memory.Length);
                    }
                    else if (!(info.Format is PerfEventFormat infoFormat))
                    {
                        // No TraceFS format for this event. Unexpected.
                        writer.WriteString("n", info.Name);
                        writer.WriteNumber("size", eventBytes.Memory.Length);
                    }
                    else if (infoFormat.DecodingStyle != PerfEventDecodingStyle.EventHeader ||
                        !this.enumerator.StartEvent(infoFormat.Name, info.UserData))
                    {
                        // Non-EventHeader decoding.

                        if (this.Meta.HasFlag(PerfDataMeta.N))
                        {
                            writer.WriteString("n", info.Name);
                        }

                        // Write the event fields. Skip the common fields by default.
                        var firstField = this.Meta.HasFlag(PerfDataMeta.Common) ? 0 : infoFormat.CommonFieldCount;
                        for (int i = firstField; i < infoFormat.Fields.Count; i++)
                        {
                            var fieldFormat = infoFormat.Fields[i];
                            var fieldValue = fieldFormat.GetFieldValue(info.RawDataSpan, byteReader);
                            if (!fieldValue.IsArrayOrElement)
                            {
                                writer.WritePropertyName(fieldFormat.Name);
                                this.WriteValue(fieldValue, ref charBuf);
                            }
                            else
                            {
                                writer.WriteStartArray(fieldFormat.Name);
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
                    }
                    else
                    {
                        // EventHeader decoding.

                        var ei = this.enumerator.GetEventInfo();
                        if (this.Meta.HasFlag(PerfDataMeta.N))
                        {
                            writer.WriteString("n", // Garbage
                                infoFormat.SystemName == "user_events"
                                ? $"{ei.ProviderName.ToString()}:{ei.NameAsString}"
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
                                        if (!item.Value.IsArrayOrElement)
                                        {
                                            writer.WritePropertyName(MakeName(item, ref charBuf));
                                        }
                                        this.WriteValue(item.Value, ref charBuf);
                                        break;
                                    case EventHeaderEnumeratorState.StructBegin:
                                        if (!item.Value.IsArrayOrElement)
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
                                        if (item.Value.TypeSize != 0)
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
                    }
                }

                writer.WriteEndObject(); // event
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

        private void WriteNonSampleMeta(in PerfNonSampleEventInfo info)
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

            if (0 != (this.Meta & (PerfDataMeta.Provider | PerfDataMeta.Event)))
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
                case EventHeaderFieldEncoding.Invalid:
                    writer.WriteNullValue();
                    return;
                case EventHeaderFieldEncoding.Struct:
                    throw new InvalidOperationException("Invalid encoding for FormatScalar.");
                case EventHeaderFieldEncoding.Value8:
                    switch (item.Format)
                    {
                        default:
                        case EventHeaderFieldFormat.UnsignedInt:
                            writer.WriteNumberValue(item.GetU8());
                            return;
                        case EventHeaderFieldFormat.SignedInt:
                            writer.WriteNumberValue(item.GetI8());
                            return;
                        case EventHeaderFieldFormat.HexInt:
                            writer.WriteStringValue(PerfConvert.HexU32FormatAtEnd(charBuf, item.GetU8()));
                            return;
                        case EventHeaderFieldFormat.Boolean:
                            this.WriteBooleanValue(item.GetU8());
                            return;
                        case EventHeaderFieldFormat.HexBytes:
                            writer.WriteStringValue(PerfConvert.HexBytesFormat(charBuf, item.GetSpan8()));
                            return;
                        case EventHeaderFieldFormat.String8:
                            charBuf[0] = (char)item.GetU8();
                            writer.WriteStringValue(charBuf.Slice(0, 1));
                            return;
                    }
                case EventHeaderFieldEncoding.Value16:
                    switch (item.Format)
                    {
                        default:
                        case EventHeaderFieldFormat.UnsignedInt:
                            writer.WriteNumberValue(item.GetU16());
                            return;
                        case EventHeaderFieldFormat.SignedInt:
                            writer.WriteNumberValue(item.GetI16());
                            return;
                        case EventHeaderFieldFormat.HexInt:
                            writer.WriteStringValue(PerfConvert.HexU32FormatAtEnd(charBuf, item.GetU16()));
                            return;
                        case EventHeaderFieldFormat.Boolean:
                            this.WriteBooleanValue(item.GetU16());
                            return;
                        case EventHeaderFieldFormat.HexBytes:
                            writer.WriteStringValue(PerfConvert.HexBytesFormat(charBuf, item.GetSpan16()));
                            return;
                        case EventHeaderFieldFormat.StringUtf:
                            charBuf[0] = (char)item.GetU16();
                            writer.WriteStringValue(charBuf.Slice(0, 1));
                            return;
                        case EventHeaderFieldFormat.Port:
                            writer.WriteNumberValue(item.GetPort());
                            return;
                    }
                case EventHeaderFieldEncoding.Value32:
                    switch (item.Format)
                    {
                        default:
                        case EventHeaderFieldFormat.UnsignedInt:
                            writer.WriteNumberValue(item.GetU32());
                            return;
                        case EventHeaderFieldFormat.SignedInt:
                        case EventHeaderFieldFormat.Pid:
                            writer.WriteNumberValue(item.GetI32());
                            return;
                        case EventHeaderFieldFormat.HexInt:
                            writer.WriteStringValue(PerfConvert.HexU32FormatAtEnd(charBuf, item.GetU32()));
                            return;
                        case EventHeaderFieldFormat.Errno:
                            this.WriteErrnoValue(item.GetI32());
                            return;
                        case EventHeaderFieldFormat.Time:
                            writer.WriteStringValue(PerfConvert.UnixTime32ToDateTime(item.GetI32()));
                            return;
                        case EventHeaderFieldFormat.Boolean:
                            this.WriteBooleanValue(item.GetU32());
                            return;
                        case EventHeaderFieldFormat.Float:
                            this.WriteFloat32Value(charBuf, item.GetF32());
                            return;
                        case EventHeaderFieldFormat.HexBytes:
                            writer.WriteStringValue(PerfConvert.HexBytesFormat(charBuf, item.GetSpan32()));
                            return;
                        case EventHeaderFieldFormat.StringUtf:
                            writer.WriteStringValue(PerfConvert.Utf32Format(charBuf, item.GetU32()));
                            return;
                        case EventHeaderFieldFormat.IPv4:
                            writer.WriteStringValue(PerfConvert.IPv4Format(charBuf, item.GetIPv4()));
                            return;
                    }
                case EventHeaderFieldEncoding.Value64:
                    switch (item.Format)
                    {
                        default:
                        case EventHeaderFieldFormat.UnsignedInt:
                            writer.WriteNumberValue(item.GetU64());
                            return;
                        case EventHeaderFieldFormat.SignedInt:
                            writer.WriteNumberValue(item.GetI64());
                            return;
                        case EventHeaderFieldFormat.HexInt:
                            writer.WriteStringValue(PerfConvert.HexU64FormatAtEnd(charBuf, item.GetU64()));
                            return;
                        case EventHeaderFieldFormat.Time:
                            this.WriteUnixTime64Value(item.GetI64());
                            return;
                        case EventHeaderFieldFormat.Float:
                            this.WriteFloat64Value(charBuf, item.GetF64());
                            return;
                        case EventHeaderFieldFormat.HexBytes:
                            writer.WriteStringValue(PerfConvert.HexBytesFormat(charBuf, item.GetSpan64()));
                            return;
                    }
                case EventHeaderFieldEncoding.Value128:
                    switch (item.Format)
                    {
                        default:
                        case EventHeaderFieldFormat.HexBytes:
                            writer.WriteStringValue(PerfConvert.HexBytesFormat(charBuf, item.GetSpan128()));
                            return;
                        case EventHeaderFieldFormat.Uuid:
                            writer.WriteStringValue(item.GetGuid());
                            return;
                        case EventHeaderFieldFormat.IPv6:
                            this.WriteIPv6Value(charBuf, item.GetIPv6());
                            return;
                    }
                case EventHeaderFieldEncoding.ZStringChar8:
                case EventHeaderFieldEncoding.StringLength16Char8:
                    switch (item.Format)
                    {
                        case EventHeaderFieldFormat.HexBytes:
                            WriteHexBytesValue(item.Bytes, ref charBuf);
                            return;
                        case EventHeaderFieldFormat.String8:
                            WriteDecodedStringValue(item.Bytes, PerfConvert.EncodingLatin1, ref charBuf);
                            return;
                        case EventHeaderFieldFormat.StringUtfBom:
                        case EventHeaderFieldFormat.StringXml:
                        case EventHeaderFieldFormat.StringJson:
                            var encoding = PerfConvert.EncodingFromBom(item.Bytes);
                            if (encoding == null)
                            {
                                goto case EventHeaderFieldFormat.StringUtf;
                            }
                            WriteBomStringValue(item.Bytes, encoding, ref charBuf);
                            return;
                        default:
                        case EventHeaderFieldFormat.StringUtf:
                            writer.WriteStringValue(item.Bytes); // UTF-8
                            return;
                    }
                case EventHeaderFieldEncoding.ZStringChar16:
                case EventHeaderFieldEncoding.StringLength16Char16:
                    switch (item.Format)
                    {
                        case EventHeaderFieldFormat.HexBytes:
                            WriteHexBytesValue(item.Bytes, ref charBuf);
                            return;
                        case EventHeaderFieldFormat.StringUtfBom:
                        case EventHeaderFieldFormat.StringXml:
                        case EventHeaderFieldFormat.StringJson:
                            var encoding = PerfConvert.EncodingFromBom(item.Bytes);
                            if (encoding == null)
                            {
                                goto case EventHeaderFieldFormat.StringUtf;
                            }
                            WriteBomStringValue(item.Bytes, encoding, ref charBuf);
                            return;
                        default:
                        case EventHeaderFieldFormat.StringUtf:
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
                case EventHeaderFieldEncoding.ZStringChar32:
                case EventHeaderFieldEncoding.StringLength16Char32:
                    switch (item.Format)
                    {
                        case EventHeaderFieldFormat.HexBytes:
                            WriteHexBytesValue(item.Bytes, ref charBuf);
                            return;
                        case EventHeaderFieldFormat.StringUtfBom:
                        case EventHeaderFieldFormat.StringXml:
                        case EventHeaderFieldFormat.StringJson:
                            var encoding = PerfConvert.EncodingFromBom(item.Bytes);
                            if (encoding == null)
                            {
                                goto case EventHeaderFieldFormat.StringUtf;
                            }
                            WriteBomStringValue(item.Bytes, encoding, ref charBuf);
                            return;
                        default:
                        case EventHeaderFieldFormat.StringUtf:
                            WriteDecodedStringValue(
                                item.Bytes,
                                item.FromBigEndian ? PerfConvert.EncodingUTF32BE : Encoding.UTF32,
                                ref charBuf);
                            return;
                    }
            }
        }

        /// <summary>
        /// Interprets the item as the BeginArray of a simple array (TypeSize != 0).
        /// Calls writer.WriteValue(...) for each element in the array.
        /// </summary>
        private void WriteSimpleArrayValues(in PerfValue item, ref Span<char> charBuf)
        {
            var elementCount = item.ElementCount;
            switch (item.Encoding)
            {
                default:
                    throw new NotSupportedException("Unknown encoding.");
                case EventHeaderFieldEncoding.Invalid:
                case EventHeaderFieldEncoding.Struct:
                case EventHeaderFieldEncoding.ZStringChar8:
                case EventHeaderFieldEncoding.StringLength16Char8:
                case EventHeaderFieldEncoding.ZStringChar16:
                case EventHeaderFieldEncoding.StringLength16Char16:
                case EventHeaderFieldEncoding.ZStringChar32:
                case EventHeaderFieldEncoding.StringLength16Char32:
                    throw new InvalidOperationException("Invalid encoding for WriteSimpleArray.");
                case EventHeaderFieldEncoding.Value8:
                    switch (item.Format)
                    {
                        default:
                        case EventHeaderFieldFormat.UnsignedInt:
                            for (int i = 0; i < elementCount; i += 1)
                            {
                                writer.WriteNumberValue(item.GetU8(i));
                            }
                            return;
                        case EventHeaderFieldFormat.SignedInt:
                            for (int i = 0; i < elementCount; i += 1)
                            {
                                writer.WriteNumberValue(item.GetI8(i));
                            }
                            return;
                        case EventHeaderFieldFormat.HexInt:
                            for (int i = 0; i < elementCount; i += 1)
                            {
                                writer.WriteStringValue(PerfConvert.HexU32FormatAtEnd(charBuf, item.GetU8(i)));
                            }
                            return;
                        case EventHeaderFieldFormat.Boolean:
                            for (int i = 0; i < elementCount; i += 1)
                            {
                                this.WriteBooleanValue(item.GetU8(i));
                            }
                            return;
                        case EventHeaderFieldFormat.HexBytes:
                            for (int i = 0; i < elementCount; i += 1)
                            {
                                writer.WriteStringValue(PerfConvert.HexBytesFormat(charBuf, item.GetSpan8(i)));
                            }
                            return;
                        case EventHeaderFieldFormat.String8:
                            var chars1 = charBuf.Slice(0, 1);
                            for (int i = 0; i < elementCount; i += 1)
                            {
                                chars1[0] = (char)item.GetU8(i);
                                writer.WriteStringValue(chars1);
                            }
                            return;
                    }
                case EventHeaderFieldEncoding.Value16:
                    switch (item.Format)
                    {
                        default:
                        case EventHeaderFieldFormat.UnsignedInt:
                            for (int i = 0; i < elementCount; i += 1)
                            {
                                writer.WriteNumberValue(item.GetU16(i));
                            }
                            return;
                        case EventHeaderFieldFormat.SignedInt:
                            for (int i = 0; i < elementCount; i += 1)
                            {
                                writer.WriteNumberValue(item.GetI16(i));
                            }
                            return;
                        case EventHeaderFieldFormat.HexInt:
                            for (int i = 0; i < elementCount; i += 1)
                            {
                                writer.WriteStringValue(PerfConvert.HexU32FormatAtEnd(charBuf, item.GetU16(i)));
                            }
                            return;
                        case EventHeaderFieldFormat.Boolean:
                            for (int i = 0; i < elementCount; i += 1)
                            {
                                this.WriteBooleanValue(item.GetU16(i));
                            }
                            return;
                        case EventHeaderFieldFormat.HexBytes:
                            for (int i = 0; i < elementCount; i += 1)
                            {
                                writer.WriteStringValue(PerfConvert.HexBytesFormat(charBuf, item.GetSpan16(i)));
                            }
                            return;
                        case EventHeaderFieldFormat.StringUtf:
                            var chars1 = charBuf.Slice(0, 1);
                            for (int i = 0; i < elementCount; i += 1)
                            {
                                chars1[0] = (char)item.GetU16(i);
                                writer.WriteStringValue(chars1);
                            }
                            return;
                        case EventHeaderFieldFormat.Port:
                            for (int i = 0; i < elementCount; i += 1)
                            {
                                writer.WriteNumberValue(item.GetPort(i));
                            }
                            return;
                    }
                case EventHeaderFieldEncoding.Value32:
                    switch (item.Format)
                    {
                        default:
                        case EventHeaderFieldFormat.UnsignedInt:
                            for (int i = 0; i < elementCount; i += 1)
                            {
                                writer.WriteNumberValue(item.GetU32(i));
                            }
                            return;
                        case EventHeaderFieldFormat.SignedInt:
                        case EventHeaderFieldFormat.Pid:
                            for (int i = 0; i < elementCount; i += 1)
                            {
                                writer.WriteNumberValue(item.GetI32(i));
                            }
                            return;
                        case EventHeaderFieldFormat.HexInt:
                            for (int i = 0; i < elementCount; i += 1)
                            {
                                writer.WriteStringValue(PerfConvert.HexU32FormatAtEnd(charBuf, item.GetU32(i)));
                            }
                            return;
                        case EventHeaderFieldFormat.Errno:
                            for (int i = 0; i < elementCount; i += 1)
                            {
                                this.WriteErrnoValue(item.GetI32(i));
                            }
                            return;
                        case EventHeaderFieldFormat.Time:
                            for (int i = 0; i < elementCount; i += 1)
                            {
                                writer.WriteStringValue(PerfConvert.UnixTime32ToDateTime(item.GetI32(i)));
                            }
                            return;
                        case EventHeaderFieldFormat.Boolean:
                            for (int i = 0; i < elementCount; i += 1)
                            {
                                this.WriteBooleanValue(item.GetU32(i));
                            }
                            return;
                        case EventHeaderFieldFormat.Float:
                            for (int i = 0; i < elementCount; i += 1)
                            {
                                this.WriteFloat32Value(charBuf, item.GetF32(i));
                            }
                            return;
                        case EventHeaderFieldFormat.HexBytes:
                            for (int i = 0; i < elementCount; i += 1)
                            {
                                writer.WriteStringValue(PerfConvert.HexBytesFormat(charBuf, item.GetSpan32(i)));
                            }
                            return;
                        case EventHeaderFieldFormat.StringUtf:
                            for (int i = 0; i < elementCount; i += 1)
                            {
                                writer.WriteStringValue(PerfConvert.Utf32Format(charBuf, item.GetU32(i)));
                            }
                            return;
                        case EventHeaderFieldFormat.IPv4:
                            for (int i = 0; i < elementCount; i += 1)
                            {
                                writer.WriteStringValue(PerfConvert.IPv4Format(charBuf, item.GetIPv4(i)));
                            }
                            return;
                    }
                case EventHeaderFieldEncoding.Value64:
                    switch (item.Format)
                    {
                        default:
                        case EventHeaderFieldFormat.UnsignedInt:
                            for (int i = 0; i < elementCount; i += 1)
                            {
                                writer.WriteNumberValue(item.GetU64(i));
                            }
                            return;
                        case EventHeaderFieldFormat.SignedInt:
                            for (int i = 0; i < elementCount; i += 1)
                            {
                                writer.WriteNumberValue(item.GetI64(i));
                            }
                            return;
                        case EventHeaderFieldFormat.HexInt:
                            for (int i = 0; i < elementCount; i += 1)
                            {
                                writer.WriteStringValue(PerfConvert.HexU64FormatAtEnd(charBuf, item.GetU64(i)));
                            }
                            return;
                        case EventHeaderFieldFormat.Time:
                            for (int i = 0; i < elementCount; i += 1)
                            {
                                this.WriteUnixTime64Value(item.GetI64(i));
                            }
                            return;
                        case EventHeaderFieldFormat.Float:
                            for (int i = 0; i < elementCount; i += 1)
                            {
                                this.WriteFloat64Value(charBuf, item.GetF64(i));
                            }
                            return;
                        case EventHeaderFieldFormat.HexBytes:
                            for (int i = 0; i < elementCount; i += 1)
                            {
                                writer.WriteStringValue(PerfConvert.HexBytesFormat(charBuf, item.GetSpan64(i)));
                            }
                            return;
                    }
                case EventHeaderFieldEncoding.Value128:
                    switch (item.Format)
                    {
                        default:
                        case EventHeaderFieldFormat.HexBytes:
                            for (int i = 0; i < elementCount; i += 1)
                            {
                                writer.WriteStringValue(PerfConvert.HexBytesFormat(charBuf, item.GetSpan128(i)));
                            }
                            return;
                        case EventHeaderFieldFormat.Uuid:
                            for (int i = 0; i < elementCount; i += 1)
                            {
                                writer.WriteStringValue(item.GetGuid(i));
                            }
                            return;
                        case EventHeaderFieldFormat.IPv6:
                            for (int i = 0; i < elementCount; i += 1)
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
