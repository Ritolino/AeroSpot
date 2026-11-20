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
        public async Task Roundtrip_CloseAll_Success()
        {
            var original = new IpcMessage(IpcMessageType.CloseAll);

            using var ms = new MemoryStream();
            await IpcMessage.WriteAsync(ms, original);

            ms.Position = 0;
            var deserialized = await IpcMessage.ReadAsync(ms);

            Assert.IsNotNull(deserialized);
            Assert.AreEqual(IpcMessageType.CloseAll, deserialized.Type);
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

        [TestMethod]
        public async Task Roundtrip_ShutdownHost_And_ShowUpdateNotification_Success()
        {
            var shutdownMsg = new IpcMessage(IpcMessageType.ShutdownHost);
            Assert.AreEqual((ushort)21, (ushort)shutdownMsg.Type);
            using (var ms = new MemoryStream())
            {
                await IpcMessage.WriteAsync(ms, shutdownMsg);
                ms.Position = 0;
                var back = await IpcMessage.ReadAsync(ms);
                Assert.IsNotNull(back);
                Assert.AreEqual(IpcMessageType.ShutdownHost, back.Type);
            }

            string payload = "1.6.1|https://example/dl.zip|https://example/rel|AeroSpot-v1.6.1-win-x64-portable.zip|https://example/SHA256SUMS.txt";
            var notifyMsg = new IpcMessage(IpcMessageType.ShowUpdateNotification, payload);
            Assert.AreEqual((ushort)22, (ushort)notifyMsg.Type);
            using (var ms = new MemoryStream())
            {
                await IpcMessage.WriteAsync(ms, notifyMsg);
                ms.Position = 0;
                var back = await IpcMessage.ReadAsync(ms);
                Assert.IsNotNull(back);
                Assert.AreEqual(IpcMessageType.ShowUpdateNotification, back.Type);
                Assert.AreEqual(payload, back.Payload);
            }
        }
    }
}
