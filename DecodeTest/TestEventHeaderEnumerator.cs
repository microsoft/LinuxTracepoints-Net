namespace DecodeTest
{
    using Microsoft.LinuxTracepoints.Decode;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using System;
    using System.IO;
    using Encoding = System.Text.Encoding;
    using Logging = Microsoft.VisualStudio.TestTools.UnitTesting.Logging;

    [TestClass]
    //[UnitTesting.DeploymentItem("EventHeaderInterceptorLE64.dat")]
    //[UnitTesting.DeploymentItem("EventHeaderInterceptorLE64.json")]
    public class TestEventHeaderEnumerator
    {
        private static readonly char[] LineSplitChars = new char[] { '\r', '\n' };

        [TestMethod]
        public void DecodeDat()
        {
            string expected = File.ReadAllText("EventHeaderInterceptorLE64.json");
            using (var output = new StringWriter())
            {
                var decode = new DatDecode(output);
                output.Write("[");
                decode.DecodeFile("EventHeaderInterceptorLE64.dat");
                output.WriteLine(" ]");

                string actual = output.ToString();
                if (expected != actual)
                {
                    File.WriteAllText("EventHeaderInterceptorLE64.json.actual", actual, Encoding.UTF8);
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
