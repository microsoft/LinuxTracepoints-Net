# Microsoft.LinuxTracepoints.Decode

.NET library for parsing `perf.data` files and decoding tracepoint events. This
includes decoding of `perf.data` files, parsing of tracefs `format` files, formatting
of tracepoint event fields, and decoding of tracepoints that use the `EventHeader`
decoding system.

## Decoding procedure

For an example, see [DecodeSample](https://github.com/microsoft/LinuxTracepoints-Net/tree/main/DecodeSample).

- Create a file reader: `var reader = new PerfDataFileReader()`.

- If you will be decoding events that use the `EventHeader` decoding system, create an
  enumerator to use for decoding them:
  `var eventHeaderEnumerator = reader.GetEventHeaderEnumerator()`.

- Call reader.OpenFile(perfDataFileName, sortOrder) to open the `perf.data` file and read
  the file header.

  - The `sortOrder` parameter specifies whether the reader should return events in the order
    they occur in the file (`PerfDataFileEventOrder.File`, less resource-intensive) or in
    timestamp order (`PerfDataFileEventOrder.Time`).

- Call `reader.ReadEvent(out eventBytes)` to read the header and bytes of the next event.

- Use `eventBytes.Header.Type` to determine the type of event.

- If `Type != Sample`, this is a non-sample event that contains information about the trace
  or about the system on which the trace was collected. This library provides no support for
  decoding these events. Refer to `linux/uapi/linux/perf_event.h` for the format of these
  events.

  - Some of these events may have associated metadata, e.g. timestamp, CPU, pid, tid. This
    library does provide support for accessing the metadata by calling
    `reader.GetNonSampleEventInfo(eventBytes, out nonSampleEventInfo)`.

- If `Type == Sample`, this is a sample event, e.g. a tracepoint event.

  - Use `reader.GetSampleEventInfo(eventBytes, out sampleEventInfo)` to look up metadata for
    the event. If metadata lookup succeeds and `sampleEventInfo.Format` is not `null`, you can
    use this library to decode the event.

  - If `sampleEventInfo.Format.DecodingStyle` is `TraceEventFormat`, decode the event using
    `sampleEventInfo.Format.Fields`. Use
    `sampleEventInfo.Format.Fields[i].GetFieldValue(sampleEventInfo)` to get a `PerfItemValue` for
    the field at index `i`. The `PerfItemValue` has the field's type information and a
    `ReadOnlySpan<byte>` with the field's value. It also has helper methods for accessing the
    field's value as a .NET type or as a string. (The first `Format.CommonFieldCount` fields
    are usually not interesting and are typically skipped.)
    
  - If `sampleEventInfo.Format.DecodingStyle` is `EventHeader`, decode the event using
	`eventHeaderEnumerator`. Start enumeration by calling
    `eventHeaderEnumerator.StartEvent(sampleEventInfo)`. You can then get EventHeader-specific
    information about the event from `eventHeaderEnumerator.GetEventInfo()`, and you can iterate
    through the fields of the event using `eventHeaderEnumerator.MoveNext()`.

## Classes and structs

- `EventHeaderEnumerator` class - provides access to information and field values
  of an event that uses the `EventHeader` decoding format. This can be used in
  combination with the `PerfDataFileReader` class to read and decode events from
  a `perf.data` file, or with some other source of event data.

- `EventHeaderEventInfo` struct - provides information about an `EventHeader` event,
  such as the tracepoint name, event name, severity level, activity ID, etc.

- `EventHeaderItemInfo` struct - provides information about a field in an `EventHeader`
  event, such as the field name, field type, and field value.

- `PerfByteReader` struct - helper for reading values from a data source that may be
  big-endian or little-endian, e.g. events from a `perf.data` file.

