# DecodePerfToJson tool

Tool for converting perf.data files to JSON text.

```

Usage: DecodePerfToJson [options] input.perf.data

Converts a perf.data file to JSON. Supports EventHeader-encoded events.

Options:

  -o, --output <file>  Write output to the specified file (default: stdout).
  -s, --sort <order>   Order events by file or time (default: time).
  -n, --nonsample      Include non-sample events in the output.
  -m, --meta <options> Comma-separated list of fields to include in "meta".
  -j, --json <options> Comma-separated list of JSON control options.
  -v, --validate       Validate the JSON output.
  -V, --novalidate     Do not validate the JSON output (default).
  -h, --help           Show this help message.

Meta options:

  N               "n" field with the event identity before event.
  Time            "time" field with the event timestamp.
  Cpu             "cpu" field with the event CPU index.
  Pid             "pid" field with the event process ID.
  Tid             "tid" field with the event thread ID.
  Id              "id" field with the EventHeader stable event ID.
  Version         "version" field with the EventHeader stable event Version.
  Level           "level" field with the EventHeader event Level.
  Keyword         "keyword" field with the EventHeader event Keyword.
  Opcode          "opcode" field with the EventHeader event Opcode.
  Tag             "tag" field with the EventHeader event Tag.
  Activity        "activity" field with EventHeader Activity ID.
  RelatedActivity "relatedActivity" with EventHeader Related ID.
  Provider        "provider" field with Provider/System name.
  Event           "event" field with Event/Tracepoint name.
  Options         "options" field with EventHeader provider options.
  Flags           "flags" field with EventHeader provider flags.
  Common          Include "common" fields before the user fields.

  Meta fields will be omitted if not available or if the field has a default
  value. For example, the "opcode" field will be omitted if it is 0, and the
  "tid" field will be omitted if it is the same as the "pid".

  On by default:  N,Time,Cpu,Pid,Tid,Id,Version,Level,Keyword,Opcode,Tag,Activity,RelatedActivity

  Off by default: Provider,Event,Options,Flags,Common

JSON options:

  Space                       Include spaces, newlines, indents in output.
  FieldTag                    Fields with nonzero field tags are included in
                              field name e.g. "Name;tag=0xNN": value.
  FloatNonFiniteAsString      Non-finite float is a string instead of a null.
  IntHexAsString              Hex integer is string "0xNNN" instead of a
                              number.
  BoolOutOfRangeAsString      Boolean other than 0..1 is string "BOOL(N)"
                              instead of a number.
  UnixTimeWithinRangeAsString Time with year in 0001..9999 is formatted as a
                              string like "2024-04-08T23:59:59Z" instead of
                              a number (time_t). (Does not apply to event
                              timestamps.)
  UnixTimeOutOfRangeAsString  Time with year beyond 0000.9999 is formatted as
                              a string like "TIME(NNN)" instead of a number.
  ErrnoKnownAsString          Known errno values are formatted as a string
                              like "ERRNO(0)" or "ENOENT(2)" instead of a
                              number.
  ErrnoUnknownAsString        Unknown errno values are formatted as a string
                              like "ERRNO(N)" instead of a number.

  On by default:  Space,FieldTag,FloatNonFiniteAsString,IntHexAsString,BoolOutOfRangeAsString,UnixTimeWithinRangeAsString,UnixTimeOutOfRangeAsString,ErrnoKnownAsString,ErrnoUnknownAsString

  Off by default: <none>
```

## Project

This component is a part of the
[LinuxTracepoints-Net](https://github.com/microsoft/LinuxTracepoints-Net)
project. Feedback and contributions are welcome.

## Changelog

### 0.2.0 (2024-06-19)

- Add support for `BinaryLength16Char8` encoding.
- Add support for extended formats for `StringLength16Char8` encoding.
- Add support for `IPAddress` format.
- Rename "info" suffix to "meta".

### 0.1.3 (2024-05-20)

- When system/event name is not available via `EVENT_DESC` header, fall back to
  system/event name from sample format info instead of names as missing.

### 0.1.2 (2024-05-06)

- Fix TID field for non-Sample events.

### 0.1.1 (2024-04-22)

- Initial release.
