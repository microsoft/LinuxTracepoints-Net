# Microsoft.LinuxTracepoints.Decode

.NET library for parsing `perf.data` files and decoding tracepoint events. This
includes decoding of `perf.data` files, parsing of tracefs `format` files, formatting
of tracepoint event fields, and decoding of tracepoints that use the `EventHeader`
decoding system.

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

- `PerfEventSessionInfo` class - provides information about a trace decoding session such as the
  byte order and the clock source.

- `PerfEventTimeSpec` struct - represents a timestamp as a `time64_t` plus nanoseconds.

- `PerfFieldFormat` class - parses a field format string from a tracefs `format` file and
  provides access to the field name, type, and size.

- `PerfNonSampleEventInfo` struct - provides information about a non-sample event, such as the
  event bytes, session information, cpu, time, pid, tracepoint name, format, and timestamp.

- `PerfSampleEventInfo` struct - provides information about a sample event, such as the event
  bytes, session information, cpu, time, pid, tracepoint name, format, timestamp, raw, and
  format.

- `PerfValue` struct - represents a field value from a tracepoint event. Includes type
  information. Provides byte-order-aware helpers for accessing the value as a .NET type
  (e.g. `GetU32`, `GetF64`, `GetGuid`). Provides helpers for formatting the value as a
  `string` or appending it to a `StringBuilder`.
