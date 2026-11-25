using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AeroSpot.Tests
{
    /// <summary>
    /// Owns a single persistent STA UI thread and WPF Application for the entire test assembly,
    /// avoiding testhost crashes caused by short-lived STA threads shutting down Application.Current.
    /// </summary>
    internal static class StaTestRunner
    {
        private static readonly object _initLock = new object();
        private static Thread? _staThread;
        private static Dispatcher? _dispatcher;
        private static bool _isShutdown;

        private static Dispatcher EnsureStarted()
        {
            lock (_initLock)
            {
                if (_isShutdown)
                {
                    throw new ObjectDisposedException(nameof(StaTestRunner), "StaTestRunner has already been shut down.");
                }

                if (_dispatcher != null && !_dispatcher.HasShutdownStarted)
                {
                    return _dispatcher;
                }

                using var ready = new ManualResetEventSlim(false);
                Dispatcher? createdDispatcher = null;
                Exception? startupError = null;

                _staThread = new Thread(() =>
                {
                    try
                    {
                        var dispatcher = Dispatcher.CurrentDispatcher;
                        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));

                        if (Application.Current == null)
                        {
                            _ = new Application
                            {
                                ShutdownMode = ShutdownMode.OnExplicitShutdown
                            };
                        }

                        createdDispatcher = dispatcher;
                        ready.Set();
                        Dispatcher.Run();
                    }
                    catch (Exception ex)
                    {
                        startupError = ex;
                        ready.Set();
                    }
                })
                {
                    IsBackground = true,
                    Name = "AeroSpot.StaTestRunner"
                };

                _staThread.SetApartmentState(ApartmentState.STA);
                _staThread.Start();

                if (!ready.Wait(TimeSpan.FromSeconds(10)))
                {
                    Assert.Fail("Timed out waiting for STA test runner thread initialization.");
                }

                if (startupError != null)
                {
                    ExceptionDispatchInfo.Capture(startupError).Throw();
                }

                _dispatcher = createdDispatcher ?? throw new InvalidOperationException("Failed to initialize STA Dispatcher.");
                return _dispatcher;
            }
        }

        public static void Run(Action action, int timeoutMs = 15000)
        {
            ArgumentNullException.ThrowIfNull(action);

            var dispatcher = EnsureStarted();
            if (dispatcher.CheckAccess())
            {
                action();
                return;
            }

            using var done = new ManualResetEventSlim(false);
            ExceptionDispatchInfo? captured = null;

            dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    captured = ExceptionDispatchInfo.Capture(ex);
                }
                finally
                {
                    done.Set();
                }
            }));

            if (!done.Wait(timeoutMs))
            {
                Assert.Fail($"STA test action timed out after {timeoutMs} ms.");
            }

            captured?.Throw();
        }

        public static async Task RunAsync(Func<Task> action, int timeoutMs = 15000)
        {
            ArgumentNullException.ThrowIfNull(action);

            var dispatcher = EnsureStarted();
            if (dispatcher.CheckAccess())
            {
                await action().ConfigureAwait(true);
                return;
            }

            var tcs = new TaskCompletionSource<ExceptionDispatchInfo?>(TaskCreationOptions.RunContinuationsAsynchronously);

            _ = dispatcher.BeginInvoke(new Action(async () =>
            {
                try
                {
                    await action().ConfigureAwait(true);
                    tcs.TrySetResult(null);
                }
                catch (Exception ex)
                {
                    tcs.TrySetResult(ExceptionDispatchInfo.Capture(ex));
                }
            }));

            var completed = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs)).ConfigureAwait(false);
            if (completed != tcs.Task)
            {
                Assert.Fail($"STA async test action timed out after {timeoutMs} ms.");
            }

            var captured = await tcs.Task.ConfigureAwait(false);
            captured?.Throw();
        }

        public static void Shutdown()
        {
            Thread? threadToJoin;
            Dispatcher? dispatcherToStop;

            lock (_initLock)
            {
                _isShutdown = true;
                dispatcherToStop = _dispatcher;
                threadToJoin = _staThread;
                _dispatcher = null;
                _staThread = null;
            }

            if (dispatcherToStop != null && !dispatcherToStop.HasShutdownStarted)
            {
                try
                {
                    dispatcherToStop.BeginInvokeShutdown(DispatcherPriority.Send);
                }
                catch { }
            }

            if (threadToJoin != null && threadToJoin.IsAlive)
            {
                try
                {
                    threadToJoin.Join(TimeSpan.FromSeconds(5));
                }
                catch { }
            }
        }
    }
}
