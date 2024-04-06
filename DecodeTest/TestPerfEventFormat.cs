namespace DecodeTest
{
    using Microsoft.LinuxTracepoints.Decode;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using System;
    using Path = System.IO.Path;
    using ZipFile = System.IO.Compression.ZipFile;

    [TestClass]
    public class TestPerfEventFormat
    {
        private const int FakeEventDataSize = 1024;
        private const int FormatSizeMax = 8192; // Size of the largest format file.
        private readonly byte[] fakeEventData = new byte[FakeEventDataSize];
        private readonly byte[] formatBytes = new byte[FormatSizeMax];
        private readonly char[] formatChars = new char[FormatSizeMax];

        public TestContext TestContext { get; set; } = null!;

        [TestMethod]
        public void ParseFormats()
        {
            ParseFormat("formats0.zip");
        }

        /// <summary>
        /// Ensures that we can parse all of the format files without hitting any
        /// Debug.Assert failures, and the resulting fields can GetFieldValue without
        /// hitting any Debug.Assert failures.
        /// </summary>
        private void ParseFormat(string formatZipFileName)
        {
            var formatBytesSpan = this.formatBytes.AsSpan();
            var formatCharsSpan = this.formatChars.AsSpan();
            var fakeEventDataSpan = this.fakeEventData.AsSpan();
            using (var zip = ZipFile.OpenRead(Path.Combine(TestContext.TestDeploymentDir, "input", formatZipFileName)))
            {
                foreach (var formatEntry in zip.Entries)
                {
                    Assert.IsTrue(formatEntry.Length <= FormatSizeMax, "Format file too large");
                    var entryLength = (int)formatEntry.Length;

                    var name = formatEntry.Name;
                    var nameParts = name.Split(' ', 2); // All files should be named "SystemName EventName"
                    var systemName = nameParts[0];
                    var eventName = nameParts[1];

                    using (var stream = formatEntry.Open())
                    {
                        var bytesRead = stream.Read(formatBytesSpan);
                        Assert.AreEqual(entryLength, bytesRead);
                        var charsRead = PerfConvert.EncodingLatin1.GetChars(formatBytesSpan.Slice(0, bytesRead), formatCharsSpan);
                        Assert.AreEqual(entryLength, charsRead);
                    }

                    var format = PerfEventFormat.Parse(IntPtr.Size == 8, systemName, formatCharsSpan.Slice(0, entryLength));
                    Assert.IsNotNull(format);
                    Assert.AreEqual(systemName, format.SystemName);
                    Assert.AreEqual(eventName, format.Name);
                    Assert.IsNotNull(format.PrintFmt);
                    Assert.IsNotNull(format.Fields);
                    Assert.AreNotEqual(0, format.Id);
                    Assert.IsTrue(format.CommonFieldCount <= format.Fields.Count);

                    foreach (var field in format.Fields)
                    {
                        Assert.IsNotNull(field.Name);
                        Assert.IsNotNull(field.Field);
                        field.GetFieldBytes(fakeEventDataSpan, PerfByteReader.HostEndian);
                        field.GetFieldBytes(fakeEventDataSpan, PerfByteReader.SwapEndian);
                        field.GetFieldValue(fakeEventDataSpan, PerfByteReader.HostEndian);
                        field.GetFieldValue(fakeEventDataSpan, PerfByteReader.SwapEndian);
                    }
                }
            }
        }
    }
}
