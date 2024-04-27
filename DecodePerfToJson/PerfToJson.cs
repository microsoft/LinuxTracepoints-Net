// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace DecodePerfToJson
{
    using Microsoft.LinuxTracepoints;
    using Microsoft.LinuxTracepoints.Decode;
    using System;
    using System.Buffers;
    using System.Text.Json;
    using Debug = System.Diagnostics.Debug;
    using Encoding = System.Text.Encoding;
    using MemoryMarshal = System.Runtime.InteropServices.MemoryMarshal;
    using Stream = System.IO.Stream;

    internal sealed class DecodePerfJsonWriter : IDisposable
    {
        private readonly PerfDataFileReader reader = new PerfDataFileReader();
        private readonly EventHeaderEnumerator enumerator = new EventHeaderEnumerator();

        // Scratch buffer for formatting strings.
        private readonly ArrayBufferWriter<char> charBufStorage = new ArrayBufferWriter<char>();

        public DecodePerfJsonWriter(
            IBufferWriter<byte> utf8JsonBufferWriter,
            JsonWriterOptions writerOptions = default)
        {
            this.JsonWriter = new Utf8JsonWriter(utf8JsonBufferWriter, writerOptions);
            this.InfoOptions = PerfInfoOptions.Default;
            this.JsonOptions = PerfConvertOptions.Default;
        }

        public DecodePerfJsonWriter(
            Stream utf8JsonStream,
            JsonWriterOptions writerOptions = default)
        {
            this.JsonWriter = new Utf8JsonWriter(utf8JsonStream, writerOptions);
            this.InfoOptions = PerfInfoOptions.Default;
            this.JsonOptions = PerfConvertOptions.Default;
        }

        public Utf8JsonWriter JsonWriter { get; }

        public PerfInfoOptions InfoOptions { get; set; }

        /// <summary>
        /// Respected options:
        /// FieldTag,
        /// FloatNonFiniteAsString,
        /// IntHexAsString,
        /// BoolOutOfRangeAsString,
        /// UnixTimeWithinRangeAsString,
        /// UnixTimeOutOfRangeAsString,
        /// ErrnoKnownAsString,
        /// ErrnoUnknownAsString.
        /// </summary>
        public PerfConvertOptions JsonOptions { get; set; }

        public bool ShowNonSample { get; set; }

        public void WriteFile(string fileName, PerfDataFileEventOrder eventOrder)
        {
            this.reader.OpenFile(fileName, eventOrder);
            this.WriteFromReader();
        }

        public void WriteFile(Stream stream, PerfDataFileEventOrder eventOrder, bool leaveOpen = false)
        {
            this.reader.OpenStream(stream, eventOrder, leaveOpen);
            this.WriteFromReader();
        }

        public void Dispose()
        {
            this.reader.Dispose();
            this.JsonWriter.Dispose();
        }

        private void WriteFromReader()
        {
            var byteReader = this.reader.ByteReader;
            PerfDataFileResult result;
            PerfEventBytes eventBytes;

            // We assume charBuf is large enough for all fixed-length fields.
            // The long ones are Value128.HexBytes (needs 47) and Value128.IPv6 (needs 45).
            var charBuf = this.charBufStorage.GetSpan(Math.Max(this.charBufStorage.FreeCapacity, 64));
            Debug.Assert(this.charBufStorage.WrittenCount == 0);

            while (true)
            {
                result = this.reader.ReadEvent(out eventBytes);
                if (result != PerfDataFileResult.Ok)
                {
                    if (result != PerfDataFileResult.EndOfFile)
                    {
                        this.JsonWriter.WriteStartObject();
                        this.JsonWriter.WriteString("ReadEvent", result.AsString());
                        this.JsonWriter.WriteEndObject();
                    }
                    break; // No more events.
                }

                if (!this.ShowNonSample &&
                    eventBytes.Header.Type != PerfEventHeaderType.Sample)
                {
                    continue; // Skip non-sample events.
                }

                this.JsonWriter.WriteStartObject(); // event

                if (eventBytes.Header.Type != PerfEventHeaderType.Sample)
                {
                    this.JsonWriter.WriteString("NonSample", eventBytes.Header.Type.AsString());

                    PerfNonSampleEventInfo nonSampleEventInfo;
                    result = this.reader.GetNonSampleEventInfo(eventBytes, out nonSampleEventInfo);
                    if (result != PerfDataFileResult.Ok &&
                        result != PerfDataFileResult.IdNotFound)
                    {
                        this.JsonWriter.WriteString("GetNonSampleEventInfo", result.AsString());
                    }

                    this.JsonWriter.WriteNumber("size", eventBytes.Memory.Length);
                    if (result == PerfDataFileResult.Ok)
                    {
                        WriteNonSampleMeta(nonSampleEventInfo);
                    }
                }
                else
                {
                    PerfSampleEventInfo sampleEventInfo;
                    result = this.reader.GetSampleEventInfo(eventBytes, out sampleEventInfo);
                    if (result != PerfDataFileResult.Ok)
                    {
                        // Unable to lookup attributes for event. Unexpected.
                        if (this.InfoOptions.HasFlag(PerfInfoOptions.N))
                        {
                            this.JsonWriter.WriteNull("n");
                        }

                        this.JsonWriter.WriteString("GetSampleEventInfo", result.AsString());
                        this.JsonWriter.WriteNumber("size", eventBytes.Memory.Length);
                    }
                    else if (sampleEventInfo.Format.IsEmpty)
                    {
                        // No TraceFS format for this event. Unexpected.
                        if (this.InfoOptions.HasFlag(PerfInfoOptions.N))
                        {
                            this.JsonWriter.WriteString("n", sampleEventInfo.Name);
                        }

                        this.JsonWriter.WriteString("GetSampleEventInfo", "NoFormat");
                        this.JsonWriter.WriteNumber("size", eventBytes.Memory.Length);

                        if (0 != (this.InfoOptions & ~PerfInfoOptions.N))
                        {
                            this.JsonWriter.WriteStartObject("info");
                            WriteSampleMeta(sampleEventInfo, true);
                            this.JsonWriter.WriteEndObject(); // info
                        }
                    }
                    else if (sampleEventInfo.Format.DecodingStyle != PerfEventDecodingStyle.EventHeader ||
                        !this.enumerator.StartEvent(sampleEventInfo))
                    {
                        // Non-EventHeader decoding.
                        if (this.InfoOptions.HasFlag(PerfInfoOptions.N))
                        {
                            this.JsonWriter.WriteString("n", sampleEventInfo.Name);
                        }

                        // Write the event fields. Skip the common fields by default.
                        var infoFormat = sampleEventInfo.Format;
                        var firstField = this.InfoOptions.HasFlag(PerfInfoOptions.Common) ? 0 : infoFormat.CommonFieldCount;
                        for (int i = firstField; i < infoFormat.Fields.Count; i++)
                        {
                            this.WriteField(sampleEventInfo, i, ref charBuf);
                        }

                        if (0 != (this.InfoOptions & ~PerfInfoOptions.N))
                        {
                            this.JsonWriter.WriteStartObject("info");
                            WriteSampleMeta(sampleEventInfo, true);
                            this.JsonWriter.WriteEndObject(); // info
                        }
                    }
                    else
                    {
                        // EventHeader decoding.

                        var infoFormat = sampleEventInfo.Format;
                        var ei = this.enumerator.GetEventInfo();

                        if (this.InfoOptions.HasFlag(PerfInfoOptions.N))
                        {
                            var systemName = infoFormat.SystemName;
                            var providerName = ei.ProviderName;
                            var eventNameBytes = ei.NameBytes;

                            var maxChars =
                                providerName.Length + 1 +
                                Encoding.UTF8.GetMaxCharCount(eventNameBytes.Length);

                            var pos = 0;
                            if (systemName == "user_events")
                            {
                                this.EnsureSpan(maxChars, ref charBuf);
                            }
                            else
                            {
                                this.EnsureSpan(maxChars + systemName.Length + 1, ref charBuf);
                                systemName.AsSpan().CopyTo(charBuf.Slice(pos));
                                pos += systemName.Length;
                                charBuf[pos++] = ':';
                            }

                            providerName.CopyTo(charBuf.Slice(pos));
                            pos += providerName.Length;
                            charBuf[pos++] = ':';
                            pos += Encoding.UTF8.GetChars(eventNameBytes, charBuf.Slice(pos));

                            this.JsonWriter.WriteString("n", charBuf.Slice(0, pos));
                        }

                        if (this.InfoOptions.HasFlag(PerfInfoOptions.Common))
                        {
                            for (var i = 0; i < infoFormat.CommonFieldCount; i++)
                            {
                                this.WriteField(sampleEventInfo, i, ref charBuf);
                            }
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
                                            this.JsonWriter.WritePropertyName(MakeName(item, ref charBuf));
                                        }
                                        this.WriteValue(item.Value, ref charBuf);
                                        break;
                                    case EventHeaderEnumeratorState.StructBegin:
                                        if (!item.Value.IsArrayOrElement)
                                        {
                                            this.JsonWriter.WritePropertyName(MakeName(item, ref charBuf));
                                        }
                                        this.JsonWriter.WriteStartObject();
                                        break;
                                    case EventHeaderEnumeratorState.StructEnd:
                                        this.JsonWriter.WriteEndObject();
                                        break;
                                    case EventHeaderEnumeratorState.ArrayBegin:
                                        this.JsonWriter.WritePropertyName(MakeName(item, ref charBuf));
                                        this.JsonWriter.WriteStartArray();
                                        if (item.Value.TypeSize != 0)
                                        {
                                            // Process the simple array directly without using the enumerator.
                                            WriteSimpleArrayValues(item.Value, ref charBuf);
                                            this.JsonWriter.WriteEndArray();

                                            // Skip the entire array at once.
                                            if (!this.enumerator.MoveNextSibling()) // Instead of MoveNext().
                                            {
                                                goto EventDone; // End of event, or error.
                                            }

                                            continue; // Skip the MoveNext().
                                        }
                                        break;
                                    case EventHeaderEnumeratorState.ArrayEnd:
                                        this.JsonWriter.WriteEndArray();
                                        break;
                                }

                                if (!this.enumerator.MoveNext())
                                {
                                    goto EventDone; // End of event, or error.
                                }
                            }
                        }

                    EventDone:

                        if (0 != (this.InfoOptions & ~PerfInfoOptions.N))
                        {
                            this.JsonWriter.WriteStartObject("info");

                            WriteSampleMeta(sampleEventInfo, false);

                            // Same as enumerator.AppendJsonEventInfoTo, but with a Utf8JsonWriter.

                            if (this.InfoOptions.HasFlag(PerfInfoOptions.Provider))
                            {
                                this.JsonWriter.WriteString("provider", ei.ProviderName);
                            }

                            if (this.InfoOptions.HasFlag(PerfInfoOptions.Event))
                            {
                                this.JsonWriter.WriteString("event", ei.NameBytes);
                            }

                            if (this.InfoOptions.HasFlag(PerfInfoOptions.Id) && ei.Header.Id != 0)
                            {
                                this.JsonWriter.WriteNumber("id", ei.Header.Id);
                            }

                            if (this.InfoOptions.HasFlag(PerfInfoOptions.Version) && ei.Header.Version != 0)
                            {
                                this.JsonWriter.WriteNumber("version", ei.Header.Version);
                            }

                            if (this.InfoOptions.HasFlag(PerfInfoOptions.Level) && ei.Header.Level != 0)
                            {
                                this.JsonWriter.WriteNumber("level", (byte)ei.Header.Level);
                            }

                            if (this.InfoOptions.HasFlag(PerfInfoOptions.Keyword) && ei.Keyword != 0)
                            {
                                this.JsonWriter.WritePropertyName("keyword");
                                this.WriteHex64Value(charBuf, ei.Keyword);
                            }

                            if (this.InfoOptions.HasFlag(PerfInfoOptions.Opcode) && ei.Header.Opcode != 0)
                            {
                                this.JsonWriter.WriteNumber("opcode", (byte)ei.Header.Opcode);
                            }

                            if (this.InfoOptions.HasFlag(PerfInfoOptions.Tag) && ei.Header.Tag != 0)
                            {
                                this.JsonWriter.WritePropertyName("tag");
                                this.WriteHex32Value(charBuf, ei.Header.Tag);
                            }

                            if (this.InfoOptions.HasFlag(PerfInfoOptions.Activity) && ei.ActivityId is Guid aid)
                            {
                                this.JsonWriter.WriteString("activity", aid);
                            }

                            if (this.InfoOptions.HasFlag(PerfInfoOptions.RelatedActivity) && ei.RelatedActivityId is Guid rid)
                            {
                                this.JsonWriter.WriteString("relatedActivity", rid);
                            }

                            if (this.InfoOptions.HasFlag(PerfInfoOptions.Options) && !ei.Options.IsEmpty)
                            {
                                this.JsonWriter.WriteString("options", ei.Options);
                            }

                            if (this.InfoOptions.HasFlag(PerfInfoOptions.Flags) && ei.Header.Flags != 0)
                            {
                                this.JsonWriter.WritePropertyName("flags");
                                this.WriteHex32Value(charBuf, (uint)ei.Header.Flags);
                            }

                            this.JsonWriter.WriteEndObject(); // info
                        }
                    }
                }

                this.JsonWriter.WriteEndObject(); // event
            }
        }

        private void WriteField(PerfSampleEventInfo sampleEventInfo, int i, ref Span<char> charBuf)
        {
            var fieldFormat = sampleEventInfo.Format.Fields[i];
            var fieldValue = fieldFormat.GetFieldValue(sampleEventInfo.RawDataSpan, sampleEventInfo.ByteReader);
            if (!fieldValue.IsArrayOrElement)
            {
                this.JsonWriter.WritePropertyName(fieldFormat.Name);
                this.WriteValue(fieldValue, ref charBuf);
            }
            else
            {
                this.JsonWriter.WriteStartArray(fieldFormat.Name);
                WriteSimpleArrayValues(fieldValue, ref charBuf);
                this.JsonWriter.WriteEndArray();
            }
        }

        /// <summary>
        /// Same as nonSampleEventInfo.AppendJsonEventInfoTo, but with a Utf8JsonWriter
        /// instead of a StringBuilder.
        /// </summary>
        private void WriteSampleMeta(in PerfSampleEventInfo info, bool showProviderEvent)
        {
            this.WriteCommonMetadata(
                info.SampleType,
                info.SessionInfo,
                info.Time,
                info.Cpu,
                info.Pid,
                info.Tid,
                showProviderEvent ? info.Name : null);
        }

        /// <summary>
        /// Same as nonSampleEventInfo.AppendJsonEventInfoTo, but with a Utf8JsonWriter
        /// instead of a StringBuilder.
        /// </summary>
        private void WriteNonSampleMeta(in PerfNonSampleEventInfo info)
        {
            this.WriteCommonMetadata(
                info.SampleType,
                info.SessionInfo,
                info.Time,
                info.Cpu,
                info.Pid,
                info.Tid,
                info.Name);
        }

        /// <summary>
        /// Same as nonSampleEventInfo.AppendJsonEventInfoTo, but with a Utf8JsonWriter
        /// instead of a StringBuilder.
        /// </summary>
        private void WriteCommonMetadata(
            PerfEventAttrSampleType sampleType,
            PerfSessionInfo sessionInfo,
            ulong time,
            uint cpu,
            uint pid,
            uint tid,
            string? name)
        {
            if (sampleType.HasFlag(PerfEventAttrSampleType.Time) &&
                this.InfoOptions.HasFlag(PerfInfoOptions.Time))
            {
                if (sessionInfo.ClockOffsetKnown && sessionInfo.TimeToTimeSpec(time).DateTime is DateTime dt)
                {
                    this.JsonWriter.WriteString("time", dt);
                }
                else
                {
                    this.JsonWriter.WriteNumber("time", time / 1000000000.0);
                }
            }

            if (sampleType.HasFlag(PerfEventAttrSampleType.Cpu) &&
                this.InfoOptions.HasFlag(PerfInfoOptions.Cpu))
            {
                this.JsonWriter.WriteNumber("cpu", cpu);
            }

            if (sampleType.HasFlag(PerfEventAttrSampleType.Tid))
            {
                if (this.InfoOptions.HasFlag(PerfInfoOptions.Pid))
                {
                    this.JsonWriter.WriteNumber("pid", pid);
                }

                if (this.InfoOptions.HasFlag(PerfInfoOptions.Tid) &&
                    (pid != tid || !this.InfoOptions.HasFlag(PerfInfoOptions.Pid)))
                {
                    this.JsonWriter.WriteNumber("tid", tid);
                }
            }

            if (0 != (this.InfoOptions & (PerfInfoOptions.Provider | PerfInfoOptions.Event)) &&
                !string.IsNullOrEmpty(name))
            {
                var nameSpan = name.AsSpan();
                var colonPos = nameSpan.IndexOf(':');
                ReadOnlySpan<char> providerName, eventName;
                if (colonPos < 0)
                {
                    providerName = default;
                    eventName = nameSpan;
                }
                else
                {
                    providerName = nameSpan.Slice(0, colonPos);
                    eventName = nameSpan.Slice(colonPos + 1);
                }

                if (this.InfoOptions.HasFlag(PerfInfoOptions.Provider) &&
                    !providerName.IsEmpty)
                {
                    this.JsonWriter.WriteString("provider", providerName);
                }

                if (this.InfoOptions.HasFlag(PerfInfoOptions.Event) &&
                    !eventName.IsEmpty)
                {
                    this.JsonWriter.WriteString("event", eventName);
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
                    this.JsonWriter.WriteNullValue();
                    return;
                case EventHeaderFieldEncoding.Struct:
                    throw new InvalidOperationException("Invalid encoding for FormatScalar.");
                case EventHeaderFieldEncoding.Value8:
                    switch (item.Format)
                    {
                        default:
                        case EventHeaderFieldFormat.UnsignedInt:
                            this.JsonWriter.WriteNumberValue(item.GetU8());
                            return;
                        case EventHeaderFieldFormat.SignedInt:
                            this.JsonWriter.WriteNumberValue(item.GetI8());
                            return;
                        case EventHeaderFieldFormat.HexInt:
                            this.WriteHex32Value(charBuf, item.GetU8());
                            return;
                        case EventHeaderFieldFormat.Boolean:
                            this.WriteBooleanValue(charBuf, item.GetU8());
                            return;
                        case EventHeaderFieldFormat.HexBytes:
                            this.JsonWriter.WriteStringValue(PerfConvert.HexBytesFormat(charBuf, item.GetSpan8()));
                            return;
                        case EventHeaderFieldFormat.String8:
                            charBuf[0] = (char)item.GetU8();
                            this.JsonWriter.WriteStringValue(charBuf.Slice(0, 1));
                            return;
                    }
                case EventHeaderFieldEncoding.Value16:
                    switch (item.Format)
                    {
                        default:
                        case EventHeaderFieldFormat.UnsignedInt:
                            this.JsonWriter.WriteNumberValue(item.GetU16());
                            return;
                        case EventHeaderFieldFormat.SignedInt:
                            this.JsonWriter.WriteNumberValue(item.GetI16());
                            return;
                        case EventHeaderFieldFormat.HexInt:
                            this.WriteHex32Value(charBuf, item.GetU16());
                            return;
                        case EventHeaderFieldFormat.Boolean:
                            this.WriteBooleanValue(charBuf, item.GetU16());
                            return;
                        case EventHeaderFieldFormat.HexBytes:
                            this.JsonWriter.WriteStringValue(PerfConvert.HexBytesFormat(charBuf, item.GetSpan16()));
                            return;
                        case EventHeaderFieldFormat.StringUtf:
                            charBuf[0] = (char)item.GetU16();
                            this.JsonWriter.WriteStringValue(charBuf.Slice(0, 1));
                            return;
                        case EventHeaderFieldFormat.Port:
                            this.JsonWriter.WriteNumberValue(item.GetPort());
                            return;
                    }
                case EventHeaderFieldEncoding.Value32:
                    switch (item.Format)
                    {
                        default:
                        case EventHeaderFieldFormat.UnsignedInt:
                            this.JsonWriter.WriteNumberValue(item.GetU32());
                            return;
                        case EventHeaderFieldFormat.SignedInt:
                        case EventHeaderFieldFormat.Pid:
                            this.JsonWriter.WriteNumberValue(item.GetI32());
                            return;
                        case EventHeaderFieldFormat.HexInt:
                            this.WriteHex32Value(charBuf, item.GetU32());
                            return;
                        case EventHeaderFieldFormat.Errno:
                            this.WriteErrnoValue(charBuf, item.GetI32());
                            return;
                        case EventHeaderFieldFormat.Time:
                            this.WriteUnixTime32Value(item.GetI32());
                            return;
                        case EventHeaderFieldFormat.Boolean:
                            this.WriteBooleanValue(charBuf, item.GetU32());
                            return;
                        case EventHeaderFieldFormat.Float:
                            this.WriteFloat32Value(charBuf, item.GetF32());
                            return;
                        case EventHeaderFieldFormat.HexBytes:
                            this.JsonWriter.WriteStringValue(PerfConvert.HexBytesFormat(charBuf, item.GetSpan32()));
                            return;
                        case EventHeaderFieldFormat.StringUtf:
                            this.JsonWriter.WriteStringValue(PerfConvert.Char32Format(charBuf, item.GetU32()));
                            return;
                        case EventHeaderFieldFormat.IPv4:
                            this.JsonWriter.WriteStringValue(PerfConvert.IPv4Format(charBuf, item.GetIPv4()));
                            return;
                    }
                case EventHeaderFieldEncoding.Value64:
                    switch (item.Format)
                    {
                        default:
                        case EventHeaderFieldFormat.UnsignedInt:
                            this.JsonWriter.WriteNumberValue(item.GetU64());
                            return;
                        case EventHeaderFieldFormat.SignedInt:
                            this.JsonWriter.WriteNumberValue(item.GetI64());
                            return;
                        case EventHeaderFieldFormat.HexInt:
                            this.WriteHex64Value(charBuf, item.GetU64());
                            return;
                        case EventHeaderFieldFormat.Time:
                            this.WriteUnixTime64Value(charBuf, item.GetI64());
                            return;
                        case EventHeaderFieldFormat.Float:
                            this.WriteFloat64Value(charBuf, item.GetF64());
                            return;
                        case EventHeaderFieldFormat.HexBytes:
                            this.JsonWriter.WriteStringValue(PerfConvert.HexBytesFormat(charBuf, item.GetSpan64()));
                            return;
                    }
                case EventHeaderFieldEncoding.Value128:
                    switch (item.Format)
                    {
                        default:
                        case EventHeaderFieldFormat.HexBytes:
                            this.JsonWriter.WriteStringValue(PerfConvert.HexBytesFormat(charBuf, item.GetSpan128()));
                            return;
                        case EventHeaderFieldFormat.Uuid:
                            this.JsonWriter.WriteStringValue(item.GetGuid());
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
                            this.JsonWriter.WriteStringValue(item.Bytes); // UTF-8
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
                                this.JsonWriter.WriteStringValue(MemoryMarshal.Cast<byte, char>(item.Bytes));
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
        /// Calls this.JsonWriter.WriteValue(...) for each element in the array.
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
                                this.JsonWriter.WriteNumberValue(item.GetU8(i));
                            }
                            return;
                        case EventHeaderFieldFormat.SignedInt:
                            for (int i = 0; i < elementCount; i += 1)
                            {
                                this.JsonWriter.WriteNumberValue(item.GetI8(i));
                            }
                            return;
                        case EventHeaderFieldFormat.HexInt:
                            for (int i = 0; i < elementCount; i += 1)
                            {
                                this.WriteHex32Value(charBuf, item.GetU8(i));
                            }
                            return;
                        case EventHeaderFieldFormat.Boolean:
                            for (int i = 0; i < elementCount; i += 1)
                            {
                                this.WriteBooleanValue(charBuf, item.GetU8(i));
                            }
                            return;
                        case EventHeaderFieldFormat.HexBytes:
                            for (int i = 0; i < elementCount; i += 1)
                            {
                                this.JsonWriter.WriteStringValue(PerfConvert.HexBytesFormat(charBuf, item.GetSpan8(i)));
                            }
                            return;
                        case EventHeaderFieldFormat.String8:
                            var chars1 = charBuf.Slice(0, 1);
                            for (int i = 0; i < elementCount; i += 1)
                            {
                                chars1[0] = (char)item.GetU8(i);
                                this.JsonWriter.WriteStringValue(chars1);
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
                                this.JsonWriter.WriteNumberValue(item.GetU16(i));
                            }
                            return;
                        case EventHeaderFieldFormat.SignedInt:
                            for (int i = 0; i < elementCount; i += 1)
                            {
                                this.JsonWriter.WriteNumberValue(item.GetI16(i));
                            }
                            return;
                        case EventHeaderFieldFormat.HexInt:
                            for (int i = 0; i < elementCount; i += 1)
                            {
                                this.WriteHex32Value(charBuf, item.GetU16(i));
                            }
                            return;
                        case EventHeaderFieldFormat.Boolean:
                            for (int i = 0; i < elementCount; i += 1)
                            {
                                this.WriteBooleanValue(charBuf, item.GetU16(i));
                            }
                            return;
                        case EventHeaderFieldFormat.HexBytes:
                            for (int i = 0; i < elementCount; i += 1)
                            {
                                this.JsonWriter.WriteStringValue(PerfConvert.HexBytesFormat(charBuf, item.GetSpan16(i)));
                            }
                            return;
                        case EventHeaderFieldFormat.StringUtf:
                            var chars1 = charBuf.Slice(0, 1);
                            for (int i = 0; i < elementCount; i += 1)
                            {
                                chars1[0] = (char)item.GetU16(i);
                                this.JsonWriter.WriteStringValue(chars1);
                            }
                            return;
                        case EventHeaderFieldFormat.Port:
                            for (int i = 0; i < elementCount; i += 1)
                            {
                                this.JsonWriter.WriteNumberValue(item.GetPort(i));
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
                                this.JsonWriter.WriteNumberValue(item.GetU32(i));
                            }
                            return;
                        case EventHeaderFieldFormat.SignedInt:
                        case EventHeaderFieldFormat.Pid:
                            for (int i = 0; i < elementCount; i += 1)
                            {
                                this.JsonWriter.WriteNumberValue(item.GetI32(i));
                            }
                            return;
                        case EventHeaderFieldFormat.HexInt:
                            for (int i = 0; i < elementCount; i += 1)
                            {
                                this.WriteHex32Value(charBuf, item.GetU32(i));
                            }
                            return;
                        case EventHeaderFieldFormat.Errno:
                            for (int i = 0; i < elementCount; i += 1)
                            {
                                this.WriteErrnoValue(charBuf, item.GetI32(i));
                            }
                            return;
                        case EventHeaderFieldFormat.Time:
                            for (int i = 0; i < elementCount; i += 1)
                            {
                                this.WriteUnixTime32Value(item.GetI32(i));
                            }
                            return;
                        case EventHeaderFieldFormat.Boolean:
                            for (int i = 0; i < elementCount; i += 1)
                            {
                                this.WriteBooleanValue(charBuf, item.GetU32(i));
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
                                this.JsonWriter.WriteStringValue(PerfConvert.HexBytesFormat(charBuf, item.GetSpan32(i)));
                            }
                            return;
                        case EventHeaderFieldFormat.StringUtf:
                            for (int i = 0; i < elementCount; i += 1)
                            {
                                this.JsonWriter.WriteStringValue(PerfConvert.Char32Format(charBuf, item.GetU32(i)));
                            }
                            return;
                        case EventHeaderFieldFormat.IPv4:
                            for (int i = 0; i < elementCount; i += 1)
                            {
                                this.JsonWriter.WriteStringValue(PerfConvert.IPv4Format(charBuf, item.GetIPv4(i)));
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
                                this.JsonWriter.WriteNumberValue(item.GetU64(i));
                            }
                            return;
                        case EventHeaderFieldFormat.SignedInt:
                            for (int i = 0; i < elementCount; i += 1)
                            {
                                this.JsonWriter.WriteNumberValue(item.GetI64(i));
                            }
                            return;
                        case EventHeaderFieldFormat.HexInt:
                            for (int i = 0; i < elementCount; i += 1)
                            {
                                this.WriteHex64Value(charBuf, item.GetU64(i));
                            }
                            return;
                        case EventHeaderFieldFormat.Time:
                            for (int i = 0; i < elementCount; i += 1)
                            {
                                this.WriteUnixTime64Value(charBuf, item.GetI64(i));
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
                                this.JsonWriter.WriteStringValue(PerfConvert.HexBytesFormat(charBuf, item.GetSpan64(i)));
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
                                this.JsonWriter.WriteStringValue(PerfConvert.HexBytesFormat(charBuf, item.GetSpan128(i)));
                            }
                            return;
                        case EventHeaderFieldFormat.Uuid:
                            for (int i = 0; i < elementCount; i += 1)
                            {
                                this.JsonWriter.WriteStringValue(item.GetGuid(i));
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
                    this.JsonWriter.WriteStringValue(bytes.Slice(3));
                    break;
                case 1200: // UTF-16LE
                    if (BitConverter.IsLittleEndian)
                    {
                        this.JsonWriter.WriteStringValue(MemoryMarshal.Cast<byte, char>(bytes.Slice(2)));
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
                        this.JsonWriter.WriteStringValue(MemoryMarshal.Cast<byte, char>(bytes.Slice(2)));
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
            this.JsonWriter.WriteStringValue(charBuf.Slice(0, charCount));
        }

        private void WriteHexBytesValue(ReadOnlySpan<byte> bytes, ref Span<char> charBuf)
        {
            EnsureSpan(PerfConvert.HexBytesLength(bytes.Length), ref charBuf);
            this.JsonWriter.WriteStringValue(PerfConvert.HexBytesFormat(charBuf, bytes));
        }

        private void WriteFloat32Value(Span<char> charBuf, float value)
        {
            if (float.IsFinite(value))
            {
                this.JsonWriter.WriteNumberValue(value);
            }
            else
            {
                // Write Infinity, -Infinity, or NaN as a string.
                this.JsonWriter.WriteStringValue(PerfConvert.Float32Format(charBuf, value));
            }
        }

        private void WriteFloat64Value(Span<char> charBuf, double value)
        {
            if (double.IsFinite(value))
            {
                this.JsonWriter.WriteNumberValue(value);
            }
            else if (!this.JsonOptions.HasFlag(PerfConvertOptions.FloatNonFiniteAsString))
            {
                this.JsonWriter.WriteNullValue();
            }
            else
            {
                this.JsonWriter.WriteStringValue(PerfConvert.Float64Format(charBuf, value));
            }
        }

        private void WriteHex32Value(Span<char> charBuf, UInt32 value)
        {
            if (this.JsonOptions.HasFlag(PerfConvertOptions.IntHexAsString))
            {
                this.JsonWriter.WriteStringValue(PerfConvert.UInt32HexFormatAtEnd(charBuf, value));
            }
            else
            {
                this.JsonWriter.WriteNumberValue(value);
            }
        }

        private void WriteHex64Value(Span<char> charBuf, UInt64 value)
        {
            if (this.JsonOptions.HasFlag(PerfConvertOptions.IntHexAsString))
            {
                this.JsonWriter.WriteStringValue(PerfConvert.UInt64HexFormatAtEnd(charBuf, value));
            }
            else
            {
                this.JsonWriter.WriteNumberValue(value);
            }
        }

        private void WriteIPv6Value(Span<char> charBuf, ReadOnlySpan<byte> value)
        {
            this.JsonWriter.WriteStringValue(PerfConvert.IPv6Format(charBuf, value)); // Garbage.
        }

        private void WriteUnixTime32Value(Int32 value)
        {
            if (this.JsonOptions.HasFlag(PerfConvertOptions.UnixTimeWithinRangeAsString))
            {
                this.JsonWriter.WriteStringValue(PerfConvert.UnixTime32ToDateTime(value));
                return;
            }

            this.JsonWriter.WriteNumberValue(value);
        }

        private void WriteUnixTime64Value(Span<char> charBuf, Int64 value)
        {
            if (PerfConvert.UnixTime64IsInDateTimeRange(value))
            {
                if (this.JsonOptions.HasFlag(PerfConvertOptions.UnixTimeWithinRangeAsString))
                {
                    this.JsonWriter.WriteStringValue(PerfConvert.UnixTime64ToDateTimeUnchecked(value));
                    return;
                }
            }
            else
            {
                if (this.JsonOptions.HasFlag(PerfConvertOptions.UnixTimeOutOfRangeAsString))
                {
                    this.JsonWriter.WriteStringValue(PerfConvert.UnixTime64Format(charBuf, value));
                    return;
                }
            }

            this.JsonWriter.WriteNumberValue(value);
        }

        private void WriteErrnoValue(Span<char> charBuf, int value)
        {
            if (PerfConvert.ErrnoIsKnown(value))
            {
                if (this.JsonOptions.HasFlag(PerfConvertOptions.ErrnoKnownAsString))
                {
                    this.JsonWriter.WriteStringValue(PerfConvert.ErrnoLookup(value));
                    return;
                }
            }
            else
            {
                if (this.JsonOptions.HasFlag(PerfConvertOptions.ErrnoUnknownAsString))
                {
                    this.JsonWriter.WriteStringValue(PerfConvert.ErrnoFormat(charBuf, value));
                    return;
                }
            }

            this.JsonWriter.WriteNumberValue(value);
        }

        private void WriteBooleanValue(Span<char> charBuf, UInt32 value)
        {
            switch (value)
            {
                case 0:
                    this.JsonWriter.WriteBooleanValue(false);
                    break;
                case 1:
                    this.JsonWriter.WriteBooleanValue(true);
                    break;
                default:
                    if (this.JsonOptions.HasFlag(PerfConvertOptions.BoolOutOfRangeAsString))
                    {
                        this.JsonWriter.WriteStringValue(PerfConvert.BooleanFormat(charBuf, value));
                    }
                    else
                    {
                        this.JsonWriter.WriteNumberValue(unchecked((int)value));
                    }
                    break;
            }
        }

        private ReadOnlySpan<byte> MakeName(in EventHeaderItemInfo item, ref Span<char> charBuf)
        {
            var tag = item.Value.FieldTag;
            var nameBytes = item.NameBytes;
            if (!this.JsonOptions.HasFlag(PerfConvertOptions.FieldTag) || tag == 0)
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
