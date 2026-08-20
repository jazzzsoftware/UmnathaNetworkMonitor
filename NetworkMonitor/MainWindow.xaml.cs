using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using NetworkMonitor.Services.Data;
using NetworkMonitor.Models.Devices;
using NetworkMonitor.Notifications;
using NetworkMonitor.Models.Digest;
using NetworkMonitor.Services.Scanning;
using NetworkMonitor.Services.Digest;
using NetworkMonitor.Services.SpeedTest;
using NetworkMonitor.Services.Platform;
using NetworkMonitor.ViewModels;
using NetworkMonitor.Views;
using System.Runtime.InteropServices;
using Windows.Graphics;
using NetworkMonitor.Core.SpeedTest;

namespace NetworkMonitor
{
    public sealed partial class MainWindow : Window
    {
        private readonly DispatcherQueue _dispatcherQueue;
        private readonly Settings _settings;
        private readonly InAppNotificationService _notificationService;
        private readonly SpeedTestWorker _speedTestWorker;
        private readonly DispatcherTimer _toastTimer;
        private readonly DispatcherTimer _savePlacementTimer;
        private readonly ToastPresenter _toasts;
        private readonly TrayIconService _trayIcon;
        private readonly MiniGraphState _miniGraphState;
        private readonly IntPtr _hwnd;
        private bool _exitRequested;
        private bool _placementRestored;
        private bool _shutdownCompleted;
        private const int SwShowNormal = 1;
        private const int SwShowMinimized = 2;
        private const int SwShowMaximized = 3;

        public MainWindow(ScanWorker scanWorker, Settings settings, InAppNotificationService notificationService, SpeedTestWorker speedTestWorker, UpdateViewModel updateViewModel, MiniGraphState miniGraphState)
        {
            Current = this;
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            _toasts = new ToastPresenter(App.Aumid, _dispatcherQueue);
            _settings = settings;
            _notificationService = notificationService;
            _speedTestWorker = speedTestWorker;
            UpdateViewModel = updateViewModel;
            _miniGraphState = miniGraphState;
            InitializeComponent();

            _toastTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            _toastTimer.Tick += OnToastTimerTick;

            _savePlacementTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(400)
            };
            _savePlacementTimer.Tick += OnSavePlacementTimerTick;

            ToastBorder.OpacityTransition = new ScalarTransition
            {
                Duration = TimeSpan.FromMilliseconds(250)
            };
            _notificationService.NotificationRequested += OnNotificationRequested;

            ExtendsContentIntoTitleBar = true;
            AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico"));
            AppWindow.Changed += OnAppWindowChanged;
            scanWorker.ScanCompleted += OnScanCompleted;
            scanWorker.DeviceStatusChanged += OnDeviceStatusChanged;
            scanWorker.NetworkChanged += OnNetworkChanged;
            _speedTestWorker.SpeedTestCompleted += OnSpeedTestCompleted;

            DigestGenerator digestGenerator = App.AppHost.Services.GetRequiredService<DigestGenerator>();
            digestGenerator.ReportGenerated += OnDigestReportGenerated;

            _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            _trayIcon = new TrayIconService(
                _hwnd,
                OnExitApp,
                () => _miniGraphState.IsVisible = !_miniGraphState.IsVisible,
                () => _miniGraphState.IsVisible);
            AppWindow.Closing += OnAppWindowClosing;
        }

        public static new MainWindow? Current
        {
            get;
            private set;
        }

        public UpdateViewModel UpdateViewModel
        {
            get;
        }

        internal void RestoreWindowPlacement()
        {

            if (_settings.WindowWidth > 0 && _settings.WindowHeight > 0)
            {
                RectInt32 workArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;

                int restoredWidth = Math.Min(_settings.WindowWidth, workArea.Width);
                int restoredHeight = Math.Min(_settings.WindowHeight, workArea.Height);

                WINDOWPLACEMENT placement = new()
                {
                    Length = Marshal.SizeOf<WINDOWPLACEMENT>(),
                    Flags = 0,
                    ShowCmd = SwShowNormal,
                    MinPosition = new POINT { X = -1, Y = -1 },
                    MaxPosition = new POINT { X = -1, Y = -1 },
                    NormalPosition = new RECT
                    {
                        Left = _settings.WindowX,
                        Top = _settings.WindowY,
                        Right = _settings.WindowX + restoredWidth,
                        Bottom = _settings.WindowY + restoredHeight
                    }
                };

                SetWindowPlacement(_hwnd, ref placement);
            }
            else
            {
                ApplyDefaultWindowSize();
            }

            if (_settings.WindowMaximized && AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.Maximize();
            }

            _placementRestored = true;
        }

