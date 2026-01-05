namespace DecodeTest
{
    using Microsoft.LinuxTracepoints.Decode;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using Path = System.IO.Path;

    [TestClass]
    public class TestEventHeaderEnumerator
    {
        public TestContext TestContext { get; set; } = null!;

        private void Decode(string inputName)
        {
            var writer = new JsonStringWriter(2);
            var decode = new DatDecode(writer);

            writer.Reset();
            writer.WriteStartArray();
            decode.DecodeFile(Path.Combine(TestContext.DeploymentDirectory, "input", inputName));
            writer.WriteEndArrayOnNewLine();

            var result = writer.ToString();
            TextCompare.AssertSame(TestContext, inputName + ".json", result);
        }

        [TestMethod]
        public void DecodeDat()
        {
            Decode("EventHeaderInterceptorLE64.dat");
        }

        [TestMethod]
        public void EmptyToString()
        {
            // Make sure empty structs can be converted to strings without throwing.
            new EventHeaderEventInfo().ToString();
            new EventHeaderItemInfo().ToString();
            new PerfByteReader().ToString();
            new PerfEventBytes().ToString();
            new PerfTimeSpec().ToString();
            new PerfTimeSpec(long.MinValue, 999999999).ToString();
            new PerfTimeSpec(long.MaxValue, 999999999).ToString();
            new PerfNonSampleEventInfo().ToString();
            new PerfSampleEventInfo().ToString();
            new PerfItemValue().ToString();
        }
    }
}
