// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace DecodeSample
{
    using Microsoft.LinuxTracepoints.Decode;
    using System;

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
                                    Console.WriteLine($"{eventBytes.Header.Type}/{nonSampleEventInfo.Name}: {nonSampleEventInfo.DateTime}");
                                }
                                else if (result == PerfDataFileResult.IdNotFound)
                                {
                                    // Event info not available. This is expected in a many cases, such
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
                                // Sample event, e.g. tracepoint event. PerfSampleEventInfo metadata may be
                                // available and is usually needed.
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

                                    Console.WriteLine($"Sample: TraceFS {eventFormat.SystemName}:{eventFormat.Name} @ {sampleEventInfo.DateTime}");

                                    // Typically the "common" fields are not interesting, so skip them.
                                    var fieldsStart = eventFormat.CommonFieldCount;
                                    var fieldsEnd = eventFormat.Fields.Count;
                                    for (int i = fieldsStart; i < fieldsEnd; i += 1)
                                    {
                                        var fieldFormat = eventFormat.Fields[i];
                                        var fieldValue = fieldFormat.GetFieldValue(sampleEventInfo.RawDataSpan, sampleEventInfo.ByteReader);

                                        // fieldValue has lots of properties for getting its value in different formats.
                                        Console.WriteLine($"  {fieldFormat.Name}={fieldValue.ToString()}");
                                    }
                                }
                                else
                                {
                                    // EventHeader-based event decoding.

                                    var eventInfo = enumerator.GetEventInfo(); // Information about the event.
                                    Console.WriteLine($"Sample: EventHeader {eventInfo.NameAsString} @ {sampleEventInfo.DateTime}");

                                    // Skip past BeforeFirstItem.
                                    while (enumerator.MoveNextSibling())
                                    {
                                        var itemInfo = enumerator.GetItemInfo(); // Information about the item.
                                        Console.WriteLine($"  {itemInfo.NameAsString}={itemInfo.Value.ToString()}");
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
