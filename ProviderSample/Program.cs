namespace ProviderSample
{
    using Microsoft.LinuxTracepoints.Provider;
    using System;

    internal static class Program
    {
        static void Main(string[] args)
        {
            args = new string[] { "EmptyEvent", "Event1 int arg1" };

            foreach (var arg in args)
            {
                Console.WriteLine();
                Console.WriteLine("[{0}]:", arg);
                using (var h = new PerfTracepoint(arg))
                {
                    Check(h);
                    h.Dispose();
                    Console.WriteLine("disposed:");
                    Check(h);
                }
            }
        }

        static void Check(PerfTracepoint h)
        {
            int[] x = { 2 };
            var span = new ReadOnlySpan<int>(x);
            Console.WriteLine("  IsEnabled = {0}", h.IsEnabled);
            Console.WriteLine("  RegisterResult = {0}", h.RegisterResult);
            Console.WriteLine("  Write = {0}", h.Write(span));
        }
    }
}
