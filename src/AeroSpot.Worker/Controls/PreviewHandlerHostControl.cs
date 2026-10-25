using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using AeroSpot.Core.Logging;
using AeroSpot.Core.Native;
using AeroSpot.Core.Preview;

namespace AeroSpot.Worker.Controls
{
    public sealed class PreviewHandlerHostControl : HwndHost, IPreviewHandlerFrame
    {
        private readonly string _filePath;
        private readonly Guid _clsid;

        private IntPtr _hwndHost = IntPtr.Zero;
        private IPreviewHandler? _previewHandler;
        private bool _isUnloaded;

        public event Action? CloseRequested;
        public event EventHandler<Exception>? PreviewFailed;

        public PreviewHandlerHostControl(string filePath, Guid clsid)
        {
            _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
            _clsid = clsid;
        }

        protected override HandleRef BuildWindowCore(HandleRef hwndParent)
        {
            int width = Math.Max((int)ActualWidth, 100);
            int height = Math.Max((int)ActualHeight, 100);

            _hwndHost = Win32.CreateWindowEx(
                0,
                "STATIC",
                string.Empty,
                Win32.WS_CHILD | Win32.WS_VISIBLE | Win32.WS_CLIPCHILDREN,
                0, 0,
                width, height,
                hwndParent.Handle,
                IntPtr.Zero,
                IntPtr.Zero,
                IntPtr.Zero);

            if (_hwndHost == IntPtr.Zero)
            {
                int err = Marshal.GetLastWin32Error();
                AppLogger.Error($"CreateWindowEx per PreviewHandlerHost fallito con errore {err}");
                throw new InvalidOperationException($"CreateWindowEx failed: {err}");
            }

            try
            {
                InitializeAndStartHandler(width, height);
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Impossibile inizializzare il preview handler {_clsid:B} per '{_filePath}': {ex.Message}");
                UnloadHandler();
                PreviewFailed?.Invoke(this, ex);
            }

            return new HandleRef(this, _hwndHost);
        }

        private void InitializeAndStartHandler(int width, int height)
        {
            Type? handlerType = Type.GetTypeFromCLSID(_clsid);
            if (handlerType == null)
            {
                throw new InvalidOperationException($"COM Type non trovato per CLSID {_clsid:B}");
            }

            object? instance = Activator.CreateInstance(handlerType);
            if (instance == null)
            {
                throw new InvalidOperationException($"Istanziazione COM fallita per CLSID {_clsid:B}");
            }

            if (instance is not IPreviewHandler handler)
            {
                Marshal.FinalReleaseComObject(instance);
                throw new InvalidOperationException($"L'oggetto COM {_clsid:B} non implementa IPreviewHandler");
            }

            _previewHandler = handler;

            // Collega il frame host tramite IObjectWithSite (fondamentale per Office Excel/Word/PowerPoint previewers)
            if (instance is IObjectWithSite site)
            {
                try
                {
                    site.SetSite(this);
                    AppLogger.Info($"IObjectWithSite.SetSite collegato con successo per {_clsid:B}.");
                }
                catch (Exception ex)
                {
                    AppLogger.Warn($"SetSite non riuscito per {_clsid:B}: {ex.Message}");
                }
            }

            // Inizializzazione tramite interfaccia supportata (ordine di priorità Windows Shell)
            bool initialized = false;

            // 1. Priorità massima: IInitializeWithItem
            if (instance is IInitializeWithItem initItem)
            {
                try
                {
                    Guid iid = PreviewHandlerNativeMethods.IidShellItem;
                    PreviewHandlerNativeMethods.SHCreateItemFromParsingName(_filePath, IntPtr.Zero, iid, out object shellItem);
                    initItem.Initialize(shellItem, PreviewHandlerNativeMethods.STGM_READ);
                    initialized = true;
                    AppLogger.Info($"Preview handler {_clsid:B} inizializzato tramite IInitializeWithItem.");
                }
                catch (Exception ex)
                {
                    AppLogger.Debug($"IInitializeWithItem fallito per {_filePath}: {ex.Message}");
                }
            }

            // 2. Seconda priorità: IInitializeWithStream
            if (!initialized && instance is IInitializeWithStream initStream)
            {
                try
                {
                    PreviewHandlerNativeMethods.SHCreateStreamOnFileEx(
                        _filePath,
                        PreviewHandlerNativeMethods.STGM_READ | PreviewHandlerNativeMethods.STGM_SHARE_DENY_NONE,
                        PreviewHandlerNativeMethods.FILE_ATTRIBUTE_NORMAL,
                        false,
                        IntPtr.Zero,
                        out var stream);

                    initStream.Initialize(stream, PreviewHandlerNativeMethods.STGM_READ);
                    initialized = true;
                    AppLogger.Info($"Preview handler {_clsid:B} inizializzato tramite IInitializeWithStream.");
                }
                catch (Exception ex)
                {
                    AppLogger.Debug($"IInitializeWithStream fallito per {_filePath}: {ex.Message}");
                }
            }

            // 3. Terza priorità: IInitializeWithFile
            if (!initialized && instance is IInitializeWithFile initFile)
            {
                try
                {
                    initFile.Initialize(_filePath, PreviewHandlerNativeMethods.STGM_READ);
                    initialized = true;
                    AppLogger.Info($"Preview handler {_clsid:B} inizializzato tramite IInitializeWithFile.");
                }
                catch (Exception ex)
                {
                    AppLogger.Debug($"IInitializeWithFile fallito per {_filePath}: {ex.Message}");
                }
            }

            if (!initialized)
            {
                throw new NotSupportedException($"L'handler {_clsid:B} non implementa alcuna interfaccia di inizializzazione supportata.");
            }

            var rect = new Win32.RECT
            {
                Left = 0,
                Top = 0,
                Right = width,
                Bottom = height
            };

            _previewHandler.SetWindow(_hwndHost, ref rect);
            _previewHandler.SetRect(ref rect);
            _previewHandler.DoPreview();
            AppLogger.Info($"DoPreview() eseguito con successo per {_filePath}");
        }

