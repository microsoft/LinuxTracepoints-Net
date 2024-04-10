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
                using (var reader = new PerfDataFileReader())
                {
                    var enumerator = new EventHeaderEnumerator();
                    var stringBuilder = new StringBuilder();
                    foreach (var arg in args)
                    {
                        Console.WriteLine($"******* OpenFile: {arg}");

                        // Open the file. Ask the reader to sort the events by timestamp.
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
                                    Console.WriteLine($"ReadEvent error: {result}");
                                }
                                break; // No more events.
                            }

                            if (eventBytes.Header.Type != PerfEventHeaderType.Sample)
                            {
                                // Non-sample event, typically information about the system or information
                                // about the trace itself. PerfNonSampleEventInfo metadata may be available.
                                // Note that PerfNonSampleEventInfo is not always available and may not be
                                // needed.
                                result = reader.GetNonSampleEventInfo(eventBytes, out var nonSampleEventInfo);
                                if (result == PerfDataFileResult.Ok)
                                {
                                    // Found event info (attributes).
                                    Console.WriteLine($"{eventBytes.Header.Type}/{nonSampleEventInfo.Name}: {nonSampleEventInfo.DateTime:s}");
                                }
                                else if (result == PerfDataFileResult.IdNotFound)
                                {
                                    // Event info not available. This is expected in many cases, such
                                    // as when Header.Type >= UserTypeStart or before we've seen the
                                    // FinishedInit event. Event is frequently still usable and the content
                                    // can be decoded based on Type.
                                    Console.WriteLine($"{eventBytes.Header.Type}: IdNotFound");
                                }
                                else
                                {
                                    // Unexpected: Other error getting event info.
                                    Console.WriteLine($"{eventBytes.Header.Type}: {result}");
                                }
                            }
                            else
                            {
                                // Sample event, e.g. tracepoint event.
                                // PerfSampleEventInfo metadata may be available and is usually needed.
                                result = reader.GetSampleEventInfo(eventBytes, out var sampleEventInfo);
                                if (result != PerfDataFileResult.Ok)
                                {
                                    // Unexpected: Error getting sample event info.
                                    Console.WriteLine($"Sample: {result}");
                                    continue;
                                }

                                var eventFormat = sampleEventInfo.Format;
                                if (eventFormat == null)
                                {
                                    // Unexpected: Did not find TraceFS format info for this event. Unexpected.
                                    Console.WriteLine($"Sample: no format");
                                    continue;
                                }

                                if (eventFormat.DecodingStyle != PerfEventDecodingStyle.EventHeader ||
                                    !enumerator.StartEvent(eventFormat.Name, sampleEventInfo.UserData))
                                {
                                    // TraceFS-based event decoding.

                                    Console.WriteLine($"Sample: TraceFS {eventFormat.SystemName}:{eventFormat.Name} @ {sampleEventInfo.DateTime:s}");

                                    // Typically the "common" fields are not interesting, so skip them.
                                    var fieldsStart = eventFormat.CommonFieldCount;
                                    var fieldsEnd = eventFormat.Fields.Count;
                                    for (int i = fieldsStart; i < fieldsEnd; i += 1)
                                    {
                                        var fieldFormat = eventFormat.Fields[i];
                                        var fieldValue = fieldFormat.GetFieldValue(sampleEventInfo.RawDataSpan, sampleEventInfo.ByteReader);

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
                                    // EventHeader-based event decoding.

                                    var eventInfo = enumerator.GetEventInfo(); // Information about the event.
                                    Console.WriteLine($"Sample: EventHeader {eventInfo.NameAsString} @ {sampleEventInfo.DateTime:s}");

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
