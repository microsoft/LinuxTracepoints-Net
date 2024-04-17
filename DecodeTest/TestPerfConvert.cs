namespace DecodeTest
{
    using Microsoft.LinuxTracepoints.Decode;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using System;
    using System.Globalization;
    using System.Text;
    using IPAddress = System.Net.IPAddress;

    [TestClass]
    public class TestPerfConvert
    {
        private StringBuilder sb = new StringBuilder();

        [TestMethod]
        public void Conversions()
        {
            string expected;
            byte[] bytes;
            DateTime dt;

            Assert.AreEqual(null, PerfConvert.EncodingFromBom(Array.Empty<byte>()));
            Assert.AreEqual(null, PerfConvert.EncodingFromBom(new byte[] { 1, 2, 3 }));
            Assert.AreEqual(Encoding.UTF8, PerfConvert.EncodingFromBom(Encoding.UTF8.Preamble));
            Assert.AreEqual(Encoding.Unicode, PerfConvert.EncodingFromBom(Encoding.Unicode.Preamble));
            Assert.AreEqual(Encoding.BigEndianUnicode, PerfConvert.EncodingFromBom(Encoding.BigEndianUnicode.Preamble));
            Assert.AreEqual(Encoding.UTF32, PerfConvert.EncodingFromBom(Encoding.UTF32.Preamble));
            Assert.AreEqual(PerfConvert.EncodingUTF32BE, PerfConvert.EncodingFromBom(PerfConvert.EncodingUTF32BE.Preamble));

            uint utf32 = 0x10FFFF0;
            do
            {
                utf32 >>= 4;
                expected = Encoding.UTF32.GetString(BitConverter.GetBytes(utf32));
                Assert.AreEqual(expected, PerfConvert.Char32ToString(utf32));
                sb.Clear();
                Assert.AreEqual(expected, PerfConvert.Char32Append(sb, utf32).ToString());
            }
            while (utf32 != 0);

            uint ipv4 = 0xFEFDFCFB;
            expected = new IPAddress(ipv4).ToString();
            Assert.AreEqual(expected, PerfConvert.IPv4ToString(ipv4));
            sb.Clear();
            Assert.AreEqual(expected, PerfConvert.IPv4Append(sb, ipv4).ToString());

            expected = uint.MaxValue.ToString(CultureInfo.InvariantCulture);
            Assert.AreEqual(expected, PerfConvert.UInt32DecimalToString(uint.MaxValue));
            sb.Clear();
            Assert.AreEqual(expected, PerfConvert.UInt32DecimalAppend(sb, uint.MaxValue).ToString());

            expected = ulong.MaxValue.ToString(CultureInfo.InvariantCulture);
            Assert.AreEqual(expected, PerfConvert.UInt64DecimalToString(ulong.MaxValue));
            sb.Clear();
            Assert.AreEqual(expected, PerfConvert.UInt64DecimalAppend(sb, ulong.MaxValue).ToString());

            expected = int.MaxValue.ToString(CultureInfo.InvariantCulture);
            Assert.AreEqual(expected, PerfConvert.Int32DecimalToString(int.MaxValue));
            sb.Clear();
            Assert.AreEqual(expected, PerfConvert.Int32DecimalAppend(sb, int.MaxValue).ToString());

            expected = int.MinValue.ToString(CultureInfo.InvariantCulture);
            Assert.AreEqual(expected, PerfConvert.Int32DecimalToString(int.MinValue));
            sb.Clear();
            Assert.AreEqual(expected, PerfConvert.Int32DecimalAppend(sb, int.MinValue).ToString());

            expected = long.MaxValue.ToString(CultureInfo.InvariantCulture);
            Assert.AreEqual(expected, PerfConvert.Int64DecimalToString(long.MaxValue));
            sb.Clear();
            Assert.AreEqual(expected, PerfConvert.Int64DecimalAppend(sb, long.MaxValue).ToString());

            expected = long.MinValue.ToString(CultureInfo.InvariantCulture);
            Assert.AreEqual(expected, PerfConvert.Int64DecimalToString(long.MinValue));
            sb.Clear();
            Assert.AreEqual(expected, PerfConvert.Int64DecimalAppend(sb, long.MinValue).ToString());

            expected = string.Format(CultureInfo.InvariantCulture, "0x{0:X}", uint.MaxValue);
            Assert.AreEqual(expected, PerfConvert.UInt32HexToString(uint.MaxValue));
            sb.Clear();
            Assert.AreEqual(expected, PerfConvert.UInt32HexAppend(sb, uint.MaxValue).ToString());

            expected = string.Format(CultureInfo.InvariantCulture, "0x{0:X}", ulong.MaxValue);
            Assert.AreEqual(expected, PerfConvert.UInt64HexToString(ulong.MaxValue));
            sb.Clear();
            Assert.AreEqual(expected, PerfConvert.UInt64HexAppend(sb, ulong.MaxValue).ToString());

            CheckFloat32(float.MinValue);
            CheckFloat32(float.MaxValue);
            CheckFloat32(float.Epsilon);
            CheckFloat32(float.NaN);
            CheckFloat32(float.PositiveInfinity);
            CheckFloat32(float.NegativeInfinity);

            CheckFloat64(double.MinValue);
            CheckFloat64(double.MaxValue);
            CheckFloat64(double.Epsilon);
            CheckFloat64(double.NaN);
            CheckFloat64(double.PositiveInfinity);
            CheckFloat64(double.NegativeInfinity);

            Assert.AreEqual(0, PerfConvert.HexBytesLength(0));
            Assert.AreEqual(2, PerfConvert.HexBytesLength(1));
            Assert.AreEqual(5, PerfConvert.HexBytesLength(2));
            Assert.AreEqual(2147483645, PerfConvert.HexBytesLength(715827882));

            bytes = Array.Empty<byte>();
            expected = "";
            Assert.AreEqual(expected, PerfConvert.HexBytesToString(bytes));
            sb.Clear();
            Assert.AreEqual(expected, PerfConvert.HexBytesAppend(sb, bytes).ToString());

            bytes = new byte[] { 0xAB };
            expected = "AB";
            Assert.AreEqual(expected, PerfConvert.HexBytesToString(bytes));
            sb.Clear();
            Assert.AreEqual(expected, PerfConvert.HexBytesAppend(sb, bytes).ToString());

            bytes = new byte[] { 0x00 };
            expected = "00";
            Assert.AreEqual(expected, PerfConvert.HexBytesToString(bytes));
            sb.Clear();
            Assert.AreEqual(expected, PerfConvert.HexBytesAppend(sb, bytes).ToString());

            bytes = new byte[] { 0x00, 0xAB };
            expected = "00 AB";
            Assert.AreEqual(expected, PerfConvert.HexBytesToString(bytes));
            sb.Clear();
            Assert.AreEqual(expected, PerfConvert.HexBytesAppend(sb, bytes).ToString());

            bytes = new byte[] { 0x00, 0xAB, 0xFF };
            expected = "00 AB FF";
            Assert.AreEqual(expected, PerfConvert.HexBytesToString(bytes));
            sb.Clear();
            Assert.AreEqual(expected, PerfConvert.HexBytesAppend(sb, bytes).ToString());

            dt = DateTime.UnixEpoch;
            expected = "1970-01-01T00:00:00Z";
            Assert.AreEqual(expected, PerfConvert.DateTimeNoSubsecondsToString(dt));
            sb.Clear();
            Assert.AreEqual(expected, PerfConvert.DateTimeNoSubsecondsAppend(sb, dt).ToString());

            dt = DateTime.MinValue;
            expected = "0001-01-01T00:00:00Z";
            Assert.AreEqual(expected, PerfConvert.DateTimeNoSubsecondsToString(dt));
            sb.Clear();
            Assert.AreEqual(expected, PerfConvert.DateTimeNoSubsecondsAppend(sb, dt).ToString());

            dt = DateTime.MaxValue;
            expected = "9999-12-31T23:59:59Z";
            Assert.AreEqual(expected, PerfConvert.DateTimeNoSubsecondsToString(dt));
            sb.Clear();
            Assert.AreEqual(expected, PerfConvert.DateTimeNoSubsecondsAppend(sb, dt).ToString());

            dt = DateTime.UnixEpoch;
            expected = "1970-01-01T00:00:00.0000000Z";
            Assert.AreEqual(expected, PerfConvert.DateTimeFullToString(dt));
            sb.Clear();
            Assert.AreEqual(expected, PerfConvert.DateTimeFullAppend(sb, dt).ToString());

            dt = DateTime.UnixEpoch.AddTicks(1);
            expected = "1970-01-01T00:00:00.0000001Z";
            Assert.AreEqual(expected, PerfConvert.DateTimeFullToString(dt));
            sb.Clear();
            Assert.AreEqual(expected, PerfConvert.DateTimeFullAppend(sb, dt).ToString());

            dt = DateTime.UnixEpoch.AddTicks(-1);
            expected = "1969-12-31T23:59:59.9999999Z";
            Assert.AreEqual(expected, PerfConvert.DateTimeFullToString(dt));
            sb.Clear();
            Assert.AreEqual(expected, PerfConvert.DateTimeFullAppend(sb, dt).ToString());


            var expectedDT = DateTime.UnixEpoch;
            Assert.AreEqual(expectedDT, PerfConvert.UnixTime32ToDateTime(0));
            Assert.AreEqual(expectedDT, PerfConvert.UnixTime64ToDateTime(0));
            expectedDT = DateTime.UnixEpoch.AddSeconds(int.MinValue);
            Assert.AreEqual(expectedDT, PerfConvert.UnixTime32ToDateTime(int.MinValue));
            Assert.AreEqual(expectedDT, PerfConvert.UnixTime64ToDateTime(int.MinValue));
            expectedDT = DateTime.UnixEpoch.AddSeconds(int.MaxValue);
            Assert.AreEqual(expectedDT, PerfConvert.UnixTime32ToDateTime(int.MaxValue));
            Assert.AreEqual(expectedDT, PerfConvert.UnixTime64ToDateTime(int.MaxValue));

            var dateTimeMax = DateTime.MaxValue.AddTicks(-9999999); // Round down to nearest second
            var secondsMin = -(long)(DateTime.UnixEpoch - DateTime.MinValue).TotalSeconds;
            var secondsMax = (long)(dateTimeMax - DateTime.UnixEpoch).TotalSeconds;
            Assert.AreEqual(DateTime.MinValue, PerfConvert.UnixTime64ToDateTime(secondsMin));
            Assert.AreEqual(dateTimeMax, PerfConvert.UnixTime64ToDateTime(secondsMax));

            expected = "1970-01-01T00:00:00Z";
            Assert.AreEqual(expected, PerfConvert.UnixTime32ToString(0));
            Assert.AreEqual(expected, PerfConvert.UnixTime64ToString(0));
            sb.Clear();
            Assert.AreEqual(expected, PerfConvert.UnixTime32Append(sb, 0).ToString());
            sb.Clear();
            Assert.AreEqual(expected, PerfConvert.UnixTime64Append(sb, 0).ToString());

            expected = "1901-12-13T20:45:52Z";
            Assert.AreEqual(expected, PerfConvert.UnixTime32ToString(int.MinValue));
            Assert.AreEqual(expected, PerfConvert.UnixTime64ToString(int.MinValue));
            sb.Clear();
            Assert.AreEqual(expected, PerfConvert.UnixTime32Append(sb, int.MinValue).ToString());
            sb.Clear();
            Assert.AreEqual(expected, PerfConvert.UnixTime64Append(sb, int.MinValue).ToString());

            expected = "2038-01-19T03:14:07Z";
            Assert.AreEqual(expected, PerfConvert.UnixTime32ToString(int.MaxValue));
            Assert.AreEqual(expected, PerfConvert.UnixTime64ToString(int.MaxValue));
            sb.Clear();
            Assert.AreEqual(expected, PerfConvert.UnixTime32Append(sb, int.MaxValue).ToString());
            sb.Clear();
            Assert.AreEqual(expected, PerfConvert.UnixTime64Append(sb, int.MaxValue).ToString());

            expected = "0001-01-01T00:00:00Z";
            Assert.AreEqual(expected, PerfConvert.UnixTime64ToString(secondsMin));
            sb.Clear();
            Assert.AreEqual(expected, PerfConvert.UnixTime64Append(sb, secondsMin).ToString());

            expected = "9999-12-31T23:59:59Z";
            Assert.AreEqual(expected, PerfConvert.UnixTime64ToString(secondsMax));
            sb.Clear();
            Assert.AreEqual(expected, PerfConvert.UnixTime64Append(sb, secondsMax).ToString());

            CheckUnixTime64OutOfRange(secondsMin - 1);
            CheckUnixTime64OutOfRange(secondsMax + 1);
            CheckUnixTime64OutOfRange(long.MinValue);
            CheckUnixTime64OutOfRange(long.MaxValue);

            expected = "ENOTRECOVERABLE(131)";
            Assert.AreEqual(expected, PerfConvert.ErrnoToString(131));
            sb.Clear();
            Assert.AreEqual(expected, PerfConvert.ErrnoAppend(sb, 131).ToString());

            CheckErrnoOutOfRange(int.MinValue);
            CheckErrnoOutOfRange(int.MaxValue);
            CheckErrnoOutOfRange(-1);
            CheckErrnoOutOfRange(0);
            CheckErrnoOutOfRange(134);

            expected = "false";
            Assert.AreEqual(expected, PerfConvert.BooleanToString(0));
            sb.Clear();
            Assert.AreEqual(expected, PerfConvert.BooleanAppend(sb, 0).ToString());

            expected = "true";
            Assert.AreEqual(expected, PerfConvert.BooleanToString(1));
            sb.Clear();
            Assert.AreEqual(expected, PerfConvert.BooleanAppend(sb, 1).ToString());

            CheckBoolOutOfRange(-1);
            CheckBoolOutOfRange(2);
            CheckBoolOutOfRange(int.MinValue);
            CheckBoolOutOfRange(int.MaxValue);
        }

        private void CheckFloat32(float value)
        {
            string expected;

            expected = value.ToString("g", CultureInfo.InvariantCulture);
            Assert.AreEqual(expected, PerfConvert.Float32ToString(value, PerfConvertOptions.None));
            sb.Clear();
            Assert.AreEqual(expected, PerfConvert.Float32Append(sb, value, PerfConvertOptions.None).ToString());

            expected = value.ToString("g9", CultureInfo.InvariantCulture);
            Assert.AreEqual(expected, PerfConvert.Float32ToString(value, PerfConvertOptions.FloatExtraPrecision));
            sb.Clear();
            Assert.AreEqual(expected, PerfConvert.Float32Append(sb, value, PerfConvertOptions.FloatExtraPrecision).ToString());
        }

        private void CheckFloat64(double value)
        {
            string expected;

            expected = value.ToString("g", CultureInfo.InvariantCulture);
            Assert.AreEqual(expected, PerfConvert.Float64ToString(value, PerfConvertOptions.None));
            sb.Clear();
            Assert.AreEqual(expected, PerfConvert.Float64Append(sb, value, PerfConvertOptions.None).ToString());

            expected = value.ToString("g17", CultureInfo.InvariantCulture);
            Assert.AreEqual(expected, PerfConvert.Float64ToString(value, PerfConvertOptions.FloatExtraPrecision));
            sb.Clear();
            Assert.AreEqual(expected, PerfConvert.Float64Append(sb, value, PerfConvertOptions.FloatExtraPrecision).ToString());
        }

        private void CheckUnixTime64OutOfRange(long outOfRange)
        {
            var expected = "TIME(" + outOfRange.ToString(CultureInfo.InvariantCulture) + ")";
            Assert.AreEqual(expected, PerfConvert.UnixTime64ToString(outOfRange));
            sb.Clear();
            Assert.AreEqual(expected, PerfConvert.UnixTime64Append(sb, outOfRange).ToString());
        }

        private void CheckErrnoOutOfRange(int outOfRange)
        {
            var expected = "ERRNO(" + outOfRange.ToString(CultureInfo.InvariantCulture) + ")";
            Assert.AreEqual(expected, PerfConvert.ErrnoToString(outOfRange));
            sb.Clear();
            Assert.AreEqual(expected, PerfConvert.ErrnoAppend(sb, outOfRange).ToString());
        }

        private void CheckBoolOutOfRange(int outOfRange)
        {
            var expected = "BOOL(" + outOfRange.ToString(CultureInfo.InvariantCulture) + ")";
            Assert.AreEqual(expected, PerfConvert.BooleanToString((uint)outOfRange));
            sb.Clear();
            Assert.AreEqual(expected, PerfConvert.BooleanAppend(sb, (uint)outOfRange).ToString());
        }
    }
}
