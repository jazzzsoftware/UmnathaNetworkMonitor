using System.Collections.ObjectModel;
using System.Data.Common;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.EntityFrameworkCore;
using NetworkMonitor.Data;
using NetworkMonitor.Models;
using NetworkMonitor.Services.Common;
using NetworkMonitor.Services.Traffic;

namespace NetworkMonitor.ViewModels
{
    public partial class LocalViewModel : ObservableObject
    {
        private const int MinimumSpinnerMs = 500;

        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly Settings _settings;
        private long _windowCutoffEpoch;
        private long _windowBucketSeconds;
        private List<ChartPoint> _windowChartPoints = [];
        private Dictionary<string, Dictionary<string, (long Upload, long Download)>> _windowAppPeerTotals = new();
        private Dictionary<string, string> _namesByIp = new();

        public LocalViewModel(IDbContextFactory<AppDbContext> dbFactory, Settings settings)
        {
            _dbFactory = dbFactory;
            _settings = settings;
            _timeRangeHours = settings.LocalTimeRangeHours;
        }

        private double _timeRangeHours = 5.0 / 60.0;

        public double TimeRangeHours
        {
            get => _timeRangeHours;
            set
            {

                if (SetProperty(ref _timeRangeHours, value))
                {
                    _settings.LocalTimeRangeHours = value;
                    _settings.Save();
                    _ = LoadAsync(true);
                }

            }
        }

        private string? _selectedApp;

        public string? SelectedApp
        {
            get => _selectedApp;
            set => SetProperty(ref _selectedApp, value);
        }

        private DateTime? _selectedBucketStart;

        public DateTime? SelectedBucketStart
        {
            get => _selectedBucketStart;
            set => SetProperty(ref _selectedBucketStart, value);
        }

        private ObservableCollection<ChartPoint> _chartPoints = [];

        public ObservableCollection<ChartPoint> ChartPoints
        {
            get => _chartPoints;
            set => SetProperty(ref _chartPoints, value);
        }

        private ObservableCollection<LocalTrafficAppRow> _apps = [];

        public ObservableCollection<LocalTrafficAppRow> Apps
        {
            get => _apps;
            set => SetProperty(ref _apps, value);
        }

        private string _statusText = string.Empty;

        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        private bool _isLoading;

        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public async Task LoadAsync(bool showLoading = false, bool refreshList = true)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();

            if (showLoading)
            {
                IsLoading = true;
            }

            try
            {
                double timeRangeHours = TimeRangeHours;
                string? selectedApp = SelectedApp;
                DateTime? selectedBucketStart = SelectedBucketStart;

                LocalLoadResult result = await Task.Run(() => BuildDataAsync(timeRangeHours, selectedApp, selectedBucketStart));

                ChartPoints = new ObservableCollection<ChartPoint>(result.ChartPoints);
                SeedWindowState(result);

                if (refreshList)
                {
                    Apps = new ObservableCollection<LocalTrafficAppRow>(result.DisplayRows);
                    StatusText = result.StatusText;
                }

                if (showLoading)
                {
                    long elapsed = stopwatch.ElapsedMilliseconds;

                    if (elapsed < MinimumSpinnerMs)
                    {
                        await Task.Delay(MinimumSpinnerMs - (int)elapsed);
                    }

                }
            }
            finally
            {

                if (showLoading)
                {
                    IsLoading = false;
                }

            }

        }

        public async Task ApplyLiveFlushAsync(IReadOnlyList<LocalTrafficDelta> deltas)
        {

            if (_windowChartPoints.Count == 0)
            {
                await LoadAsync();
            }
            else
            {
                long nowEpoch = (long)(DateTime.UtcNow - DateTime.UnixEpoch).TotalSeconds;
                long cutoffEpoch = TrafficWindow.AlignedCutoffEpoch(nowEpoch, _windowBucketSeconds, _windowChartPoints.Count);

                if (cutoffEpoch != _windowCutoffEpoch)
                {
                    await LoadAsync();
                }
                else
                {
                    await ApplyFlushToWindow(deltas);
                }

            }

        }

