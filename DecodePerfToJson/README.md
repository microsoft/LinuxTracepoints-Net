# DecodePerfToJson tool

```
Usage: DecodePerfToJson [options] input.perf.data

Converts a perf.data file to JSON. Supports EventHeader-encoded events.

Options:

  -o, --output <file>  Write output to the specified file (default: stdout).
  -s, --sort <order>   Order events by file or time (default: time).
  -n, --nonsample      Include non-sample events in the output.
  -i, --info <options> Comma-separated list of fields to include in "info".
  -j, --json <options> Comma-separated list of JSON control options.
  -v, --validate       Validate the JSON output.
  -V, --novalidate     Do not validate the JSON output (default).
  -h, --help           Show this help message.

Info options:

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

  Info fields will be omitted if not available or if the field has a default
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

## Changelog

### 0.1.0

- Initial release.