        public void GetWindowContext(ref PreviewHandlerFrameInfo pinfo)
        {
            pinfo.haccel = IntPtr.Zero;
            pinfo.cAccelEntries = 0;
        }

        int IPreviewHandlerFrame.TranslateAccelerator(ref Win32.MSG pmsg)
        {
            return 1; // S_FALSE: non gestito dall'accelerator del frame
        }

        protected override void OnWindowPositionChanged(Rect rcBoundingBox)
        {
            base.OnWindowPositionChanged(rcBoundingBox);

            if (_previewHandler != null && !_isUnloaded)
            {
                var rect = new Win32.RECT
                {
                    Left = 0,
                    Top = 0,
                    Right = (int)rcBoundingBox.Width,
                    Bottom = (int)rcBoundingBox.Height
                };

                try
                {
                    _previewHandler.SetRect(ref rect);
                }
                catch (Exception ex)
                {
                    AppLogger.Debug($"Errore SetRect su PreviewHandler: {ex.Message}");
                }
            }
        }

        protected override IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == (int)Win32.WM_KEYDOWN)
            {
                uint vk = (uint)wParam.ToInt64();
                if (vk == Win32.VK_SPACE || vk == 0x1B) // Spazio o Esc
                {
                    CloseRequested?.Invoke();
                    handled = true;
                    return IntPtr.Zero;
                }
            }

            return base.WndProc(hwnd, msg, wParam, lParam, ref handled);
        }

        public void UnloadHandler()
        {
            if (_isUnloaded) return;
            _isUnloaded = true;

            if (_previewHandler != null)
            {
                if (_previewHandler is IObjectWithSite site)
                {
                    try { site.SetSite(null); } catch { }
                }

                try
                {
                    _previewHandler.Unload();
                }
                catch (Exception ex)
                {
                    AppLogger.Debug($"Eccezione durante Unload PreviewHandler: {ex.Message}");
                }

                try
                {
                    Marshal.FinalReleaseComObject(_previewHandler);
                }
                catch { }

                _previewHandler = null;
            }
        }

        protected override void DestroyWindowCore(HandleRef hwnd)
        {
            UnloadHandler();

            if (_hwndHost != IntPtr.Zero)
            {
                Win32.DestroyWindow(_hwndHost);
                _hwndHost = IntPtr.Zero;
            }
        }
    }
}
