namespace DecodeTest
{
    using Microsoft.LinuxTracepoints;
    using Microsoft.LinuxTracepoints.Decode;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using System.Diagnostics.Tracing;
    using Marshal = System.Runtime.InteropServices.Marshal;

    [TestClass]
    public class TestTypes
    {
        [TestMethod]
        public void Sizes()
        {
            Assert.AreEqual(EventHeader.SizeOfStruct, Marshal.SizeOf<EventHeader>());
            Assert.AreEqual(EventHeaderExtension.SizeOfStruct, Marshal.SizeOf<EventHeaderExtension>());

            Assert.AreEqual(PerfEventAttr.SizeOfStruct, Marshal.SizeOf<PerfEventAttr>());
            Assert.AreEqual(PerfEventHeaderMisc.SizeOfStruct, Marshal.SizeOf<PerfEventHeaderMisc>());
            Assert.AreEqual(PerfEventHeader.SizeOfStruct, Marshal.SizeOf<PerfEventHeader>());
        }

        [TestMethod]
        public void Encoding()
        {
            for (int value = 0; value < 256; value += 1)
            {
                var encoding = (EventHeaderFieldEncoding)value;
                Assert.AreEqual(value, (byte)encoding);
                Assert.AreEqual(value & 0x1F, (byte)encoding.WithoutFlags());
                Assert.AreEqual(value & 0x60, (byte)encoding.ArrayFlags());
                Assert.AreEqual((value & 0x20) != 0, encoding.IsCArray());
                Assert.AreEqual((value & 0x40) != 0, encoding.IsVArray());
                Assert.AreEqual((value & 0x60) != 0, encoding.IsArray());
                Assert.AreEqual((value & 0x80) != 0, encoding.HasChainFlag());

                var baseEnc = encoding.WithoutFlags();
                var fmt = encoding.DefaultFormat();
                if (baseEnc >= EventHeaderFieldEncoding.Value8 &&
                    baseEnc <= EventHeaderFieldEncoding.Value64)
                {
                    Assert.AreEqual(EventHeaderFieldFormat.UnsignedInt, fmt);
                }
                else if (baseEnc == EventHeaderFieldEncoding.Value128 ||
                    baseEnc == EventHeaderFieldEncoding.BinaryLength16Char8)
                {
                    Assert.AreEqual(EventHeaderFieldFormat.HexBytes, fmt);
                }
                else if (
                    baseEnc >= EventHeaderFieldEncoding.ZStringChar8 &&
                    baseEnc <= EventHeaderFieldEncoding.StringLength16Char32)
                {
                    Assert.AreEqual(EventHeaderFieldFormat.StringUtf, fmt);
                }
                else
                {
                    Assert.AreEqual(EventHeaderFieldFormat.Default, fmt);
                }
            }
        }

        [TestMethod]
        public void Format()
        {
            for (int value = 0; value < 256; value += 1)
            {
                var format = (EventHeaderFieldFormat)value;
                Assert.AreEqual(value, (byte)format);
                Assert.AreEqual(value & 0x7F, (byte)format.WithoutFlags());
                Assert.AreEqual((value & 0x80) != 0, format.HasChainFlag());
            }
        }

        [TestMethod]
        public void Header()
        {
            var header = new EventHeader();

            Assert.AreEqual(EventOpcode.Info, header.Opcode);
            header.Opcode = EventOpcode.Resume;
            Assert.AreEqual(EventOpcode.Resume, header.Opcode);

            Assert.AreEqual(EventLevel.LogAlways, header.Level);
            header.Level = EventLevel.Warning;
            Assert.AreEqual(EventLevel.Warning, header.Level);
        }

        [TestMethod]
        public void HeaderExtensionKind()
        {
            ushort[] baseKinds = new ushort[] { 0, 1, 2, 3, 0x7FFE, 0x7FFF };
            for (ushort chain = 0; chain < 2; chain += 1)
            {
                foreach (var baseKind in baseKinds)
                {
                    var kind = (EventHeaderExtensionKind)(baseKind | (chain << 15));
                    Assert.AreEqual((EventHeaderExtensionKind)baseKind, kind.BaseKind());
                    Assert.AreEqual(chain != 0, kind.HasChainFlag());
                }
            }
        }
    }
}
