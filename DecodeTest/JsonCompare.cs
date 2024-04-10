﻿namespace DecodeTest
{
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using System;
    using System.IO;
    using Encoding = System.Text.Encoding;
    using Logging = Microsoft.VisualStudio.TestTools.UnitTesting.Logging;

    internal static class JsonCompare
    {
        private static readonly char[] LineSplitChars = new char[] { '\r', '\n' };
        private static readonly byte[] Utf8Preamble = Encoding.UTF8.GetPreamble();

        public static MemoryStream CreateStream()
        {
            var stream = new MemoryStream();
            stream.Write(Utf8Preamble);
            return stream;
        }

        public static void AssertSame(
            TestContext testContext,
            string baseFileName,
            string actualText)
        {
            var jsonFileName = baseFileName + ".json";
            var actualDirectory = Path.Combine(testContext.DeploymentDirectory, "actual");
            Directory.CreateDirectory(actualDirectory);

            var expectedFileName = Path.Combine(testContext.DeploymentDirectory, "expected", jsonFileName);
            var expectedText = File.ReadAllText(expectedFileName, Encoding.UTF8);
            var expectedLines = expectedText.Split(LineSplitChars, StringSplitOptions.RemoveEmptyEntries);

            var actualFileName = Path.Combine(actualDirectory, jsonFileName);
            var actualLines = actualText .Split(LineSplitChars, StringSplitOptions.RemoveEmptyEntries);

            using (var stream = new StreamWriter(actualFileName, false, Encoding.UTF8))
            {
                stream.Write(actualText);
            }
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
