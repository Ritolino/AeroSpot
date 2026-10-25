using System;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;

namespace AeroSpot.Core.IPC
{
    public sealed class NamedPipeHostServer : IDisposable
    {
        private readonly string _pipeName;
        private NamedPipeServerStream? _serverStream;
        private CancellationTokenSource? _cts;
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
        private bool _isDisposed;

        public event Action? ClientConnected;
        public event Action? ClientDisconnected;
        public event Action<IpcMessage>? MessageReceived;

        public bool IsClientConnected => _serverStream?.IsConnected == true;

        public NamedPipeHostServer(string? customPipeName = null)
        {
            _pipeName = customPipeName ?? GetDefaultPipeName();
        }

        public static string GetDefaultPipeName()
        {
            string user = Environment.UserName.Replace("\\", "_");
            return $"AeroSpot_Pipe_{user}";
        }

        public void Start()
        {
            if (_cts != null) return;
            _cts = new CancellationTokenSource();
            _ = RunServerLoopAsync(_cts.Token);
        }

        private async Task RunServerLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested && !_isDisposed)
            {
                try
                {
                    _serverStream = new NamedPipeServerStream(
                        _pipeName,
                        PipeDirection.InOut,
                        1, // Single client (one worker at a time)
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    await _serverStream.WaitForConnectionAsync(token).ConfigureAwait(false);

                    // Security check: ensure caller belongs to the same Windows user
                    try
                    {
                        string clientUser = _serverStream.GetImpersonationUserName();
                        string currentUser = WindowsIdentity.GetCurrent().Name;
                        if (!string.Equals(clientUser, currentUser, StringComparison.OrdinalIgnoreCase))
                        {
                            _serverStream.Disconnect();
                            _serverStream.Dispose();
                            continue;
                        }
                    }
                    catch
                    {
                        // Ignore impersonation check failures on platforms where not supported
                    }

                    ClientConnected?.Invoke();

                    // Read loop
                    while (_serverStream.IsConnected && !token.IsCancellationRequested)
                    {
                        IpcMessage? msg = await IpcMessage.ReadAsync(_serverStream, token).ConfigureAwait(false);
                        if (msg == null) break; // Client disconnected
                        MessageReceived?.Invoke(msg);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception)
                {
                    // Connection dropped or error; loop resets
                }
                finally
                {
                    ClientDisconnected?.Invoke();
                    try
                    {
                        if (_serverStream != null)
                        {
                            if (_serverStream.IsConnected) _serverStream.Disconnect();
                            _serverStream.Dispose();
                            _serverStream = null;
                        }
                    }
                    catch { }
                }
            }
        }

        public async Task<bool> SendMessageAsync(IpcMessage message, int timeoutMs = 3000, CancellationToken token = default)
        {
            if (_serverStream == null || !_serverStream.IsConnected) return false;

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            cts.CancelAfter(timeoutMs);

            await _sendLock.WaitAsync(cts.Token).ConfigureAwait(false);
            try
            {
                if (_serverStream == null || !_serverStream.IsConnected) return false;
                await IpcMessage.WriteAsync(_serverStream, message, cts.Token).ConfigureAwait(false);
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

        public void Stop()
        {
            _cts?.Cancel();
            try
            {
                if (_serverStream != null)
                {
                    if (_serverStream.IsConnected) _serverStream.Disconnect();
                    _serverStream.Dispose();
                    _serverStream = null;
                }
            }
            catch { }
            _cts?.Dispose();
            _cts = null;
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            Stop();
            _sendLock.Dispose();
        }
    }
}
