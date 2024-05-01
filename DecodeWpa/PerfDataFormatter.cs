// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.Performance.Toolkit.Plugins.PerfDataExtension
{
    using Microsoft.LinuxTracepoints.Decode;
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.Text;
    using System.Threading;

    /// <summary>
    /// Provides methods for formatting event properties with automatic string interning.
    /// </summary>
    public class PerfDataFormatter
    {
        private readonly ConcurrentDictionary<ReadOnlyMemory<char>, string> utf16pool =
            new ConcurrentDictionary<ReadOnlyMemory<char>, string>(MemoryComparer.Instance);
        private readonly ConcurrentDictionary<ReadOnlyMemory<byte>, string> utf8Pool =
            new ConcurrentDictionary<ReadOnlyMemory<byte>, string>(MemoryComparer.Instance);
        private readonly StringBuilder builder =
            new StringBuilder();
        private EventHeaderEnumerator? enumerator;
        private ConcurrentDictionary<UInt32, string>? uint32Pool;

        /// <summary>
        /// Returns the interned string for the given value.
        /// <br/>
        /// This method is thread-safe (accesses ConcurrentDictionary).
        /// </summary>
        public string InternString(string value)
        {
            return string.IsNullOrEmpty(value)
                ? ""
                : this.InternNonEmptyString(value);
        }

        /// <summary>
        /// Returns the interned string for the given value.
        /// <br/>
        /// This method is thread-safe (accesses ConcurrentDictionary).
        /// </summary>
        public string InternChars(ReadOnlyMemory<char> value)
        {
            string s;
            if (value.IsEmpty)
            {
                s = "";
            }
            else if (!this.utf16pool.TryGetValue(value, out s))
            {
                s = this.InternNonEmptyString(value.ToString());
            }
            return s;
        }

        /// <summary>
        /// Returns the interned string for the given UTF-8 value.
        /// <br/>
        /// This method is thread-safe (accesses ConcurrentDictionary).
        /// </summary>
        public string InternUtf8(ReadOnlyMemory<byte> utf8value)
        {
            string s;
            if (utf8value.IsEmpty)
            {
                s = "";
            }
            else if (!this.utf8Pool.TryGetValue(utf8value, out s))
            {
                var value = utf8value.ToArray();
                s = this.InternNonEmptyString(Encoding.UTF8.GetString(value));
                this.utf8Pool.TryAdd(value, s);
            }
            return s;
        }

        /// <summary>
        /// Returns the interned string for value.ToString(InvariantCulture).
        /// <br/>
        /// This method is thread-safe (accesses ConcurrentDictionary).
        /// </summary>
        public string InternUInt32(UInt32 value)
        {
            var pool = this.uint32Pool
                ?? InterlockedInitSingleton(ref this.uint32Pool, new ConcurrentDictionary<UInt32, string>());

            string s;
            if (!pool.TryGetValue(value, out s))
            {
                s = this.InternNonEmptyString(value.ToString(CultureInfo.InvariantCulture));
                pool.TryAdd(value, s);
            }
            return s;
        }

        /// <summary>
        /// Returns a string with a human-friendly group for the event.
        /// For EventHeader events, this is the EventHeader provider name.
        /// For tracepoint events, this is the tracepoint system name.
        /// Otherwise, this is the PERF_TYPE (Hardware, Software, HwCache, etc.).
        /// <br/>
        /// This method is thread-safe (accesses ConcurrentDictionary).
        /// </summary>
        public string GetFriendlyGroupName(PerfDataEvent perfEvent)
        {
            if (perfEvent.HasEventHeader)
            {
                return this.InternChars(perfEvent.ProviderNameMemory);
            }

            var desc = perfEvent.EventDesc;
            var type = perfEvent.Header.Type;
            if (type == PerfEventHeaderType.Sample)
            {
                var format = desc.Format;
                var formatSystemName = format.SystemName;
                if (formatSystemName.Length > 0)
                {
                    return formatSystemName;
                }

                var tracepointId = desc.Name;
                var systemNameLength = PerfDataEvent.GetSystemNameLength(tracepointId);
                if (systemNameLength > 0)
                {
                    return this.InternChars(tracepointId.AsMemory(0, systemNameLength));
                }
            }

            var attrType = desc.Attr.Type;
            return attrType.AsStringIfKnown() ?? this.InternUInt32((uint)attrType);
        }

        /// <summary>
        /// Returns a string with a human-friendly name for the event.
        /// For EventHeader events, this is the EventHeader event name.
        /// For tracepoint events, this is the tracepoint name.
        /// Otherwise (e.g. non-Sample events), this is the event type (Header.Type.AsString).
        /// <br/>
        /// This method is thread-safe (accesses ConcurrentDictionary).
        /// </summary>
        public string GetFriendlyEventName(PerfDataEvent perfEvent)
        {
            if (perfEvent.HasEventHeader)
            {
                return this.GetEventHeaderName(perfEvent);
            }

            var type = perfEvent.Header.Type;
            if (type == PerfEventHeaderType.Sample)
            {
                var desc = perfEvent.EventDesc;
                var format = desc.Format;
                if (!format.IsEmpty)
                {
                    return format.Name;
                }

                var tracepointId = desc.Name;
                var tracepointNameStart = PerfDataEvent.GetTracepointNameStart(tracepointId);
                if (tracepointId.Length > tracepointNameStart)
                {
                    return this.InternChars(tracepointId.AsMemory(tracepointNameStart));
                }
            }

            return type.AsStringIfKnown() ?? this.InternUInt32((uint)type);
        }

        /// <summary>
        /// For EventHeader events, returns the EventHeader event name.
        /// Otherwise, returns empty.
        /// <br/>
        /// This method is thread-safe (accesses ConcurrentDictionary).
        /// </summary>
        public string GetEventHeaderName(PerfDataEvent perfEvent)
        {
            return perfEvent.EventHeaderNameLength == 0
                ? ""
                : this.InternUtf8(perfEvent.EventHeaderNameMemory);
        }

        /// <summary>
        /// Formats the event's Common fields as JSON text (one JSON name-value pair per
        /// field). If the event has no Common fields (e.g. if the event is a non-Sample
        /// event), this returns an empty string. In current tracefs, the common fields
        /// are common_type, common_flags, common_preempt_count, and common_pid.
        /// <br/>
        /// This method is thread-safe (serialized).
        /// </summary>
        public string GetCommonFieldsAsJsonSynchronized(
            PerfDataEvent perfEvent,
            PerfConvertOptions convertOptions = PerfConvertOptions.Default)
        {
            var format = perfEvent.Format;
            var commonFieldCount = format.CommonFieldCount;
            if (commonFieldCount == 0)
            {
                return "";
            }

            bool needComma = false;
            var comma = convertOptions.HasFlag(PerfConvertOptions.Space) ? ", " : "";

            var fields = format.Fields;
            var byteReader = perfEvent.ByteReader;
            var rawData = perfEvent.RawDataSpan;

            var sb = this.builder;
            lock (sb)
            {
                sb.Clear();
                for (int fieldIndex = 0; fieldIndex < commonFieldCount; fieldIndex += 1)
                {
                    if (needComma)
                    {
                        sb.Append(comma);
                    }

                    needComma = true;
                    AppendFieldAsJson(byteReader, rawData, fields[fieldIndex], convertOptions);
                }

                return this.BuilderIntern();
            }
        }

        /// <summary>
        /// Formats the event's fields as JSON text (one JSON name-value pair per field),
        /// e.g. ["field1": "string", "field2": 45].
        /// <list type="bullet"><item>
        /// If the event contains no tracefs format information and no content (e.g. a
        /// non-Sample event with ContentLength == 0), this returns "".
        /// </item><item>
        /// If the event contains no tracefs format information but contains content
        /// (e.g. a non-Sample event with ContentLength != 0), this returns a string with
        /// a "raw" field and string of hex bytes, e.g. ["raw": "67 89 ab cd ef"].
        /// </item><item>
        /// If the event contains tracefs format information but is not an EventHeader
        /// event, returns all of the user fields (skips the Common fields).
        /// If the event has no fields, this returns "".
        /// </item><item>
        /// If the event is an EventHeader event, returns all of the EventHeader
        /// fields. If the event has no fields, this returns "".
        /// </item></list>
        /// <br/>
        /// This method is thread-safe (serialized).
        /// </summary>
        public string GetFieldsAsJsonSynchronized(
            PerfDataEvent perfEvent,
            PerfConvertOptions convertOptions = PerfConvertOptions.Default)
        {
            StringBuilder sb;
            bool needComma = false;
            bool space = convertOptions.HasFlag(PerfConvertOptions.Space);
            var comma = space ? ", " : "";

            var format = perfEvent.Format;
            if (format.IsEmpty)
            {
                var contentsLength = perfEvent.ContentsLength;

                // No format - probably a non-Sample event.
                if (contentsLength == 0)
                {
                    return "";
                }

                sb = this.builder;
                lock (sb)
                {
                    sb.Clear();
                    if (needComma)
                    {
                        sb.Append(comma);
                    }

                    needComma = true;

                    sb.Append(space
                        ? @"""raw"": """
                        : @"""raw"":""");
                    var len = Math.Min(Utility.RawBytesMax, contentsLength); // Limit output to 256 bytes.
                    PerfConvert.HexBytesAppend(sb, perfEvent.ContentsSpan.Slice(0, len));
                    sb.Append('"');
                    return this.BuilderIntern();
                }
            }

            if (format.DecodingStyle == PerfEventDecodingStyle.EventHeader)
            {
                var e = this.enumerator ?? InterlockedInitSingleton(ref this.enumerator, new EventHeaderEnumerator());
                sb = this.builder;
                lock (sb)
                {
                    sb.Clear();
                    if (e.StartEvent(format.Name, perfEvent.RawDataMemory.Slice(format.CommonFieldsSize)))
                    {
                        // EventHeader-style decoding.
                        e.AppendJsonItemToAndMoveNextSibling(sb, needComma, convertOptions);
                        return this.BuilderIntern();
                    }
                }
            }

            // TraceFS format file decoding.
            var fields = format.Fields;
            var byteReader = perfEvent.ByteReader;
            var rawData = perfEvent.RawDataSpan;

            sb = this.builder;
            lock (sb)
            {
                sb.Clear();
                for (int fieldIndex = format.CommonFieldCount; fieldIndex < fields.Count; fieldIndex += 1)
                {
                    if (needComma)
                    {
                        this.builder.Append(comma);
                    }

                    needComma = true;
                    this.AppendFieldAsJson(byteReader, rawData, fields[fieldIndex], convertOptions);
                }

                return this.BuilderIntern();
            }
        }

        /// <summary>
        /// Formats the specified field as a JSON ["name": "value"] pair.
        /// <br/>
        /// This method is thread-safe (serialized).
        /// </summary>
        public string GetFieldAsJsonSynchronized(
            PerfDataEvent perfEvent,
            PerfFieldFormat field,
            PerfConvertOptions convertOptions = PerfConvertOptions.Default)
        {
            var sb = this.builder;
            lock (sb)
            {
                sb.Clear();
                this.AppendFieldAsJson(perfEvent.ByteReader, perfEvent.RawDataSpan, field, convertOptions);
                return this.BuilderIntern();
            }
        }

        /// <summary>
        /// Returns a new array of KeyValuePair objects, one for each top-level field
        /// in the event, up to maxTopLevelFields. The key will be the field name. The
        /// value will be a human-readable field value.
        /// <br/>
        /// This method is thread-safe (serialized).
        /// </summary>
        public KeyValuePair<string, string>[] MakeRowSynchronized(PerfDataEvent perfEvent, int maxTopLevelFields)
        {
            if (maxTopLevelFields <= 0 || perfEvent.TopLevelFieldCount <= 0)
            {
                return Array.Empty<KeyValuePair<string, string>>();
            }

            int fieldIndex;
            bool comma;
            var format = perfEvent.Format;

            var sb = this.builder;
            lock (sb)
            {
                if (format.IsEmpty)
                {
                    // No format - probably a non-Sample event.

                    sb.Clear();
                    PerfConvert.HexBytesAppend(
                        sb,
                        perfEvent.ContentsSpan.Slice(0, Math.Min(Utility.RawBytesMax, perfEvent.ContentsLength)));
                    var bytesHex = this.BuilderIntern();
                    Debug.Assert(perfEvent.TopLevelFieldCount == 1);
                    return new KeyValuePair<string, string>[] { new KeyValuePair<string, string>("Bytes", bytesHex) };
                }

                KeyValuePair<string, string>[] row;
                int topLevelFieldCount;
                if (maxTopLevelFields < perfEvent.TopLevelFieldCount)
                {
                    // Not enough columns. Last column will hold "...".
                    row = new KeyValuePair<string, string>[maxTopLevelFields];
                    topLevelFieldCount = maxTopLevelFields - 1;
                }
                else
                {
                    // Enough columns.
                    topLevelFieldCount = perfEvent.TopLevelFieldCount;
                    row = new KeyValuePair<string, string>[topLevelFieldCount];
                }

                if (format.DecodingStyle == PerfEventDecodingStyle.EventHeader)
                {
                    if (this.enumerator == null)
                    {
                        this.enumerator = new EventHeaderEnumerator();
                    }

                    var userMemory = perfEvent.RawDataMemory.Slice(format.CommonFieldsSize);
                    if (this.enumerator.StartEvent(format.Name, userMemory))
                    {
                        // EventHeader-style decoding.
                        var userSpan = userMemory.Span;
                        if (!this.enumerator.MoveNext(userSpan))
                        {
                            return row;
                        }

                        for (fieldIndex = 0; fieldIndex < topLevelFieldCount; fieldIndex += 1)
                        {
                            var item = this.enumerator.GetItemInfo(userSpan);
                            sb.Clear();

                            if (this.enumerator.State == EventHeaderEnumeratorState.Value)
                            {
                                item.Value.AppendScalarTo(sb);
                                this.enumerator.MoveNext(userSpan);
                            }
                            else
                            {
                                this.enumerator.AppendJsonItemToAndMoveNextSibling(
                                    userSpan,
                                    sb,
                                    false,
                                    PerfConvertOptions.Default & ~PerfConvertOptions.RootName);
                            }

                            row[fieldIndex] = new KeyValuePair<string, string>(
                                this.InternUtf8(userMemory.Slice(item.NameStart, item.NameLength)),
                                this.BuilderIntern());

                            if (this.enumerator.State < EventHeaderEnumeratorState.BeforeFirstItem)
                            {
                                return row;
                            }
                        }

                        if (fieldIndex == row.Length)
                        {
                            Debug.Fail("perfEvent.TopLevelFieldCount out of sync with MakeRow (EventHeader).");
                            return row;
                        }

                        // Not enough columns. Last column will hold "...".
                        comma = false;
                        sb.Clear();
                        do
                        {
                            comma = this.enumerator.AppendJsonItemToAndMoveNextSibling(userSpan, sb, comma);
                        } while (this.enumerator.State > EventHeaderEnumeratorState.BeforeFirstItem);

                        row[fieldIndex] = new KeyValuePair<string, string>("...", this.BuilderIntern());
                        return row;
                    }
                }

                // TraceFS format file decoding.
                var fields = format.Fields;
                var byteReader = perfEvent.ByteReader;
                var rawData = perfEvent.RawDataSpan;
                var commonFieldCount = format.CommonFieldCount;
                if (commonFieldCount >= fields.Count)
                {
                    return row;
                }

                fieldIndex = 0;
                while (fieldIndex < topLevelFieldCount)
                {
                    var field = fields[fieldIndex + commonFieldCount];
                    var fieldVal = field.GetFieldValue(rawData, byteReader);
                    sb.Clear();
                    if (fieldVal.Type.IsArrayOrElement)
                    {
                        fieldVal.AppendScalarTo(sb, PerfConvertOptions.Default & ~PerfConvertOptions.RootName);
                    }
                    else
                    {
                        fieldVal.AppendScalarTo(sb, PerfConvertOptions.Default & ~PerfConvertOptions.RootName);
                    }

                    row[fieldIndex] = new KeyValuePair<string, string>(field.Name, this.BuilderIntern());
                    fieldIndex += 1;

                    if (fieldIndex + commonFieldCount >= fields.Count)
                    {
                        return row;
                    }
                }

                if (fieldIndex == row.Length)
                {
                    Debug.Fail("perfEvent.TopLevelFieldCount out of sync with MakeRow (TraceFS).");
                    return row;
                }

                // Not enough columns. Last column will hold "...".
                comma = false;
                sb.Clear();
                for (var i = fieldIndex + commonFieldCount; i < fields.Count; i += 1)
                {
                    if (comma)
                    {
                        sb.Append(", ");
                    }

                    comma = true;
                    var field = fields[i];
                    this.AppendFieldAsJson(byteReader, rawData, field, PerfConvertOptions.Default);
                }

                row[fieldIndex] = new KeyValuePair<string, string>("...", this.BuilderIntern());
                return row;
            }
        }

        private static T InterlockedInitSingleton<T>(ref T? location, T value)
            where T : class
        {
            return Interlocked.CompareExchange(ref location, value, null) ?? value;
        }

        private string BuilderIntern()
        {
            return this.builder.Length == 0 ? "" : this.InternNonEmptyString(this.builder.ToString());
        }

        private void AppendFieldAsJson(PerfByteReader byteReader, ReadOnlySpan<byte> rawData, PerfFieldFormat field, PerfConvertOptions convertOptions)
        {
            if (convertOptions.HasFlag(PerfConvertOptions.RootName))
            {
                PerfConvert.StringAppendJson(this.builder, field.Name);
                this.builder.Append(convertOptions.HasFlag(PerfConvertOptions.Space) ? ": " : ":");
            }

            var fieldVal = field.GetFieldValue(rawData, byteReader);
            if (fieldVal.Type.IsArrayOrElement)
            {
                fieldVal.AppendJsonSimpleArrayTo(this.builder, convertOptions);
            }
            else
            {
                fieldVal.AppendJsonScalarTo(this.builder, convertOptions);
            }
        }

        private string InternNonEmptyString(string value)
        {
            Debug.Assert(!string.IsNullOrEmpty(value));
            return this.utf16pool.GetOrAdd(value.AsMemory(), value);
        }

        private sealed class MemoryComparer
            : IEqualityComparer<ReadOnlyMemory<char>>
            , IEqualityComparer<ReadOnlyMemory<byte>>
        {
            public static readonly MemoryComparer Instance = new MemoryComparer();

            public bool Equals(ReadOnlyMemory<char> x, ReadOnlyMemory<char> y)
            {
                return x.Span.SequenceEqual(y.Span);
            }

            public bool Equals(ReadOnlyMemory<byte> x, ReadOnlyMemory<byte> y)
            {
                return x.Span.SequenceEqual(y.Span);
            }

            public int GetHashCode(ReadOnlyMemory<char> obj)
            {
                uint val = 0x811c9dc5;
                foreach (var c in obj.Span)
                {
                    val ^= c;
                    val *= 0x1000193;
                }

                return unchecked((int)val);
            }

            public int GetHashCode(ReadOnlyMemory<byte> obj)
            {
                uint val = 0x811c9dc5;
                foreach (var c in obj.Span)
                {
                    val ^= c;
                    val *= 0x1000193;
                }

                return unchecked((int)val);
            }
        }
    }
}