        private void SeedWindowState(LocalLoadResult result)
        {
            _windowCutoffEpoch = result.CutoffEpoch;
            _windowBucketSeconds = result.BucketSeconds;
            _windowChartPoints = new List<ChartPoint>(result.ChartPoints);
            _windowAppPeerTotals = new Dictionary<string, Dictionary<string, (long Upload, long Download)>>();

            foreach (LocalTrafficAppRow row in result.DisplayRows)
            {

                if (!row.IsAllApps && row.ProcessName is not null)
                {
                    Dictionary<string, (long Upload, long Download)> inner = new Dictionary<string, (long Upload, long Download)>();

                    foreach (LocalTrafficDeviceRow peer in row.Peers)
                    {
                        inner[peer.RemoteIp] = (peer.BytesUploaded, peer.BytesDownloaded);
                    }

                    _windowAppPeerTotals[row.ProcessName] = inner;
                }

            }

        }

        private async Task ApplyFlushToWindow(IReadOnlyList<LocalTrafficDelta> deltas)
        {
            bool hasNewApp = false;

            foreach (LocalTrafficDelta delta in deltas)
            {

                if (!_windowAppPeerTotals.ContainsKey(delta.ProcessName))
                {
                    hasNewApp = true;
                }

            }

            if (hasNewApp)
            {
                // A new app appeared mid-window; the in-memory patch cannot synthesize its
                // initial peer set, so fall back to an exact full reload via LoadAsync.
                await LoadAsync();
            }
            else
            {
                string? selectedApp = SelectedApp;
                long chartDeltaUpload = 0;
                long chartDeltaDownload = 0;

                foreach (LocalTrafficDelta delta in deltas)
                {

                    if (selectedApp is null || delta.ProcessName == selectedApp)
                    {
                        chartDeltaUpload += delta.BytesUploaded;
                        chartDeltaDownload += delta.BytesDownloaded;
                    }

                    Dictionary<string, (long Upload, long Download)> inner = _windowAppPeerTotals[delta.ProcessName];
                    inner.TryGetValue(delta.RemoteIp, out (long Upload, long Download) currentPeer);
                    inner[delta.RemoteIp] = (currentPeer.Upload + delta.BytesUploaded, currentPeer.Download + delta.BytesDownloaded);
                }

                int lastIndex = _windowChartPoints.Count - 1;
                ChartPoint last = _windowChartPoints[lastIndex];
                _windowChartPoints[lastIndex] = last with
                {
                    BytesUploaded = last.BytesUploaded + chartDeltaUpload,
                    BytesDownloaded = last.BytesDownloaded + chartDeltaDownload
                };

                ChartPoints = new ObservableCollection<ChartPoint>(_windowChartPoints);

                RebuildAppRows();
            }

        }

