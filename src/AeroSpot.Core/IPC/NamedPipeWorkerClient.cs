using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace AeroSpot.Core.IPC
{
    public sealed class NamedPipeWorkerClient : IDisposable
    {
        private readonly string _pipeName;
        private NamedPipeClientStream? _clientStream;
        private CancellationTokenSource? _cts;
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
        private bool _isDisposed;

        public event Action? Connected;
        public event Action? Disconnected;
        public event Action<IpcMessage>? MessageReceived;

        public bool IsConnected => _clientStream?.IsConnected == true;

        public NamedPipeWorkerClient(string? customPipeName = null)
        {
            _pipeName = customPipeName ?? NamedPipeHostServer.GetDefaultPipeName();
        }

        public async Task<bool> ConnectAsync(int timeoutMs = 5000, CancellationToken token = default)
        {
            try
            {
                _clientStream = new NamedPipeClientStream(
                    ".",
                    _pipeName,
                    PipeDirection.InOut,
                    PipeOptions.Asynchronous);

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
                cts.CancelAfter(timeoutMs);

                await _clientStream.ConnectAsync(cts.Token).ConfigureAwait(false);

                _cts = new CancellationTokenSource();
                Connected?.Invoke();
                _ = RunReadLoopAsync(_cts.Token);
                return true;
            }
            catch
            {
                Disconnect();
                return false;
            }
        }

        private async Task RunReadLoopAsync(CancellationToken token)
        {
            try
            {
                while (_clientStream != null && _clientStream.IsConnected && !token.IsCancellationRequested)
                {
                    IpcMessage? msg = await IpcMessage.ReadAsync(_clientStream, token).ConfigureAwait(false);
                    if (msg == null) break;
                    MessageReceived?.Invoke(msg);
                }
            }
            catch
            {
                // Disconnected or read error
            }
            finally
            {
                Disconnected?.Invoke();
                Disconnect();
            }
        }

        public async Task<bool> SendMessageAsync(IpcMessage message, int timeoutMs = 3000, CancellationToken token = default)
        {
            if (_clientStream == null || !_clientStream.IsConnected) return false;

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            cts.CancelAfter(timeoutMs);

            await _sendLock.WaitAsync(cts.Token).ConfigureAwait(false);
            try
            {
                if (_clientStream == null || !_clientStream.IsConnected) return false;
                await IpcMessage.WriteAsync(_clientStream, message, cts.Token).ConfigureAwait(false);
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                _sendLock.Release();
            }
        }

        public void Disconnect()
        {
            _cts?.Cancel();
            try
            {
                _clientStream?.Dispose();
                _clientStream = null;
            }
            catch { }
            _cts?.Dispose();
            _cts = null;
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            Disconnect();
            _sendLock.Dispose();
        }
    }
}
