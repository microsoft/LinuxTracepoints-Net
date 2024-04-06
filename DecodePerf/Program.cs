// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace DecodePerf
{
    using System;
    using System.Text.Json;
    using PerfDataFileEventOrder = Microsoft.LinuxTracepoints.Decode.PerfDataFileEventOrder;

    internal class Program
    {
        static int Main(string[] args)
        {
            int result;
            try
            {
                using (var output = Console.OpenStandardOutput())
                {
                    using (var writer = new Utf8JsonWriter(output, new JsonWriterOptions { Indented = true, SkipValidation = true }))
                    {
                        var decode = new PerfDataDecode(writer);
                        writer.WriteStartArray();
                        foreach (var arg in args)
                        {
                            decode.DecodeFile(arg, PerfDataFileEventOrder.File);
                        }
                        writer.WriteEndArray();
                    }
                }

                result = 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine(ex.Message);
                result = 1;
            }

            return result;
        }
    }
}
