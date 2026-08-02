using System.IO;
using System.Runtime.InteropServices;

namespace NetworkMonitor.Services.Platform
{
    public sealed class TrayIconService : IDisposable
    {
        private delegate IntPtr SubclassProcDelegate(
            IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam,
            nuint uIdSubclass, nuint dwRefData);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadImage(
            IntPtr hInst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

        [DllImport("user32.dll")]
        private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("comctl32.dll")]
        private static extern bool SetWindowSubclass(
            IntPtr hWnd, SubclassProcDelegate pfnSubclass, nuint uIdSubclass, nuint dwRefData);

        [DllImport("comctl32.dll")]
        private static extern bool RemoveWindowSubclass(
            IntPtr hWnd, SubclassProcDelegate pfnSubclass, nuint uIdSubclass);

        [DllImport("comctl32.dll")]
        private static extern IntPtr DefSubclassProc(
            IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr CreatePopupMenu();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, uint uIDNewItem, string lpNewItem);

        [DllImport("user32.dll")]
        private static extern uint TrackPopupMenu(
            IntPtr hMenu, uint uFlags, int x, int y, int nReserved, IntPtr hWnd, IntPtr prcRect);

        [DllImport("user32.dll")]
        private static extern bool DestroyMenu(IntPtr hMenu);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NOTIFYICONDATA
        {
            public uint cbSize;
            public IntPtr hWnd;
            public uint uID;
            public uint uFlags;
            public uint uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szTip;
            public uint dwState;
            public uint dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string szInfo;
            public uint uTimeoutOrVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string szInfoTitle;
            public uint dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int x;
            public int y;
        }

        private const uint ImageIcon = 1;
        private const uint LrLoadFromFile = 0x0010;
        private const uint NimAdd = 0;
        private const uint NimDelete = 2;
        private const uint NifMessage = 1;
        private const uint NifIcon = 2;
        private const uint NifTip = 4;
        private const uint WmTrayIcon = 0x8001;
        private const uint WmRButtonUp = 0x0205;
        private const uint WmLButtonDblClk = 0x0203;
        private const uint TpmReturnCmd = 0x0100;
        private const uint TpmRightButton = 0x0002;
        private const uint MfString = 0;
        private const int SwRestore = 9;
        private const int SwShow = 5;
        private const uint MenuShow = 1;
        private const uint MenuExit = 2;
        private const uint MenuMiniGraph = 3;
        private const uint MfChecked = 0x0008;

        private readonly IntPtr _hwnd;
        private readonly Action _onExit;
        private readonly Action _onToggleMiniGraph;
        private readonly Func<bool> _isMiniGraphVisible;
        private readonly SubclassProcDelegate _subclassProc;
        private readonly IntPtr _hIcon;
        private readonly bool _ownsIcon;
        private bool _disposed;

        public TrayIconService(IntPtr hwnd, Action onExit, Action onToggleMiniGraph, Func<bool> isMiniGraphVisible)
        {
            _hwnd = hwnd;
            _onExit = onExit;
            _onToggleMiniGraph = onToggleMiniGraph;
            _isMiniGraphVisible = isMiniGraphVisible;
            _subclassProc = SubclassProc;

            string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
            IntPtr hIcon = LoadImage(IntPtr.Zero, iconPath, ImageIcon, 0, 0, LrLoadFromFile);
            _ownsIcon = hIcon != IntPtr.Zero;

            if (!_ownsIcon)
            {
                hIcon = LoadIcon(IntPtr.Zero, new IntPtr(32512));
            }

            _hIcon = hIcon;

            NOTIFYICONDATA nid = new()
            {
                cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = hwnd,
                uID = 1,
                uFlags = NifMessage | NifIcon | NifTip,
                uCallbackMessage = WmTrayIcon,
                hIcon = hIcon,
                szTip = "Umnatha Network Monitor"
            };

            Shell_NotifyIcon(NimAdd, ref nid);
            SetWindowSubclass(hwnd, _subclassProc, 1, 0);
        }

        public void Dispose()
        {

            if (!_disposed)
            {
                _disposed = true;
                RemoveWindowSubclass(_hwnd, _subclassProc, 1);
                NOTIFYICONDATA nid = new()
                {
                    cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
                    hWnd = _hwnd,
                    uID = 1
                };
                Shell_NotifyIcon(NimDelete, ref nid);

                if (_ownsIcon && _hIcon != IntPtr.Zero)
                {
                    DestroyIcon(_hIcon);
                }

            }

        }

        private IntPtr SubclassProc(
            IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam,
            nuint uIdSubclass, nuint dwRefData)
        {

            if (uMsg == WmTrayIcon)
            {
                uint mouseEvent = (uint)(lParam.ToInt64() & 0xFFFF);

                if (mouseEvent == WmRButtonUp)
                {
                    ShowContextMenu(hWnd);
                }
                else if (mouseEvent == WmLButtonDblClk)
                {
                    ShowFromTray(hWnd);
                }

            }

            IntPtr result = DefSubclassProc(hWnd, uMsg, wParam, lParam);

            return result;
        }

        private static void ShowFromTray(IntPtr hWnd)
        {

            if (IsIconic(hWnd))
            {
                ShowWindow(hWnd, SwRestore);
            }
            else
            {
                ShowWindow(hWnd, SwShow);
            }

            SetForegroundWindow(hWnd);
        }

        private void ShowContextMenu(IntPtr hWnd)
        {
            GetCursorPos(out POINT pt);
            IntPtr hMenu = CreatePopupMenu();
            uint miniGraphFlags = _isMiniGraphVisible() ? MfString | MfChecked : MfString;

            AppendMenu(hMenu, miniGraphFlags, MenuMiniGraph, "Mini graph");
            AppendMenu(hMenu, MfString, MenuShow, "Show Umnatha Network Monitor");
            AppendMenu(hMenu, MfString, MenuExit, "Exit");
            SetForegroundWindow(hWnd);
            uint cmd = TrackPopupMenu(
                hMenu, TpmReturnCmd | TpmRightButton, pt.x, pt.y, 0, hWnd, IntPtr.Zero);
            DestroyMenu(hMenu);

            if (cmd == MenuMiniGraph)
            {
                _onToggleMiniGraph();
            }
            else if (cmd == MenuShow)
            {
                ShowFromTray(hWnd);
            }
            else if (cmd == MenuExit)
            {
                _onExit();
            }

        }
    }
}
