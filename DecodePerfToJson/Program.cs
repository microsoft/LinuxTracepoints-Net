// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace DecodePerfToJson
{
    using Microsoft.LinuxTracepoints.Decode;
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Text.Json;

    public static class Program
    {
        private readonly struct EO
        {
            public readonly string Name;
            public readonly uint Value;
            public readonly bool Default;

            public EO(string name, uint value, uint def)
            {
                Name = name;
                Value = value;
                Default = 0 != (value & def);
            }
        }

        private static readonly EO[] infoOptions = {
            new EO("N",                 (uint)PerfInfoOptions.N,         (uint)PerfInfoOptions.Default),
            new EO("Time",              (uint)PerfInfoOptions.Time,      (uint)PerfInfoOptions.Default),
            new EO("Cpu",               (uint)PerfInfoOptions.Cpu,       (uint)PerfInfoOptions.Default),
            new EO("Pid",               (uint)PerfInfoOptions.Pid,       (uint)PerfInfoOptions.Default),
            new EO("Tid",               (uint)PerfInfoOptions.Tid,       (uint)PerfInfoOptions.Default),
            new EO("Id",                (uint)PerfInfoOptions.Id,        (uint)PerfInfoOptions.Default),
            new EO("Version",           (uint)PerfInfoOptions.Version,   (uint)PerfInfoOptions.Default),
            new EO("Level",             (uint)PerfInfoOptions.Level,     (uint)PerfInfoOptions.Default),
            new EO("Keyword",           (uint)PerfInfoOptions.Keyword,   (uint)PerfInfoOptions.Default),
            new EO("Opcode",            (uint)PerfInfoOptions.Opcode,    (uint)PerfInfoOptions.Default),
            new EO("Tag",               (uint)PerfInfoOptions.Tag,       (uint)PerfInfoOptions.Default),
            new EO("Activity",          (uint)PerfInfoOptions.Activity,  (uint)PerfInfoOptions.Default),
            new EO("RelatedActivity",   (uint)PerfInfoOptions.RelatedActivity, (uint)PerfInfoOptions.Default),
            new EO("Provider",          (uint)PerfInfoOptions.Provider,  (uint)PerfInfoOptions.Default),
            new EO("Event",             (uint)PerfInfoOptions.Event,     (uint)PerfInfoOptions.Default),
            new EO("Options",           (uint)PerfInfoOptions.Options,   (uint)PerfInfoOptions.Default),
            new EO("Flags",             (uint)PerfInfoOptions.Flags,     (uint)PerfInfoOptions.Default),
            new EO("Common",            (uint)PerfInfoOptions.Common,    (uint)PerfInfoOptions.Default),
        };

        private static readonly EO[] convertOptions = {
            new EO("Space",                          (uint)PerfConvertOptions.Space,                         (uint)PerfConvertOptions.Default),
            new EO("FieldTag",                       (uint)PerfConvertOptions.FieldTag,                      (uint)PerfConvertOptions.Default),
            new EO("FloatNonFiniteAsString",         (uint)PerfConvertOptions.FloatNonFiniteAsString,        (uint)PerfConvertOptions.Default),
            new EO("IntHexAsString",                    (uint)PerfConvertOptions.IntHexAsString,                (uint)PerfConvertOptions.Default),
            new EO("BoolOutOfRangeAsString",         (uint)PerfConvertOptions.BoolOutOfRangeAsString,        (uint)PerfConvertOptions.Default),
            new EO("UnixTimeWithinRangeAsString",    (uint)PerfConvertOptions.UnixTimeWithinRangeAsString,   (uint)PerfConvertOptions.Default),
            new EO("UnixTimeOutOfRangeAsString",     (uint)PerfConvertOptions.UnixTimeOutOfRangeAsString,    (uint)PerfConvertOptions.Default),
            new EO("ErrnoKnownAsString",             (uint)PerfConvertOptions.ErrnoKnownAsString,            (uint)PerfConvertOptions.Default),
            new EO("ErrnoUnknownAsString",           (uint)PerfConvertOptions.ErrnoUnknownAsString,          (uint)PerfConvertOptions.Default),
        };

        public static int Main(string[] args)
        {
            int result;

            if (Debugger.IsAttached)
            {
                result = Run(args);
            }
            else
            {
                try
                {
                    result = Run(args);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine();
                    Console.Error.WriteLine(ex.Message);
                    result = 1;
                }
            }

            return result;
        }

        private static int Run(string[] args)
        {
            var outputName = "";
            var sort = PerfDataFileEventOrder.Time;
            var nonsample = false;
            var info = PerfInfoOptions.Default;
            var json = PerfConvertOptions.Default;
            var validate = false;
            var help = false;
            var inputName = "";

            for (var argIndex = 0; argIndex < args.Length; argIndex += 1)
            {
                var arg = args[argIndex];
                if (arg.StartsWith('-') || arg.StartsWith('/'))
                {
                    if (arg.StartsWith("--", StringComparison.Ordinal))
                    {
                        var flag = arg.Substring(2).ToLowerInvariant();
                        switch (flag)
                        {
                            case "output":
                                if (argIndex + 1 >= args.Length)
                                {
                                    Console.Error.WriteLine("Missing argument for --output flag (expected output file name)");
                                    help = true;
                                }
                                else
                                {
                                    argIndex += 1;
                                    outputName = args[argIndex];
                                }
                                break;
                            case "sort":
                                if (argIndex + 1 >= args.Length)
                                {
                                    Console.Error.WriteLine("Missing argument for --sort flag (expected \"file\" or \"time\")");
                                }
                                else
                                {
                                    argIndex += 1;
                                    var orderStr = args[argIndex].ToLowerInvariant();
                                    switch (orderStr)
                                    {
                                        case "file":
                                            sort = PerfDataFileEventOrder.File;
                                            break;
                                        case "time":
                                            sort = PerfDataFileEventOrder.Time;
                                            break;
                                        default:
                                            Console.Error.WriteLine($"Unknown argument \"--sort {args[argIndex]}\" (expected \"file\" or \"time\")");
                                            help = true;
                                            break;
                                    }
                                }
                                break;
                            case "nonsample":
                                nonsample = true;
                                break;
                            case "info":
                                if (argIndex + 1 >= args.Length)
                                {
                                    Console.Error.WriteLine("Missing argument for --info flag");
                                }
                                else
                                {
                                    argIndex += 1;
                                    info = (PerfInfoOptions)MakeOptions("--info", infoOptions, args[argIndex], ref help);
                                }
                                break;
                            case "json":
                                if (argIndex + 1 >= args.Length)
                                {
                                    Console.Error.WriteLine("Missing argument for --json flag");
                                }
                                else
                                {
                                    argIndex += 1;
                                    json = (PerfConvertOptions)MakeOptions("--json", convertOptions, args[argIndex], ref help);
                                }
                                break;
                            case "validate":
                                validate = true;
                                break;
                            case "novalidate":
                                validate = false;
                                break;
                            case "help":
                                help = true;
                                break;
                            default:
                                Console.Error.WriteLine($"Unknown flag: {arg}");
                                help = true;
                                break;
                        }
                    }
                    else
                    {
                        for (var flagIndex = 1; flagIndex < arg.Length; flagIndex += 1)
                        {
                            var flag = arg[flagIndex];
                            switch (flag)
                            {
                                case 'o':
                                    if (argIndex + 1 >= args.Length)
                                    {
                                        Console.Error.WriteLine("Missing argument for -o flag (expected output file name)");
                                        help = true;
                                    }
                                    else
                                    {
                                        argIndex += 1;
                                        outputName = args[argIndex];
                                    }
                                    break;
                                case 's':
                                    if (argIndex + 1 >= args.Length)
                                    {
                                        Console.Error.WriteLine("Missing argument for -s flag (expected \"file\" or \"time\")");
                                    }
                                    else
                                    {
                                        argIndex += 1;
                                        var orderStr = args[argIndex].ToLowerInvariant();
                                        switch (orderStr)
                                        {
                                            case "file":
                                                sort = PerfDataFileEventOrder.File;
                                                break;
                                            case "time":
                                                sort = PerfDataFileEventOrder.Time;
                                                break;
                                            default:
                                                Console.Error.WriteLine($"Unknown argument \"-s {args[argIndex]}\" (expected \"file\" or \"time\")");
                                                help = true;
                                                break;
                                        }
                                    }
                                    break;
                                case 'n':
                                    nonsample = true;
                                    break;
                                case 'i':
                                    if (argIndex + 1 >= args.Length)
                                    {
                                        Console.Error.WriteLine("Missing argument for -i flag");
                                    }
                                    else
                                    {
                                        argIndex += 1;
                                        info = (PerfInfoOptions)MakeOptions("-i", infoOptions, args[argIndex], ref help);
                                    }
                                    break;
                                case 'j':
                                    if (argIndex + 1 >= args.Length)
                                    {
                                        Console.Error.WriteLine("Missing argument for -j flag");
                                    }
                                    else
                                    {
                                        argIndex += 1;
                                        json = (PerfConvertOptions)MakeOptions("-j", convertOptions, args[argIndex], ref help);
                                    }
                                    break;
                                case 'v':
                                    validate = true;
                                    break;
                                case 'V':
                                    validate = false;
                                    break;
                                case '?':
                                case 'h':
                                    help = true;
                                    break;
                                default:
                                    Console.Error.WriteLine($"Unknown flag: -{flag}");
                                    help = true;
                                    break;
                            }
                        }
                    }
                }
                else if (string.IsNullOrEmpty(inputName))
                {
                    inputName = arg;
                }
                else
                {
                    Console.Error.WriteLine($"Input already set: {arg}");
                    help = true;
                }
            }

            if (help || string.IsNullOrEmpty(inputName))
            {
                Console.Out.WriteLine(@$"
Usage: DecodePerfToJson [options] input.perf.data

Converts a perf.data file to JSON. Supports EventHeader-encoded events.

Options:

  -o, --output <file>  Write output to the specified file (default: stdout).
  -s, --sort <order>   Order events by file or time (default: time).
  -n, --nonsample      Include non-sample events in the output.
  -i, --info <options> Comma-separated list of fields to include in ""info"".
  -j, --json <options> Comma-separated list of JSON control options.
  -v, --validate       Validate the JSON output.
  -V, --novalidate     Do not validate the JSON output (default).
  -h, --help           Show this help message.

Info options:

  N               ""n"" field with the event identity before event.
  Time            ""time"" field with the event timestamp.
  Cpu             ""cpu"" field with the event CPU index.
  Pid             ""pid"" field with the event process ID.
  Tid             ""tid"" field with the event thread ID.
  Id              ""id"" field with the EventHeader stable event ID.
  Version         ""version"" field with the EventHeader stable event Version.
  Level           ""level"" field with the EventHeader event Level.
  Keyword         ""keyword"" field with the EventHeader event Keyword.
  Opcode          ""opcode"" field with the EventHeader event Opcode.
  Tag             ""tag"" field with the EventHeader event Tag.
  Activity        ""activity"" field with EventHeader Activity ID.
  RelatedActivity ""relatedActivity"" with EventHeader Related ID.
  Provider        ""provider"" field with Provider/System name.
  Event           ""event"" field with Event/Tracepoint name.
  Options         ""options"" field with EventHeader provider options.
  Flags           ""flags"" field with EventHeader provider flags.
  Common          Include ""common"" fields before the user fields.

  Info fields will be omitted if not available or if the field has a default
  value. For example, the ""opcode"" field will be omitted if it is 0, and the
  ""tid"" field will be omitted if it is the same as the ""pid"".

  On by default:  {MakeList(infoOptions, true)}

  Off by default: {MakeList(infoOptions, false)}

JSON options:

  Space                       Include spaces, newlines, indents in output.
  FieldTag                    Fields with nonzero field tags are included in
                              field name e.g. ""Name;tag=0xNN"": value.
  FloatNonFiniteAsString      Non-finite float is a string instead of a null.
  IntHexAsString                 Hex integer is string ""0xNNN"" instead of a
                              number.
  BoolOutOfRangeAsString      Boolean other than 0..1 is string ""BOOL(N)""
                              instead of a number.
  UnixTimeWithinRangeAsString Time with year in 0001..9999 is formatted as a
                              string like ""2024-04-08T23:59:59Z"" instead of
                              a number (time_t). (Does not apply to event
                              timestamps.)
  UnixTimeOutOfRangeAsString  Time with year beyond 0000.9999 is formatted as
                              a string like ""TIME(NNN)"" instead of a number.
  ErrnoKnownAsString          Known errno values are formatted as a string
                              like ""ERRNO(0)"" or ""ENOENT(2)"" instead of a
                              number.
  ErrnoUnknownAsString        Unknown errno values are formatted as a string
                              like ""ERRNO(N)"" instead of a number.

  On by default:  {MakeList(convertOptions, true)}

  Off by default: {MakeList(convertOptions, false)}
");
                return 1;
            }

            using (var decode = new DecodePerfJsonWriter(
                string.IsNullOrEmpty(outputName) ? Console.OpenStandardOutput() : CreateWithBom(outputName),
                new JsonWriterOptions {
                    Indented = json.HasFlag(PerfConvertOptions.Space),
                    SkipValidation = !validate }))
            {
                decode.JsonOptions = json;
                decode.InfoOptions = info;
                decode.ShowNonSample = nonsample;
                decode.JsonWriter.WriteStartArray();
                decode.WriteFile(inputName, sort);
                decode.JsonWriter.WriteEndArray();
            }

            return 0;
        }

        private static FileStream CreateWithBom(string path)
        {
            var stream = new FileStream(
                path,
                FileMode.Create,
                FileAccess.Write,
                FileShare.Delete | FileShare.Read);
            stream.WriteByte(0xEF);
            stream.WriteByte(0xBB);
            stream.WriteByte(0xBF);
            return stream;
        }

        private static string MakeList(EO[] valid, bool def)
        {
            var list = new List<string>();
            foreach (var eo in valid)
            {
                if (eo.Default == def)
                {
                    list.Add(eo.Name);
                }
            }

            if (list.Count == 0)
            {
                return "<none>";
            }
            else
            {
                return string.Join(',', list);
            }
        }

        private static uint MakeOptions(string flag, EO[] valid, string arg, ref bool help)
        {
            var map = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
            foreach (var eo in valid)
            {
                map[eo.Name] = eo.Value;
            }

            uint result = default;
            foreach (var token in arg.Split(','))
            {
                if (string.IsNullOrWhiteSpace(token))
                {
                    continue;
                }

                var trimmed = token.Trim();
                if (map.TryGetValue(trimmed, out var value))
                {
                    result |= value;
                }
                else
                {
                    Console.Error.WriteLine($"Unknown option {flag} \"{token}\"");
                    help = true;
                }
            }

            return result;
        }
    }
}
