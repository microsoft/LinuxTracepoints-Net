﻿# Microsoft.LinuxTracepoints.Types

.NET library with types used by the `Decode` library.

- `EventHeader` struct - header used at the beginning of each event.
  Indicates byte order, pointer size, event Level (severity), event Opcode
  (e.g. activity start, activity stop), event ID/Version (optional
  manually-assigned stable identifier for event), and event tag
  (provider-defined 16-bit value).

- `EventHeaderExtension` struct - size and kind of a metadata block in an
  event.

- `EventHeaderExtensionKind` enum - the kind of a metadata block in an event,
  e.g. activity ID or decoding information (event name, field names, field
  types).

- `EventHeaderFieldEncoding` enum - base type of a field in an event, indicating
  how to determine the field size (e.g. Value8, Value64, ZStringChar8).

- `EventHeaderFieldFormat` enum - extended type of a field in an event, indicating
  how to interpret the bytes of the field data (e.g. UnsignedInt, Errno, String8,
  StringUtf).

- `EventHeaderFlags` enum - flags indicating the pointer size (32 or 64 bits),
  byte order (big-endian or little), and whether any header extensions are
  present.

## EventHeader

EventHeader is a tracing convention layered on top of Linux Tracepoints. It
extends the Tracepoint system with additional event attributes and a stronger
field value type system.

To reduce the number of unique Tracepoint names tracked by the kernel, we
use a small number of Tracepoints to manage a larger number of events. All
events with the same attributes (provider name, severity level, category
keyword, etc.) will share one Tracepoint.

- This means we cannot enable/disable events individually. Instead, all events
  with similar attributes will be enabled/disabled as a group.
- This means we cannot rely on the kernel's Tracepoint metadata for event
  identity or event field names/types. Instead, all events contain a common
  header that provides event identity, core event attributes, and support for
  optional event attributes. The kernel's Tracepoint metadata is used only for
  the Tracepoint's name and to determine whether the event follows the
  EventHeader conventions.
