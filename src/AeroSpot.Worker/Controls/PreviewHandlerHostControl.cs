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
        private IntPtr _hwndViewport = IntPtr.Zero;
        private IPreviewHandler? _previewHandler;
        private bool _isUnloaded;
        private IntPtr _mouseHookHandle = IntPtr.Zero;
        private Win32.HookProc? _mouseHookProc;
        private bool _isPanningWithMouse;
        private int _panLastScreenX;
        private int _panLastScreenY;

        public event Action? CloseRequested;
        public event EventHandler<Exception>? PreviewFailed;
        public event Action<double, double, double>? ViewportChanged;

        public PreviewHandlerHostControl(string filePath, Guid clsid)
        {
            _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
            _clsid = clsid;
        }

        private void EnsureMouseHookInstalled()
        {
            if (_mouseHookHandle != IntPtr.Zero) return;

            _mouseHookProc = MouseHookCallback;
            IntPtr hMod = Win32.GetModuleHandle(null);
            _mouseHookHandle = Win32.SetWindowsHookEx(Win32.WH_MOUSE_LL, _mouseHookProc, hMod, 0);
        }

        private void RemoveMouseHook()
        {
            _isPanningWithMouse = false;
            if (_mouseHookHandle != IntPtr.Zero)
            {
                try { Win32.UnhookWindowsHookEx(_mouseHookHandle); } catch { }
                _mouseHookHandle = IntPtr.Zero;
            }
            _mouseHookProc = null;
        }

        private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && !_isUnloaded && _hwndHost != IntPtr.Zero)
            {
                uint msg = (uint)wParam.ToInt64();
                if (msg == Win32.WM_MOUSEWHEEL)
                {
                    bool isCtrl = (Win32.GetAsyncKeyState(Win32.VK_CONTROL) & 0x8000) != 0;
                    bool isShift = (Win32.GetAsyncKeyState(Win32.VK_SHIFT) & 0x8000) != 0;
                    if (isCtrl || (isShift && _zoomScale > 1.001))
                    {
                        var hookStruct = Marshal.PtrToStructure<Win32.MSLLHOOKSTRUCT>(lParam);
                        Win32.GetWindowRect(_hwndHost, out var rect);
                        if (hookStruct.pt.X >= rect.Left && hookStruct.pt.X <= rect.Right &&
                            hookStruct.pt.Y >= rect.Top && hookStruct.pt.Y <= rect.Bottom)
                        {
                            short delta = (short)((hookStruct.mouseData >> 16) & 0xFFFF);
                            int ptX = hookStruct.pt.X;
                            int ptY = hookStruct.pt.Y;
                            Dispatcher.BeginInvoke(new Action(() =>
                            {
                                if (!_isUnloaded)
                                {
                                    if (isCtrl)
                                        ApplyCtrlWheelZoom(delta, ptX, ptY);
                                    else
                                        PanViewportBy(delta * 0.45, 0);
                                }
                            }));
                            return new IntPtr(1);
                        }
                    }
                }
                else if (_zoomScale > 1.001)
                {
                    if (msg == Win32.WM_LBUTTONDOWN || msg == Win32.WM_MBUTTONDOWN)
                    {
                        var hookStruct = Marshal.PtrToStructure<Win32.MSLLHOOKSTRUCT>(lParam);
                        Win32.GetWindowRect(_hwndHost, out var rect);
                        // Lascia liberi i 18px a destra per la scrollbar verticale nativa di Edge
                        if (hookStruct.pt.X >= rect.Left && hookStruct.pt.X <= rect.Right - 18 &&
                            hookStruct.pt.Y >= rect.Top && hookStruct.pt.Y <= rect.Bottom)
                        {
                            _isPanningWithMouse = true;
                            _panLastScreenX = hookStruct.pt.X;
                            _panLastScreenY = hookStruct.pt.Y;
                            if (msg == Win32.WM_MBUTTONDOWN)
                                return new IntPtr(1);
                        }
                    }
                    else if (msg == Win32.WM_MOUSEMOVE && _isPanningWithMouse)
                    {
                        var hookStruct = Marshal.PtrToStructure<Win32.MSLLHOOKSTRUCT>(lParam);
                        int dx = hookStruct.pt.X - _panLastScreenX;
                        int dy = hookStruct.pt.Y - _panLastScreenY;
                        if (Math.Abs(dx) > 0 || Math.Abs(dy) > 0)
                        {
                            _panLastScreenX = hookStruct.pt.X;
                            _panLastScreenY = hookStruct.pt.Y;
                            Dispatcher.BeginInvoke(new Action(() =>
                            {
                                if (!_isUnloaded && _isPanningWithMouse)
                                {
                                    PanViewportBy(dx, dy);
                                }
                            }));
                        }
                    }
                    else if (msg == Win32.WM_LBUTTONUP || msg == Win32.WM_MBUTTONUP)
                    {
                        _isPanningWithMouse = false;
                    }
                }
            }
            return Win32.CallNextHookEx(_mouseHookHandle, nCode, wParam, lParam);
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

            _hwndViewport = Win32.CreateWindowEx(
                0,
                "STATIC",
                string.Empty,
                Win32.WS_CHILD | Win32.WS_VISIBLE | Win32.WS_CLIPCHILDREN,
                0, 0,
                width, height,
                _hwndHost,
                IntPtr.Zero,
                IntPtr.Zero,
                IntPtr.Zero);

            if (_hwndViewport == IntPtr.Zero)
            {
                _hwndViewport = _hwndHost;
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
                    initFile.Initialize(_filePath, PreviewHandlerNativeMethods.STGM_READ | PreviewHandlerNativeMethods.STGM_SHARE_DENY_NONE);
                    initialized = true;
                    AppLogger.Info($"Preview handler {_clsid:B} inizializzato tramite IInitializeWithFile.");
                }
                catch (Exception ex)
                {
                    AppLogger.Debug($"IInitializeWithFile fallito per {_filePath}: {ex.Message}");
                }
            }

            // 4. Quarta priorità: IPersistFile (necessario per Microsoft Word Previewer ed handler legacy)
            if (!initialized && instance is IPersistFile persistFile)
            {
                try
                {
                    persistFile.Load(_filePath, PreviewHandlerNativeMethods.STGM_READ | PreviewHandlerNativeMethods.STGM_SHARE_DENY_NONE);
                    initialized = true;
                    AppLogger.Info($"Preview handler {_clsid:B} inizializzato tramite IPersistFile.");
                }
                catch (Exception ex)
                {
                    AppLogger.Debug($"IPersistFile fallito per {_filePath}: {ex.Message}");
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

            IntPtr targetParent = _hwndViewport != IntPtr.Zero ? _hwndViewport : _hwndHost;
            _previewHandler.SetWindow(targetParent, ref rect);
            _previewHandler.SetRect(ref rect);
            _previewHandler.DoPreview();
            EnsureMouseHookInstalled();
            AppLogger.Info($"DoPreview() eseguito con successo per {_filePath}");
        }

        public void GetWindowContext(ref PreviewHandlerFrameInfo pinfo)
        {
            pinfo.haccel = IntPtr.Zero;
            pinfo.cAccelEntries = 0;
        }

        int IPreviewHandlerFrame.TranslateAccelerator(ref Win32.MSG pmsg)
        {
            if (pmsg.message == (uint)Win32.WM_KEYDOWN || pmsg.message == (uint)Win32.WM_SYSKEYDOWN)
            {
                uint vk = (uint)pmsg.wParam.ToInt64();
                if (vk == Win32.VK_SPACE || vk == 0x1B) // Spazio o Escape
                {
                    Dispatcher.InvokeAsync(() => CloseRequested?.Invoke());
                    return 0; // S_OK: intercettato e gestito
                }
            }
            return 1; // S_FALSE: non gestito dall'accelerator del frame
        }

        private System.Windows.Threading.DispatcherTimer? _zoomSetRectTimer;
        private double _zoomScale = 1.0;
        private double _zoomPanX = 0.0;
        private double _zoomPanY = 0.0;
        private int _lastHandlerWidth = -1;
        private int _lastHandlerHeight = -1;
        private int _lastClipWidth = -1;
        private int _lastClipHeight = -1;
        private Rect _lastBoundingBox;

        protected override void OnWindowPositionChanged(Rect rcBoundingBox)
        {
            base.OnWindowPositionChanged(rcBoundingBox);
            _lastBoundingBox = rcBoundingBox;
            ApplyHandlerBounds(updateHandlerRect: true);
        }

        public void ResetZoom()
        {
            _zoomSetRectTimer?.Stop();
            _zoomScale = 1.0;
            _zoomPanX = 0.0;
            _zoomPanY = 0.0;
            ApplyHandlerBounds(updateHandlerRect: true);
            NotifyViewportChanged();
        }

        public void PanViewportBy(double dx, double dy)
        {
            if (_zoomScale <= 1.001 || _isUnloaded) return;
            _zoomPanX += dx;
            _zoomPanY += dy;
            ApplyHandlerBounds(updateHandlerRect: false);
            NotifyViewportChanged();
        }

        public void SetHorizontalScrollFraction(double fraction)
        {
            if (_zoomScale <= 1.001 || _isUnloaded) return;
            GetHostPixelSize(out double baseW, out _);
            double scaledW = baseW * _zoomScale;
            double maxOffset = Math.Max(0.0, scaledW - baseW);
            _zoomPanX = -Math.Clamp(fraction, 0.0, 1.0) * maxOffset;
            ApplyHandlerBounds(updateHandlerRect: false);
            NotifyViewportChanged();
        }

        private void GetHostPixelSize(out double baseW, out double baseH)
        {
            if (_lastBoundingBox.Width > 10 && _lastBoundingBox.Height > 10)
            {
                baseW = _lastBoundingBox.Width;
                baseH = _lastBoundingBox.Height;
                return;
            }

            if (_hwndHost != IntPtr.Zero && Win32.GetWindowRect(_hwndHost, out var hostRect))
            {
                int w = hostRect.Right - hostRect.Left;
                int h = hostRect.Bottom - hostRect.Top;
                if (w > 10 && h > 10)
                {
                    baseW = w;
                    baseH = h;
                    return;
                }
            }

            baseW = Math.Max(100, ActualWidth);
            baseH = Math.Max(100, ActualHeight);
        }

        private void NotifyViewportChanged()
        {
            ZoomChanged?.Invoke(_zoomScale);

            GetHostPixelSize(out double baseW, out _);
            double scaledW = baseW * _zoomScale;
            double maxOffset = Math.Max(0.0, scaledW - baseW);
            double hFraction = maxOffset > 0.5 ? Math.Clamp(-_zoomPanX / maxOffset, 0.0, 1.0) : 0.0;
            double viewportRatio = _zoomScale > 1.001 ? (1.0 / _zoomScale) : 1.0;
            ViewportChanged?.Invoke(_zoomScale, hFraction, viewportRatio);
        }

        private void ApplyCtrlWheelZoom(int delta, int screenX, int screenY)
        {
            double oldScale = _zoomScale;
            double factor = delta > 0 ? 1.15 : (1.0 / 1.15);
            double newScale = Math.Clamp(oldScale * factor, 1.0, 4.0);

            if (Math.Abs(newScale - 1.0) < 0.04)
            {
                newScale = 1.0;
            }

            if (Math.Abs(newScale - oldScale) > 0.001)
            {
                GetHostPixelSize(out double baseW, out double baseH);
                double localX = baseW / 2.0;
                double localY = baseH / 2.0;
                if (_hwndHost != IntPtr.Zero && Win32.GetWindowRect(_hwndHost, out var hostRect))
                {
                    localX = Math.Clamp(screenX - hostRect.Left, 0.0, baseW);
                    localY = Math.Clamp(screenY - hostRect.Top, 0.0, baseH);
                }

                double ratio = newScale / oldScale;
                _zoomPanX = localX - (localX - _zoomPanX) * ratio;
                _zoomPanY = localY - (localY - _zoomPanY) * ratio;
                _zoomScale = newScale;

                // Sposta subito la viewport e programma SetRect con debounce (65ms) per evitare che
                // ogni singolo scatto ravvicinato della rotellina scateni un ricalcolo multiplo sull'handler COM
                ApplyHandlerBounds(updateHandlerRect: false);
                NotifyViewportChanged();
                ScheduleZoomSetRect();
            }
        }

        private void ScheduleZoomSetRect()
        {
            if (_zoomSetRectTimer == null)
            {
                _zoomSetRectTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(55)
                };
                _zoomSetRectTimer.Tick += (s, e) =>
                {
                    _zoomSetRectTimer.Stop();
                    if (!_isUnloaded)
                    {
                        ApplyHandlerBounds(updateHandlerRect: true);
                    }
                };
            }
            _zoomSetRectTimer.Stop();
            _zoomSetRectTimer.Start();
        }

        private void ApplyHandlerBounds(bool updateHandlerRect = true)
        {
            if (_previewHandler == null || _isUnloaded)
                return;

            GetHostPixelSize(out double baseW, out double baseH);
            double scaledW = baseW * _zoomScale;
            double scaledH = baseH * _zoomScale;

            // Limita il pan orizzontale e verticale entro i bordi del viewport ingrandito
            double minX = Math.Min(0.0, baseW - scaledW);
            double minY = Math.Min(0.0, baseH - scaledH);
            _zoomPanX = Math.Clamp(_zoomPanX, minX, 0.0);
            _zoomPanY = Math.Clamp(_zoomPanY, minY, 0.0);

            int hostW = Math.Max(10, (int)Math.Round(baseW));
            int hostH = Math.Max(10, (int)Math.Round(baseH));
            int panX = (int)Math.Round(_zoomPanX);
            int panY = (int)Math.Round(_zoomPanY);
            int w = Math.Max(10, (int)Math.Round(scaledW));
            int h = Math.Max(10, (int)Math.Round(scaledH));

            try
            {
                // Imposta una Regione Win32 rigida a livello DWM su _hwndHost così che la superficie grafica
                // dell'handler COM non possa MAI sbordare fuori dall'area (0, 0, hostW, hostH) nemmeno per un millisecondo
                if (_hwndHost != IntPtr.Zero && (hostW != _lastClipWidth || hostH != _lastClipHeight))
                {
                    _lastClipWidth = hostW;
                    _lastClipHeight = hostH;
                    IntPtr hRgn = Win32.CreateRectRgn(0, 0, hostW, hostH);
                    if (hRgn != IntPtr.Zero)
                    {
                        if (Win32.SetWindowRgn(_hwndHost, hRgn, true) == 0)
                        {
                            Win32.DeleteObject(hRgn);
                        }
                    }
                }

                // Imposta anche una Regione Win32 sulla finestra viewport interna corrispondente alla porzione visibile
                // (-panX, -panY, -panX + hostW, -panY + hostH), garantendo il clipping hardware DWM dell'handler
                if (_hwndViewport != IntPtr.Zero && _hwndViewport != _hwndHost)
                {
                    if (_zoomScale > 1.001)
                    {
                        IntPtr vpRgn = Win32.CreateRectRgn(-panX, -panY, -panX + hostW, -panY + hostH);
                        if (vpRgn != IntPtr.Zero)
                        {
                            if (Win32.SetWindowRgn(_hwndViewport, vpRgn, false) == 0)
                            {
                                Win32.DeleteObject(vpRgn);
                            }
                        }

                        IntPtr childHwnd = Win32.GetWindow(_hwndViewport, Win32.GW_CHILD);
                        if (childHwnd != IntPtr.Zero)
                        {
                            IntPtr childRgn = Win32.CreateRectRgn(-panX, -panY, -panX + hostW, -panY + hostH);
                            if (childRgn != IntPtr.Zero)
                            {
                                if (Win32.SetWindowRgn(childHwnd, childRgn, false) == 0)
                                {
                                    Win32.DeleteObject(childRgn);
                                }
                            }
                        }
                    }
                    else
                    {
                        Win32.SetWindowRgn(_hwndViewport, IntPtr.Zero, false);
                        IntPtr childHwnd = Win32.GetWindow(_hwndViewport, Win32.GW_CHILD);
                        if (childHwnd != IntPtr.Zero)
                        {
                            Win32.SetWindowRgn(childHwnd, IntPtr.Zero, false);
                        }
                    }

                    Win32.SetWindowPos(
                        _hwndViewport,
                        IntPtr.Zero,
                        panX,
                        panY,
                        w,
                        h,
                        Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE | Win32.SWP_NOCOPYBITS);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Debug($"Errore aggiornamento regioni/viewport PreviewHandler: {ex.Message}");
            }

            bool sizeChanged = (w != _lastHandlerWidth) || (h != _lastHandlerHeight);
            if (updateHandlerRect && sizeChanged)
            {
                _lastHandlerWidth = w;
                _lastHandlerHeight = h;

                var rect = new Win32.RECT
                {
                    Left = 0,
                    Top = 0,
                    Right = w,
                    Bottom = h
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
            else if (msg == (int)Win32.WM_MOUSEWHEEL)
            {
                int delta = (short)((wParam.ToInt64() >> 16) & 0xFFFF);
                bool isCtrl = ((wParam.ToInt64() & 0x0008) != 0) || (Win32.GetAsyncKeyState(Win32.VK_CONTROL) & 0x8000) != 0;
                ForwardMouseWheel(delta, isCtrl);
                handled = true;
                return IntPtr.Zero;
            }

            return base.WndProc(hwnd, msg, wParam, lParam, ref handled);
        }

        public double ZoomScale => _zoomScale;
        public double ZoomPanX => _zoomPanX;
        public double ZoomPanY => _zoomPanY;
        public IntPtr ViewportHandle => _hwndViewport;
        public event Action<double>? ZoomChanged;

        public void ZoomAtScreenPoint(int delta, int screenX, int screenY)
        {
            ApplyCtrlWheelZoom(delta, screenX, screenY);
        }

        public void ForwardMouseWheel(int delta, bool isCtrl)
        {
            Win32.GetCursorPos(out var pt);
            int x = pt.X;
            int y = pt.Y;

            if (_hwndHost != IntPtr.Zero && Win32.GetWindowRect(_hwndHost, out var hostRect))
            {
                x = (pt.X >= hostRect.Left && pt.X <= hostRect.Right) ? pt.X : (hostRect.Left + hostRect.Right) / 2;
                y = (pt.Y >= hostRect.Top && pt.Y <= hostRect.Bottom) ? pt.Y : (hostRect.Top + hostRect.Bottom) / 2;
            }

            if (isCtrl)
            {
                ApplyCtrlWheelZoom(delta, x, y);
                return;
            }

            if (_hwndHost == IntPtr.Zero) return;

            int fwKeys = 0;
            IntPtr wParam = new IntPtr((delta << 16) | (fwKeys & 0xFFFF));
            IntPtr lParam = new IntPtr(((short)y << 16) | ((ushort)x));

            IntPtr targetHwnd = FindDeepestFirstChild(_hwndHost);
            if (targetHwnd == IntPtr.Zero || targetHwnd == _hwndHost)
            {
                targetHwnd = FindDeepestChildAtScreenPoint(_hwndHost, new Win32.POINT { X = x, Y = y });
            }

            if (targetHwnd != IntPtr.Zero)
            {
                Win32.PostMessage(targetHwnd, Win32.WM_MOUSEWHEEL, wParam, lParam);
            }
        }

        private static IntPtr FindDeepestChildAtScreenPoint(IntPtr rootHwnd, Win32.POINT screenPt)
        {
            IntPtr hit = Win32.WindowFromPoint(screenPt);
            if (hit != IntPtr.Zero && IsDescendantWindow(rootHwnd, hit))
            {
                return hit;
            }
            return FindDeepestFirstChild(rootHwnd);
        }

        private static bool IsDescendantWindow(IntPtr parent, IntPtr candidate)
        {
            IntPtr cur = candidate;
            for (int depth = 0; depth < 16 && cur != IntPtr.Zero; depth++)
            {
                if (cur == parent) return true;
                cur = Win32.GetAncestor(cur, 1); // GA_PARENT = 1
            }
            return false;
        }

        private static IntPtr FindDeepestFirstChild(IntPtr parent)
        {
            IntPtr cur = parent;
            for (int depth = 0; depth < 10; depth++)
            {
                IntPtr child = Win32.GetWindow(cur, Win32.GW_CHILD);
                if (child == IntPtr.Zero) break;
                cur = child;
            }
            return cur;
        }

        public void UnloadHandler()
        {
            if (_isUnloaded) return;
            _isUnloaded = true;
            _zoomSetRectTimer?.Stop();
            RemoveMouseHook();

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

            if (_hwndViewport != IntPtr.Zero && _hwndViewport != _hwndHost)
            {
                Win32.DestroyWindow(_hwndViewport);
                _hwndViewport = IntPtr.Zero;
            }

            if (_hwndHost != IntPtr.Zero)
            {
                Win32.DestroyWindow(_hwndHost);
                _hwndHost = IntPtr.Zero;
            }
        }
    }
}
