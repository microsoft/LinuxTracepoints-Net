namespace DecodeTest
{
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using System;
    using System.Buffers;
    using System.IO;
    using System.Text.Json;
    using Encoding = System.Text.Encoding;
    using Logger = Microsoft.VisualStudio.TestTools.UnitTesting.Logging.Logger;

    [TestClass]
    public class TestPerfDataFileReader
    {
        private static readonly char[] LineSplitChars = new char[] { '\r', '\n' };

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

        private static void Decode(string baseName)
        {
            string expected = File.ReadAllText(baseName + ".json");
            var buffer = new ArrayBufferWriter<byte>();
            using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true, SkipValidation = true }))
            {
                var decode = new DecodePerf.PerfDataDecode(writer);
                writer.WriteStartArray();
                decode.DecodeFile(baseName);
                writer.WriteEndArray();
                writer.Flush();

                var writtenBytes = buffer.WrittenSpan;
                string actual = Encoding.UTF8.GetString(writtenBytes);
                if (expected != actual)
                {
                    File.WriteAllBytes(baseName + ".json.actual", writtenBytes.ToArray());
                }

                string[] expectedLines = expected.Split(LineSplitChars, StringSplitOptions.RemoveEmptyEntries);
                string[] actualLines = actual.Split(LineSplitChars, StringSplitOptions.RemoveEmptyEntries);
                Assert.AreEqual(expectedLines.Length, actualLines.Length);

                bool anyDifferences = false;
                for (var i = 0; i < expectedLines.Length; i++)
                {
                    if (expectedLines[i] != actualLines[i])
                    {
                        anyDifferences = true;
                        Logger.LogMessage("Line {0}:\nexpected = <{1}>\nactual   = <{2}>", i + 1, expectedLines[i], actualLines[i]);
                    }
                }

                Assert.IsFalse(anyDifferences, "Expected and actual output are different.");
            }
        }
    }
}
