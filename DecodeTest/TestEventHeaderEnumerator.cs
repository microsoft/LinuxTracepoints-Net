namespace DecodeTest
{
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using System;
    using System.Buffers;
    using System.IO;
    using System.Text.Json;
    using Encoding = System.Text.Encoding;
    using Logging = Microsoft.VisualStudio.TestTools.UnitTesting.Logging;

    [TestClass]
    public class TestEventHeaderEnumerator
    {
        private static readonly char[] LineSplitChars = new char[] { '\r', '\n' };

        [TestMethod]
        public void DecodeDat()
        {
            string expected = File.ReadAllText("EventHeaderInterceptorLE64.json");
            var buffer = new ArrayBufferWriter<byte>();
            using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true, SkipValidation = true }))
            {
                var decode = new DatDecode(writer);
                writer.WriteStartArray();
                decode.DecodeFile("EventHeaderInterceptorLE64.dat");
                writer.WriteEndArray();
                writer.Flush();

                var writtenBytes = buffer.WrittenSpan;
                string actual = Encoding.UTF8.GetString(writtenBytes);
                if (expected != actual)
                {
                    File.WriteAllBytes("EventHeaderInterceptorLE64.json.actual", writtenBytes.ToArray());
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
                        Logging.Logger.LogMessage("Line {0}:\nexpected = <{1}>\nactual   = <{2}>", i + 1, expectedLines[i], actualLines[i]);
                    }
                }

                Assert.IsFalse(anyDifferences, "Expected and actual output are different.");
            }
        }
    }
}
