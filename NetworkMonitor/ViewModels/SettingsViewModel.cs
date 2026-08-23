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
            _miniGraphOrientationIndex = (int)miniGraphState.Orientation;
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

        private int _miniGraphOrientationIndex;

        public int MiniGraphOrientationIndex
        {
            get => _miniGraphOrientationIndex;
            set
            {

                if (SetProperty(ref _miniGraphOrientationIndex, value))
                {
                    _miniGraphState.Orientation = (MiniGraphOrientation)value;
                    OnPropertyChanged(nameof(MiniGraphOrientationHelp));
                }

            }
        }

        public string MiniGraphOrientationHelp
        {
            get
            {
                string help = _miniGraphOrientationIndex == (int)MiniGraphOrientation.Horizontal
                    ? "Lays the sections out side by side in a short, wide strip — short enough to sit over the taskbar if you drag it there. Its width follows whichever sections you have switched on; drag its top or bottom edge to set the height."
                    : "Stacks the sections one above the other in a tall, narrow panel. Drag any edge to set both its width and its height.";

                return help;
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
                    NotifyCustomColoursChanged();
                }

            }
        }

        public bool IsCustomScheme => _chartPalette.IsCustom;

        public Color CustomDownloadColour
        {
            get => ColourForRole(ChartRole.Download);
            set
            {
                SetCustomColour(ChartRole.Download, value);
            }
        }

        public Color CustomUploadColour
        {
            get => ColourForRole(ChartRole.Upload);
            set
            {
                SetCustomColour(ChartRole.Upload, value);
            }
        }

        public Color CustomLatencyColour
        {
            get => ColourForRole(ChartRole.Latency);
            set
            {
                SetCustomColour(ChartRole.Latency, value);
            }
        }

        public Color CustomJitterColour
        {
            get => ColourForRole(ChartRole.Jitter);
            set
            {
                SetCustomColour(ChartRole.Jitter, value);
            }
        }

        public Color CustomSelectionColour
        {
            get => ColourForRole(ChartRole.Selection);
            set
            {
                SetCustomColour(ChartRole.Selection, value);
            }
        }

        private bool PersistAll()
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

            bool saved = _settings.Save();

            return saved;
        }

        public void SyncMiniGraphFromState()
        {
            _showMiniGraph = _miniGraphState.IsVisible;
            _miniGraphShowInternet = _miniGraphState.ShowInternet;
            _miniGraphShowLocal = _miniGraphState.ShowLocal;
            _miniGraphShowSpeedTest = _miniGraphState.ShowSpeedTest;
            _miniGraphShowUnknownDevices = _miniGraphState.ShowUnknownDevices;
            _miniGraphOpacity = _miniGraphState.Opacity;
            _miniGraphOrientationIndex = (int)_miniGraphState.Orientation;
            _miniGraphShowBorder = _miniGraphState.ShowBorder;

            OnPropertyChanged(nameof(ShowMiniGraph));
            OnPropertyChanged(nameof(MiniGraphShowInternet));
            OnPropertyChanged(nameof(MiniGraphShowLocal));
            OnPropertyChanged(nameof(MiniGraphShowSpeedTest));
            OnPropertyChanged(nameof(MiniGraphShowUnknownDevices));
            OnPropertyChanged(nameof(MiniGraphOpacity));
            OnPropertyChanged(nameof(MiniGraphOrientationIndex));
            OnPropertyChanged(nameof(MiniGraphOrientationHelp));
            OnPropertyChanged(nameof(MiniGraphShowBorder));
        }

        // A day count of 0 means "retention disabled" everywhere else in the app — the label under the
        // box says so, and ScanWorker.PurgeOldHistoryAsync skips the purge entirely on 0. Without the
        // same guard here, Purge Now at 0 computed a cutoff of "now" and deleted the ENTIRE history,
        // which is the exact opposite of what the setting claims to do at that value.
        public async Task<int> PurgeHistoryAsync()
        {
            int deleted = 0;

            if (HistoryPurgeDays > 0)
            {
                await using AppDbContext db = await _dbFactory.CreateDbContextAsync();
                DateTime deviceCutoff = DateTime.UtcNow.AddDays(-HistoryPurgeDays);
                deleted = await db.DeviceEvents
                    .Where(deviceEvent => deviceEvent.Timestamp < deviceCutoff)
                    .ExecuteDeleteAsync();
                await db.ScanSessions
                    .Where(session => session.CompletedAt.HasValue && session.CompletedAt.Value < deviceCutoff)
                    .ExecuteDeleteAsync();
                PurgeStatus = $"Purged {deleted} event{(deleted == 1 ? string.Empty : "s")} at {DateTime.Now:HH:mm:ss}";
            }
            else
            {
                PurgeStatus = "Purging is disabled while the day count is 0.";
            }

            return deleted;
        }

        public async Task<int> PurgeTrafficAsync()
        {
            int deleted = 0;

            if (TrafficPurgeDays > 0)
            {
                await using AppDbContext db = await _dbFactory.CreateDbContextAsync();
                DateTime trafficCutoff = DateTime.UtcNow.AddDays(-TrafficPurgeDays);

                // Deliberately NOT TrafficEntries. TrafficTracker already deletes every raw entry
                // older than an hour, every five minutes, so a query for raw entries older than
                // TrafficPurgeDays - a value in DAYS - could never match a single row. This button
                // reported "Purged 0 entries" every time it was pressed and did nothing at all.
                // What retention actually means for traffic is the rollups, which is what
                // ScanWorker's automatic sweep purges; this now runs the same sweep on demand.
                long rollupCutoffEpoch = (long)(trafficCutoff - DateTime.UnixEpoch).TotalSeconds;

                int rollupsDeleted = await db.Database.ExecuteSqlRawAsync(
                    "DELETE FROM TrafficRollups WHERE MinuteEpoch < {0}",
                    new object[] { rollupCutoffEpoch });

                int localRollupsDeleted = await db.Database.ExecuteSqlRawAsync(
                    "DELETE FROM LocalTrafficRollups WHERE MinuteEpoch < {0}",
                    new object[] { rollupCutoffEpoch });

                int speedTestsDeleted = await db.SpeedTestResults
                    .Where(result => result.Timestamp < trafficCutoff)
                    .ExecuteDeleteAsync();

                deleted = rollupsDeleted + localRollupsDeleted + speedTestsDeleted;
                TrafficPurgeStatus = $"Purged {deleted} record{(deleted == 1 ? string.Empty : "s")} at {DateTime.Now:HH:mm:ss}";
            }
            else
            {
                TrafficPurgeStatus = "Purging is disabled while the day count is 0.";
            }

            return deleted;
        }

        [RelayCommand]
        public void ResetChartScheme()
        {
            _chartPalette.ResetToDefault();
            ChartSchemeIndex = IndexForSchemeId(ChartSchemeCatalog.DefaultSchemeId);
            OnPropertyChanged(nameof(IsCustomScheme));
            NotifyCustomColoursChanged();
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
                && args.PropertyName != nameof(MiniGraphOrientationIndex)
                && args.PropertyName != nameof(MiniGraphOrientationHelp)
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
                bool saved = PersistAll();

                if (saved)
                {
                    _notificationService.Show("Settings saved");
                }

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

        // Deliberately does NOT raise PropertyChanged for the property being set. These five
        // properties are the target of a TwoWay x:Bind from a ColorPicker, so notifying the
        // property the binding is currently writing pushes the value straight back into the
        // picker, which re-raises its own change, which re-enters this method — an unbounded
        // source→target→source cycle that overflowed the stack inside the PaletteChanged
        // fan-out. The picker already holds the value it just sent; it needs no echo. When the
        // base palette changes from somewhere else, NotifyCustomColoursChanged does the refresh.
        private void SetCustomColour(ChartRole role, Color colour)
        {
            Color current = ColourForRole(role);

            if (current.R != colour.R || current.G != colour.G || current.B != colour.B)
            {
                string hex = string.Format(
                    CultureInfo.InvariantCulture,
                    "#{0:X2}{1:X2}{2:X2}",
                    colour.R,
                    colour.G,
                    colour.B);

                _chartPalette.ApplyCustomColour(role, hex);
            }

        }

        private void NotifyCustomColoursChanged()
        {
            OnPropertyChanged(nameof(CustomDownloadColour));
            OnPropertyChanged(nameof(CustomUploadColour));
            OnPropertyChanged(nameof(CustomLatencyColour));
            OnPropertyChanged(nameof(CustomJitterColour));
            OnPropertyChanged(nameof(CustomSelectionColour));
        }
    }
}
