using System;
using System.IO;
using System.Threading.Tasks;
using AeroSpot.Core.IPC;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AeroSpot.Tests
{
    [TestClass]
    public class IpcMessageTests
    {
        [TestMethod]
        public async Task Roundtrip_WithPayload_Success()
        {
            var original = new IpcMessage(IpcMessageType.ShowPreview, @"C:\Samples\test.png");

            using var ms = new MemoryStream();
            await IpcMessage.WriteAsync(ms, original);

            ms.Position = 0;
            var deserialized = await IpcMessage.ReadAsync(ms);

            Assert.IsNotNull(deserialized);
            Assert.AreEqual(original.Type, deserialized.Type);
            Assert.AreEqual(original.Payload, deserialized.Payload);
        }

        [TestMethod]
        public async Task Roundtrip_EmptyPayload_Success()
        {
            var original = new IpcMessage(IpcMessageType.ShowLauncher);

            using var ms = new MemoryStream();
            await IpcMessage.WriteAsync(ms, original);

            ms.Position = 0;
            var deserialized = await IpcMessage.ReadAsync(ms);

            Assert.IsNotNull(deserialized);
            Assert.AreEqual(original.Type, deserialized.Type);
            Assert.AreEqual(string.Empty, deserialized.Payload);
        }

        [TestMethod]
        public async Task Read_CorruptedMagicHeader_ThrowsInvalidDataException()
        {
            byte[] corrupted = new byte[11];
            BitConverter.GetBytes(0x12345678).CopyTo(corrupted, 0);

            using var ms = new MemoryStream(corrupted);
            bool threw = false;
            try
            {
                await IpcMessage.ReadAsync(ms);
            }
            catch (InvalidDataException)
            {
                threw = true;
            }
            Assert.IsTrue(threw, "Expected InvalidDataException on corrupted magic header.");
        }

        [TestMethod]
        public async Task Read_UnsupportedVersion_ThrowsInvalidDataException()
        {
            byte[] header = new byte[11];
            BitConverter.GetBytes(IpcMessage.MagicHeader).CopyTo(header, 0);
            header[4] = 99; // Unsupported version

            using var ms = new MemoryStream(header);
            bool threw = false;
            try
            {
                await IpcMessage.ReadAsync(ms);
            }
            catch (InvalidDataException)
            {
                threw = true;
            }
            Assert.IsTrue(threw, "Expected InvalidDataException on unsupported version.");
        }
    }
}