- `PerfConvert` static class - provides methods for converting tracepoint event field
  data to .NET types.
  - Helpers for formatting fields as text (new string with `ToString()`, append to
    `StringBuilder`, or format to `Span<char>`).
  - Helpers for converting UNIX `time_32` and `time_64` timestamps to .NET `DateTime`
    values.
 
- `PerfDataFileReader` class - parses a `perf.data` file into headers and events (optionally
  sorted by timestamp), provides access to event metadata like timestamp or CPU, tracks
  tracefs `format` decoding information for each event.

- `PerfEventBytes` struct - bytes of a single event from a `perf.data` file.

- `PerfEventDesc` class - stores the `perf_event_attr`, tracepoint name, and tracefs `format`
  information for a set of events.

- `PerfEventFormat` class - parses a tracefs `format` file and provides access to the
  decoding information for tracepoint events.

- `PerfFieldFormat` class - parses a field format string from a tracefs `format` file and
  provides access to the field name, type, and size.

- `PerfNonSampleEventInfo` struct - provides information about a non-sample event, such as the
  event bytes, session information, cpu, time, pid, tracepoint name, format, and timestamp.

- `PerfSampleEventInfo` struct - provides information about a sample event, such as the event
  bytes, session information, cpu, time, pid, tracepoint name, format, timestamp, raw, and
  format.

- `PerfSessionInfo` class - provides information about a trace decoding session such as the
  byte order and the clock source.

- `PerfTimeSpec` struct - represents a timestamp as a `time64_t` plus nanoseconds.
  Semantics equivalent to `struct timespec` from `time.h`.

- `PerfItemValue` struct - represents the value of a field of a tracepoint event. Includes type
  information. Provides byte-order-aware helpers for accessing the value as a .NET type
  (e.g. `GetU32`, `GetF64`, `GetGuid`). Provides helpers for formatting the value as a
  `string` or appending it to a `StringBuilder`.

## Changelog

### 0.1.2 (2024-05-02)

- Fix invalid TID returned from `GetNonSampleEventInfo`.
- Fix `PerfTimeSpec.ToString()` to properly include subsecond values.
- Renamed `PerfValue` to `PerfItemValue`.
- Refactored some of the `PerfItemValue` fields into a separate `PerfItemType`
  struct. Several `perfItemValue.<property>` members are now accessed as
  `perfItemValue.Type.<property>`: ElementCount, FieldTag, TypeSize, Encoding,
  ArrayFlags, EncodingAndArrayFlags, IsArrayOrElement, Format,
  StructFieldCount.
- `PerfSampleEventInfo.Format` is now non-nullable. In cases where it was
  previously null, it now holds the Empty format (format.IsEmpty is true).
- `EventHeaderEnumerator` now has a `GetItemType()` method. This returns a
  subset of the information returned from `GetItemInfo()` and can be used as
  an optimization when only the item type information is needed.
- `PerfConvert` string methods have been refactored to ensure that callers
  correctly handle control characters (space, newline, etc.) when formatting
  strings (the new methods have a required parameter to indicate how control
  characters should be formatted - as-is, as-space, or as-escape-sequence).
  `StringAppend` is now `StringAppendWithControlChars`, and
  `StringLatin1Append` is now `StringLatin1AppendWithControlChars`. Default
  control character handling is replace-with-space for basic formatting and
  replace-with-JSON-escape-sequence for JSON formatting.
- `PerfConvert` exposes several new methods: `ReadGuidBigEndian`,
  `Char16AppendWithControlChars`, etc.
- `PerfDataFileReader` exposes new property `SessionInfo`.
- `PerfDataFileReader` exposes new method `HeaderString`.
- Added `AsString` extension method for `PerfEventAttrType`.
- `PerfTimeSpec` now implements `IComparable`, `IEquatable`, and overloads
  the comparison operators.
- `PerfTimeSpec` can now be constructed from a `DateTime`.
- `PerfTimeSpec` exposes an `AddNanoseconds` method.

### 0.1.1 (2024-04-22)

- Initial release.
