using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Win32;
using NetworkMonitor.Services.Data;
using NetworkMonitor.Services.Scanning;
using NetworkMonitor.Services.Traffic;
using NetworkMonitor.Services.Digest;
using NetworkMonitor.Services.Backup;
using NetworkMonitor.Services.Platform;
using NetworkMonitor.Services.SpeedTest;
using NetworkMonitor.Services.Update;
using NetworkMonitor.ViewModels;
using NetworkMonitor.Models.Formatting;
using NetworkMonitor.Core.Data;
using NetworkMonitor.Core.Traffic;
using NetworkMonitor.Core.Charting;
using NetworkMonitor.Services.Charting;
using NetworkMonitor.Charting;

namespace NetworkMonitor
{
    public partial class App : Application
    {
        internal const string Aumid = "NetworkMonitor.App";
        private const string MutexName = "NetworkMonitor.App.SingleInstance.Mutex";
        private const string ActivationEventName = "NetworkMonitor.App.SingleInstance.Event";
        private const int SwRestore = 9;
        private const int SwHide = 0;
        private const int SwShow = 5;

        private static Mutex? _instanceMutex;
        private static EventWaitHandle? _activationEvent;
        private static IntPtr _mainWindowHwnd;
        private static MiniGraphWindow? _miniGraphWindow;
        private static bool? _miniGraphVisible;
        private static Exception? _settingsLoadFailure;

        [DllImport("shell32.dll", SetLastError = true)]
        private static extern int SetCurrentProcessExplicitAppUserModelID(
            [MarshalAs(UnmanagedType.LPWStr)] string appId);

