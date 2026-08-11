using System.Runtime.InteropServices;
using System.Text;

namespace NetworkMonitor.Services.Platform
{
    // The taskbar is a topmost window in its own right, so it shares the topmost band with the mini
    // graph strip and the last window raised into that band wins. Activating the taskbar raises it
    // above the strip and nothing ever puts the strip back, which left a strip parked on the taskbar
    // buried until the widget was toggled off and on again. Re-asserting HWND_TOPMOST on the
    // foreground change puts it back at the top of the band without stealing activation.
    public sealed class TaskbarTopmostGuard : IDisposable
    {
        private const uint EventSystemForeground = 0x0003;
        private const uint WinEventOutOfContext = 0x0000;
        private const uint WinEventSkipOwnProcess = 0x0002;
        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoMove = 0x0002;
        private const uint SwpNoActivate = 0x0010;
        private static readonly IntPtr HwndTopmost = new IntPtr(-1);

        private readonly IntPtr _windowHandle;
        private readonly WinEventDelegate _callback;
        private IntPtr _hook;
        private bool _disposed;

        public TaskbarTopmostGuard(IntPtr windowHandle)
        {
            _windowHandle = windowHandle;

            // The delegate is held in a field on purpose: the hook keeps only an unmanaged pointer to
            // it, so letting it be collected would tear the callback down at an arbitrary later point.
            _callback = OnForegroundChanged;

            _hook = SetWinEventHook(
                EventSystemForeground,
                EventSystemForeground,
                IntPtr.Zero,
                _callback,
                0,
                0,
                WinEventOutOfContext | WinEventSkipOwnProcess);

            // A failed hook leaves the guard doing nothing forever, and the symptom — the strip
            // buried under the taskbar — is exactly the bug this class exists to prevent. Silence
            // here is indistinguishable from that bug.
            if (_hook == IntPtr.Zero)
            {
                AppLog.Info("TaskbarTopmostGuard: SetWinEventHook returned a null handle; the widget will not be restored above the taskbar.");
            }

        }

        private delegate void WinEventDelegate(
            IntPtr hook,
            uint eventType,
            IntPtr windowHandle,
            int objectId,
            int childId,
            uint eventThread,
            uint eventTime);

        public void Dispose()
        {

            if (!_disposed)
            {
                _disposed = true;

                if (_hook != IntPtr.Zero)
                {
                    UnhookWinEvent(_hook);

                    _hook = IntPtr.Zero;
                }

            }

        }

        [DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(
            uint eventMin,
            uint eventMax,
            IntPtr module,
            WinEventDelegate callback,
            uint processId,
            uint threadId,
            uint flags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWinEvent(IntPtr hook);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(
            IntPtr windowHandle,
            IntPtr insertAfter,
            int positionX,
            int positionY,
            int width,
            int height,
            uint flags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindow(IntPtr windowHandle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr windowHandle);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr windowHandle, StringBuilder className, int capacity);

        // Only the taskbar matters here. An ordinary window cannot cover a topmost strip however it is
        // activated, so re-raising on every foreground change would be churn that buys nothing — and it
        // would drag the strip over the Start menu and full-screen apps as well.
        private static bool IsTaskbar(IntPtr windowHandle)
        {
            StringBuilder className = new StringBuilder(256);
            bool isTaskbar = false;

            if (GetClassName(windowHandle, className, className.Capacity) > 0)
            {
                string name = className.ToString();

                isTaskbar = name == "Shell_TrayWnd" || name == "Shell_SecondaryTrayWnd";
            }

            return isTaskbar;
        }

        private void OnForegroundChanged(
            IntPtr hook,
            uint eventType,
            IntPtr windowHandle,
            int objectId,
            int childId,
            uint eventThread,
            uint eventTime)
        {

            if (!_disposed && windowHandle != IntPtr.Zero && IsTaskbar(windowHandle) && IsWindow(_windowHandle) && IsWindowVisible(_windowHandle))
            {
                SetWindowPos(_windowHandle, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
            }

        }
    }
}
