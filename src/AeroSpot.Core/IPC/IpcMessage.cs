using System;
using System.IO;
using System.Text;
using System.Text.Json;
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
        RefreshIndex = 15,
        PreviewOpened = 16,
        PreviewClosed = 17,
        CloseWorker = 20,
        WorkerReady = 30,
        WorkerState = 31,
        StatusQuery = 40,
        StatusReply = 41
    }

    public sealed class IpcMessage
    {
        public const uint MagicHeader = 0x4F524541; // 'A','E','R','O' in little-endian
        public const byte ProtocolVersion = 1;

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
            byte[] payloadBytes = Encoding.UTF8.GetBytes(message.Payload);
            byte[] header = new byte[11];
            
            // Magic (4 bytes)
            BitConverter.GetBytes(MagicHeader).CopyTo(header, 0);
            // Version (1 byte)
            header[4] = ProtocolVersion;
            // Type (2 bytes)
            BitConverter.GetBytes((ushort)message.Type).CopyTo(header, 5);
            // Payload Length (4 bytes)
            BitConverter.GetBytes(payloadBytes.Length).CopyTo(header, 7);

            await stream.WriteAsync(header, 0, header.Length, cancellationToken).ConfigureAwait(false);
            if (payloadBytes.Length > 0)
            {
                await stream.WriteAsync(payloadBytes, 0, payloadBytes.Length, cancellationToken).ConfigureAwait(false);
            }
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        public static async Task<IpcMessage?> ReadAsync(Stream stream, CancellationToken cancellationToken = default)
        {
            byte[] header = new byte[11];
            int read = 0;
            while (read < header.Length)
            {
                int r = await stream.ReadAsync(header, read, header.Length - read, cancellationToken).ConfigureAwait(false);
                if (r == 0) return null; // End of stream
                read += r;
            }

            uint magic = BitConverter.ToUInt32(header, 0);
            if (magic != MagicHeader)
            {
                throw new InvalidDataException($"Invalid IPC magic header: 0x{magic:X8}");
            }

            byte version = header[4];
            if (version != ProtocolVersion)
            {
                throw new InvalidDataException($"Unsupported IPC protocol version: {version}");
            }

            IpcMessageType type = (IpcMessageType)BitConverter.ToUInt16(header, 5);
            int payloadLength = BitConverter.ToInt32(header, 7);

            if (payloadLength < 0 || payloadLength > 10 * 1024 * 1024) // 10 MB sanity limit
            {
                throw new InvalidDataException($"Invalid IPC payload length: {payloadLength}");
            }

            string payload = string.Empty;
            if (payloadLength > 0)
            {
                byte[] payloadBytes = new byte[payloadLength];
                int pRead = 0;
                while (pRead < payloadLength)
                {
                    int r = await stream.ReadAsync(payloadBytes, pRead, payloadLength - pRead, cancellationToken).ConfigureAwait(false);
                    if (r == 0) return null;
                    pRead += r;
                }
                payload = Encoding.UTF8.GetString(payloadBytes);
            }

            return new IpcMessage(type, payload);
        }
    }
}
