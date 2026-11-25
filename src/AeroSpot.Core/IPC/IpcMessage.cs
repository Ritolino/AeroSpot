using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AeroSpot.Core.IPC
{
    public enum IpcMessageType : ushort
    {
        Unknown = 0,
        Ping = 1,
        Pong = 2,
        ShowLauncher = 10,
        HideLauncher = 11,
        ShowPreview = 12,
        ShowSettings = 13,
        ConfigReloaded = 14,
        PreviewOpened = 16,
        PreviewClosed = 17,
        HidePreview = 18,
        CloseAll = 19,
        CloseWorker = 20,
        ShutdownHost = 21,
        ShowUpdateNotification = 22,
        WorkerReady = 30,
        WorkerState = 31
    }

    public sealed class IpcMessage
    {
        public const uint MagicHeader = 0x4F524541; // 'A','E','R','O' in little-endian
        public const byte ProtocolVersion = 1;
        private const int HeaderSize = 11;
        private const int MaxPayloadBytes = 10 * 1024 * 1024; // 10 MB sanity limit

        public IpcMessageType Type { get; set; }
        public string Payload { get; set; } = string.Empty;

        public IpcMessage() { }

        public IpcMessage(IpcMessageType type, string payload = "")
        {
            Type = type;
            Payload = payload;
        }

        public static async Task WriteAsync(Stream stream, IpcMessage message, CancellationToken cancellationToken = default)
        {
            string payload = message.Payload ?? string.Empty;
            int payloadLength = Encoding.UTF8.GetByteCount(payload);
            byte[] packet = new byte[HeaderSize + payloadLength];

            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), MagicHeader);
            packet[4] = ProtocolVersion;
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(5, 2), (ushort)message.Type);
            BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(7, 4), payloadLength);

            if (payloadLength > 0)
            {
                Encoding.UTF8.GetBytes(payload, packet.AsSpan(HeaderSize, payloadLength));
            }

            await stream.WriteAsync(packet, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        public static async Task<IpcMessage?> ReadAsync(Stream stream, CancellationToken cancellationToken = default)
        {
            byte[] header = new byte[HeaderSize];
            try
            {
                await stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
            }
            catch (EndOfStreamException)
            {
                return null;
            }

            uint magic = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(0, 4));
            if (magic != MagicHeader)
            {
                throw new InvalidDataException($"Invalid IPC magic header: 0x{magic:X8}");
            }

            byte version = header[4];
            if (version != ProtocolVersion)
            {
                throw new InvalidDataException($"Unsupported IPC protocol version: {version}");
            }

            IpcMessageType type = (IpcMessageType)BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(5, 2));
            int payloadLength = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(7, 4));

            if (payloadLength < 0 || payloadLength > MaxPayloadBytes)
            {
                throw new InvalidDataException($"Invalid IPC payload length: {payloadLength}");
            }

            string payload = string.Empty;
            if (payloadLength > 0)
            {
                byte[] payloadBytes = new byte[payloadLength];
                try
                {
                    await stream.ReadExactlyAsync(payloadBytes, cancellationToken).ConfigureAwait(false);
                }
                catch (EndOfStreamException)
                {
                    return null;
                }
                payload = Encoding.UTF8.GetString(payloadBytes);
            }

            return new IpcMessage(type, payload);
        }
    }
}
