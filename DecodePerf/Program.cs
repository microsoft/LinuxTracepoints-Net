namespace DecodePerf
{
    using System;
    using Microsoft.LinuxTracepoints.Decode;

    internal class Program
    {
        static int Main(string[] args)
        {
            int result;
            try
            {
                using (var decode = new PerfDataDecode(Console.Out))
                {
                    var comma = false;

                    Console.Out.WriteLine('{');
                    foreach (var arg in args)
                    {
                        if (comma)
                        {
                            Console.WriteLine(',');
                        }
                        Console.Out.Write($@" ""{arg.Replace("\\", "\\\\")}"": [");
                        decode.DecodeFile(arg);
                        Console.Out.Write(" ]");
                        comma = true;
                    }

                    Console.Out.WriteLine('}');
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
