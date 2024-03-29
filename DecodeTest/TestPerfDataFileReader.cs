namespace DecodeTest
{
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using System;
    using System.IO;
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
            using (var output = new StringWriter())
            {
                var decode = new DecodePerf.PerfDataDecode(output);
                output.Write("[");
                decode.DecodeFile(baseName);
                output.WriteLine(" ]");

                string actual = output.ToString();
                if (expected != actual)
                {
                    File.WriteAllText(baseName + ".json.actual", actual, Encoding.UTF8);
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
