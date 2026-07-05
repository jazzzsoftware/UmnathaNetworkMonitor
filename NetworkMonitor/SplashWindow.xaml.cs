using System.IO;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;

namespace NetworkMonitor
{
    public sealed partial class SplashWindow : Window
    {
        private const double SplashWidth = 640;
        private const double SplashHeight = 320;
        private const int DwmwaWindowCornerPreference = 33;
        private const int DwmwaBorderColor = 34;
        private const uint DwmwcpRound = 2;
        private const uint DwmwaColorNone = 0xFFFFFFFE;
        private const int GwlStyle = -16;
        private const long WsOverlappedWindow = 0x00CF0000;
        private const long WsPopup = 0x80000000;
        private const uint SwpFrameChangedFlags = 0x0027;

        private readonly IntPtr _hwnd;

        public SplashWindow()
        {
            InitializeComponent();
            _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            LogoImage.Source = new BitmapImage(
                new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "splash-logo.png")));
            ConfigureWindow();
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint dwFlags);

        [DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr hMonitor, int dpiType, out uint dpiX, out uint dpiY);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref uint value, int size);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

        private void ConfigureWindow()
        {
            AppWindow.IsShownInSwitchers = false;

            if (AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.SetBorderAndTitleBar(false, false);
                presenter.IsAlwaysOnTop = true;
                presenter.IsResizable = false;
                presenter.IsMaximizable = false;
                presenter.IsMinimizable = false;
            }

            StripWindowFrame();
            RemoveWindowBorder();

            double scale = GetDisplayScale();
            int width = (int)Math.Round(SplashWidth * scale);
            int height = (int)Math.Round(SplashHeight * scale);
            DisplayArea displayArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
            RectInt32 workArea = displayArea.WorkArea;
            int positionX = workArea.X + ((workArea.Width - width) / 2);
            int positionY = workArea.Y + ((workArea.Height - height) / 2);

            AppWindow.MoveAndResize(new RectInt32(positionX, positionY, width, height));
        }

        private double GetDisplayScale()
        {
            IntPtr monitor = MonitorFromWindow(_hwnd, 2 /* MONITOR_DEFAULTTONEAREST */);
            double scale = 1.0;

            if (monitor != IntPtr.Zero && GetDpiForMonitor(monitor, 0 /* MDT_EFFECTIVE_DPI */, out uint dpiX, out _) == 0 && dpiX != 0)
            {
                scale = dpiX / 96.0;
            }

            return scale;
        }

        private void RemoveWindowBorder()
        {
            uint cornerPreference = DwmwcpRound;
            uint borderColor = DwmwaColorNone;

            DwmSetWindowAttribute(_hwnd, DwmwaWindowCornerPreference, ref cornerPreference, sizeof(uint));
            DwmSetWindowAttribute(_hwnd, DwmwaBorderColor, ref borderColor, sizeof(uint));
        }

        private void StripWindowFrame()
        {
            long style = GetWindowLongPtr(_hwnd, GwlStyle).ToInt64();
            style &= ~WsOverlappedWindow;
            style |= WsPopup;

            SetWindowLongPtr(_hwnd, GwlStyle, new IntPtr(style));
            SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0, SwpFrameChangedFlags);
        }
    }
}
