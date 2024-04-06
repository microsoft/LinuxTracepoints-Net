namespace DecodeTest
{
    using Microsoft.LinuxTracepoints;
    using Microsoft.LinuxTracepoints.Decode;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using System;
    using System.Text;

    [TestClass]
    public class TestPerfValue
    {
        [TestMethod]
        public void Strings()
        {
            const EventHeaderFieldEncoding B8 = EventHeaderFieldEncoding.StringLength16Char8;
            const EventHeaderFieldEncoding B16 = EventHeaderFieldEncoding.StringLength16Char16;
            const EventHeaderFieldEncoding B32 = EventHeaderFieldEncoding.StringLength16Char32;
            const EventHeaderFieldFormat F8 = EventHeaderFieldFormat.String8;
            const EventHeaderFieldFormat FUtf = EventHeaderFieldFormat.StringUtf;
            var latin1 = PerfConvert.EncodingLatin1;
            var utf8 = Encoding.UTF8;
            var utf16LE = Encoding.Unicode;
            var utf16BE = Encoding.BigEndianUnicode;
            var utf32LE = Encoding.UTF32;
            var utf32BE = PerfConvert.EncodingUTF32BE;

            var abcB8S8 = MakeStringValue(latin1.GetBytes("abc"), B8, F8);
            Assert.AreEqual("abc", abcB8S8.FormatScalar());
            Assert.AreEqual("String8:abc", abcB8S8.ToString());
            CheckStringBytes(abcB8S8, latin1, 0);

            var abcB8SUtf = MakeStringValue(utf8.GetBytes("abc"), B8, FUtf);
            Assert.AreEqual("abc", abcB8SUtf.FormatScalar());
            Assert.AreEqual("StringUtf8:abc", abcB8SUtf.ToString());
            CheckStringBytes(abcB8SUtf, utf8, 0);

            var abcB16SUtfLE = MakeStringValue(utf16LE.GetBytes("abc"), B16, FUtf, false);
            Assert.AreEqual("abc", abcB16SUtfLE.FormatScalar());
            Assert.AreEqual("StringUtf16:abc", abcB16SUtfLE.ToString());
            CheckStringBytes(abcB16SUtfLE, utf16LE, 0);

            var abcB16SUtfBE = MakeStringValue(utf16BE.GetBytes("abc"), B16, FUtf, true);
            Assert.AreEqual("abc", abcB16SUtfBE.FormatScalar());
            Assert.AreEqual("StringUtf16:abc", abcB16SUtfBE.ToString());
            CheckStringBytes(abcB16SUtfBE, utf16BE, 0);

            var abcB32SUtfLE = MakeStringValue(utf32LE.GetBytes("abc"), B32, FUtf, false);
            Assert.AreEqual("abc", abcB32SUtfLE.FormatScalar());
            Assert.AreEqual("StringUtf32:abc", abcB32SUtfLE.ToString());
            CheckStringBytes(abcB32SUtfLE, utf32LE, 0);

            var abcB32SUtfBE = MakeStringValue(utf32BE.GetBytes("abc"), B32, FUtf, true);
            Assert.AreEqual("abc", abcB32SUtfBE.FormatScalar());
            Assert.AreEqual("StringUtf32:abc", abcB32SUtfBE.ToString());
            CheckStringBytes(abcB32SUtfBE, utf32BE, 0);

            for (var fUtfBom = EventHeaderFieldFormat.StringUtfBom; fUtfBom <= EventHeaderFieldFormat.StringJson; fUtfBom += 1)
            {
                var fstr = fUtfBom.ToString();

                var abcB8 = MakeStringValue(utf8.GetBytes("abc"), B8, fUtfBom);
                Assert.AreEqual("abc", abcB8.FormatScalar());
                Assert.AreEqual(fstr + "8:abc", abcB8.ToString());
                CheckStringBytes(abcB8, utf8, 0);

                var abcB16LE = MakeStringValue(utf16LE.GetBytes("abc"), B16, fUtfBom, false);
                Assert.AreEqual("abc", abcB16LE.FormatScalar());
                Assert.AreEqual(fstr + "16:abc", abcB16LE.ToString());
                CheckStringBytes(abcB16LE, utf16LE, 0);

                var abcB16BE = MakeStringValue(utf16BE.GetBytes("abc"), B16, fUtfBom, true);
                Assert.AreEqual("abc", abcB16BE.FormatScalar());
                Assert.AreEqual(fstr + "16:abc", abcB16BE.ToString());
                CheckStringBytes(abcB16BE, utf16BE, 0);

                var abcB32LE = MakeStringValue(utf32LE.GetBytes("abc"), B32, fUtfBom, false);
                Assert.AreEqual("abc", abcB32LE.FormatScalar());
                Assert.AreEqual(fstr + "32:abc", abcB32LE.ToString());
                CheckStringBytes(abcB32LE, utf32LE, 0);

                var abcB32BE = MakeStringValue(utf32BE.GetBytes("abc"), B32, fUtfBom, true);
                Assert.AreEqual("abc", abcB32BE.FormatScalar());
                Assert.AreEqual(fstr + "32:abc", abcB32BE.ToString());
                CheckStringBytes(abcB32BE, utf32BE, 0);

                var abcB8Bom8 = MakeStringValue(utf8.GetBytes("\uFEFFabc"), B8, fUtfBom);
                Assert.AreEqual("abc", abcB8Bom8.FormatScalar());
                Assert.AreEqual(fstr + "8:abc", abcB8Bom8.ToString());
                CheckStringBytes(abcB8Bom8, utf8, 3);

                var abcB8Bom16LE = MakeStringValue(utf16LE.GetBytes("\uFEFFabc"), B8, fUtfBom);
                Assert.AreEqual("abc", abcB8Bom16LE.FormatScalar());
                Assert.AreEqual(fstr + "8:abc", abcB8Bom16LE.ToString());
                CheckStringBytes(abcB8Bom16LE, utf16LE, 2);

                var abcB8Bom16BE = MakeStringValue(utf16BE.GetBytes("\uFEFFabc"), B8, fUtfBom);
                Assert.AreEqual("abc", abcB8Bom16BE.FormatScalar());
                Assert.AreEqual(fstr + "8:abc", abcB8Bom16BE.ToString());
                CheckStringBytes(abcB8Bom16BE, utf16BE, 2);

                var abcB8Bom32LE = MakeStringValue(utf32LE.GetBytes("\uFEFFabc"), B8, fUtfBom, true);
                Assert.AreEqual("abc", abcB8Bom32LE.FormatScalar());
                Assert.AreEqual(fstr + "8:abc", abcB8Bom32LE.ToString());
                CheckStringBytes(abcB8Bom32LE, utf32LE, 4);

                var abcB8Bom32BE = MakeStringValue(utf32BE.GetBytes("\uFEFFabc"), B8, fUtfBom, true);
                Assert.AreEqual("abc", abcB8Bom32BE.FormatScalar());
                Assert.AreEqual(fstr + "8:abc", abcB8Bom32BE.ToString());
                CheckStringBytes(abcB8Bom32BE, utf32BE, 4);

                var abcB16Bom16LE = MakeStringValue(utf16LE.GetBytes("\uFEFFabc"), B16, fUtfBom);
                Assert.AreEqual("abc", abcB16Bom16LE.FormatScalar());
                Assert.AreEqual(fstr + "16:abc", abcB16Bom16LE.ToString());
                CheckStringBytes(abcB16Bom16LE, utf16LE, 2);

                var abcB16Bom16BE = MakeStringValue(utf16BE.GetBytes("\uFEFFabc"), B16, fUtfBom);
                Assert.AreEqual("abc", abcB16Bom16BE.FormatScalar());
                Assert.AreEqual(fstr + "16:abc", abcB16Bom16BE.ToString());
                CheckStringBytes(abcB16Bom16BE, utf16BE, 2);

                var abcB16Bom32LE = MakeStringValue(utf32LE.GetBytes("\uFEFFabc"), B16, fUtfBom, true);
                Assert.AreEqual("abc", abcB16Bom32LE.FormatScalar());
                Assert.AreEqual(fstr + "16:abc", abcB16Bom32LE.ToString());
                CheckStringBytes(abcB16Bom32LE, utf32LE, 4);

                var abcB16Bom32BE = MakeStringValue(utf32BE.GetBytes("\uFEFFabc"), B16, fUtfBom, true);
                Assert.AreEqual("abc", abcB16Bom32BE.FormatScalar());
                Assert.AreEqual(fstr + "16:abc", abcB16Bom32BE.ToString());
                CheckStringBytes(abcB16Bom32BE, utf32BE, 4);

                var abcB32Bom16LE = MakeStringValue(utf16LE.GetBytes("\uFEFFabc"), B32, fUtfBom);
                Assert.AreEqual("abc", abcB32Bom16LE.FormatScalar());
                Assert.AreEqual(fstr + "32:abc", abcB32Bom16LE.ToString());
                CheckStringBytes(abcB32Bom16LE, utf16LE, 2);

                var abcB32Bom16BE = MakeStringValue(utf16BE.GetBytes("\uFEFFabc"), B32, fUtfBom);
                Assert.AreEqual("abc", abcB32Bom16BE.FormatScalar());
                Assert.AreEqual(fstr + "32:abc", abcB32Bom16BE.ToString());
                CheckStringBytes(abcB32Bom16BE, utf16BE, 2);

                var abcB32Bom32LE = MakeStringValue(utf32LE.GetBytes("\uFEFFabc"), B32, fUtfBom, true);
                Assert.AreEqual("abc", abcB32Bom32LE.FormatScalar());
                Assert.AreEqual(fstr + "32:abc", abcB32Bom32LE.ToString());
                CheckStringBytes(abcB32Bom32LE, utf32LE, 4);

                var abcB32Bom32BE = MakeStringValue(utf32BE.GetBytes("\uFEFFabc"), B32, fUtfBom, true);
                Assert.AreEqual("abc", abcB32Bom32BE.FormatScalar());
                Assert.AreEqual(fstr + "32:abc", abcB32Bom32BE.ToString());
                CheckStringBytes(abcB32Bom32BE, utf32BE, 4);
            }
        }

        private static void CheckStringBytes(in PerfValue value, Encoding expected, int bomLength)
        {
            Encoding actual;
            var bytes = value.GetStringBytes(out actual);
            Assert.AreEqual(expected.CodePage, actual.CodePage);
            Assert.IsTrue(value.Bytes.Slice(bomLength) == bytes);
            if (bomLength != 0)
            {
                Assert.IsTrue(value.Bytes.Slice(0, bomLength).SequenceEqual(expected.GetPreamble()));
            }
        }

        private static PerfValue MakeStringValue(
            byte[] bytes,
            EventHeaderFieldEncoding encoding,
            EventHeaderFieldFormat format,
            bool fromBigEndian = false)
        {
            return new PerfValue(
                bytes,
                new PerfByteReader(fromBigEndian),
                encoding,
                format,
                0,
                1);
        }
    }
}
