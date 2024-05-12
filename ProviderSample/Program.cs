namespace ProviderSample
{
    using Microsoft.LinuxTracepoints.Provider;
    using System;
    using System.Diagnostics.Tracing;

    internal static class Program
    {
        static void Main(string[] args)
        {
            args = new string[] { "EmptyEvent", "Event1 int arg1" };

            var g1 = Guid.NewGuid();
            var g2 = Guid.NewGuid();
            Console.WriteLine("  ActivityId={0}", g1);
            Console.WriteLine("  RelatedId={0}", g2);

            using (var prov = new EventHeaderDynamicProvider(
                "MyProv",
                new EventHeaderDynamicProviderOptions { GroupName = "mygroup"}))
            {
                Console.WriteLine($"prov = {prov}");
                Console.WriteLine($"  Name = {prov.Name}");
                Console.WriteLine($"  Options = {prov.Options}");

                var eb = new EventHeaderDynamicBuilder();

                for (int i = 0; i < args.Length; i += 1)
                {
                    eb.Reset(args[i])
                        .SetIdVersion(1, 2)
                        .SetOpcode(EventOpcode.Extension)
                        .SetTag(0x123);
                    var tp = prov.FindOrRegister(EventLevel.Informational, 1);
                    Console.WriteLine($"tp = {tp}");
                    Console.WriteLine($"  Name = {tp.Name}");
                    Console.WriteLine($"  RegisterResult = {tp.RegisterResult}");
                    Console.WriteLine($"  IsEnabled = {tp.IsEnabled}");
                    Console.WriteLine($"  Level = {tp.Level}");
                    Console.WriteLine($"  LevelByte = {tp.LevelByte:X}");
                    Console.WriteLine($"  Keyword = {tp.Keyword:X}");
                    Console.WriteLine("  Write = {0}", tp.Write(eb));
                    Console.WriteLine("  Write = {0}", tp.Write(eb, g1));
                    Console.WriteLine("  Write = {0}", tp.Write(eb, g1, g2));
                }
            }

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
