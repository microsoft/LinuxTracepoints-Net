using System;
using System.IO;
using Text = System.Text;
using UnitTesting = Microsoft.VisualStudio.TestTools.UnitTesting;

namespace TypesTest
{
    [UnitTesting.TestClass]
    [UnitTesting.DeploymentItem("EventHeaderInterceptorLE64.dat")]
    [UnitTesting.DeploymentItem("EventHeaderInterceptorLE64.json")]
    public class TestEventEnumerator
    {
        private static readonly char[] LineSplitChars = new char[] { '\r', '\n' };

        [UnitTesting.TestMethod]
        public void DecodeDat()
        {
            byte[] datBytes = File.ReadAllBytes("EventHeaderInterceptorLE64.dat");
            string expected = File.ReadAllText("EventHeaderInterceptorLE64.json");
            using (var output = new StringWriter())
            {
                var decode = new DatDecode(output);
                output.Write("[");
                decode.Decode(datBytes);
                output.WriteLine(" ]");

                string actual = output.ToString();
                if (expected != actual)
                {
                    File.WriteAllText("EventHeaderInterceptorLE64.json.actual", actual, Text.Encoding.UTF8);
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