        private void RebuildAppRows()
        {
            long totalUpload = 0;
            long totalDownload = 0;
            List<LocalTrafficAppRow> perAppRows = new List<LocalTrafficAppRow>(_windowAppPeerTotals.Count);

            foreach (KeyValuePair<string, Dictionary<string, (long Upload, long Download)>> appEntry in _windowAppPeerTotals)
            {
                List<LocalTrafficDeviceRow> peerRows = new List<LocalTrafficDeviceRow>(appEntry.Value.Count);
                long appUpload = 0;
                long appDownload = 0;

                foreach (KeyValuePair<string, (long Upload, long Download)> peerEntry in appEntry.Value)
                {
                    string displayName = LocalTrafficNameResolver.Resolve(peerEntry.Key, _namesByIp);
                    LocalTrafficDeviceRow peerRow = new LocalTrafficDeviceRow(peerEntry.Key, displayName, peerEntry.Value.Upload, peerEntry.Value.Download);

                    peerRows.Add(peerRow);
                    appUpload += peerEntry.Value.Upload;
                    appDownload += peerEntry.Value.Download;
                }

                peerRows.Sort((left, right) => right.TotalBytes.CompareTo(left.TotalBytes));

                perAppRows.Add(new LocalTrafficAppRow(appEntry.Key, appEntry.Key, appUpload, appDownload, peerRows));
                totalUpload += appUpload;
                totalDownload += appDownload;
            }

            perAppRows.Sort((left, right) => right.TotalBytes.CompareTo(left.TotalBytes));

            LocalTrafficAppRow allAppsRow = new LocalTrafficAppRow(null, "All Apps", totalUpload, totalDownload, Array.Empty<LocalTrafficDeviceRow>());
            List<LocalTrafficAppRow> displayRows = new List<LocalTrafficAppRow> { allAppsRow };
            displayRows.AddRange(perAppRows);

            string statusText = $"{perAppRows.Count} app{(perAppRows.Count == 1 ? string.Empty : "s")} · {ByteSizeFormatter.Format(allAppsRow.TotalBytes)} total";

            Apps = new ObservableCollection<LocalTrafficAppRow>(displayRows);
            StatusText = statusText;
        }

        private async Task<LocalLoadResult> BuildDataAsync(double timeRangeHours, string? selectedApp, DateTime? selectedBucketStart)
        {
            TimeSpan bucketSize = InternetViewModel.BucketSizeFor(timeRangeHours, _settings.TrafficIntervalSeconds);
            long bucketSeconds = Math.Max(1L, (long)bucketSize.TotalSeconds);
            int totalBuckets = (int)Math.Ceiling(timeRangeHours * 3600.0 / bucketSize.TotalSeconds);
            long nowEpoch = (long)(DateTime.UtcNow - DateTime.UnixEpoch).TotalSeconds;
            long cutoffEpoch = TrafficWindow.AlignedCutoffEpoch(nowEpoch, bucketSeconds, totalBuckets);
            DateTime cutoff = DateTime.UnixEpoch.AddSeconds(cutoffEpoch);
            bool useRollup = bucketSeconds >= 60;

            DateTime? bucketRangeStart = selectedBucketStart;
            DateTime? bucketRangeEnd = selectedBucketStart.HasValue ? selectedBucketStart.Value + bucketSize : null;

            await using AppDbContext db = await _dbFactory.CreateDbContextAsync();

            Dictionary<string, string> namesByIp = await BuildNameMapAsync(db);
            _namesByIp = namesByIp;

            List<LocalTrafficAppRow> perAppRows = await LoadAppRowsAsync(db, useRollup, cutoff, cutoffEpoch, bucketRangeStart, bucketRangeEnd, namesByIp);

            Dictionary<int, (long Upload, long Download)> dataByBucket =
                await LoadChartBucketsAsync(db, useRollup, cutoff, cutoffEpoch, bucketSeconds, selectedApp);

            List<ChartPoint> chartPoints = Enumerable
                .Range(0, totalBuckets)
                .Select(bucketIndex =>
                {
                    DateTime bucketStart = cutoff + TimeSpan.FromTicks((long)bucketIndex * bucketSize.Ticks);
                    dataByBucket.TryGetValue(bucketIndex, out (long Upload, long Download) trafficData);
                    ChartPoint point = new ChartPoint(bucketStart, trafficData.Upload, trafficData.Download);

                    return point;
                })
                .ToList();

            long totalUpload = perAppRows.Sum(row => row.BytesUploaded);
            long totalDownload = perAppRows.Sum(row => row.BytesDownloaded);
            LocalTrafficAppRow allAppsRow = new LocalTrafficAppRow(null, "All Apps", totalUpload, totalDownload, Array.Empty<LocalTrafficDeviceRow>());

            List<LocalTrafficAppRow> displayRows = new List<LocalTrafficAppRow> { allAppsRow };
            displayRows.AddRange(perAppRows);

            string scopeText = selectedBucketStart.HasValue
                ? $"at {selectedBucketStart.Value.ToLocalTime():dd MMM HH:mm:ss}"
                : "total";
            string statusText = $"{perAppRows.Count} app{(perAppRows.Count == 1 ? string.Empty : "s")} · {ByteSizeFormatter.Format(allAppsRow.TotalBytes)} {scopeText}";

            LocalLoadResult result = new LocalLoadResult(chartPoints, displayRows, statusText, cutoffEpoch, bucketSeconds);

            return result;
        }

