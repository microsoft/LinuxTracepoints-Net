namespace DecodePerf
{
    using System;
    using System.Buffers;
    using System.Text.Json;
    using Microsoft.LinuxTracepoints.Decode;

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
                            decode.DecodeFile(arg);
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
