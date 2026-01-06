namespace DecodeTest
{
    using Microsoft.LinuxTracepoints.Decode;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using System;
    using System.IO;
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
        [DeploymentItem(@"input/formats0.zip", @"input")]
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
            Assert.IsNotNull(TestContext.DeploymentDirectory);
            
            var actualDirectory = Path.Combine(TestContext.DeploymentDirectory, "actual");
            Directory.CreateDirectory(actualDirectory);
            var logFileName = Path.Combine(actualDirectory, Path.ChangeExtension(formatZipFileName, "log"));
            using (var log = new StreamWriter(logFileName))
            {
                var formatBytesSpan = this.formatBytes.AsSpan();
                var formatCharsSpan = this.formatChars.AsSpan();
                var fakeEventDataSpan = this.fakeEventData.AsSpan();
                using (var zip = ZipFile.OpenRead(Path.Combine(TestContext.DeploymentDirectory, "input", formatZipFileName)))
                {
                    foreach (var formatEntry in zip.Entries)
                    {
                        Assert.IsLessThanOrEqualTo(FormatSizeMax, formatEntry.Length, "Format file too large");
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

                        var format = PerfEventFormat.Parse(false, systemName, formatCharsSpan.Slice(0, entryLength));
                        Assert.IsNotNull(format);
                        Assert.AreEqual(systemName, format.SystemName);
                        Assert.AreEqual(eventName, format.Name);
                        Assert.IsNotNull(format.PrintFmt);
                        Assert.IsNotNull(format.Fields);
                        Assert.AreNotEqual<uint>(0, format.Id);
                        Assert.IsLessThanOrEqualTo(format.Fields.Count, format.CommonFieldCount);

                        log.WriteLine(name);
                        log.WriteLine("  sys={0}, nam={1}, id={2}, cfc={3}, cfs={4} ds={5}",
                                format.SystemName,
                                format.Name,
                                format.Id,
                                format.CommonFieldCount,
                                format.CommonFieldsSize,
                                format.DecodingStyle);

                        foreach (var field in format.Fields)
                        {
                            Assert.IsNotNull(field.Name);
                            Assert.IsNotNull(field.Field);
                            field.GetFieldBytes(fakeEventDataSpan, PerfByteReader.HostEndian);
                            field.GetFieldBytes(fakeEventDataSpan, PerfByteReader.SwapEndian);
                            field.GetFieldValue(fakeEventDataSpan, PerfByteReader.HostEndian);
                            field.GetFieldValue(fakeEventDataSpan, PerfByteReader.SwapEndian);

                            var signedStr = !field.Signed.HasValue
                                ? "default"
                                : field.Signed.Value
                                ? "signed"
                                : "unsigned";
                            log.WriteLine("  {0}: \"{1}\" {2} {3} {4}",
                                field.Name,
                                field.Field,
                                field.Offset,
                                field.Size,
                                signedStr);
                            log.WriteLine("  - array: {0} raw={1} deduced={2}",
                                field.Array,
                                field.SpecifiedArrayCount,
                                field.DeducedArrayCount);
                            log.WriteLine("  - enc: raw={0}/{1} deduced={2}/{3}",
                                (int)field.SpecifiedEncoding,
                                (int)field.SpecifiedFormat,
                                (int)field.DeducedEncoding,
                                (int)field.DeducedFormat);
                            log.WriteLine("  - element: size={0} shift={1}",
                                (byte)(1 << (field.ElementSizeShift & 0x1F)),
                                field.ElementSizeShift);
                        }
                    }
                }
            }
        }
    }
}
