namespace DecodeTest
{
    using System;
    using System.IO;
    using Text = System.Text;
    using UnitTesting = Microsoft.VisualStudio.TestTools.UnitTesting;

    [UnitTesting.TestClass]
    public class TestPerfDataFileReader
    {
        private static readonly char[] LineSplitChars = new char[] { '\r', '\n' };

        [UnitTesting.TestMethod]
        public void DecodePerfData()
        {
            this.Decode("perf.data");
        }

        [UnitTesting.TestMethod]
        public void DecodePipeData()
        {
            this.Decode("pipe.data");
        }

        private void Decode(string baseName)
        {
            string expected = File.ReadAllText(baseName + ".json");
            using (var output = new StringWriter())
            {
                var decode = new PerfDataDecode(output);
                output.Write("[");
                decode.DecodeFile(baseName);
                output.WriteLine(" ]");

                string actual = output.ToString();
                if (expected != actual)
                {
                    File.WriteAllText(baseName + ".json.actual", actual, Text.Encoding.UTF8);
                }

                string[] expectedLines = expected.Split(LineSplitChars, StringSplitOptions.RemoveEmptyEntries);
                string[] actualLines = actual.Split(LineSplitChars, StringSplitOptions.RemoveEmptyEntries);
                UnitTesting.Assert.AreEqual(expectedLines.Length, actualLines.Length);

                bool anyDifferences = false;
                for (var i = 0; i < expectedLines.Length; i++)
                {
                    if (expectedLines[i] != actualLines[i])
                    {
                        anyDifferences = true;
                        UnitTesting.Logging.Logger.LogMessage("Line {0}:\nexpected = <{1}>\nactual   = <{2}>", i + 1, expectedLines[i], actualLines[i]);
                    }
                }

                UnitTesting.Assert.IsFalse(anyDifferences, "Expected and actual output are different.");
            }
        }
    }
}