        public App()
        {

            if (!IsElevated())
            {
                RelaunchElevated();
                Environment.Exit(0);
            }
            else
            {
                _instanceMutex = new Mutex(true, MutexName, out bool createdNew);

                if (!createdNew)
                {

                    try
                    {
                        EventWaitHandle existing = EventWaitHandle.OpenExisting(ActivationEventName);
                        existing.Set();
                        existing.Dispose();
                    }
                    catch (WaitHandleCannotBeOpenedException)
                    {
                    }

                    Environment.Exit(0);
                }

                _activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivationEventName);
                Task.Run(ListenForActivation);

                InitializeComponent();
                UnhandledException += OnUnhandledException;

                QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

                // Not Host.CreateDefaultBuilder: it registers console, debug and event-source logging
                // providers this app never writes to — it logs through AppLog — and watches
                // appsettings.json and the environment for changes with two FileSystemWatchers held
                // for the life of the process, for a file read exactly once below. HostBuilder still
                // brings the options, logging and lifetime plumbing the hosted services need.
                //
                // The base path is the executable's folder rather than the working directory, which
                // CreateDefaultBuilder used. Launched from the Startup task or a shortcut, the working
                // directory is not the install folder, so the optional appsettings.json quietly failed
                // to load and first-run defaults came from `new Settings()` instead of the file.
                AppHost = new HostBuilder()
                    .ConfigureAppConfiguration(config =>
                    {
                        config.SetBasePath(AppContext.BaseDirectory);
                        config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
                    })
                    .ConfigureServices((ctx, services) =>
                    {
                        Settings? scannerSettings = null;

                        if (File.Exists(Settings.SettingsFilePath))
                        {

                            try
                            {
                                string json = File.ReadAllText(Settings.SettingsFilePath);
                                scannerSettings = JsonSerializer.Deserialize<Settings>(json);
                            }
                            catch (Exception exception)
                            {
                                _settingsLoadFailure = exception;
                            }

                        }

                        if (scannerSettings is null)
                        {
                            scannerSettings = ctx.Configuration
                                .GetSection("Scanner")
                                .Get<Settings>() ?? new Settings();
                            scannerSettings.SubnetBase = Settings.DetectSubnetBase();
                        }

                        services.AddSingleton(scannerSettings);
                        services.AddSingleton<MiniGraphState>();
                        services.AddSingleton<ChartPaletteService>();
                        services.AddSingleton<OuiDatabase>();
                        services.AddSingleton<MdnsProbe>();
                        services.AddSingleton<WindowsStartupService>();
                        services.AddSingleton<NetworkScanner>();
                        services.AddDbContextFactory<AppDbContext>(opts =>
                            opts.UseSqlite($"Data Source={AppDbContext.DbPath}"));
                        services.AddSingleton<DeviceTracker>();
                        services.AddSingleton<ScanWorker>();
                        services.AddHostedService(sp => sp.GetRequiredService<ScanWorker>());
                        services.AddSingleton<LanClassifier>();
                        services.AddSingleton<TrafficCollector>();
                        services.AddHostedService(sp => sp.GetRequiredService<TrafficCollector>());
                        services.AddSingleton<TrafficTracker>();
                        services.AddHostedService(sp => sp.GetRequiredService<TrafficTracker>());
                        services.AddSingleton<LiveTrafficFeed>();
                        services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<LiveTrafficFeed>());
                        services.AddSingleton<InAppNotificationService>();
                        services.AddSingleton<SpeedTestService>(serviceProvider =>
                        {
                            SocketsHttpHandler handler = new SocketsHttpHandler
                            {
                                MaxConnectionsPerServer = 32,
                                PooledConnectionLifetime = TimeSpan.FromMinutes(2)
                            };

                            HttpClient httpClient = new HttpClient(handler)
                            {
                                Timeout = TimeSpan.FromSeconds(120)
                            };

                            SpeedTestService speedTestService = new SpeedTestService(httpClient);

                            return speedTestService;
                        });
                        services.AddSingleton<SpeedTestWorker>();
                        services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<SpeedTestWorker>());
                        services.AddSingleton<IInstallerLauncher, InstallerLauncher>();
                        services.AddSingleton<IUpdateService>(serviceProvider =>
                        {
                            HttpClient updateHttpClient = new HttpClient
                            {
                                Timeout = TimeSpan.FromMinutes(10)
                            };

                            updateHttpClient.DefaultRequestHeaders.Add("User-Agent", "UmnathaNetworkMonitor");

                            IInstallerLauncher installerLauncher = serviceProvider.GetRequiredService<IInstallerLauncher>();
                            UpdateService updateService = new UpdateService(updateHttpClient, installerLauncher);

                            return updateService;
                        });
                        services.AddSingleton<UpdateCheckWorker>();
                        services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<UpdateCheckWorker>());
                        services.AddSingleton<UpdateViewModel>();
                        services.AddSingleton<DigestGenerator>();
                        services.AddSingleton<DigestChartRenderer>();
                        services.AddSingleton<DigestPdfExporter>();
                        services.AddSingleton<DigestWorker>();
                        services.AddHostedService(sp => sp.GetRequiredService<DigestWorker>());
                        services.AddHostedService<DatabaseBackupWorker>();
                        services.AddTransient<AllDevicesViewModel>();
                        services.AddTransient<UnapprovedDevicesViewModel>();
                        services.AddTransient<SettingsViewModel>();
                        services.AddTransient<ReportsViewModel>();
                        services.AddTransient<DeviceHistoryViewModel>();
                        services.AddSingleton<InternetViewModel>();
                        services.AddSingleton<LocalViewModel>();
                        services.AddSingleton<SpeedTestViewModel>();
                        services.AddSingleton<MiniGraphViewModel>();
                        services.AddTransient<MainWindow>();
                    })
                    .Build();
            }

        }

        public static IHost AppHost
        {
            get;
            private set;
        } = null!;

        protected override async void OnLaunched(LaunchActivatedEventArgs args)
        {
            bool startMinimized = ShouldStartMinimized();
            SplashWindow? splash = null;

            try
            {
#if DEBUG
                bool loggingEnabled = true;
#else
                Settings logSettings = AppHost.Services.GetRequiredService<Settings>();
                bool loggingEnabled = logSettings.EnableLogging;
#endif

                AppLog.Initialize(loggingEnabled);
                AppLog.Info($"Application started (v{AppInfo.GetVersion()}, minimized={startMinimized}).");

                if (_settingsLoadFailure is not null)
                {
                    AppLog.Error($"App.LoadSettings ({Settings.SettingsFilePath} unreadable — started from defaults)", _settingsLoadFailure);
                    _settingsLoadFailure = null;
                }

                Settings appSettings = AppHost.Services.GetRequiredService<Settings>();
                TrafficRateFormatter.Mode = appSettings.RateUnitMode;

                if (!startMinimized)
                {
                    splash = new SplashWindow();
                    splash.Activate();
                }

                Directory.CreateDirectory(Path.GetDirectoryName(AppDbContext.DbPath)!);

                await Task.Run(async () =>
                {
                    await using AppDbContext db = await AppHost.Services
                        .GetRequiredService<IDbContextFactory<AppDbContext>>()
                        .CreateDbContextAsync();

                    await DatabaseInitializer.InitializeAsync(db);

                });

                OuiDatabase oui = AppHost.Services.GetRequiredService<OuiDatabase>();
                string ouiPath = Path.Combine(AppContext.BaseDirectory, "Assets", "oui.txt");
                await Task.Run(() => oui.Load(ouiPath));

                SetCurrentProcessExplicitAppUserModelID(Aumid);

                using (RegistryKey key = Registry.CurrentUser
                           .CreateSubKey($@"SOFTWARE\Classes\AppUserModelId\{Aumid}"))
                {
                    key.SetValue("DisplayName", "Umnatha Network Monitor");
                    key.SetValue("IconUri", Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico"));
                }

                await AppHost.StartAsync();

                MainWindow window = AppHost.Services.GetRequiredService<MainWindow>();

                ChartPaletteService chartPalette = AppHost.Services.GetRequiredService<ChartPaletteService>();

                if (window.Content is FrameworkElement paletteRoot)
                {
                    ChartSurface startingSurface = paletteRoot.ActualTheme == ElementTheme.Light
                        ? ChartSurface.Light
                        : ChartSurface.Dark;
                    chartPalette.SetSurface(startingSurface);

                    paletteRoot.ActualThemeChanged += (FrameworkElement sender, object args) =>
                    {
                        ChartSurface surface = sender.ActualTheme == ElementTheme.Light
                            ? ChartSurface.Light
                            : ChartSurface.Dark;
                        chartPalette.SetSurface(surface);
                    };

                }

                ChartBrushes.Attach(chartPalette);
                _mainWindowHwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);

                if (splash is not null)
                {
                    SplashWindow activeSplash = splash;
                    bool splashClosed = false;

                    void CloseSplashOnce()
                    {

                        if (!splashClosed)
                        {
                            splashClosed = true;
                            activeSplash.Close();
                        }

                    }

                    if (window.Content is FrameworkElement root)
                    {
                        root.Loaded += (sender, eventArgs) =>
                        {
                            window.DispatcherQueue.TryEnqueue(
                                Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                                CloseSplashOnce);
                        };
                    }

                    Microsoft.UI.Dispatching.DispatcherQueueTimer fallbackTimer = window.DispatcherQueue.CreateTimer();
                    fallbackTimer.Interval = TimeSpan.FromSeconds(5);
                    fallbackTimer.IsRepeating = false;

                    fallbackTimer.Tick += (timerSender, timerArgs) =>
                    {
                        CloseSplashOnce();
                    };

                    fallbackTimer.Start();
                }

                window.Activate();
                window.RestoreWindowPlacement();

                MiniGraphState miniGraphState = AppHost.Services.GetRequiredService<MiniGraphState>();
                DispatcherQueue mainDispatcher = window.DispatcherQueue;

                // Every other Changed handler — MiniGraphWindow, TrafficHostPage, SettingsPage —
                // marshals defensively, so the class's own convention is that Changed may be raised
                // from anywhere. This was the one handler that did not, and it is the one that
                // constructs a Window and calls ShowWidget: strictly UI-thread-only, and the one that
                // would fail hardest. Every current caller happens to be on the UI thread; a future
                // background writer (a hosted service auto-hiding the widget, a hotkey listener)
                // would have broken it silently.
                miniGraphState.Changed += (stateSender, stateArgs) => mainDispatcher.TryEnqueue(ApplyMiniGraphVisibility);
                ApplyMiniGraphVisibility();

                if (startMinimized)
                {
                    ShowWindow(_mainWindowHwnd, SwHide);
                }

            }
            catch (Exception exception)
            {
                AppLog.Error("App.OnLaunched", exception);
                splash?.Close();
                ShowFatalError(exception.Message);
            }

        }

        internal static void ApplyMiniGraphVisibility()
        {

            try
            {
                MiniGraphState state = AppHost.Services.GetRequiredService<MiniGraphState>();
                bool visible = state.IsVisible;

                // MiniGraphState raises one Changed event for all six of its setters, so most calls
                // land here for a section or opacity edit. Showing on those would re-activate the
                // widget and steal focus — the opacity slider alone would do it ten times per drag.
                if (_miniGraphVisible != visible)
                {

                    if (visible)
                    {

                        if (_miniGraphWindow is null)
                        {
                            _miniGraphWindow = new MiniGraphWindow(
                                AppHost.Services.GetRequiredService<MiniGraphViewModel>(),
                                state,
                                AppHost.Services.GetRequiredService<Settings>());
                        }

                        _miniGraphWindow.ShowWidget();
                    }
                    else
                    {
                        _miniGraphWindow?.HideWidget();
                    }

                    // Assigned only once the show or hide has actually succeeded. Set before
                    // construction, a MiniGraphWindow that threw left the flag reading "visible"
                    // with no window behind it, so every later toggle compared equal and no-opped —
                    // the widget was unavailable for the rest of the session.
                    _miniGraphVisible = visible;
                }

            }
            catch (Exception exception)
            {
                // GetRequiredService is inside the try on purpose: after AppHost.Dispose it throws
                // ObjectDisposedException, and while CloseMiniGraph currently runs before StopHost,
                // the guard costs nothing and does not depend on that ordering holding.
                AppLog.Error("App.ApplyMiniGraphVisibility", exception);
            }

        }

        internal static void CloseMiniGraph()
        {

            try
            {
                _miniGraphWindow?.CloseWidget();
            }
            catch (Exception exception)
            {
                // Shutdown must not stall on the widget. Before this, a fault here left the main
                // window closing with the host still running and the tray icon still in place.
                AppLog.Error("App.CloseMiniGraph", exception);
            }

            _miniGraphWindow = null;
            _miniGraphVisible = null;
        }

        // The widget can be destroyed by Alt+F4 without anyone asking, so it tells the app to drop the
        // dead reference rather than leaving the next show call to fail against it.
        internal static void ForgetMiniGraph()
        {
            _miniGraphWindow = null;
            _miniGraphVisible = false;
        }

        // Closing to the tray hides the window with SW_HIDE, which leaves its maximized state intact —
        // a hidden window is still a maximized one. SW_RESTORE un-maximizes by definition, so showing it
        // again that way brought a maximized window back at its pre-maximized size. SW_SHOW displays the
        // window in whatever state it already holds. SW_RESTORE stays correct for a genuinely minimized
        // window, where it returns to the state held before minimising, maximized included.
        internal static void ShowMainWindow()
        {

            if (_mainWindowHwnd != IntPtr.Zero)
            {
                int command = SwShow;

                if (IsIconic(_mainWindowHwnd))
                {
                    command = SwRestore;
                }

                ShowWindow(_mainWindowHwnd, command);
                SetForegroundWindow(_mainWindowHwnd);
            }

        }

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

        private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs eventArgs)
        {
            eventArgs.Handled = true;

            AppLog.Error("App.UnhandledException", eventArgs.Exception);
            ShowFatalError(eventArgs.Message);
        }

        private static void ShowFatalError(string message)
        {
            const uint mbIconError = 0x10;

            MessageBox(IntPtr.Zero, message, "Umnatha Network Monitor — a problem occurred", mbIconError);
        }

        private static bool IsElevated()
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            WindowsPrincipal principal = new WindowsPrincipal(identity);
            bool isAdmin = principal.IsInRole(WindowsBuiltInRole.Administrator);

            return isAdmin;
        }

        private static void RelaunchElevated()
        {
            string? exePath = Environment.ProcessPath;

            if (exePath is not null)
            {
                string[] commandLineArgs = Environment.GetCommandLineArgs();
                string forwardedArguments = commandLineArgs.Length > 1
                    ? string.Join(" ", commandLineArgs.Skip(1).Select(QuoteArgument))
                    : string.Empty;

                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = forwardedArguments,
                    UseShellExecute = true,
                    Verb = "runas"
                };

                try
                {
                    Process.Start(startInfo);
                }
                catch (Win32Exception)
                {
                }

            }

        }

        private static string QuoteArgument(string argument)
        {
            string quoted = argument.Contains(' ') ? $"\"{argument}\"" : argument;

            return quoted;
        }

        private static bool ShouldStartMinimized()
        {
            string[] commandLineArgs = Environment.GetCommandLineArgs();
            bool startMinimized = false;

            foreach (string argument in commandLineArgs)
            {

                if (string.Equals(argument, "--minimized", StringComparison.OrdinalIgnoreCase))
                {
                    startMinimized = true;

                    break;
                }

            }

            return startMinimized;
        }

        private static void ListenForActivation()
        {
            bool active = true;

            while (active && _activationEvent is not null)
            {

                try
                {
                    _activationEvent.WaitOne();

                    // Launching a second copy activates the running one, and it lost the maximized
                    // state for the same reason the mini graph's double-click did.
                    ShowMainWindow();
                }
                catch (ObjectDisposedException)
                {
                    active = false;
                }

            }

        }

    }
}
