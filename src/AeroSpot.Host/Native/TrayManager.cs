using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using AeroSpot.Core.Config;
using AeroSpot.Core.Localization;
using AeroSpot.Core.Logging;
using AeroSpot.Core.Native;

namespace AeroSpot.Host.Native
{
    public sealed class TrayManager : IDisposable
    {
        private readonly IntPtr _hWnd;
        private readonly uint _trayId;
        private readonly object _syncLock = new object();
        private AppConfig? _config;
        private IntPtr _hIcon;
        private bool _ownsIcon;
        private volatile bool _isAdded;
        private volatile bool _isDisposed;

        public const uint CMD_SHOW_LAUNCHER = 1001;
        public const uint CMD_SETTINGS = 1002;
        public const uint CMD_WORKER_STATUS = 1003;
        public const uint CMD_EXIT = 1004;

        public event Action? ShowLauncherRequested;
        public event Action? SettingsRequested;
        public event Action? ExitRequested;

        public Func<string>? GetWorkerStatusFunc { get; set; }
        public Func<string>? GetHotkeyLabelFunc { get; set; }

        public TrayManager(IntPtr hWnd, uint trayId = 1, AppConfig? config = null)
        {
            _hWnd = hWnd;
            _trayId = trayId;
            _config = config;
        }

        public void UpdateConfig(AppConfig config)
        {
            lock (_syncLock)
            {
                _config = config;
            }
        }

        public bool AddTrayIcon(string tooltip = "AeroSpot", bool forceReadd = false)
        {
            Win32.NOTIFYICONDATA nid;
            lock (_syncLock)
            {
                if (_isDisposed) return false;
                if (forceReadd)
                {
                    _isAdded = false;
                }
                if (_isAdded) return true;

                if (_ownsIcon && _hIcon != IntPtr.Zero)
                {
                    Win32.DestroyIcon(_hIcon);
                    _hIcon = IntPtr.Zero;
                    _ownsIcon = false;
                }

                IntPtr hIcon = IntPtr.Zero;
                bool ownsIcon = false;
                string iconPath = Path.Combine(AppContext.BaseDirectory, "app.ico");
                if (File.Exists(iconPath))
                {
                    hIcon = Win32.LoadImage(IntPtr.Zero, iconPath, Win32.IMAGE_ICON, 16, 16, Win32.LR_LOADFROMFILE);
                    if (hIcon != IntPtr.Zero)
                    {
                        ownsIcon = true;
                    }
                }
                if (hIcon == IntPtr.Zero)
                {
                    hIcon = Win32.LoadIcon(IntPtr.Zero, (IntPtr)Win32.IDI_APPLICATION);
                }

                _hIcon = hIcon;
                _ownsIcon = ownsIcon;

                nid = new Win32.NOTIFYICONDATA();
                nid.cbSize = (uint)Marshal.SizeOf(nid);
                nid.hWnd = _hWnd;
                nid.uID = _trayId;
                nid.uFlags = Win32.NIF_MESSAGE | Win32.NIF_ICON | Win32.NIF_TIP;
                nid.uCallbackMessage = Win32.WM_TRAYICON;
                nid.hIcon = hIcon;
                nid.szTip = tooltip.Length > 127 ? tooltip.Substring(0, 127) : tooltip;

                // Assicura la pulizia di eventuali icone residue prima della registrazione
                Win32.Shell_NotifyIcon(Win32.NIM_DELETE, ref nid);
            }

            for (int attempt = 1; attempt <= 3; attempt++)
            {
                lock (_syncLock)
                {
                    if (_isDisposed) return false;
                    if (Win32.Shell_NotifyIcon(Win32.NIM_ADD, ref nid) || Win32.Shell_NotifyIcon(Win32.NIM_MODIFY, ref nid))
                    {
                        _isAdded = true;
                        break;
                    }
                }

                if (attempt < 3)
                {
                    System.Threading.Thread.Sleep(100);
                }
            }

            if (!_isAdded)
            {
                int err = Marshal.GetLastWin32Error();
                AppLogger.Warn($"Shell_NotifyIcon NIM_ADD returned false (LastError: {err}). Avvio retry asincrono in background...");
                _ = Task.Run(async () =>
                {
                    for (int i = 1; i <= 5; i++)
                    {
                        await Task.Delay(1000).ConfigureAwait(false);
                        lock (_syncLock)
                        {
                            if (_isAdded || _isDisposed) break;

                            var retryNid = nid;
                            if (Win32.Shell_NotifyIcon(Win32.NIM_ADD, ref retryNid) || Win32.Shell_NotifyIcon(Win32.NIM_MODIFY, ref retryNid))
                            {
                                _isAdded = true;
                                AppLogger.Info($"Tray icon aggiunta con successo al tentativo differito #{i}.");
                                break;
                            }
                        }
                    }
                });
            }
            else
            {
                AppLogger.Info("Tray icon added successfully.");
            }
            return _isAdded;
        }