        private async Task<Dictionary<string, string>> BuildNameMapAsync(AppDbContext db)
        {
            Dictionary<string, string> namesByIp = new Dictionary<string, string>();
            List<Device> devices = await db.Devices.AsNoTracking().ToListAsync();

            foreach (Device device in devices)
            {

                if (!string.IsNullOrWhiteSpace(device.IpAddress))
                {
                    namesByIp[device.IpAddress] = device.DisplayName;
                }

            }

            return namesByIp;
        }

        private async Task<List<LocalTrafficAppRow>> LoadAppRowsAsync(
            AppDbContext db,
            bool useRollup,
            DateTime cutoff,
            long cutoffEpoch,
            DateTime? bucketRangeStart,
            DateTime? bucketRangeEnd,
            IReadOnlyDictionary<string, string> namesByIp)
        {
            List<LocalTrafficMinute> minutes = new List<LocalTrafficMinute>();
            string sourceTable = useRollup ? "LocalTrafficRollups" : "LocalTrafficEntries";
            string whereClause = useRollup ? "MinuteEpoch >= $cutoffEpoch" : "Timestamp >= $cutoffTime";

            if (bucketRangeStart.HasValue && bucketRangeEnd.HasValue)
            {

                if (useRollup)
                {
                    whereClause += " AND MinuteEpoch >= $bucketStartEpoch AND MinuteEpoch < $bucketEndEpoch";
                }
                else
                {
                    whereClause += " AND Timestamp >= $bucketStartTime AND Timestamp < $bucketEndTime";
                }

            }

            await db.Database.OpenConnectionAsync();

            DbConnection connection = db.Database.GetDbConnection();

            await using (DbCommand command = connection.CreateCommand())
            {
                command.CommandText = $"""
                    SELECT ProcessName, RemoteIp,
                           SUM(BytesUploaded)   AS Upload,
                           SUM(BytesDownloaded) AS Download
                    FROM {sourceTable}
                    WHERE {whereClause}
                    GROUP BY ProcessName, RemoteIp
                    """;

                if (useRollup)
                {
                    DbParameter cutoffParameter = command.CreateParameter();
                    cutoffParameter.ParameterName = "$cutoffEpoch";
                    cutoffParameter.Value = cutoffEpoch;
                    command.Parameters.Add(cutoffParameter);
                }
                else
                {
                    DbParameter cutoffParameter = command.CreateParameter();
                    cutoffParameter.ParameterName = "$cutoffTime";
                    cutoffParameter.Value = cutoff;
                    command.Parameters.Add(cutoffParameter);
                }

                if (bucketRangeStart.HasValue && bucketRangeEnd.HasValue)
                {

                    if (useRollup)
                    {
                        long bucketStartEpoch = (long)(bucketRangeStart.Value - DateTime.UnixEpoch).TotalSeconds;
                        long bucketEndEpoch = (long)(bucketRangeEnd.Value - DateTime.UnixEpoch).TotalSeconds;

                        DbParameter bucketStartParameter = command.CreateParameter();
                        bucketStartParameter.ParameterName = "$bucketStartEpoch";
                        bucketStartParameter.Value = bucketStartEpoch;
                        command.Parameters.Add(bucketStartParameter);

                        DbParameter bucketEndParameter = command.CreateParameter();
                        bucketEndParameter.ParameterName = "$bucketEndEpoch";
                        bucketEndParameter.Value = bucketEndEpoch;
                        command.Parameters.Add(bucketEndParameter);
                    }
                    else
                    {
                        DbParameter bucketStartParameter = command.CreateParameter();
                        bucketStartParameter.ParameterName = "$bucketStartTime";
                        bucketStartParameter.Value = bucketRangeStart.Value;
                        command.Parameters.Add(bucketStartParameter);

                        DbParameter bucketEndParameter = command.CreateParameter();
                        bucketEndParameter.ParameterName = "$bucketEndTime";
                        bucketEndParameter.Value = bucketRangeEnd.Value;
                        command.Parameters.Add(bucketEndParameter);
                    }

                }

                await using (DbDataReader reader = await command.ExecuteReaderAsync())
                {

                    while (await reader.ReadAsync())
                    {
                        string processName = reader.GetString(0);
                        string remoteIp = reader.GetString(1);
                        long upload = reader.GetInt64(2);
                        long download = reader.GetInt64(3);
                        LocalTrafficMinute minute = new LocalTrafficMinute(0, processName, remoteIp, upload, download);
                        minutes.Add(minute);
                    }

                }

            }

            IReadOnlyList<LocalTrafficAppRow> appRows = LocalTrafficAggregator.Build(minutes, namesByIp);
            List<LocalTrafficAppRow> rows = appRows.ToList();

            return rows;
        }

