namespace DecodeTest
{
    using System;
    using Microsoft.LinuxTracepoints;
    using Microsoft.LinuxTracepoints.Decode;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using System.Diagnostics.Tracing;

    [TestClass]
    public class TestPerfTimeSpec
    {
        [TestMethod]
        public void TestPerfTimeSpecConstructor()
        {
            var timeSpec = new PerfTimeSpec(1, 2);
            Assert.AreEqual(1, timeSpec.TvSec);
            Assert.AreEqual(2u, timeSpec.TvNsec);

            timeSpec = new PerfTimeSpec(1, uint.MaxValue);
            Assert.AreEqual(5, timeSpec.TvSec);
            Assert.AreEqual(uint.MaxValue % 1000000000, timeSpec.TvNsec);

            Assert.AreEqual(DateTime.MaxValue, new PerfTimeSpec(DateTime.MaxValue).DateTime);
            Assert.AreEqual(DateTime.MinValue, new PerfTimeSpec(DateTime.MinValue).DateTime);
            Assert.AreEqual(DateTime.UnixEpoch, new PerfTimeSpec(DateTime.UnixEpoch).DateTime);
        }
    }
}