        public void RemoveTrayIcon()
        {
            lock (_syncLock)
            {
                if (!_isAdded) return;
                var nid = new Win32.NOTIFYICONDATA();
                nid.cbSize = (uint)Marshal.SizeOf(nid);
                nid.hWnd = _hWnd;
                nid.uID = _trayId;
                Win32.Shell_NotifyIcon(Win32.NIM_DELETE, ref nid);
                _isAdded = false;
                AppLogger.Info("Tray icon removed.");
            }
        }

        public void ShowBalloonNotification(string title, string text, uint infoFlags = Win32.NIIF_INFO)
        {
            lock (_syncLock)
            {
                if (!_isAdded || _isDisposed) return;
                var nid = new Win32.NOTIFYICONDATA();
                nid.cbSize = (uint)Marshal.SizeOf(nid);
                nid.hWnd = _hWnd;
                nid.uID = _trayId;
                nid.uFlags = Win32.NIF_INFO;
                nid.szInfoTitle = title.Length > 63 ? title.Substring(0, 63) : title;
                nid.szInfo = text.Length > 255 ? text.Substring(0, 255) : text;
                nid.dwInfoFlags = infoFlags;
                Win32.Shell_NotifyIcon(Win32.NIM_MODIFY, ref nid);
            }
        }

        public void ShowContextMenu()
        {
            IntPtr hMenu = Win32.CreatePopupMenu();
            if (hMenu == IntPtr.Zero) return;

            string statusText = GetWorkerStatusFunc?.Invoke() ?? LocalizationService.Get("Tray_Worker_Idle");

            string? hotkeyLabel = GetHotkeyLabelFunc?.Invoke();
            if (string.IsNullOrWhiteSpace(hotkeyLabel))
            {
                lock (_syncLock)
                {
                    hotkeyLabel = _config?.SpotlightHotkeyText;
                }
            }
            if (string.IsNullOrWhiteSpace(hotkeyLabel))
            {
                hotkeyLabel = $"Alt + {LocalizationService.Get("Settings_Key_Space")}";
            }

            Win32.AppendMenu(hMenu, Win32.MF_STRING, CMD_SHOW_LAUNCHER, LocalizationService.Get("Tray_OpenAeroSpot", hotkeyLabel));
            Win32.AppendMenu(hMenu, Win32.MF_STRING, CMD_SETTINGS, LocalizationService.Get("Tray_Settings"));
            Win32.AppendMenu(hMenu, Win32.MF_STRING | 0x00000001, CMD_WORKER_STATUS, LocalizationService.Get("Tray_Status_Format", statusText)); // 1 = MF_GRAYED
            Win32.AppendMenu(hMenu, Win32.MF_SEPARATOR, 0, string.Empty);
            Win32.AppendMenu(hMenu, Win32.MF_STRING, CMD_EXIT, LocalizationService.Get("Tray_Exit"));

            Win32.GetCursorPos(out Win32.POINT pt);
            Win32.SetForegroundWindow(_hWnd);

            uint cmd = Win32.TrackPopupMenuEx(
                hMenu,
                Win32.TPM_RETURNCMD | Win32.TPM_NONOTIFY | Win32.TPM_RIGHTBUTTON,
                pt.X,
                pt.Y,
                _hWnd,
                IntPtr.Zero);

            Win32.PostMessage(_hWnd, 0, IntPtr.Zero, IntPtr.Zero);
            Win32.DestroyMenu(hMenu);

            HandleCommand(cmd);
        }

        public void HandleCommand(uint cmd)
        {
            switch (cmd)
            {
                case CMD_SHOW_LAUNCHER:
                    ShowLauncherRequested?.Invoke();
                    break;
                case CMD_SETTINGS:
                    SettingsRequested?.Invoke();
                    break;
                case CMD_EXIT:
                    ExitRequested?.Invoke();
                    break;
            }
        }

        public void Dispose()
        {
            lock (_syncLock)
            {
                if (_isDisposed) return;
                _isDisposed = true;
            }

            RemoveTrayIcon();

            lock (_syncLock)
            {
                if (_ownsIcon && _hIcon != IntPtr.Zero)
                {
                    Win32.DestroyIcon(_hIcon);
                }
                _hIcon = IntPtr.Zero;
                _ownsIcon = false;
            }
        }
    }
}