        private async Task<Dictionary<int, (long Upload, long Download)>> LoadChartBucketsAsync(
            AppDbContext db,
            bool useRollup,
            DateTime cutoff,
            long cutoffEpoch,
            long bucketSeconds,
            string? selectedApp)
        {
            Dictionary<int, (long Upload, long Download)> dataByBucket = new Dictionary<int, (long Upload, long Download)>();
            string sourceTable = useRollup ? "LocalTrafficRollups" : "LocalTrafficEntries";
            string epochExpr = useRollup ? "MinuteEpoch" : "CAST(strftime('%s', Timestamp) AS INTEGER)";
            string whereClause = useRollup ? "MinuteEpoch >= $cutoffEpoch" : "Timestamp >= $cutoffTime";

            await db.Database.OpenConnectionAsync();

            DbConnection connection = db.Database.GetDbConnection();

            await using (DbCommand command = connection.CreateCommand())
            {
                command.CommandText = $"""
                    SELECT
                        CAST(({epochExpr} - $cutoffEpoch) / $bucketSeconds AS INTEGER) AS BucketIndex,
                        SUM(BytesUploaded)   AS Upload,
                        SUM(BytesDownloaded) AS Download
                    FROM {sourceTable}
                    WHERE {whereClause}
                        AND ($app IS NULL OR ProcessName = $app)
                    GROUP BY BucketIndex
                    """;

                DbParameter cutoffParameter = command.CreateParameter();
                cutoffParameter.ParameterName = "$cutoffEpoch";
                cutoffParameter.Value = cutoffEpoch;
                command.Parameters.Add(cutoffParameter);

                DbParameter bucketParameter = command.CreateParameter();
                bucketParameter.ParameterName = "$bucketSeconds";
                bucketParameter.Value = bucketSeconds;
                command.Parameters.Add(bucketParameter);

                DbParameter appParameter = command.CreateParameter();
                appParameter.ParameterName = "$app";
                appParameter.Value = selectedApp is null ? (object)DBNull.Value : selectedApp;
                command.Parameters.Add(appParameter);

                if (!useRollup)
                {
                    DbParameter cutoffTimeParameter = command.CreateParameter();
                    cutoffTimeParameter.ParameterName = "$cutoffTime";
                    cutoffTimeParameter.Value = cutoff;
                    command.Parameters.Add(cutoffTimeParameter);
                }

                await using (DbDataReader reader = await command.ExecuteReaderAsync())
                {

                    while (await reader.ReadAsync())
                    {
                        int bucketIndex = reader.GetInt32(0);
                        long upload = reader.GetInt64(1);
                        long download = reader.GetInt64(2);
                        dataByBucket[bucketIndex] = (upload, download);
                    }

                }

            }

            return dataByBucket;
        }
    }
}
