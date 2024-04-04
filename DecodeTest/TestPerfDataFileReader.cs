namespace DecodeTest
{
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using System.IO;
    using System.Text.Json;

    [TestClass]
    public class TestPerfDataFileReader
    {
        public TestContext TestContext { get; set; } = null!;

        private void Decode(string inputName)
        {
            var buffer = JsonCompare.CreateBuffer();
            using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
            {
                using (var decode = new DecodePerf.PerfDataDecode(writer))
                {
                    writer.WriteStartArray();
                    decode.DecodeFile(Path.Combine(TestContext.TestDeploymentDir, "input", inputName));
                    writer.WriteEndArray();
                }
            }

            JsonCompare.AssertSame(TestContext, inputName, buffer);
        }

        [TestMethod]
        public void DecodePerfData()
        {
            Decode("perf.data");
        }

        [TestMethod]
        public void DecodePipeData()
        {
            Decode("pipe.data");
        }
    }
}
