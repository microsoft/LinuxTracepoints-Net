// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace DecodeSample
{
    using System;

    /// <summary>
    /// Simple decoding of a perf.data file.
    /// </summary>
    internal static class Program
    {
        public static int Main(string[] args)
        {
            try
            {
                using (var dataToWriter = new DataToWriter(Console.Out, true))
                {
                    foreach (var arg in args)
                    {
                        Console.Out.WriteLine($"******* OpenFile: {arg}");
                        dataToWriter.WritePerfData(arg);
                    }
                }

                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
        }
    }
}
