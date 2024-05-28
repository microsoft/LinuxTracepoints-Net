// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace DecodeSample
{
    using Microsoft.LinuxTracepoints.Decode;
    using System;
    using StringBuilder = System.Text.StringBuilder;
    using TextWriter = System.IO.TextWriter;

    /// <summary>
    /// Simple class illustrating how to read a perf data file and write its
    /// contents to a TextWriter.
    /// </summary>
    internal sealed class DataToWriter : IDisposable
    {
        private readonly PerfDataFileReader reader = new PerfDataFileReader();
        private readonly EventHeaderEnumerator enumerator = new EventHeaderEnumerator();
        private readonly StringBuilder scratch = new StringBuilder();
        private readonly TextWriter writer;
        private readonly bool leaveOpenWriter;

        public DataToWriter(TextWriter writer, bool leaveOpen)
        {
            this.writer = writer;
            this.leaveOpenWriter = leaveOpen;
        }

        public void Dispose()
        {
            this.reader.Dispose();
            if (!this.leaveOpenWriter)
            {
                this.writer.Dispose();
            }
        }

        public void WritePerfData(string perfDataFilePath)
        {
            // Open the file. Ask reader to sort the events by timestamp (sorted
            // in chunks bounded by the FinishedRound events). The alternative is
            // to read the events in the order they appear in the file (slightly
            // less overhead in cases where the event order doesn't matter).
            if (!this.reader.OpenFile(perfDataFilePath, PerfDataFileEventOrder.Time))
            {
                this.writer.WriteLine($"OpenFile error: Invalid data");
                return;
            }

            while (true)
            {
                var result = this.reader.ReadEvent(out var eventBytes);
                if (result != PerfDataFileResult.Ok)
                {
                    if (result != PerfDataFileResult.EndOfFile)
                    {
                        // Unexpected. This usually means a corrupt file.
                        this.writer.WriteLine($"ReadEvent error: {result.AsString()}");
                    }
                    break; // No more events.
                }

                if (eventBytes.Header.Type != PerfEventHeaderType.Sample)
                {
                    // Non-sample event, typically information about the system or information
                    // about the trace itself.

                    // Event info (timestamp, cpu, pid, etc.) may be available.
                    PerfNonSampleEventInfo nonSampleEventInfo;
                    result = this.reader.GetNonSampleEventInfo(eventBytes, out nonSampleEventInfo);
                    if (result != PerfDataFileResult.Ok &&          // Success getting event info.
                        result != PerfDataFileResult.IdNotFound)    // Event info not available (common).
                    {
                        // Unexpected: error getting event info.
                        this.writer.WriteLine($"GetNonSampleEventInfo error: {result.AsString()}");
                    }

                    this.writer.WriteLine($"NonSample: {eventBytes.Header.Type.AsString()}");
                    this.writer.WriteLine($"  size = {eventBytes.Header.Size}");

                    if (result == PerfDataFileResult.Ok)
                    {
                        // Event info was found. Include it in the output.
                        this.scratch.Clear();
                        nonSampleEventInfo.AppendJsonEventInfoTo(this.scratch);
                        this.writer.WriteLine($"  info = {{ {this.scratch} }}");
                    }
                }
                else
                {
                    // Sample event, e.g. tracepoint event.

                    // Event info (timestamp, cpu, pid, etc.) may be available.
                    PerfSampleEventInfo sampleEventInfo;
                    result = this.reader.GetSampleEventInfo(eventBytes, out sampleEventInfo);
                    if (result != PerfDataFileResult.Ok)
                    {
                        // Unexpected: error getting event info.
                        this.writer.WriteLine($"GetSampleEventInfo error: {result.AsString()}");
                        this.writer.WriteLine($"  size = {eventBytes.Header.Size}");
                        continue; // Usually can't make use of the event without the metadata.
                    }

                    this.writer.WriteLine($"Sample: {sampleEventInfo.GetName()}");
                    this.writer.WriteLine($"  size = {eventBytes.Header.Size}");

                    // Found event info (attributes). Include data from it in the output.
                    // Will be written to output later, after we know the format of the event.
                    this.scratch.Clear();
                    sampleEventInfo.AppendJsonEventInfoTo(this.scratch);

                    var eventFormat = sampleEventInfo.Format;
                    if (eventFormat.IsEmpty)
                    {
                        // Unexpected: Did not find TraceFS format metadata for this event.
                        this.writer.WriteLine($"  info = {{ {this.scratch} }}");
                        this.writer.WriteLine($"  no format");
                    }
                    else if (eventFormat.DecodingStyle != PerfEventDecodingStyle.EventHeader ||
                        !this.enumerator.StartEvent(sampleEventInfo))
                    {
                        // Decode using TraceFS format metadata.
                        this.writer.WriteLine($"  info = {{ {this.scratch} }}");

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
                            this.scratch.Clear();
                            fieldValue.AppendJsonTo(this.scratch);
                            this.writer.WriteLine($"  {fieldFormat.Name} = {this.scratch}");
                        }
                    }
                    else
                    {
                        // Decode using EventHeader metadata.

                        // eventInfo has a bunch of information about the event.
                        // We won't use it in this example, since we get the same information in JSON
                        // format from AppendJsonEventInfoTo.
                        var eventInfo = this.enumerator.GetEventInfo();

                        // Add the EventHeader-specific info.
                        this.enumerator.AppendJsonEventInfoTo(this.scratch, this.scratch.Length != 0);
                        this.writer.WriteLine($"  info = {{ {this.scratch} }}");

                        // Transition past the initial BeforeFirstItem state.
                        this.enumerator.MoveNext();

                        // This will loop once for each top-level item in the event.
                        while (this.enumerator.State >= EventHeaderEnumeratorState.BeforeFirstItem)
                        {
                            var itemInfo = this.enumerator.GetItemInfo(); // Information about the item.

                            // itemInfo.Value has lots of properties and methods for accessing its data in different
                            // formats, but they only work for simple values -- scalar, array element, or array of
                            // fixed-size elements. For complex values such as structs or arrays of variable-size
                            // elements, you need to use the enumerator to access the sub-items. In this example,
                            // we use the enumerator to convert the current item to a JSON-formatted string.
                            // In the case of a simple item, it will be the same as itemInfo.Value.AppendJsonScalarTo().
                            // In the case of a complex item, it will recursively format the item and its sub-items.
                            this.scratch.Clear();
                            this.enumerator.AppendJsonItemToAndMoveNextSibling(
                                this.scratch,
                                false,
                                PerfConvertOptions.Default & ~PerfConvertOptions.RootName); // We don't want a JSON "ItemName": prefix.
                            this.writer.WriteLine($"  {itemInfo.GetNameAsString()} = {this.scratch}");
                        }

                        if (this.enumerator.State == EventHeaderEnumeratorState.Error)
                        {
                            // Unexpected: Error decoding event.
                            this.writer.WriteLine($"  MoveNext error: {this.enumerator.LastError}");
                        }
                    }
                }
            }
        }
    }
}
