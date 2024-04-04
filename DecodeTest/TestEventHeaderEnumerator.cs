namespace DecodeTest
{
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using System.Buffers;
    using System.IO;
    using System.Text.Json;

    [TestClass]
    public class TestEventHeaderEnumerator
    {
        public TestContext TestContext { get; set; } = null!;

        private void Decode(string inputName)
        {
            var buffer = JsonCompare.CreateBuffer();
            using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
            {
                var decode = new DatDecode(writer);
                writer.WriteStartArray();
                decode.DecodeFile(Path.Combine(TestContext.TestDeploymentDir, "input", inputName));
                writer.WriteEndArray();
            }

            JsonCompare.AssertSame(TestContext, inputName, buffer);
        }

        [TestMethod]
        public void DecodeDat()
        {
            Decode("EventHeaderInterceptorLE64.dat");
        }
    }
}