        public void NavigateToHistory(string mac)
        {
            SelectNavItem("devices");

            if (ContentFrame.Content is not DevicesHostPage)
            {
                ContentFrame.Navigate(typeof(DevicesHostPage));
            }

            DevicesHostPage? host = ContentFrame.Content as DevicesHostPage;
            host?.ShowDeviceHistory(mac);
        }

        public void NavigateToTraffic(string tabTag)
        {
            SelectNavItem("traffic");

            if (ContentFrame.Content is not TrafficHostPage)
            {
                ContentFrame.Navigate(typeof(TrafficHostPage));
            }

            TrafficHostPage? host = ContentFrame.Content as TrafficHostPage;
            host?.SelectTab(tabTag);
        }

        public void NavigateToUnapprovedDevices()
        {
            NavigateToDevices("Unapproved");
        }

        public void NavigateToDevices(string tabTag)
        {
            SelectNavItem("devices");

            if (ContentFrame.Content is not DevicesHostPage)
            {
                ContentFrame.Navigate(typeof(DevicesHostPage));
            }

            DevicesHostPage? host = ContentFrame.Content as DevicesHostPage;
            host?.SelectTab(tabTag);
        }

        public void NavigateToReports()
        {
            SelectNavItem("reports");

            if (ContentFrame.Content is not ReportsPage)
            {
                ContentFrame.Navigate(typeof(ReportsPage));
            }

        }

        // With no saved placement WinUI sizes the window itself and picks something close to the
        // whole screen. The opening size comes from configuration (Scanner:DefaultWindowWidth /
        // DefaultWindowHeight in appsettings.json) rather than a fraction of the work area, because
        // a fraction is wrong on wide desktops — 68% of a 5120-wide work area is a 3482px window.
        // The configured value is in DIPs and is scaled by the window's DPI, since physical pixels
        // are what WorkArea and MoveAndResize both speak, then clamped so a small display still
        // gets a window that fits on it.
        private void ApplyDefaultWindowSize()
        {
            RectInt32 workArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;

            uint dpi = GetDpiForWindow(_hwnd);
            double scale = dpi > 0 ? dpi / 96.0 : 1.0;

            int width = (int)Math.Round(_settings.DefaultWindowWidth * scale);
            int height = (int)Math.Round(_settings.DefaultWindowHeight * scale);

            width = Math.Min(width, workArea.Width);
            height = Math.Min(height, workArea.Height);

            int left = workArea.X + ((workArea.Width - width) / 2);
            int top = workArea.Y + ((workArea.Height - height) / 2);

            AppWindow.MoveAndResize(new RectInt32(left, top, width, height));
        }

        private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
        {

            if (_placementRestored && (args.DidPositionChange || args.DidSizeChange))
            {
                _savePlacementTimer.Stop();
                _savePlacementTimer.Start();
            }

        }

        private void OnSavePlacementTimerTick(object? sender, object args)
        {
            _savePlacementTimer.Stop();
            SaveWindowPlacement();
        }