- Since any eventheader-conforming tracepoint with a particular name will be
  identical from the perspective of the kernel (they all have the same
  tracepoint fields), it is possible to pre-register tracepoints. For example,
  if I have the names of eventheader-compliant tracepoints that I need to
  collect but the tracepoints have not been registered yet (because the
  programs that generate the tracepoints haven't run yet), I can just register
  them myself since any registration will be equivalent to the "real"
  registrations.

We define a naming scheme to be used for the shared Tracepoints:

`TracepointName = ProviderName + '_' + 'L' + EventLevel + 'K' + EventKeyword + [Options]`

We define a common event layout to be used by all EventHeader events. The
event has a header, optional header extensions, and then the event data:

`Event = eventheader + [HeaderExtensions] + Data`

We define a format to be used for header extensions:

`HeaderExtension = eventheader_extension + ExtensionData`

We define a header extension to be used for activity IDs.

We define a header extension to be used for event metadata (event name, field
names, field types).

For use in the event metadata extension, we define a field type system that
supports scalar, string, binary, array, and struct.

Note that we assume that the Tracepoint name corresponding to the event is
available during event decoding. The event decoder obtains the provider name
and the keyword for an event by parsing the event's Tracepoint name.

### Provider Names

A provider is a component that generates events. Each event from a provider is
associated with a Provider Name that uniquely identifies the provider.

The provider name should be short, yet descriptive enough to minimize the
chance of collision and to help developers track down the component generating
the events. Hierarchical namespaces may be useful for provider names, e.g.
`MyCompany_MyOrg_MyComponent`.

Restrictions:

- ProviderName may not contain `' '` or `':'` characters.
- `strlen(ProviderName + '_' + Attributes)` must be less than
  `EVENTHEADER_NAME_MAX` (256) characters.
- Some event APIs (e.g. tracefs) might impose additional restrictions on
  tracepoint names. For best compatibility, use only ASCII identifier
  characters `[A-Za-z0-9_]` in provider names.

Event attribute semantics should be consistent within a given provider. While
some event attributes have generally-accepted semantics (e.g. level value 3
is defined below as "warning"), the precise semantics of the attribute values
are defined at the scope of a provider (e.g. different providers will use
different criteria for what constitutes a warning). In addition, some
attributes (tag, keyword) are completely provider-defined. All events with a
particular provider name should use consistent semantics for all attributes
(e.g. keyword bit `0x1` should have a consistent meaning for all events from a
particular provider but will mean something different for other providers).

### Tracepoint Names

A Tracepoint is registered with the kernel for each unique combination of
ProviderName + Attributes. This allows a larger number of distinct events to
be controlled by a smaller number of kernel Tracepoints while still allowing
events to be enabled/disabled at a reasonable granularity.

The Tracepoint name for an EventHeader event is defined as:

`ProviderName + '_' + 'L' + eventLevel + 'K' + eventKeyword + [Options]`
or `printf("%s_L%xK%lx%s", providerName, eventLevel, eventKeyword, options)`,
e.g. `MyProvider_L3K2a` or `OtherProvider_L5K0Gperf`.

Event level is a `uint8` value 1..255 indicating event severity, formatted as
lowercase hexadecimal, e.g. `printf("L%x", eventLevel)`. The defined level values
are: `1` = critical error, `2` = error, `3` = warning, `4` = information, `5` = verbose.

Event keyword is a `uint64` bitmask indicating event category membership,
formatted as lowercase hexadecimal, e.g. `printf("K%lx", eventKeyword)`. Each
bit in the keyword corresponds to a provider-defined category, e.g. a provider
might define `0x2` = networking and `0x4` = I/O so that keyword value of
`0x2|0x4` = `0x6` would indicate that an event is in both the networking and
I/O categories.

Options (optional attributes) can be specified after the keyword attribute.
Each option consists of an uppercase ASCII letter (option type) followed by 0
or more ASCII digits or lowercase ASCII letters (option value). To support
consistent event names, the options must be sorted in alphabetical order, e.g.
"Aoption" should come before "Boption".

The currently defined options are:

- `G` = provider Group name. Defines a group of providers. This can be used by
  event analysis tools to find all providers that generate a certain kind of
  information.

Restrictions:

- ProviderName may not contain `' '` or `':'` characters.
- Tracepoint name must be less than `EVENTHEADER_NAME_MAX` (256)
  characters in length.
- Some event APIs (e.g. tracefs) might impose additional restrictions on
  tracepoint names. For best compatibility, use only ASCII identifier
  characters `[A-Za-z0-9_]` in provider names.

### Header

Because multiple events may share a single Tracepoint, each event must contain
information to distinguish it from other events. To enable this, each event
starts with an EventHeader structure which contains information about the
event:

```c
typedef struct eventheader {
    uint8_t  flags;   // eventheader_flags: pointer64, little_endian, extension.
    uint8_t  version; // If id != 0 then increment version when event layout changes.
    uint16_t id;      // Stable id for this event, or 0 if none.
    uint16_t tag;     // Provider-defined event tag, or 0 if none.
    uint8_t  opcode;  // event_opcode: info, start activity, stop activity, etc.
    uint8_t  level;   // event_level: critical, error, warning, info, verbose.
    // Followed by: eventheader_extension block(s), then event payload.
} eventheader;
```

- **flags:** Bits indicating pointer size (32 or 64 bits), byte order
  (big-endian or little), and whether any header extensions are present.
- **opcode:** Indicates special event semantics e.g. "normal event",
  "activity start event", "activity end event".
- **tag:** Provider-defined 16-bit value. Can be used for anything.
- **id:** 16-bit stable event identifier, or 0 if no identifier is assigned.
- **version:** 8-bit event version, incremented for e.g. field type changes.
- **level:** 8-bit event severity level, `1` = critical .. `5` = verbose.
  (level value in event header must match the level in the Tracepoint name.)

If the extension flag is not set, the header is immediately followed by the
event payload.

If the extension flag is set, the header is immediately followed by one or more
header extensions. Each header extension has a 16-bit size, a 15-bit type code,
and a 1-bit flag indicating whether another header extension follows the
current extension. The final header extension is immediately followed by the
event payload.

### Extension

Each event may have any number of header extensions that contain information
associated with the event.

```c
typedef struct eventheader_extension {
    uint16_t size;
    uint16_t kind; // eventheader_extension_kind
    // Followed by size bytes of data. No padding/alignment.
} eventheader_extension;
```

The following header extensions are defined:

- **Activity ID:** Contains a 128-bit ID that can be used to correlate events. May
  also contain the 128-bit ID of the parent activity (typically used only for
  the first event of an activity).
- **Metadata:** Contains the event's metadata: event name, event attributes, field
  names, field attributes, and field types. Both simple (e.g. Int32, HexInt16,
  Float64, Char32, Uuid) and complex (e.g. NulTerminatedString8,
  CountedString16, Binary, Struct, Array) types are supported.

### Metadata

Each event may have a header extension that defines event metadata, i.e.
event name, event attributes, field names, field attributes, and field types.

Event definition format:

```c
char event_name[]; // Nul-terminated utf-8 string: "eventName{;attribName=attribValue}"
followed by 0 or more field definition blocks, tightly-packed (no padding).
```

Field definition block:

```c
char field_name[]; // Nul-terminated utf-8 string: "fieldName{;attribName=attribValue}"
uint8_t encoding; // encoding is 0..31, with 3 flag bits.
uint8_t format; // Present if 0 != (encoding & 128). format is 0..127, with 1 flag bit.
uint16_t tag; // Present if 0 != (format & 128). Contains provider-defined value.
uint16_t array_length; // Present if 0 != (encoding & 32). Contains element count of constant-length array.
```

Notes:

- `event_name` and `field_name` may not contain any `';'` characters.
- `event_name` and `field_name` may be followed by attribute strings.
- attribute string is: `';' + attribName + '=' + attribValue`.
- `attribName` may not contain any `';'` or `'='` characters.
- Semicolons in attribValue must be escaped by doubling, e.g.
    `"my;value"` is escaped as `"my;;value"`.
- `array_length` may not be `0`, i.e. constant-length arrays may not be empty.

Each field's type has an arity, an encoding, and a format.

- **Arity** indicates how many values are in a field.
  - A scalar field contains one value.
  - A const (fixed-length) array contains one or more values. The number of
    values is specified in the event metadata and does not vary from one event
    to another.
  - A variable (dynamic-length) array contains zero or more values. The number
    of values is specified in the event data and may vary from one event to
    another.
- **Encoding** indicates how to determine the field size. Supported encodings:
  - Fixed-size 8-bit, 16-bit, 32-bit, 64-bit, and 128-bit values.
  - 0-terminated blobs containing 8-bit, 16-bit, or 32-bit elements (used for
    NUL-terminated strings).
  - Counted blobs containing 8-bit, 16-bit, or 32-bit elements (used for both
    strings and for binary data).
  - Structures (fields that logically contain other fields).
- **Format** indicates how to interpret the bytes of the field data.
  - Integer: signed decimal, unsigned decimal, hexadecimal, `errno`, `pid`,
    `time_t`, Boolean, IP port.
  - String: Latin-1, UTF, UTF-BOM, XML, JSON.
  - Other: Float, IPv4 address, IPv6 address, UUID, Binary.

## Project

This component is a part of the
[LinuxTracepoints-Net](https://github.com/microsoft/LinuxTracepoints-Net)
project. Feedback and contributions are welcome.

## Changelog

### 0.2.0 (2024-06-19)

- Renamed extension method `BaseEncoding` to `WithoutFlags` for
  `EventHeaderFieldEncoding` and `EventHeaderFieldFormat`.
- New field encoding `BinaryLength16Char8`. Same as `StringLength16Char8`
  except that its default format is `HexBytes`.
- New semantics for `BinaryLength16Char8` and `StringLength16Char8` encodings
  to support nullable and variable-length fields. These encodings can now be
  used with any format.  When used with a fixed-size format, this indicates a
  nullable field. For example, a field with encoding `BinaryLength16Char8` and
  format `SignedInt` with length 1, 2, 4, or 8 would be formatted as a signed
  integer. The same field with length 0 would be formatted as a `null`. Any
  other length would be formatted as `HexBytes`.
- Removed `IPv4` and `IPv6` formats. New code should use the `IPAddress`
  format. When applied to a 4-byte field, `IPAddress` should format as IPv4,
  and when applied to a 16-byte field, `IPAddress` should format as IPv6.

### 0.1.3 (2024-05-20)

- Add static helper constants to the `EventHeader` struct: `DefaultFlags` and `IntPtrEncoding`.
- Setting invalid `Opcode` or `Level` property now throws `OverflowException` instead of
  silently truncating the value.

### 0.1.2 (2024-05-06)

- NuGet cleanup.

### 0.1.1 (2024-04-22)

- Initial release.
