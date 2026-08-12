using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Dispatching;
using NetworkMonitor.Core.Charting;
using NetworkMonitor.Services.Data;
using NetworkMonitor.Models.Formatting;
using NetworkMonitor.Models.Widget;
using NetworkMonitor.Services.Charting;
using NetworkMonitor.Services.Platform;
using Windows.UI;

namespace NetworkMonitor.ViewModels
{
    public partial class SettingsViewModel : ObservableObject
    {
        private readonly Settings _settings;
        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly WindowsStartupService _startupService;
        private readonly InAppNotificationService _notificationService;
        private readonly DispatcherQueue _dispatcherQueue;
        private readonly MiniGraphState _miniGraphState;
        private readonly ChartPaletteService _chartPalette;

        public SettingsViewModel(Settings settings, IDbContextFactory<AppDbContext> dbFactory, WindowsStartupService startupService, InAppNotificationService notificationService, MiniGraphState miniGraphState, ChartPaletteService chartPalette)
        {
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            _settings = settings;
            _dbFactory = dbFactory;
            _startupService = startupService;
            _notificationService = notificationService;
            _miniGraphState = miniGraphState;
            _chartPalette = chartPalette;
            _chartSchemeIndex = IndexForSchemeId(chartPalette.SchemeId);
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
            _showMiniGraph = miniGraphState.IsVisible;
            _miniGraphShowInternet = miniGraphState.ShowInternet;
            _miniGraphShowLocal = miniGraphState.ShowLocal;
            _miniGraphShowSpeedTest = miniGraphState.ShowSpeedTest;
            _miniGraphShowUnknownDevices = miniGraphState.ShowUnknownDevices;
            _miniGraphOpacity = miniGraphState.Opacity;

            // All eight, not six. Seeding only part of them left the view model correct solely when
            // driven by SettingsPage.OnPageLoaded, which calls SyncMiniGraphFromState before anything
            // reads them — an asymmetry that is a trap for the next editor rather than a live defect.
            _miniGraphHorizontal = miniGraphState.IsHorizontal;
            _miniGraphShowBorder = miniGraphState.ShowBorder;

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

        private bool _showMiniGraph;

        public bool ShowMiniGraph
        {
            get => _showMiniGraph;
            set
            {

                if (SetProperty(ref _showMiniGraph, value))
                {
                    _miniGraphState.IsVisible = value;
                }

            }
        }

        private bool _miniGraphShowInternet;

        public bool MiniGraphShowInternet
        {
            get => _miniGraphShowInternet;
            set
            {

                if (SetProperty(ref _miniGraphShowInternet, value))
                {
                    _miniGraphState.ShowInternet = value;

                    // The state refuses to turn off the last remaining section, and a refusal is silent.
                    // Without this the checkbox would sit unchecked against a widget still showing it.
                    SyncMiniGraphFromState();
                }

            }
        }

        private bool _miniGraphShowLocal;

        public bool MiniGraphShowLocal
        {
            get => _miniGraphShowLocal;
            set
            {

                if (SetProperty(ref _miniGraphShowLocal, value))
                {
                    _miniGraphState.ShowLocal = value;
                    SyncMiniGraphFromState();
                }

            }
        }

        private bool _miniGraphShowSpeedTest;

        public bool MiniGraphShowSpeedTest
        {
            get => _miniGraphShowSpeedTest;
            set
            {

                if (SetProperty(ref _miniGraphShowSpeedTest, value))
                {
                    _miniGraphState.ShowSpeedTest = value;
                    SyncMiniGraphFromState();
                }

            }
        }

        private bool _miniGraphShowUnknownDevices;

        public bool MiniGraphShowUnknownDevices
        {
            get => _miniGraphShowUnknownDevices;
            set
            {

                if (SetProperty(ref _miniGraphShowUnknownDevices, value))
                {
                    _miniGraphState.ShowUnknownDevices = value;
                    SyncMiniGraphFromState();
                }

            }
        }

        private double _miniGraphOpacity;

        public double MiniGraphOpacity
        {
            get => _miniGraphOpacity;
            set
            {

                if (SetProperty(ref _miniGraphOpacity, value))
                {
                    _miniGraphState.Opacity = (int)value;
                }

            }
        }

        private bool _miniGraphHorizontal;

        public bool MiniGraphHorizontal
        {
            get => _miniGraphHorizontal;
            set
            {

                if (SetProperty(ref _miniGraphHorizontal, value))
                {
                    _miniGraphState.Orientation = value ? MiniGraphOrientation.Horizontal : MiniGraphOrientation.Vertical;
                }

            }
        }

        private bool _miniGraphShowBorder;

        public bool MiniGraphShowBorder
        {
            get => _miniGraphShowBorder;
            set
            {

                if (SetProperty(ref _miniGraphShowBorder, value))
                {
                    _miniGraphState.ShowBorder = value;
                }

            }
        }

        public IReadOnlyList<string> ChartSchemeNames
        {
            get;
        } = ChartSchemeCatalog.Presets
            .Select(preset => preset.DisplayName)
            .Append("Custom")
            .ToList();

        private int _chartSchemeIndex;

        public int ChartSchemeIndex
        {
            get => _chartSchemeIndex;
            set
            {

                if (SetProperty(ref _chartSchemeIndex, value))
                {
                    string schemeId = value >= 0 && value < ChartSchemeCatalog.Presets.Count
                        ? ChartSchemeCatalog.Presets[value].Id
                        : ChartSchemeCatalog.CustomSchemeId;
                    _chartPalette.ApplyScheme(schemeId);
                    OnPropertyChanged(nameof(IsCustomScheme));
                }

            }
        }

        public bool IsCustomScheme => _chartPalette.IsCustom;

        public Color CustomDownloadColour
        {
            get => ColourForRole(ChartRole.Download);
            set
            {
                SetCustomColour(ChartRole.Download, value, nameof(CustomDownloadColour));
            }
        }

        public Color CustomUploadColour
        {
            get => ColourForRole(ChartRole.Upload);
            set
            {
                SetCustomColour(ChartRole.Upload, value, nameof(CustomUploadColour));
            }
        }

        public Color CustomLatencyColour
        {
            get => ColourForRole(ChartRole.Latency);
            set
            {
                SetCustomColour(ChartRole.Latency, value, nameof(CustomLatencyColour));
            }
        }

        public Color CustomJitterColour
        {
            get => ColourForRole(ChartRole.Jitter);
            set
            {
                SetCustomColour(ChartRole.Jitter, value, nameof(CustomJitterColour));
            }
        }

        public Color CustomSelectionColour
        {
            get => ColourForRole(ChartRole.Selection);
            set
            {
                SetCustomColour(ChartRole.Selection, value, nameof(CustomSelectionColour));
            }
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

        public void SyncMiniGraphFromState()
        {
            _showMiniGraph = _miniGraphState.IsVisible;
            _miniGraphShowInternet = _miniGraphState.ShowInternet;
            _miniGraphShowLocal = _miniGraphState.ShowLocal;
            _miniGraphShowSpeedTest = _miniGraphState.ShowSpeedTest;
            _miniGraphShowUnknownDevices = _miniGraphState.ShowUnknownDevices;
            _miniGraphOpacity = _miniGraphState.Opacity;
            _miniGraphHorizontal = _miniGraphState.IsHorizontal;
            _miniGraphShowBorder = _miniGraphState.ShowBorder;

            OnPropertyChanged(nameof(ShowMiniGraph));
            OnPropertyChanged(nameof(MiniGraphShowInternet));
            OnPropertyChanged(nameof(MiniGraphShowLocal));
            OnPropertyChanged(nameof(MiniGraphShowSpeedTest));
            OnPropertyChanged(nameof(MiniGraphShowUnknownDevices));
            OnPropertyChanged(nameof(MiniGraphOpacity));
            OnPropertyChanged(nameof(MiniGraphHorizontal));
            OnPropertyChanged(nameof(MiniGraphShowBorder));
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

        [RelayCommand]
        public void ResetChartScheme()
        {
            _chartPalette.ResetToDefault();
            ChartSchemeIndex = IndexForSchemeId(ChartSchemeCatalog.DefaultSchemeId);
            OnPropertyChanged(nameof(IsCustomScheme));
        }

        public void SaveCustomColours()
        {
            _chartPalette.SaveCustomColours();
        }

        private void OnSettingChanged(object? sender, PropertyChangedEventArgs args)
        {
            bool isPersistable = args.PropertyName is not null
                && args.PropertyName != nameof(PurgeStatus)
                && args.PropertyName != nameof(TrafficPurgeStatus)
                && args.PropertyName != nameof(RunAtStartup)
                && args.PropertyName != nameof(SubnetBaseEditable)
                && args.PropertyName != nameof(ShowMiniGraph)
                && args.PropertyName != nameof(MiniGraphShowInternet)
                && args.PropertyName != nameof(MiniGraphShowLocal)
                && args.PropertyName != nameof(MiniGraphShowSpeedTest)
                && args.PropertyName != nameof(MiniGraphShowUnknownDevices)
                && args.PropertyName != nameof(MiniGraphOpacity)
                && args.PropertyName != nameof(MiniGraphHorizontal)
                && args.PropertyName != nameof(MiniGraphShowBorder)
                && args.PropertyName != nameof(ChartSchemeIndex)
                && args.PropertyName != nameof(IsCustomScheme)
                && args.PropertyName != nameof(CustomDownloadColour)
                && args.PropertyName != nameof(CustomUploadColour)
                && args.PropertyName != nameof(CustomLatencyColour)
                && args.PropertyName != nameof(CustomJitterColour)
                && args.PropertyName != nameof(CustomSelectionColour);

            if (isPersistable)
            {
                PersistAll();
                _notificationService.Show("Settings saved");
            }

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

        private static int IndexForSchemeId(string schemeId)
        {
            int match = -1;

            for (int index = 0; index < ChartSchemeCatalog.Presets.Count; index++)
            {

                if (string.Equals(ChartSchemeCatalog.Presets[index].Id, schemeId, StringComparison.OrdinalIgnoreCase))
                {
                    match = index;
                }

            }

            int result = match >= 0 ? match : ChartSchemeCatalog.Presets.Count;

            return result;
        }

        private Color ColourForRole(ChartRole role)
        {
            string hex = _chartPalette.CurrentBasePalette().ForRole(role);
            byte red = byte.Parse(hex.Substring(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            byte green = byte.Parse(hex.Substring(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            byte blue = byte.Parse(hex.Substring(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            Color result = Color.FromArgb(0xFF, red, green, blue);

            return result;
        }

        private void SetCustomColour(ChartRole role, Color colour, string propertyName)
        {
            string hex = string.Format(
                CultureInfo.InvariantCulture,
                "#{0:X2}{1:X2}{2:X2}",
                colour.R,
                colour.G,
                colour.B);

            _chartPalette.ApplyCustomColour(role, hex);
            OnPropertyChanged(propertyName);
        }
    }
}
