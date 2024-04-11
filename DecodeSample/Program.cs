// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace DecodeSample
{
    using Microsoft.LinuxTracepoints.Decode;
    using System;
    using StringBuilder = System.Text.StringBuilder;

    /// <summary>
    /// Simple decoding of a perf.data file.
    /// </summary>
    internal static class Program
    {
        public static int Main(string[] args)
        {
            int mainResult;
            try
            {
                var enumerator = new EventHeaderEnumerator();
                var stringBuilder = new StringBuilder();

                using (var reader = new PerfDataFileReader())
                {
                    foreach (var arg in args)
                    {
                        Console.WriteLine($"******* OpenFile: {arg}");

                        // Open the file. Ask reader to sort the events by timestamp.
                        if (!reader.OpenFile(arg, PerfDataFileEventOrder.Time))
                        {
                            Console.WriteLine("Invalid data.");
                            continue;
                        }

                        while (true)
                        {
                            var result = reader.ReadEvent(out var eventBytes);
                            if (result != PerfDataFileResult.Ok)
                            {
                                if (result != PerfDataFileResult.EndOfFile)
                                {
                                    // Unexpected. This usually means a corrupt file.
                                    Console.WriteLine($"ReadEvent error: {result}");
                                }
                                break; // No more events.
                            }

                            if (eventBytes.Header.Type != PerfEventHeaderType.Sample)
                            {
                                // Non-sample event, typically information about the system or information
                                // about the trace itself. PerfNonSampleEventInfo metadata might be available.
                                // Note that PerfNonSampleEventInfo is not always needed and is not always
                                // available.
                                result = eventBytes.GetNonSampleEventInfo(reader, out var nonSampleEventInfo);
                                if (result != PerfDataFileResult.Ok)
                                {
                                    // IdNotFound is an expected result for many non-sample events.
                                    if (result != PerfDataFileResult.IdNotFound)
                                    {
                                        // Unexpected error getting event info, e.g. unexpected data layout.
                                        Console.WriteLine($"{eventBytes.Header.Type}: {result}");
                                        continue;
                                    }

                                    // Event info not found. Event is frequently still usable and the content
                                    // can be decoded based on Type, but we don't have access to attributes
                                    // like timestamp, cpu, pid, etc.
                                    Console.WriteLine($"{eventBytes.Header.Type}: IdNotFound");
                                }
                                else
                                {
                                    // Found event info (attributes). Include data from it in the output.
                                    nonSampleEventInfo.AppendJsonEventMetaTo(stringBuilder, false);
                                    Console.WriteLine($"{eventBytes.Header.Type}: {stringBuilder}");
                                    stringBuilder.Clear();
                                }
                            }
                            else
                            {
                                // Sample event, e.g. tracepoint event.
                                // PerfSampleEventInfo metadata may be available and is usually needed.
                                result = eventBytes.GetSampleEventInfo(reader, out var sampleEventInfo);
                                if (result != PerfDataFileResult.Ok)
                                {
                                    // Unexpected: Error getting event info, e.g. bad id or unexpected data layout.
                                    Console.WriteLine($"Sample: {result}");
                                    continue;
                                }

                                // Found event info (attributes). Include data from it in the output.
                                sampleEventInfo.AppendJsonEventMetaTo(stringBuilder, false);
                                Console.WriteLine($"{eventBytes.Header.Type}/{sampleEventInfo.Name}: {stringBuilder}");
                                stringBuilder.Clear();

                                var eventFormat = sampleEventInfo.Format;
                                if (eventFormat == null)
                                {
                                    // Unexpected: Did not find TraceFS format metadata for this event.
                                    Console.WriteLine($"  no format");
                                }
                                else if (eventFormat.DecodingStyle != PerfEventDecodingStyle.EventHeader ||
                                    !enumerator.StartEvent(sampleEventInfo))
                                {
                                    // Decode using TraceFS format metadata.

                                    // Typically the "common" fields are not interesting, so skip them.
                                    var fieldsStart = eventFormat.CommonFieldCount;
                                    var fieldsEnd = eventFormat.Fields.Count;
                                    for (int i = fieldsStart; i < fieldsEnd; i += 1)
                                    {
                                        var fieldFormat = eventFormat.Fields[i];
                                        var fieldValue = fieldFormat.GetFieldValue(sampleEventInfo);

                                        // fieldValue has lots of properties and methods for accessing its data in different
                                        // formats. TraceFS fields are always scalars or arrays of fixed-size elements, so
                                        // the following will work to get the data as a JSON value.
                                        if (fieldValue.IsArrayOrElement)
                                        {
                                            fieldValue.AppendJsonSimpleArrayTo(stringBuilder);
                                        }
                                        else
                                        {
                                            fieldValue.AppendJsonScalarTo(stringBuilder);
                                        }

                                        Console.WriteLine($"  {fieldFormat.Name} = {stringBuilder}");
                                        stringBuilder.Clear();
                                    }
                                }
                                else
                                {
                                    // Decode using EventHeader metadata.

                                    // eventInfo has a bunch of information about the event.
                                    // We won't use it in this example, since we get the same information in JSON
                                    // format from AppendJsonEventMetaTo.
                                    var eventInfo = enumerator.GetEventInfo();

                                    // Get a JSON representation of the event metadata.
                                    enumerator.AppendJsonEventMetaTo(stringBuilder, false);
                                    if (stringBuilder.Length > 0)
                                    {
                                        Console.WriteLine($"  meta = {{ {stringBuilder} }}");
                                        stringBuilder.Clear();
                                    }

                                    // Transition past the initial BeforeFirstItem state.
                                    enumerator.MoveNext();

                                    // This will loop once for each top-level item in the event.
                                    while (enumerator.State >= EventHeaderEnumeratorState.BeforeFirstItem)
                                    {
                                        var itemInfo = enumerator.GetItemInfo(); // Information about the item.

                                        // itemInfo.Value has lots of properties and methods for accessing its data in different
                                        // formats, but they only work for simple values -- scalar, array element, or array of
                                        // fixed-size elements. For complex values such as structs or arrays of variable-size
                                        // elements, you need to use the enumerator to access the sub-items. In this example,
                                        // we use the enumerator to convert the current item to a JSON-formatted string.
                                        // In the case of a simple item, it will be the same as itemInfo.Value.AppendJsonScalarTo().
                                        // In the case of a complex item, it will recursively format the item and its sub-items.
                                        enumerator.AppendJsonItemToAndMoveNextSibling(
                                            stringBuilder,
                                            false,
                                            PerfJsonOptions.Default & ~PerfJsonOptions.RootName); // We don't want a JSON "ItemName": prefix.
                                        Console.WriteLine($"  {itemInfo.NameAsString} = {stringBuilder}");
                                        stringBuilder.Clear();
                                    }

                                    if (enumerator.State == EventHeaderEnumeratorState.Error)
                                    {
                                        // Unexpected: Error decoding event.
                                        Console.WriteLine($"  **{enumerator.LastError}");
                                    }
                                }
                            }
                        }
                    }
                }

                mainResult = 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine(ex.Message);
                mainResult = 1;
            }

            return mainResult;
        }
    }
}