        private void SaveWindowPlacement()
        {
            WINDOWPLACEMENT placement = new()
            {
                Length = Marshal.SizeOf<WINDOWPLACEMENT>()
            };

            bool read = GetWindowPlacement(_hwnd, ref placement);
            OverlappedPresenter? presenter = AppWindow.Presenter as OverlappedPresenter;
            OverlappedPresenterState state = presenter?.State ?? OverlappedPresenterState.Restored;
            bool zoomed = IsZoomed(_hwnd);

            if (read && state != OverlappedPresenterState.Minimized)
            {
                bool isMaximized = state == OverlappedPresenterState.Maximized || zoomed;
                _settings.WindowX = placement.NormalPosition.Left;
                _settings.WindowY = placement.NormalPosition.Top;
                _settings.WindowWidth = placement.NormalPosition.Right - placement.NormalPosition.Left;
                _settings.WindowHeight = placement.NormalPosition.Bottom - placement.NormalPosition.Top;
                _settings.WindowMaximized = isMaximized;
                _settings.Save();
            }

        }

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT placement);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT placement);

        [DllImport("user32.dll")]
        private static extern bool IsZoomed(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hWnd);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WINDOWPLACEMENT
        {
            public int Length;
            public int Flags;
            public int ShowCmd;
            public POINT MinPosition;
            public POINT MaxPosition;
            public RECT NormalPosition;
        }

        internal void ShutdownForUpdate()
        {

            if (!_shutdownCompleted)
            {
                AppLog.Info("Shutting down to install an update.");
                ShutdownGracefully();
            }

        }

        private void ShutdownGracefully()
        {
            _shutdownCompleted = true;
            _savePlacementTimer.Stop();
            SaveWindowPlacement();
            App.CloseMiniGraph();
            StopHost();

            // After StopHost, so the background services' final writes are inside the checkpoint.
            DatabaseCheckpoint.Truncate();

            _trayIcon.Dispose();
        }

        private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
        {

            if (_exitRequested)
            {

                if (!_shutdownCompleted)
                {
                    ShutdownGracefully();
                }

                // The widget is a second top-level window and its close is queued, not immediate, so
                // closing this one is not enough to end the process: Exit from the tray left the mini
                // graph on screen driven by a host that had already been stopped. Everything that has
                // to survive — placement, settings, the WAL checkpoint — is written above.
                Environment.Exit(0);
            }
            else
            {
                args.Cancel = true;
                IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                ShowWindow(hwnd, 0 /* SW_HIDE */);
            }

        }

        private static void StopHost()
        {

            try
            {
                App.AppHost.StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
                App.AppHost.Dispose();
            }
            catch (Exception exception)
            {
                AppLog.Error("MainWindow.StopHost", exception);
            }

        }

        // Exit from the tray used to close this window and hope the process followed. It did not: the
        // widget is a second top-level window, its Close is queued rather than immediate, and whatever
        // it was that kept it alive left it on screen being fed by a host that had already stopped.
        // Shutting down here and ending the process outright removes the guesswork — nothing in the
        // exit path now depends on another window's close message being processed.
        private void OnExitApp()
        {
            AppLog.Info("Application stopping.");

            _exitRequested = true;
            UpdateViewModel.CancelPendingWork();

            if (!_shutdownCompleted)
            {
                ShutdownGracefully();
            }

            Environment.Exit(0);
        }

        private void NavViewLoaded(object sender, RoutedEventArgs args)
        {
            NavView.SelectedItem = NavView.MenuItems[0];

            if (ContentFrame.Content is null)
            {
                ContentFrame.Navigate(typeof(TrafficHostPage));
            }

            // The built-in Settings item has no XAML declaration to hang an AutomationId off, so
            // it is set here once the control exists.
            if (NavView.SettingsItem is NavigationViewItem settingsItem)
            {
                AutomationProperties.SetAutomationId(settingsItem, "SettingsNavItem");
            }

        }

        private void NavViewSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            Type? pageType = null;

            if (args.IsSettingsSelected)
            {
                pageType = typeof(SettingsPage);
            }
            else if (args.SelectedItem is NavigationViewItem item)
            {
                pageType = item.Tag?.ToString() switch
                {
                    "traffic" => typeof(TrafficHostPage),
                    "devices" => typeof(DevicesHostPage),
                    "reports" => typeof(ReportsPage),
                    _ => null
                };
            }

            if (pageType is not null)
            {
                ContentFrame.Navigate(pageType);
            }

        }

        private void OnScanCompleted(object? sender, ScanCompletedEventArgs args)
        {
            string message = $"Scan complete: {args.Session.DevicesFound} found, {args.Session.NewDevices} new, {args.Session.DevicesGone} gone";

            _dispatcherQueue.TryEnqueue(() =>
            {
                DateTime now = DateTime.Now;
                LastScanText.Text = now.ToString("dd MMM yyyy  HH:mm");
                NextScanText.Text = now.AddMinutes(_settings.IntervalMinutes).ToString("dd MMM yyyy  HH:mm");

                if (args.IsManual)
                {
                    _notificationService.Show(message);

                    if (_settings.ShowToasts)
                    {
                        _toasts.Show("Scan complete", message, null, TimeSpan.FromMinutes(10), () => OpenDevices("Devices"));
                    }

                }

            });
        }

        private void OnDeviceStatusChanged(object? sender, DeviceStatusChangedEventArgs args)
        {
            ShowNotification(args.Notification);
        }

        private void OnNetworkChanged(object? sender, NetworkChangedEventArgs args)
        {
            string message = $"Network changed: now scanning {args.NewSubnetBase}.x (was {args.OldSubnetBase}.x)";

            _dispatcherQueue.TryEnqueue(() =>
            {
                _notificationService.Show(message);

                if (_settings.ShowToasts)
                {
                    _toasts.Show("Network changed", message, null, TimeSpan.FromMinutes(10), () => OpenDevices("Devices"));
                }

            });
        }

        private void OnNotificationRequested(string message)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                ToastText.Text = message;
                ToastBorder.Opacity = 1.0;
                _toastTimer.Stop();
                _toastTimer.Start();
            });
        }

        private void OnToastTimerTick(object? sender, object args)
        {
            _toastTimer.Stop();
            ToastBorder.Opacity = 0.0;
        }

        private void OnDigestReportGenerated(object? sender, DigestReport report)
        {

            if (_settings.DigestNotify)
            {
                _dispatcherQueue.TryEnqueue(() =>
                {
                    _toasts.Show("Daily digest ready", report.PeriodEndDisplay, null, TimeSpan.FromMinutes(10), OpenReports);
                });
            }

        }

        private void OnSpeedTestCompleted(object? sender, SpeedTestCompletedEventArgs args)
        {
            string message = SpeedTestMessage.Format(args.Result);

            _dispatcherQueue.TryEnqueue(() =>
            {
                _notificationService.Show(message);

                if (_settings.ShowToasts)
                {
                    _toasts.Show("Speed test complete", message, null, TimeSpan.FromMinutes(10), () => OpenTraffic("SpeedTest"));
                }

            });

        }

        private void ShowNotification(DeviceNotification notification)
        {

            if (_settings.ShowToasts)
            {
                bool isUnapproved = notification.IsNew || !notification.IsApproved;

                if (!_settings.UnapprovedOnlyToasts || (isUnapproved && notification.Appeared))
                {
                    string title = notification.Appeared
                        ? isUnapproved ? "🔴 Unrecognized device joined" : "Device Joined"
                        : "Device Left";

                    string typeIcon = notification.Type switch
                    {
                        DeviceType.Router => "🌐",
                        DeviceType.Switch => "🔀",
                        DeviceType.WiFi => "📶",
                        DeviceType.PC => "💻",
                        DeviceType.Server => "🖥️",
                        DeviceType.Mobile => "📱",
                        DeviceType.Camera => "📷",
                        DeviceType.SmartDevice => "💡",
                        DeviceType.Energy => "⚡",
                        _ => "❓"
                    };

                    string nameLine = $"{typeIcon} {notification.DisplayName}";

                    string addressLine = string.IsNullOrWhiteSpace(notification.Vendor)
                        ? $"{notification.IpAddress}  ·  {notification.MacAddress}"
                        : $"{notification.IpAddress}  ·  {notification.MacAddress}  ·  {notification.Vendor}";

                    // An unrecognised device is a thing to act on, so the click lands where the
                    // approve and delete buttons are. A device already known needs explaining
                    // rather than acting on, and its history is where the answer is.
                    string macAddress = notification.MacAddress;
                    Action onClick = isUnapproved
                        ? () => OpenDevices("Unapproved")
                        : () => OpenDeviceHistory(macAddress);

                    _toasts.Show(title, nameLine, addressLine, TimeSpan.FromSeconds(5), onClick);
                }

            }

        }

        private void SelectNavItem(string tag)
        {

            foreach (object item in NavView.MenuItems)
            {

                if (item is NavigationViewItem navigationItem && navigationItem.Tag?.ToString() == tag)
                {
                    NavView.SelectedItem = navigationItem;

                    break;
                }

            }

        }

        // A toast is usually clicked while the window is minimised or hidden to the tray, so every
        // one of these brings it back the way the widget's double-click does before navigating.
        private void OpenDevices(string tabTag)
        {
            App.ShowMainWindow();
            NavigateToDevices(tabTag);
        }

        private void OpenDeviceHistory(string macAddress)
        {
            App.ShowMainWindow();
            NavigateToHistory(macAddress);
        }

        private void OpenTraffic(string tabTag)
        {
            App.ShowMainWindow();
            NavigateToTraffic(tabTag);
        }

        private void OpenReports()
        {
            App.ShowMainWindow();
            NavigateToReports();
        }
    }

}
