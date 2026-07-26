using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Dispatching;
using NetworkMonitor.Services.Data;
using NetworkMonitor.Models.Formatting;
using NetworkMonitor.Services.Platform;

namespace NetworkMonitor.ViewModels
{
    public partial class SettingsViewModel : ObservableObject
    {
        private readonly Settings _settings;
        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly WindowsStartupService _startupService;
        private readonly InAppNotificationService _notificationService;
        private readonly DispatcherQueue _dispatcherQueue;

        public SettingsViewModel(Settings settings, IDbContextFactory<AppDbContext> dbFactory, WindowsStartupService startupService, InAppNotificationService notificationService)
        {
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            _settings = settings;
            _dbFactory = dbFactory;
            _startupService = startupService;
            _notificationService = notificationService;
            _subnetBase = settings.SubnetBase;
            _autoDetectSubnet = settings.AutoDetectSubnet;
            _startHost = settings.StartHost;
            _endHost = settings.EndHost;
            _intervalMinutes = settings.IntervalMinutes;
            _pingTimeoutMs = settings.PingTimeoutMs;
            _maxParallelPings = settings.MaxParallelPings;
            _showToasts = settings.ShowToasts;
            _unapprovedOnlyToasts = settings.UnapprovedOnlyToasts;
            _historyPurgeDays = settings.HistoryPurgeDays;
            _trafficIntervalSeconds = settings.TrafficIntervalSeconds;
            _trafficPurgeDays = settings.TrafficPurgeDays;
            _chartSmoothScrolling = settings.ChartSmoothScrolling;
            _runAtStartup = false;
            _digestPurgeDays = settings.DigestPurgeDays;
            _digestGenerationHour = settings.DigestGenerationHour;
            _digestNotify = settings.DigestNotify;
            _enableLogging = settings.EnableLogging;
            _speedTestEnabled = settings.SpeedTestEnabled;
            _rateUnitModeIndex = (int)settings.RateUnitMode;
            _autoCheckForUpdates = settings.AutoCheckForUpdates;

            PropertyChanged += OnSettingChanged;
            _ = InitializeRunAtStartupAsync();
        }

        private string _subnetBase;

        public string SubnetBase
        {
            get => _subnetBase;
            set => SetProperty(ref _subnetBase, value);
        }

        private bool _autoDetectSubnet;

        public bool AutoDetectSubnet
        {
            get => _autoDetectSubnet;
            set
            {

                if (SetProperty(ref _autoDetectSubnet, value))
                {
                    OnPropertyChanged(nameof(SubnetBaseEditable));
                }

            }
        }

        public bool SubnetBaseEditable => !_autoDetectSubnet;

        private int _startHost;

        public int StartHost
        {
            get => _startHost;
            set => SetProperty(ref _startHost, value);
        }

        private int _endHost;

        public int EndHost
        {
            get => _endHost;
            set => SetProperty(ref _endHost, value);
        }

        private int _intervalMinutes;

        public int IntervalMinutes
        {
            get => _intervalMinutes;
            set => SetProperty(ref _intervalMinutes, value);
        }

        private int _pingTimeoutMs;

        public int PingTimeoutMs
        {
            get => _pingTimeoutMs;
            set => SetProperty(ref _pingTimeoutMs, value);
        }

        private int _maxParallelPings;

        public int MaxParallelPings
        {
            get => _maxParallelPings;
            set => SetProperty(ref _maxParallelPings, value);
        }

        private bool _showToasts;

        public bool ShowToasts
        {
            get => _showToasts;
            set => SetProperty(ref _showToasts, value);
        }

        private bool _unapprovedOnlyToasts;

        public bool UnapprovedOnlyToasts
        {
            get => _unapprovedOnlyToasts;
            set => SetProperty(ref _unapprovedOnlyToasts, value);
        }

        private int _historyPurgeDays;

        public int HistoryPurgeDays
        {
            get => _historyPurgeDays;
            set => SetProperty(ref _historyPurgeDays, value);
        }

        private int _trafficIntervalSeconds;

        public int TrafficIntervalSeconds
        {
            get => _trafficIntervalSeconds;
            set => SetProperty(ref _trafficIntervalSeconds, value);
        }

        private int _trafficPurgeDays;

        public int TrafficPurgeDays
        {
            get => _trafficPurgeDays;
            set => SetProperty(ref _trafficPurgeDays, value);
        }

        private bool _chartSmoothScrolling;

        public bool ChartSmoothScrolling
        {
            get => _chartSmoothScrolling;
            set => SetProperty(ref _chartSmoothScrolling, value);
        }

        private bool _runAtStartup;

        public bool RunAtStartup
        {
            get => _runAtStartup;
            set
            {

                if (SetProperty(ref _runAtStartup, value))
                {
                    _ = ApplyStartupAsync(value);
                }

            }
        }

        private string _purgeStatus = string.Empty;

        public string PurgeStatus
        {
            get => _purgeStatus;
            set => SetProperty(ref _purgeStatus, value);
        }

        private string _trafficPurgeStatus = string.Empty;

        public string TrafficPurgeStatus
        {
            get => _trafficPurgeStatus;
            set => SetProperty(ref _trafficPurgeStatus, value);
        }

        private int _digestPurgeDays;

        public int DigestPurgeDays
        {
            get => _digestPurgeDays;
            set => SetProperty(ref _digestPurgeDays, value);
        }

        private int _digestGenerationHour;

        public int DigestGenerationHour
        {
            get => _digestGenerationHour;
            set => SetProperty(ref _digestGenerationHour, value);
        }

        private bool _digestNotify;

