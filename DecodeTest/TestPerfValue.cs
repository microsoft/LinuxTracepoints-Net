namespace DecodeTest
{
    using Microsoft.LinuxTracepoints;
    using Microsoft.LinuxTracepoints.Decode;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using System;
    using System.Text;
    using BinaryPrimitives = System.Buffers.Binary.BinaryPrimitives;
    using IPAddress = System.Net.IPAddress;

    [TestClass]
    public class TestPerfValue
    {
        private const string JsonABC = "\"abc\"";
        private readonly StringBuilder builder = new StringBuilder();

        [TestMethod]
        public void Conversions()
        {
            var rng = new Random();
            var bytes = new byte[16];
            CheckIPv6(IPAddress.IPv6Any);
            CheckIPv6(IPAddress.IPv6Loopback);
            CheckIPv6(IPAddress.IPv6None);
            for (int i = 0; i < 10; i += 1)
            {
                rng.NextBytes(bytes);
                CheckIPv6(new IPAddress(bytes));
            }

            CheckTime32(0);
            CheckTime32(1);
            CheckTime32(-1);
            CheckTime32(int.MinValue);
            CheckTime32(int.MaxValue);

            CheckTime64(0);
            CheckTime64(1);
            CheckTime64(-1);
            CheckTime64(int.MinValue);
            CheckTime64(int.MaxValue);
            CheckTime64(long.MinValue);
            CheckTime64(long.MaxValue);
        }

        [TestMethod]
        public void Strings()
        {
            const EventHeaderFieldEncoding BIN = EventHeaderFieldEncoding.BinaryLength16Char8; // TODO
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

            var abcBINS8 = MakeStringValue(latin1.GetBytes("abc"), BIN, F8);
            Assert.AreEqual(JsonABC, BuilderToString(abcBINS8.AppendJsonScalarTo(builder)));
            Assert.AreEqual("abc", abcBINS8.ToString());
            CheckStringBytes(abcBINS8, latin1, 0);

            var abcBINSUtf = MakeStringValue(utf8.GetBytes("abc"), BIN, FUtf);
            Assert.AreEqual(JsonABC, BuilderToString(abcBINSUtf.AppendJsonScalarTo(builder)));
            Assert.AreEqual("abc", abcBINSUtf.ToString());
            CheckStringBytes(abcBINSUtf, utf8, 0);

            var abcB8S8 = MakeStringValue(latin1.GetBytes("abc"), B8, F8);
            Assert.AreEqual(JsonABC, BuilderToString(abcB8S8.AppendJsonScalarTo(builder)));
            Assert.AreEqual("abc", abcB8S8.ToString());
            CheckStringBytes(abcB8S8, latin1, 0);

            var abcB8SUtf = MakeStringValue(utf8.GetBytes("abc"), B8, FUtf);
            Assert.AreEqual(JsonABC, BuilderToString(abcB8SUtf.AppendJsonScalarTo(builder)));
            Assert.AreEqual("abc", abcB8SUtf.ToString());
            CheckStringBytes(abcB8SUtf, utf8, 0);

            var abcB16SUtfLE = MakeStringValue(utf16LE.GetBytes("abc"), B16, FUtf, false);
            Assert.AreEqual(JsonABC, BuilderToString(abcB16SUtfLE.AppendJsonScalarTo(builder)));
            Assert.AreEqual("abc", abcB16SUtfLE.ToString());
            CheckStringBytes(abcB16SUtfLE, utf16LE, 0);

            var abcB16SUtfBE = MakeStringValue(utf16BE.GetBytes("abc"), B16, FUtf, true);
            Assert.AreEqual(JsonABC, BuilderToString(abcB16SUtfBE.AppendJsonScalarTo(builder)));
            Assert.AreEqual("abc", abcB16SUtfBE.ToString());
            CheckStringBytes(abcB16SUtfBE, utf16BE, 0);

            var abcB32SUtfLE = MakeStringValue(utf32LE.GetBytes("abc"), B32, FUtf, false);
            Assert.AreEqual(JsonABC, BuilderToString(abcB32SUtfLE.AppendJsonScalarTo(builder)));
            Assert.AreEqual("abc", abcB32SUtfLE.ToString());
            CheckStringBytes(abcB32SUtfLE, utf32LE, 0);

            var abcB32SUtfBE = MakeStringValue(utf32BE.GetBytes("abc"), B32, FUtf, true);
            Assert.AreEqual(JsonABC, BuilderToString(abcB32SUtfBE.AppendJsonScalarTo(builder)));
            Assert.AreEqual("abc", abcB32SUtfBE.ToString());
            CheckStringBytes(abcB32SUtfBE, utf32BE, 0);

            for (var fUtfBom = EventHeaderFieldFormat.StringUtfBom; fUtfBom <= EventHeaderFieldFormat.StringJson; fUtfBom += 1)
            {
                var abcBIN = MakeStringValue(utf8.GetBytes("abc"), BIN, fUtfBom);
                Assert.AreEqual(JsonABC, BuilderToString(abcBIN.AppendJsonScalarTo(builder)));
                Assert.AreEqual("abc", abcBIN.ToString());
                CheckStringBytes(abcBIN, utf8, 0);

                var abcB8 = MakeStringValue(utf8.GetBytes("abc"), B8, fUtfBom);
                Assert.AreEqual(JsonABC, BuilderToString(abcB8.AppendJsonScalarTo(builder)));
                Assert.AreEqual("abc", abcB8.ToString());
                CheckStringBytes(abcB8, utf8, 0);

                var abcB16LE = MakeStringValue(utf16LE.GetBytes("abc"), B16, fUtfBom, false);
                Assert.AreEqual(JsonABC, BuilderToString(abcB16LE.AppendJsonScalarTo(builder)));
                Assert.AreEqual("abc", abcB16LE.ToString());
                CheckStringBytes(abcB16LE, utf16LE, 0);

                var abcB16BE = MakeStringValue(utf16BE.GetBytes("abc"), B16, fUtfBom, true);
                Assert.AreEqual(JsonABC, BuilderToString(abcB16BE.AppendJsonScalarTo(builder)));
                Assert.AreEqual("abc", abcB16BE.ToString());
                CheckStringBytes(abcB16BE, utf16BE, 0);

                var abcB32LE = MakeStringValue(utf32LE.GetBytes("abc"), B32, fUtfBom, false);
                Assert.AreEqual(JsonABC, BuilderToString(abcB32LE.AppendJsonScalarTo(builder)));
                Assert.AreEqual("abc", abcB32LE.ToString());
                CheckStringBytes(abcB32LE, utf32LE, 0);

                var abcB32BE = MakeStringValue(utf32BE.GetBytes("abc"), B32, fUtfBom, true);
                Assert.AreEqual(JsonABC, BuilderToString(abcB32BE.AppendJsonScalarTo(builder)));
                Assert.AreEqual("abc", abcB32BE.ToString());
                CheckStringBytes(abcB32BE, utf32BE, 0);

                var abcB8Bom8 = MakeStringValue(utf8.GetBytes("\uFEFFabc"), B8, fUtfBom);
                Assert.AreEqual(JsonABC, BuilderToString(abcB8Bom8.AppendJsonScalarTo(builder)));
                Assert.AreEqual("abc", abcB8Bom8.ToString());
                CheckStringBytes(abcB8Bom8, utf8, 3);

                var abcB8Bom16LE = MakeStringValue(utf16LE.GetBytes("\uFEFFabc"), B8, fUtfBom);
                Assert.AreEqual(JsonABC, BuilderToString(abcB8Bom16LE.AppendJsonScalarTo(builder)));
                Assert.AreEqual("abc", abcB8Bom16LE.ToString());
                CheckStringBytes(abcB8Bom16LE, utf16LE, 2);

                var abcB8Bom16BE = MakeStringValue(utf16BE.GetBytes("\uFEFFabc"), B8, fUtfBom);
                Assert.AreEqual(JsonABC, BuilderToString(abcB8Bom16BE.AppendJsonScalarTo(builder)));
                Assert.AreEqual("abc", abcB8Bom16BE.ToString());
                CheckStringBytes(abcB8Bom16BE, utf16BE, 2);

                var abcB8Bom32LE = MakeStringValue(utf32LE.GetBytes("\uFEFFabc"), B8, fUtfBom, true);
                Assert.AreEqual(JsonABC, BuilderToString(abcB8Bom32LE.AppendJsonScalarTo(builder)));
                Assert.AreEqual("abc", abcB8Bom32LE.ToString());
                CheckStringBytes(abcB8Bom32LE, utf32LE, 4);

                var abcB8Bom32BE = MakeStringValue(utf32BE.GetBytes("\uFEFFabc"), B8, fUtfBom, true);
                Assert.AreEqual(JsonABC, BuilderToString(abcB8Bom32BE.AppendJsonScalarTo(builder)));
                Assert.AreEqual("abc", abcB8Bom32BE.ToString());
                CheckStringBytes(abcB8Bom32BE, utf32BE, 4);

                var abcB16Bom16LE = MakeStringValue(utf16LE.GetBytes("\uFEFFabc"), B16, fUtfBom);
                Assert.AreEqual(JsonABC, BuilderToString(abcB16Bom16LE.AppendJsonScalarTo(builder)));
                Assert.AreEqual("abc", abcB16Bom16LE.ToString());
                CheckStringBytes(abcB16Bom16LE, utf16LE, 2);

                var abcB16Bom16BE = MakeStringValue(utf16BE.GetBytes("\uFEFFabc"), B16, fUtfBom);
                Assert.AreEqual(JsonABC, BuilderToString(abcB16Bom16BE.AppendJsonScalarTo(builder)));
                Assert.AreEqual("abc", abcB16Bom16BE.ToString());
                CheckStringBytes(abcB16Bom16BE, utf16BE, 2);

                var abcB16Bom32LE = MakeStringValue(utf32LE.GetBytes("\uFEFFabc"), B16, fUtfBom, true);
                Assert.AreEqual(JsonABC, BuilderToString(abcB16Bom32LE.AppendJsonScalarTo(builder)));
                Assert.AreEqual("abc", abcB16Bom32LE.ToString());
                CheckStringBytes(abcB16Bom32LE, utf32LE, 4);

                var abcB16Bom32BE = MakeStringValue(utf32BE.GetBytes("\uFEFFabc"), B16, fUtfBom, true);
                Assert.AreEqual(JsonABC, BuilderToString(abcB16Bom32BE.AppendJsonScalarTo(builder)));
                Assert.AreEqual("abc", abcB16Bom32BE.ToString());
                CheckStringBytes(abcB16Bom32BE, utf32BE, 4);

                var abcB32Bom16LE = MakeStringValue(utf16LE.GetBytes("\uFEFFabc"), B32, fUtfBom);
                Assert.AreEqual(JsonABC, BuilderToString(abcB32Bom16LE.AppendJsonScalarTo(builder)));
                Assert.AreEqual("abc", abcB32Bom16LE.ToString());
                CheckStringBytes(abcB32Bom16LE, utf16LE, 2);

                var abcB32Bom16BE = MakeStringValue(utf16BE.GetBytes("\uFEFFabc"), B32, fUtfBom);
                Assert.AreEqual(JsonABC, BuilderToString(abcB32Bom16BE.AppendJsonScalarTo(builder)));
                Assert.AreEqual("abc", abcB32Bom16BE.ToString());
                CheckStringBytes(abcB32Bom16BE, utf16BE, 2);

                var abcB32Bom32LE = MakeStringValue(utf32LE.GetBytes("\uFEFFabc"), B32, fUtfBom, true);
                Assert.AreEqual(JsonABC, BuilderToString(abcB32Bom32LE.AppendJsonScalarTo(builder)));
                Assert.AreEqual("abc", abcB32Bom32LE.ToString());
                CheckStringBytes(abcB32Bom32LE, utf32LE, 4);

                var abcB32Bom32BE = MakeStringValue(utf32BE.GetBytes("\uFEFFabc"), B32, fUtfBom, true);
                Assert.AreEqual(JsonABC, BuilderToString(abcB32Bom32BE.AppendJsonScalarTo(builder)));
                Assert.AreEqual("abc", abcB32Bom32BE.ToString());
                CheckStringBytes(abcB32Bom32BE, utf32BE, 4);
            }
        }

        private static string BuilderToString(StringBuilder builder)
        {
            var str = builder.ToString();
            builder.Clear();
            return str;
        }

        private static void CheckStringBytes(in PerfItemValue value, Encoding expected, int bomLength)
        {
            Encoding actual;
            var bytes = value.GetStringBytes(out actual);
            Assert.AreEqual(expected.CodePage, actual.CodePage);
            Assert.AreEqual("abc", value.GetString());
            Assert.IsTrue(value.Bytes.Slice(bomLength) == bytes);
            if (bomLength != 0)
            {
                Assert.IsTrue(value.Bytes.Slice(0, bomLength).SequenceEqual(expected.GetPreamble()));
            }
        }

        private void CheckIPv6(IPAddress address)
        {
            var jsonString = '"' + address.ToString() + '"';
            PerfItemValue value;
            
            value = MakeValue(address.GetAddressBytes(), EventHeaderFieldEncoding.Value128, EventHeaderFieldFormat.IPAddress);
            Assert.AreEqual(address, new IPAddress(value.GetIPv6()));
            Assert.AreEqual(address, new IPAddress(value.GetIPv6(0)));
            Assert.AreEqual(jsonString, BuilderToString(value.AppendJsonScalarTo(builder)));
            Assert.AreEqual(jsonString, BuilderToString(value.AppendJsonSimpleElementTo(builder, 0)));
            Assert.AreEqual("[ " + jsonString + " ]", BuilderToString(value.AppendJsonSimpleArrayTo(builder)));

            value = MakeValue(address.GetAddressBytes(), EventHeaderFieldEncoding.Value128, EventHeaderFieldFormat.IPAddressObsolete);
            Assert.AreEqual(address, new IPAddress(value.GetIPv6()));
            Assert.AreEqual(address, new IPAddress(value.GetIPv6(0)));
            Assert.AreEqual(jsonString, BuilderToString(value.AppendJsonScalarTo(builder)));
            Assert.AreEqual(jsonString, BuilderToString(value.AppendJsonSimpleElementTo(builder, 0)));
            Assert.AreEqual("[ " + jsonString + " ]", BuilderToString(value.AppendJsonSimpleArrayTo(builder)));
        }

        private void CheckTime32(Int32 time)
        {
            Span<byte> bytes = stackalloc byte[4];

            var dt = PerfConvert.UnixTime32ToDateTime(time);
            var jsonString = '"' + PerfConvert.DateTimeNoSubsecondsToString(dt) + '"';

            BinaryPrimitives.WriteInt32LittleEndian(bytes, time);
            var value = MakeValue(bytes, EventHeaderFieldEncoding.Value32, EventHeaderFieldFormat.Time, false);
            Assert.AreEqual(time, value.GetUnixTime32());
            Assert.AreEqual(time, value.GetUnixTime32(0));
            Assert.AreEqual(dt, value.GetUnixTime32AsDateTime());
            Assert.AreEqual(dt, value.GetUnixTime32AsDateTime(0));
            Assert.AreEqual(jsonString, BuilderToString(value.AppendJsonScalarTo(builder)));
            Assert.AreEqual(jsonString, BuilderToString(value.AppendJsonSimpleElementTo(builder, 0)));
            Assert.AreEqual("[ " + jsonString + " ]", BuilderToString(value.AppendJsonSimpleArrayTo(builder)));

            BinaryPrimitives.WriteInt32BigEndian(bytes, time);
            value = MakeValue(bytes, EventHeaderFieldEncoding.Value32, EventHeaderFieldFormat.Time, true);
            Assert.AreEqual(time, value.GetUnixTime32());
            Assert.AreEqual(time, value.GetUnixTime32(0));
            Assert.AreEqual(dt, value.GetUnixTime32AsDateTime());
            Assert.AreEqual(dt, value.GetUnixTime32AsDateTime(0));
            Assert.AreEqual(jsonString, BuilderToString(value.AppendJsonScalarTo(builder)));
            Assert.AreEqual(jsonString, BuilderToString(value.AppendJsonSimpleElementTo(builder, 0)));
            Assert.AreEqual("[ " + jsonString + " ]", BuilderToString(value.AppendJsonSimpleArrayTo(builder)));
        }

        private void CheckTime64(Int64 time)
        {
            Span<byte> bytes = stackalloc byte[8];
            var dt = PerfConvert.UnixTime64ToDateTime(time);
            var jsonString = '"' + PerfConvert.UnixTime64ToString(time) + '"';

            BinaryPrimitives.WriteInt64LittleEndian(bytes, time);
            var value = MakeValue(bytes, EventHeaderFieldEncoding.Value64, EventHeaderFieldFormat.Time, false);
            Assert.AreEqual(time, value.GetUnixTime64());
            Assert.AreEqual(time, value.GetUnixTime64(0));
            Assert.AreEqual(PerfConvert.UnixTime64ToDateTime(time), value.GetUnixTime64AsDateTime());
            Assert.AreEqual(PerfConvert.UnixTime64ToDateTime(time), value.GetUnixTime64AsDateTime(0));
            Assert.AreEqual(jsonString, BuilderToString(value.AppendJsonScalarTo(builder)));
            Assert.AreEqual(jsonString, BuilderToString(value.AppendJsonSimpleElementTo(builder, 0)));
            Assert.AreEqual("[ " + jsonString + " ]", BuilderToString(value.AppendJsonSimpleArrayTo(builder)));

            BinaryPrimitives.WriteInt64BigEndian(bytes, time);
            value = MakeValue(bytes, EventHeaderFieldEncoding.Value64, EventHeaderFieldFormat.Time, true);
            Assert.AreEqual(time, value.GetUnixTime64());
            Assert.AreEqual(time, value.GetUnixTime64(0));
            Assert.AreEqual(PerfConvert.UnixTime64ToDateTime(time), value.GetUnixTime64AsDateTime());
            Assert.AreEqual(PerfConvert.UnixTime64ToDateTime(time), value.GetUnixTime64AsDateTime(0));
            Assert.AreEqual(jsonString, BuilderToString(value.AppendJsonScalarTo(builder)));
            Assert.AreEqual(jsonString, BuilderToString(value.AppendJsonSimpleElementTo(builder, 0)));
            Assert.AreEqual("[ " + jsonString + " ]", BuilderToString(value.AppendJsonSimpleArrayTo(builder)));
        }

        private static PerfItemValue MakeValue(
            Span<byte> bytes,
            EventHeaderFieldEncoding encoding,
            EventHeaderFieldFormat format,
            bool fromBigEndian = false)
        {
            byte typeSize;
            switch (encoding)
            {
                case EventHeaderFieldEncoding.Value8:
                    typeSize = 1;
                    break;
                case EventHeaderFieldEncoding.Value16:
                    typeSize = 2;
                    break;
                case EventHeaderFieldEncoding.Value32:
                    typeSize = 4;
                    break;
                case EventHeaderFieldEncoding.Value64:
                    typeSize = 8;
                    break;
                case EventHeaderFieldEncoding.Value128:
                    typeSize = 16;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(encoding));
            }
            return new PerfItemValue(
                bytes,
                new PerfItemMetadata(
                    new PerfByteReader(fromBigEndian),
                    encoding,
                    format,
                    true,
                    typeSize,
                    1));
        }

        private static PerfItemValue MakeStringValue(
            byte[] bytes,
            EventHeaderFieldEncoding encoding,
            EventHeaderFieldFormat format,
            bool fromBigEndian = false)
        {
            return new PerfItemValue(
                bytes,
                new PerfItemMetadata(
                    new PerfByteReader(fromBigEndian),
                    encoding,
                    format,
                    true,
                    0,
                    1));
        }
    }
}
