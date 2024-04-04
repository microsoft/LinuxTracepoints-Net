namespace DecodeTest
{
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using System;
    using System.IO;
    using Encoding = System.Text.Encoding;
    using Logging = Microsoft.VisualStudio.TestTools.UnitTesting.Logging;

    internal static class JsonCompare
    {
        private static readonly char[] LineSplitChars = new char[] { '\r', '\n' };

        public static void AssertSame(
            TestContext testContext,
            string inputFileName,
            ReadOnlySpan<byte> actualBytes)
        {
            var actualDirectory = Path.Combine(testContext.DeploymentDirectory, "actual");
            Directory.CreateDirectory(actualDirectory);

            var jsonFileName = inputFileName + ".json";
            var expectedFileName = Path.Combine(testContext.DeploymentDirectory, "expected", jsonFileName);
            var expectedText = File.ReadAllText(expectedFileName, Encoding.UTF8);
            var expectedLines = expectedText.Split(LineSplitChars, StringSplitOptions.RemoveEmptyEntries);

            var actualFileName = Path.Combine(actualDirectory, jsonFileName);
            var actualText = Encoding.UTF8.GetString(actualBytes);
            var actualLines = actualText.Split(LineSplitChars, StringSplitOptions.RemoveEmptyEntries);

            File.WriteAllBytes(actualFileName, actualBytes.ToArray());
            testContext.AddResultFile(actualFileName);

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