        public bool DigestNotify
        {
            get => _digestNotify;
            set => SetProperty(ref _digestNotify, value);
        }

        private bool _enableLogging;

        public bool EnableLogging
        {
            get => _enableLogging;
            set => SetProperty(ref _enableLogging, value);
        }

        public bool EnableLoggingDisplay
        {
            get
            {
                bool result;

#if DEBUG
                result = true;
#else
                result = _enableLogging;
#endif

                return result;
            }
            set
            {
                EnableLogging = value;
            }
        }

        public bool LoggingToggleEnabled
        {
            get
            {
                bool result;

#if DEBUG
                result = false;
#else
                result = true;
#endif

                return result;
            }
        }

        public bool LoggingForced
        {
            get
            {
                bool result;

#if DEBUG
                result = true;
#else
                result = false;
#endif

                return result;
            }
        }

        private bool _speedTestEnabled;

        public bool SpeedTestEnabled
        {
            get => _speedTestEnabled;
            set => SetProperty(ref _speedTestEnabled, value);
        }

        private int _rateUnitModeIndex;

        public int RateUnitModeIndex
        {
            get => _rateUnitModeIndex;
            set => SetProperty(ref _rateUnitModeIndex, value);
        }

        private bool _autoCheckForUpdates;

        public bool AutoCheckForUpdates
        {
            get => _autoCheckForUpdates;
            set => SetProperty(ref _autoCheckForUpdates, value);
        }

        private void PersistAll()
        {
            _settings.SubnetBase = SubnetBase;
            _settings.AutoDetectSubnet = AutoDetectSubnet;
            _settings.StartHost = StartHost;
            _settings.EndHost = EndHost;
            _settings.IntervalMinutes = IntervalMinutes;
            _settings.PingTimeoutMs = PingTimeoutMs;
            _settings.MaxParallelPings = MaxParallelPings;
            _settings.ShowToasts = ShowToasts;
            _settings.UnapprovedOnlyToasts = UnapprovedOnlyToasts;
            _settings.HistoryPurgeDays = HistoryPurgeDays;
            _settings.TrafficIntervalSeconds = TrafficIntervalSeconds;
            _settings.TrafficPurgeDays = TrafficPurgeDays;
            _settings.ChartSmoothScrolling = ChartSmoothScrolling;
            _settings.DigestPurgeDays = DigestPurgeDays;
            _settings.DigestGenerationHour = DigestGenerationHour;
            _settings.DigestNotify = DigestNotify;
            _settings.EnableLogging = EnableLogging;
            _settings.SpeedTestEnabled = SpeedTestEnabled;
            _settings.RateUnitMode = (RateUnitMode)RateUnitModeIndex;
            _settings.AutoCheckForUpdates = AutoCheckForUpdates;
            TrafficRateFormatter.Mode = _settings.RateUnitMode;

#if DEBUG
            AppLog.IsEnabled = true;
#else
            AppLog.IsEnabled = EnableLogging;
#endif

            _settings.Save();
        }

        private void OnSettingChanged(object? sender, PropertyChangedEventArgs args)
        {
            bool isPersistable = args.PropertyName is not null
                && args.PropertyName != nameof(PurgeStatus)
                && args.PropertyName != nameof(TrafficPurgeStatus)
                && args.PropertyName != nameof(RunAtStartup)
                && args.PropertyName != nameof(SubnetBaseEditable);

            if (isPersistable)
            {
                PersistAll();
                _notificationService.Show("Settings saved");
            }

        }

        public async Task<int> PurgeHistoryAsync()
        {
            await using AppDbContext db = await _dbFactory.CreateDbContextAsync();
            DateTime deviceCutoff = DateTime.UtcNow.AddDays(-HistoryPurgeDays);
            int deleted = await db.DeviceEvents
                .Where(deviceEvent => deviceEvent.Timestamp < deviceCutoff)
                .ExecuteDeleteAsync();
            await db.ScanSessions
                .Where(session => session.CompletedAt.HasValue && session.CompletedAt.Value < deviceCutoff)
                .ExecuteDeleteAsync();
            PurgeStatus = $"Purged {deleted} event{(deleted == 1 ? string.Empty : "s")} at {DateTime.Now:HH:mm:ss}";

            return deleted;
        }

        public async Task<int> PurgeTrafficAsync()
        {
            await using AppDbContext db = await _dbFactory.CreateDbContextAsync();
            DateTime trafficCutoff = DateTime.UtcNow.AddDays(-TrafficPurgeDays);
            int deleted = await db.TrafficEntries
                .Where(entry => entry.Timestamp < trafficCutoff)
                .ExecuteDeleteAsync();
            TrafficPurgeStatus = $"Purged {deleted} entr{(deleted == 1 ? "y" : "ies")} at {DateTime.Now:HH:mm:ss}";

            return deleted;
        }

        private async Task InitializeRunAtStartupAsync()
        {

            try
            {
                bool enabled = await _startupService.IsEnabledAsync();

                _dispatcherQueue.TryEnqueue(() => SetProperty(ref _runAtStartup, enabled, nameof(RunAtStartup)));
            }
            catch (Exception exception)
            {
                AppLog.Error("SettingsViewModel.InitializeRunAtStartup", exception);
            }

        }

        private async Task ApplyStartupAsync(bool enable)
        {

            try
            {

                if (enable)
                {
                    await _startupService.EnableAsync();
                }
                else
                {
                    await _startupService.DisableAsync();
                }

                _notificationService.Show(enable ? "Run at startup enabled" : "Run at startup disabled");
            }
            catch (Exception exception)
            {
                AppLog.Error("SettingsViewModel.ApplyStartup", exception);
            }

        }
    }
}
