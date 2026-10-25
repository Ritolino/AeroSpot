using System;
using System.IO;
using System.Runtime.InteropServices;
using AeroSpot.Core.Logging;
using AeroSpot.Core.Native;

namespace AeroSpot.Host.Native
{
    public sealed class TrayManager : IDisposable
    {
        private readonly IntPtr _hWnd;
        private readonly uint _trayId;
        private bool _isAdded;
        private bool _isDisposed;

        public const uint CMD_SHOW_LAUNCHER = 1001;
        public const uint CMD_SETTINGS = 1002;
        public const uint CMD_WORKER_STATUS = 1003;
        public const uint CMD_EXIT = 1004;

        public event Action? ShowLauncherRequested;
        public event Action? SettingsRequested;
        public event Action? ExitRequested;

        public Func<string>? GetWorkerStatusFunc { get; set; }

        public TrayManager(IntPtr hWnd, uint trayId = 1)
        {
            _hWnd = hWnd;
            _trayId = trayId;
        }

        public bool AddTrayIcon(string tooltip = "AeroSpot")
        {
            if (_isAdded) return true;

            IntPtr hIcon = IntPtr.Zero;
            string iconPath = Path.Combine(AppContext.BaseDirectory, "app.ico");
            if (File.Exists(iconPath))
            {
                hIcon = Win32.LoadImage(IntPtr.Zero, iconPath, Win32.IMAGE_ICON, 16, 16, Win32.LR_LOADFROMFILE);
            }
            if (hIcon == IntPtr.Zero)
            {
                hIcon = Win32.LoadIcon(IntPtr.Zero, (IntPtr)Win32.IDI_APPLICATION);
            }

            var nid = new Win32.NOTIFYICONDATA();
            nid.cbSize = (uint)Marshal.SizeOf(nid);
            nid.hWnd = _hWnd;
            nid.uID = _trayId;
            nid.uFlags = Win32.NIF_MESSAGE | Win32.NIF_ICON | Win32.NIF_TIP;
            nid.uCallbackMessage = Win32.WM_TRAYICON;
            nid.hIcon = hIcon;
            nid.szTip = tooltip.Length > 127 ? tooltip.Substring(0, 127) : tooltip;

            // Assicura la pulizia di eventuali icone residue prima della registrazione
            Win32.Shell_NotifyIcon(Win32.NIM_DELETE, ref nid);

            _isAdded = Win32.Shell_NotifyIcon(Win32.NIM_ADD, ref nid);
            if (!_isAdded)
            {
                int err = Marshal.GetLastWin32Error();
                AppLogger.Warn($"Shell_NotifyIcon NIM_ADD returned false (LastError: {err})");
            }
            else
            {
                AppLogger.Info("Tray icon added successfully.");
            }
            return _isAdded;
        }

        public void RemoveTrayIcon()
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

        public void ShowBalloonNotification(string title, string text, uint infoFlags = Win32.NIIF_INFO)
        {
            if (!_isAdded) return;
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

        public void ShowContextMenu()
        {
            IntPtr hMenu = Win32.CreatePopupMenu();
            if (hMenu == IntPtr.Zero) return;

            string statusText = GetWorkerStatusFunc?.Invoke() ?? "Worker: Inattivo";

            string hotkeyLabel = "Alt + Spazio";
            Win32.AppendMenu(hMenu, Win32.MF_STRING, CMD_SHOW_LAUNCHER, AeroSpot.Core.Localization.LocalizationService.Get("Tray_OpenAeroSpot", hotkeyLabel));
            Win32.AppendMenu(hMenu, Win32.MF_STRING, CMD_SETTINGS, AeroSpot.Core.Localization.LocalizationService.Get("Tray_Settings"));
            Win32.AppendMenu(hMenu, Win32.MF_STRING | 0x00000001, CMD_WORKER_STATUS, $"Stato: {statusText}"); // 1 = MF_GRAYED
            Win32.AppendMenu(hMenu, Win32.MF_SEPARATOR, 0, string.Empty);
            Win32.AppendMenu(hMenu, Win32.MF_STRING, CMD_EXIT, AeroSpot.Core.Localization.LocalizationService.Get("Tray_Exit"));

            Win32.GetCursorPos(out Win32.POINT pt);
            Win32.SetForegroundWindow(_hWnd);

            uint cmd = Win32.TrackPopupMenuEx(
                hMenu,
                Win32.TPM_RETURNCMD | Win32.TPM_NONOTIFY | Win32.TPM_RIGHTBUTTON,
                pt.X,
                pt.Y,
                _hWnd,
                IntPtr.Zero);

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
            if (_isDisposed) return;
            _isDisposed = true;
            RemoveTrayIcon();
        }
    }
}
