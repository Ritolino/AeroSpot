using System;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
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

        private NamedPipeServerStream CreateSecurePipeServer()
        {
            var pipeSecurity = new PipeSecurity();
            var currentUserSid = WindowsIdentity.GetCurrent().User;
            if (currentUserSid != null)
            {
                pipeSecurity.AddAccessRule(new PipeAccessRule(currentUserSid, PipeAccessRights.FullControl, AccessControlType.Allow));
            }

            return NamedPipeServerStreamAcl.Create(
                _pipeName,
                PipeDirection.InOut,
                1, // Single client (one worker at a time)
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous,
                0,
                0,
                pipeSecurity);
        }

        private async Task RunServerLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested && !_isDisposed)
            {
                bool connectedRaised = false;
                try
                {
                    _serverStream = CreateSecurePipeServer();

                    await _serverStream.WaitForConnectionAsync(token).ConfigureAwait(false);

                    // Security check: ensure caller belongs to the same Windows user
                    bool clientAuthorized = true;
                    try
                    {
                        string clientUser = _serverStream.GetImpersonationUserName();
                        string currentUser = WindowsIdentity.GetCurrent().Name;
                        if (!string.Equals(clientUser, currentUser, StringComparison.OrdinalIgnoreCase))
                        {
                            clientAuthorized = false;
                        }
                    }
                    catch
                    {
                        // Ignore impersonation check failures on platforms where not supported
                    }

                    if (!clientAuthorized)
                    {
                        // Let finally block cleanly disconnect and dispose _serverStream without double-dispose
                        continue;
                    }

                    connectedRaised = true;
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
                    try { await Task.Delay(100, token).ConfigureAwait(false); } catch { }
                }
                finally
                {
                    if (connectedRaised)
                    {
                        ClientDisconnected?.Invoke();
                    }

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
            if (_isDisposed) return false;

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            cts.CancelAfter(timeoutMs);

            // Attendi brevemente che la connessione sia attiva se il client è in fase di handshake
            int waitMs = 0;
            while ((_serverStream == null || !_serverStream.IsConnected) && waitMs < Math.Min(timeoutMs, 500) && !cts.Token.IsCancellationRequested)
            {
                try { await Task.Delay(15, cts.Token).ConfigureAwait(false); } catch { break; }
                waitMs += 15;
            }

            bool lockAcquired = false;
            try
            {
                await _sendLock.WaitAsync(cts.Token).ConfigureAwait(false);
                lockAcquired = true;

                var stream = _serverStream;
                if (stream == null || !stream.IsConnected) return false;

                await IpcMessage.WriteAsync(stream, message, cts.Token).ConfigureAwait(false);
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
            catch (IOException)
            {
                return false;
            }
            finally
            {
                if (lockAcquired)
                {
                    try { _sendLock.Release(); } catch (ObjectDisposedException) { }
                }
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
