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
using Microsoft.UI.Xaml;
using Microsoft.Win32;
using NetworkMonitor.Services.Data;
using NetworkMonitor.Services.Scanning;
using NetworkMonitor.Services.Traffic;
using NetworkMonitor.Services.Digest;
using NetworkMonitor.Services.Backup;
using NetworkMonitor.Services.Platform;
using NetworkMonitor.Services.SpeedTest;
using NetworkMonitor.ViewModels;
using NetworkMonitor.Models.Formatting;
using NetworkMonitor.Core.Data;
using NetworkMonitor.Core.Traffic;

namespace NetworkMonitor
{
    public partial class App : Application
    {
        internal const string Aumid = "NetworkMonitor.App";
        private const string MutexName = "NetworkMonitor.App.SingleInstance.Mutex";
        private const string ActivationEventName = "NetworkMonitor.App.SingleInstance.Event";
        private const int SwRestore = 9;
        private const int SwHide = 0;

        private static Mutex? _instanceMutex;
        private static EventWaitHandle? _activationEvent;
        private static IntPtr _mainWindowHwnd;

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

                AppHost = Host.CreateDefaultBuilder()
                    .ConfigureServices((ctx, services) =>
                    {
                        Settings scannerSettings;

                        if (File.Exists(Settings.SettingsFilePath))
                        {
                            string json = File.ReadAllText(Settings.SettingsFilePath);
                            scannerSettings = JsonSerializer.Deserialize<Settings>(json) ?? new Settings();
                        }
                        else
                        {
                            scannerSettings = ctx.Configuration
                                .GetSection("Scanner")
                                .Get<Settings>() ?? new Settings();
                            scannerSettings.SubnetBase = Settings.DetectSubnetBase();
                        }

                        services.AddSingleton(scannerSettings);
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
                AppLog.Info($"Application started (version {AppInfo.GetVersion()}, minimized={startMinimized}).");

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

                    await db.Database.EnsureCreatedAsync();
                    await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");

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

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

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

                    if (_mainWindowHwnd != IntPtr.Zero)
                    {
                        ShowWindow(_mainWindowHwnd, SwRestore);
                        SetForegroundWindow(_mainWindowHwnd);
                    }
                }
                catch (ObjectDisposedException)
                {
                    active = false;
                }

            }

        }

    }
}
