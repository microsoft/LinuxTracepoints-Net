namespace DecodeTest
{
    using Microsoft.LinuxTracepoints.Decode;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using System.Buffers;
    using System.Globalization;
    using System.IO;
    using System.Text.Json;
    using DataToWriter = DecodeSample.DataToWriter;
    using DecodePerfJsonWriter = DecodePerfToJson.DecodePerfJsonWriter;
    using Encoding = System.Text.Encoding;

    [TestClass]
    public class TestPerfDataFileReader
    {
        public TestContext TestContext { get; set; } = null!;

        private void DecodeWithJsonWriter(string inputFileName)
        {
            var inputFilePath = Path.Combine(TestContext.DeploymentDirectory, "input", inputFileName);

            var buffer = new ArrayBufferWriter<byte>();
            using (var decode = new DecodePerfJsonWriter(
                buffer,
                new JsonWriterOptions { Indented = true }))
            {
                decode.ShowNonSample = true;
                decode.JsonWriter.WriteStartArray();

                decode.JsonWriter.WriteCommentValue(" Events in File order ");
                decode.WriteFile(inputFilePath, PerfDataFileEventOrder.File);

                decode.JsonWriter.WriteCommentValue(" Events in Time order ");
                decode.WriteFile(inputFilePath, PerfDataFileEventOrder.Time);

                decode.JsonWriter.WriteEndArray();
            }

            TextCompare.AssertSame(TestContext, inputFileName + ".json", Encoding.UTF8.GetString(buffer.WrittenSpan));
        }

        private void DecodeWithDataToWriter(string inputFileName)
        {
            var inputFilePath = Path.Combine(TestContext.DeploymentDirectory, "input", inputFileName);

            var writer = new StringWriter(CultureInfo.InvariantCulture);
            using (var dataToWriter = new DataToWriter(writer, false))
            {
                dataToWriter.WritePerfData(inputFilePath);
            }

            TextCompare.AssertSame(TestContext, inputFileName + ".txt", writer.ToString());
        }

        [TestMethod]
        public void DecodePerfData()
        {
            var inputName = "perf.data";
            DecodeWithJsonWriter(inputName);
            DecodeWithDataToWriter(inputName);
        }

        [TestMethod]
        public void DecodePipeData()
        {
            var inputName = "pipe.data";
            DecodeWithJsonWriter(inputName);
            DecodeWithDataToWriter(inputName);
        }
    }
}
