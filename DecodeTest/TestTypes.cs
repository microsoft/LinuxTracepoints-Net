namespace DecodeTest
{
    using Microsoft.LinuxTracepoints;
    using Microsoft.LinuxTracepoints.Decode;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
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
    }
}
