namespace DecodeTest
{
    using Microsoft.LinuxTracepoints.Decode;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using System;
    using System.Buffers;
    using System.IO;
    using System.Text.Json;

    [TestClass]
    public class TestEventHeaderEnumerator
    {
        public TestContext TestContext { get; set; } = null!;

        private void Decode(string inputName)
        {
            var bufferMoveNext = JsonCompare.CreateBuffer();
            using (var writer = new Utf8JsonWriter(bufferMoveNext, new JsonWriterOptions { Indented = true }))
            {
                var decode = new DatDecode(writer);
                writer.WriteStartArray();
                decode.DecodeFile(Path.Combine(TestContext.TestDeploymentDir, "input", inputName), false);
                writer.WriteEndArray();
            }

            JsonCompare.AssertSame(TestContext, inputName, bufferMoveNext);

            var bufferMoveNextSibling = JsonCompare.CreateBuffer();
            using (var writer = new Utf8JsonWriter(bufferMoveNextSibling, new JsonWriterOptions { Indented = true }))
            {
                var decode = new DatDecode(writer);
                writer.WriteStartArray();
                decode.DecodeFile(Path.Combine(TestContext.TestDeploymentDir, "input", inputName), true);
                writer.WriteEndArray();
            }

            if (!bufferMoveNext.WrittenSpan.SequenceEqual(bufferMoveNextSibling.WrittenSpan))
            {
                // A bit of a hack, but we want to see the differences in the JSON output.
                JsonCompare.AssertSame(TestContext, inputName, bufferMoveNextSibling);
            }
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
            new PerfEventTimeSpec().ToString();
            new PerfEventTimeSpec(long.MinValue, 999999999).ToString();
            new PerfEventTimeSpec(long.MaxValue, 999999999).ToString();
            new PerfNonSampleEventInfo().ToString();
            new PerfSampleEventInfo().ToString();
            new PerfValue().ToString();
        }
    }
}
