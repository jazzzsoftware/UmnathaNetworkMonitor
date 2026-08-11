using System.Collections.ObjectModel;
using System.Data.Common;
using System.Diagnostics;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.EntityFrameworkCore;
using NetworkMonitor.Services.Data;
using NetworkMonitor.Models.Charting;
using NetworkMonitor.Models.Formatting;
using NetworkMonitor.Models.Traffic;
using NetworkMonitor.Core.Traffic;

namespace NetworkMonitor.ViewModels
{
    public partial class InternetViewModel : ObservableObject
    {
        private const int MinimumSpinnerMs = 500;
        private const int RateSampleCount = 5;
        private const string AllRateKey = "__all";

        // Fixed query shapes selected by an if, rather than a command string assembled per call —
        // see the matching block in LocalViewModel. The optional bucket-range predicates are
        // expressed as nullable parameters, the same idiom the selected-app filter already uses.
        private const string AppBucketsRollupSql = """
            SELECT CAST((MinuteEpoch - $cutoffEpoch) / $bucketSeconds AS INTEGER) AS BucketIndex,
                   ProcessName,
                   SUM(BytesUploaded)   AS Upload,
                   SUM(BytesDownloaded) AS Download,
                   MAX(ProcessPath)     AS Path
            FROM TrafficRollups
            WHERE MinuteEpoch >= $cutoffEpoch
              AND ProcessName <> 'System'
              AND ($bucketStartEpoch IS NULL OR MinuteEpoch >= $bucketStartEpoch)
              AND ($bucketEndEpoch IS NULL OR MinuteEpoch < $bucketEndEpoch)
            GROUP BY BucketIndex, ProcessName
            """;

        private const string AppBucketsEntriesSql = """
            SELECT CAST((CAST(strftime('%s', Timestamp) AS INTEGER) - $cutoffEpoch) / $bucketSeconds AS INTEGER) AS BucketIndex,
                   ProcessName,
                   SUM(BytesUploaded)   AS Upload,
                   SUM(BytesDownloaded) AS Download,
                   MAX(ProcessPath)     AS Path
            FROM TrafficEntries
            WHERE Timestamp >= $cutoffTime
              AND ProcessName <> 'System'
              AND ($bucketStartTime IS NULL OR Timestamp >= $bucketStartTime)
              AND ($bucketEndTime IS NULL OR Timestamp < $bucketEndTime)
            GROUP BY BucketIndex, ProcessName
            """;

        private const string ChartBucketsRollupSql = """
            SELECT
                CAST((MinuteEpoch - $cutoffEpoch) / $bucketSeconds AS INTEGER) AS BucketIndex,
                SUM(BytesUploaded)   AS Upload,
                SUM(BytesDownloaded) AS Download
            FROM TrafficRollups
            WHERE MinuteEpoch >= $cutoffEpoch
              AND ProcessName <> 'System'
              AND ($app IS NULL OR ProcessName = $app)
            GROUP BY BucketIndex
            """;

        private const string ChartBucketsEntriesSql = """
            SELECT
                CAST((CAST(strftime('%s', Timestamp) AS INTEGER) - $cutoffEpoch) / $bucketSeconds AS INTEGER) AS BucketIndex,
                SUM(BytesUploaded)   AS Upload,
                SUM(BytesDownloaded) AS Download
            FROM TrafficEntries
            WHERE Timestamp >= $cutoffTime
              AND ProcessName <> 'System'
              AND ($app IS NULL OR ProcessName = $app)
            GROUP BY BucketIndex
            """;

        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly Settings _settings;
        private readonly SemaphoreSlim _loadGate = new SemaphoreSlim(1, 1);
        private long _windowCutoffEpoch;
        private long _windowBucketSeconds;
        private DateTime _lastFlushUtc = DateTime.MinValue;
        private List<ChartPoint> _windowChartPoints = [];
        private List<Dictionary<string, InternetAppTotals>> _windowAppBuckets = new();
        private Dictionary<string, InternetAppTotals> _windowAppTotals = new();
        private readonly Dictionary<string, RateWindow> _rateWindows = new();
        private bool _ratesActive;

        public InternetViewModel(IDbContextFactory<AppDbContext> dbFactory, Settings settings)
        {
            _dbFactory = dbFactory;
            _settings = settings;
            _timeRangeHours = settings.InternetTimeRangeHours;
        }

        private double _timeRangeHours = 5.0 / 60.0;

        public double TimeRangeHours
        {
            get => _timeRangeHours;
            set
            {

                if (SetProperty(ref _timeRangeHours, value))
                {
                    _settings.InternetTimeRangeHours = value;
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

        private ObservableCollection<InternetTrafficAppRow> _apps = [];

        public ObservableCollection<InternetTrafficAppRow> Apps
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

            // Serialised: a live tick and an explicit reload overlapping would let the slower one
            // finish last and re-seed the window from a cutoff that is no longer on screen.
            await _loadGate.WaitAsync();

            try
            {
                double timeRangeHours = TimeRangeHours;
                string? selectedApp = SelectedApp;
                DateTime? selectedBucketStart = SelectedBucketStart;

                InternetLoadResult result = await Task.Run(() => BuildDataAsync(timeRangeHours, selectedApp, selectedBucketStart));

                ChartPoints = new ObservableCollection<ChartPoint>(result.ChartPoints);
                SeedWindowState(result);

                if (refreshList)
                {
                    Apps = new ObservableCollection<InternetTrafficAppRow>(result.DisplayRows);
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
                _loadGate.Release();

                if (showLoading)
                {
                    IsLoading = false;
                }

            }

        }

        public async Task ApplyLiveFlushAsync(IReadOnlyList<TrafficEntry> entries)
        {

            AccumulateRateWindows(entries);

            if (_windowChartPoints.Count == 0 || _windowBucketSeconds <= 0)
            {
                await LoadAsync();
            }
            else
            {
                DateTime nowUtc = DateTime.UtcNow;

                // The collector accumulated these bytes since the previous drain, so they belong to
                // that whole interval rather than to this instant.
                DateTime intervalStartUtc = _lastFlushUtc == DateTime.MinValue
                    ? nowUtc.AddSeconds(-_windowBucketSeconds)
                    : _lastFlushUtc;
                _lastFlushUtc = nowUtc;

                long nowEpoch = (long)(nowUtc - DateTime.UnixEpoch).TotalSeconds;
                long cutoffEpoch = TrafficWindow.AlignedCutoffEpoch(nowEpoch, _windowBucketSeconds, _windowChartPoints.Count);
                long bucketsAdvanced = (cutoffEpoch - _windowCutoffEpoch) / _windowBucketSeconds;

                // On the 5-minute range the bucket is one second, so the aligned cutoff moves on
                // every single flush. Reloading there would mean a full round-trip per second and
                // the incremental path would never run at all — shift the window instead.
                if (cutoffEpoch == _windowCutoffEpoch)
                {
                    ApplyFlushToWindow(entries, intervalStartUtc, nowUtc);
                }
                else if (bucketsAdvanced > 0 && bucketsAdvanced < _windowChartPoints.Count)
                {
                    ShiftWindow(cutoffEpoch, (int)bucketsAdvanced);
                    ApplyFlushToWindow(entries, intervalStartUtc, nowUtc);
                }
                else
                {
                    await LoadAsync();
                }

            }

            RebuildAppRows();
        }

        private void SeedWindowState(InternetLoadResult result)
        {
            // The page unsubscribes from Flushed on Unloaded, so _lastFlushUtc freezes while the tab
            // is off-screen. Left stale, the first flush after returning claims an interval minutes
            // long and FlushSpread smears it evenly across every bucket in the window — a uniform
            // phantom floor on top of freshly loaded history. Any reload starts the interval again.
            _lastFlushUtc = DateTime.MinValue;
            _windowCutoffEpoch = result.CutoffEpoch;
            _windowBucketSeconds = result.BucketSeconds;
            _windowChartPoints = new List<ChartPoint>(result.ChartPoints);
            _windowAppBuckets = result.AppBuckets;
            _windowAppTotals = new Dictionary<string, InternetAppTotals>();

            foreach (InternetTrafficAppRow row in result.DisplayRows)
            {

                if (!row.IsAllApps && row.ProcessName is not null)
                {
                    _windowAppTotals[row.ProcessName] = new InternetAppTotals(row.BytesUploaded, row.BytesDownloaded, row.ProcessPath);
                }

            }

        }

        private void ShiftWindow(long cutoffEpoch, int bucketsAdvanced)
        {
            int totalBuckets = _windowChartPoints.Count;

            for (int shift = 0; shift < bucketsAdvanced; shift++)
            {
                Dictionary<string, InternetAppTotals> evicted = _windowAppBuckets[0];

                foreach (KeyValuePair<string, InternetAppTotals> app in evicted)
                {

                    if (_windowAppTotals.TryGetValue(app.Key, out InternetAppTotals current))
                    {
                        long upload = current.Upload - app.Value.Upload;
                        long download = current.Download - app.Value.Download;

                        if (upload > 0 || download > 0)
                        {
                            _windowAppTotals[app.Key] = new InternetAppTotals(Math.Max(0, upload), Math.Max(0, download), current.Path);
                        }
                        else
                        {
                            _windowAppTotals.Remove(app.Key);
                        }

                    }

                }

                _windowAppBuckets.RemoveAt(0);
                _windowChartPoints.RemoveAt(0);
                _windowAppBuckets.Add(new Dictionary<string, InternetAppTotals>());

                int appendedIndex = totalBuckets - bucketsAdvanced + shift;
                DateTime bucketStart = DateTime.UnixEpoch.AddSeconds(cutoffEpoch + (long)appendedIndex * _windowBucketSeconds);
                _windowChartPoints.Add(new ChartPoint(bucketStart, 0, 0));
            }

            _windowCutoffEpoch = cutoffEpoch;
        }

        private void ApplyFlushToWindow(IReadOnlyList<TrafficEntry> entries, DateTime intervalStartUtc, DateTime intervalEndUtc)
        {
            string? selectedApp = SelectedApp;
            long chartDeltaUpload = 0;
            long chartDeltaDownload = 0;

            foreach (TrafficEntry entry in entries)
            {

                if (entry.ProcessName == "System")
                {
                    continue;
                }

                if (selectedApp is null || entry.ProcessName == selectedApp)
                {
                    chartDeltaUpload += entry.BytesUploaded;
                    chartDeltaDownload += entry.BytesDownloaded;
                }

                _windowAppTotals.TryGetValue(entry.ProcessName, out InternetAppTotals current);
                _windowAppTotals[entry.ProcessName] = new InternetAppTotals(
                    current.Upload + entry.BytesUploaded,
                    current.Download + entry.BytesDownloaded,
                    current.Path ?? entry.ProcessPath);

                if (_windowAppBuckets.Count > 0)
                {
                    Dictionary<string, InternetAppTotals> newest = _windowAppBuckets[_windowAppBuckets.Count - 1];
                    newest.TryGetValue(entry.ProcessName, out InternetAppTotals bucketTotals);
                    newest[entry.ProcessName] = new InternetAppTotals(
                        bucketTotals.Upload + entry.BytesUploaded,
                        bucketTotals.Download + entry.BytesDownloaded,
                        bucketTotals.Path ?? entry.ProcessPath);
                }

            }

            SpreadAcrossBuckets(chartDeltaUpload, chartDeltaDownload, intervalStartUtc, intervalEndUtc);

            ChartPoints = new ObservableCollection<ChartPoint>(_windowChartPoints);
        }

        private void SpreadAcrossBuckets(long upload, long download, DateTime intervalStartUtc, DateTime intervalEndUtc)
        {
            ChartPointSpreader.Apply(_windowChartPoints, upload, download, _windowBucketSeconds, intervalStartUtc, intervalEndUtc);
        }

        private void RebuildAppRows()
        {
            long totalUpload = 0;
            long totalDownload = 0;
            List<InternetTrafficAppRow> perAppRows = new List<InternetTrafficAppRow>(_windowAppTotals.Count);

            double intervalSeconds = Math.Max(1.0, _settings.TrafficIntervalSeconds);

            foreach (KeyValuePair<string, InternetAppTotals> pair in _windowAppTotals)
            {
                double appRate = RateFor(pair.Key, intervalSeconds);

                perAppRows.Add(new InternetTrafficAppRow(pair.Key, pair.Value.Upload, pair.Value.Download, pair.Value.Path, appRate));
                totalUpload += pair.Value.Upload;
                totalDownload += pair.Value.Download;
            }

            perAppRows.Sort((left, right) => (right.BytesUploaded + right.BytesDownloaded).CompareTo(left.BytesUploaded + left.BytesDownloaded));

            double allRate = RateFor(AllRateKey, intervalSeconds);
            InternetTrafficAppRow allAppsRow = new InternetTrafficAppRow(null, totalUpload, totalDownload, null, allRate);
            List<InternetTrafficAppRow> displayRows = new List<InternetTrafficAppRow> { allAppsRow };
            displayRows.AddRange(perAppRows);

            string statusText = $"{perAppRows.Count} app{(perAppRows.Count == 1 ? string.Empty : "s")} · {ByteSizeFormatter.Format(allAppsRow.TotalBytes)} total";

            Apps = new ObservableCollection<InternetTrafficAppRow>(displayRows);
            StatusText = statusText;
        }

        private void AccumulateRateWindows(IReadOnlyList<TrafficEntry> entries)
        {
            Dictionary<string, long> flushByApp = new Dictionary<string, long>();
            long flushTotal = 0;

            foreach (TrafficEntry entry in entries)
            {

                if (entry.ProcessName == "System")
                {
                    continue;
                }

                long bytes = entry.BytesUploaded + entry.BytesDownloaded;

                flushByApp.TryGetValue(entry.ProcessName, out long appBytes);
                flushByApp[entry.ProcessName] = appBytes + bytes;
                flushTotal += bytes;
            }

            flushByApp[AllRateKey] = flushTotal;

            HashSet<string> keys = new HashSet<string>(_rateWindows.Keys);

            foreach (string key in flushByApp.Keys)
            {
                keys.Add(key);
            }

            foreach (string key in keys)
            {
                flushByApp.TryGetValue(key, out long bytes);

                if (!_rateWindows.TryGetValue(key, out RateWindow? window))
                {
                    window = new RateWindow();
                    _rateWindows[key] = window;
                }

                window.Add(bytes, RateSampleCount);

                if (window.Count == RateSampleCount && window.Total == 0)
                {
                    _rateWindows.Remove(key);
                }

            }

        }

        private double RateFor(string key, double intervalSeconds)
        {
            double rate = 0.0;

            if (_ratesActive && _rateWindows.TryGetValue(key, out RateWindow? window) && window.Count > 0)
            {
                rate = window.Average / intervalSeconds;
            }

            return rate;
        }

        public void SetRatesActive(bool active)
        {

            if (_ratesActive != active)
            {
                _ratesActive = active;

                if (!active)
                {
                    _rateWindows.Clear();

                    if (_windowChartPoints.Count > 0)
                    {
                        RebuildAppRows();
                    }

                }

            }

        }

        private async Task<InternetLoadResult> BuildDataAsync(double timeRangeHours, string? selectedApp, DateTime? selectedBucketStart)
        {
            TimeSpan bucketSize = BucketSizeFor(timeRangeHours, _settings.TrafficIntervalSeconds);
            long bucketSeconds = Math.Max(1L, (long)bucketSize.TotalSeconds);
            int totalBuckets = (int)Math.Ceiling(timeRangeHours * 3600.0 / bucketSize.TotalSeconds);
            long nowEpoch = (long)(DateTime.UtcNow - DateTime.UnixEpoch).TotalSeconds;
            long cutoffEpoch = TrafficWindow.AlignedCutoffEpoch(nowEpoch, bucketSeconds, totalBuckets);
            DateTime cutoff = DateTime.UnixEpoch.AddSeconds(cutoffEpoch);
            bool useRollup = bucketSeconds >= 60;

            DateTime? bucketRangeStart = selectedBucketStart;
            DateTime? bucketRangeEnd = selectedBucketStart.HasValue ? selectedBucketStart.Value + bucketSize : null;

            await using AppDbContext db = await _dbFactory.CreateDbContextAsync();

            List<Dictionary<string, InternetAppTotals>> appBuckets = await LoadAppBucketsAsync(
                db,
                useRollup,
                cutoff,
                cutoffEpoch,
                bucketSeconds,
                totalBuckets,
                bucketRangeStart,
                bucketRangeEnd);

            List<InternetTrafficAppRow> perAppRows = AggregateAppRows(appBuckets);

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
            InternetTrafficAppRow allAppsRow = new InternetTrafficAppRow(null, totalUpload, totalDownload, null);

            List<InternetTrafficAppRow> displayRows = new List<InternetTrafficAppRow> { allAppsRow };
            displayRows.AddRange(perAppRows);

            string scopeText = selectedBucketStart.HasValue
                ? $"at {selectedBucketStart.Value.ToLocalTime():dd MMM HH:mm:ss}"
                : "total";
            string statusText = $"{perAppRows.Count} app{(perAppRows.Count == 1 ? string.Empty : "s")} · {ByteSizeFormatter.Format(allAppsRow.TotalBytes)} {scopeText}";

            InternetLoadResult result = new InternetLoadResult(chartPoints, displayRows, appBuckets, statusText, cutoffEpoch, bucketSeconds);

            return result;
        }

        private static List<InternetTrafficAppRow> AggregateAppRows(List<Dictionary<string, InternetAppTotals>> appBuckets)
        {
            Dictionary<string, InternetAppTotals> totals = new Dictionary<string, InternetAppTotals>();

            foreach (Dictionary<string, InternetAppTotals> bucket in appBuckets)
            {

                foreach (KeyValuePair<string, InternetAppTotals> app in bucket)
                {
                    totals.TryGetValue(app.Key, out InternetAppTotals current);
                    totals[app.Key] = new InternetAppTotals(
                        current.Upload + app.Value.Upload,
                        current.Download + app.Value.Download,
                        current.Path ?? app.Value.Path);
                }

            }

            List<InternetTrafficAppRow> rows = new List<InternetTrafficAppRow>(totals.Count);

            foreach (KeyValuePair<string, InternetAppTotals> app in totals)
            {
                rows.Add(new InternetTrafficAppRow(app.Key, app.Value.Upload, app.Value.Download, app.Value.Path));
            }

            rows.Sort((left, right) => (right.BytesUploaded + right.BytesDownloaded).CompareTo(left.BytesUploaded + left.BytesDownloaded));

            return rows;
        }

        private async Task<List<Dictionary<string, InternetAppTotals>>> LoadAppBucketsAsync(
            AppDbContext db,
            bool useRollup,
            DateTime cutoff,
            long cutoffEpoch,
            long bucketSeconds,
            int totalBuckets,
            DateTime? bucketRangeStart,
            DateTime? bucketRangeEnd)
        {
            List<Dictionary<string, InternetAppTotals>> buckets = new List<Dictionary<string, InternetAppTotals>>(totalBuckets);

            for (int index = 0; index < totalBuckets; index++)
            {
                buckets.Add(new Dictionary<string, InternetAppTotals>());
            }

            await db.Database.OpenConnectionAsync();

            DbConnection connection = db.Database.GetDbConnection();

            await using (DbCommand command = connection.CreateCommand())
            {
                command.CommandText = useRollup ? AppBucketsRollupSql : AppBucketsEntriesSql;

                AddParameter(command, "$bucketSeconds", bucketSeconds);
                AddParameter(command, "$cutoffEpoch", cutoffEpoch);

                if (useRollup)
                {
                    object bucketStartEpoch = bucketRangeStart.HasValue
                        ? (object)(long)(bucketRangeStart.Value - DateTime.UnixEpoch).TotalSeconds
                        : DBNull.Value;
                    object bucketEndEpoch = bucketRangeEnd.HasValue
                        ? (object)(long)(bucketRangeEnd.Value - DateTime.UnixEpoch).TotalSeconds
                        : DBNull.Value;

                    AddParameter(command, "$bucketStartEpoch", bucketStartEpoch);
                    AddParameter(command, "$bucketEndEpoch", bucketEndEpoch);
                }
                else
                {
                    object bucketStartTime = bucketRangeStart.HasValue ? bucketRangeStart.Value : (object)DBNull.Value;
                    object bucketEndTime = bucketRangeEnd.HasValue ? bucketRangeEnd.Value : (object)DBNull.Value;

                    AddParameter(command, "$cutoffTime", cutoff);
                    AddParameter(command, "$bucketStartTime", bucketStartTime);
                    AddParameter(command, "$bucketEndTime", bucketEndTime);
                }

                await using (DbDataReader reader = await command.ExecuteReaderAsync())
                {

                    while (await reader.ReadAsync())
                    {
                        int bucketIndex = reader.GetInt32(0);
                        string processName = reader.GetString(1);
                        long upload = reader.GetInt64(2);
                        long download = reader.GetInt64(3);
                        string? processPath = reader.IsDBNull(4) ? null : reader.GetString(4);

                        // A row written between the cutoff calculation and this query can land one
                        // bucket past the end; clamp rather than drop, so no bytes go missing.
                        int slot = Math.Clamp(bucketIndex, 0, totalBuckets - 1);
                        Dictionary<string, InternetAppTotals> bucket = buckets[slot];

                        bucket.TryGetValue(processName, out InternetAppTotals current);
                        bucket[processName] = new InternetAppTotals(
                            current.Upload + upload,
                            current.Download + download,
                            current.Path ?? processPath);
                    }

                }

            }

            await db.Database.CloseConnectionAsync();

            return buckets;
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

            await db.Database.OpenConnectionAsync();

            DbConnection connection = db.Database.GetDbConnection();

            await using (DbCommand command = connection.CreateCommand())
            {
                command.CommandText = useRollup ? ChartBucketsRollupSql : ChartBucketsEntriesSql;

                AddParameter(command, "$cutoffEpoch", cutoffEpoch);
                AddParameter(command, "$bucketSeconds", bucketSeconds);
                AddParameter(command, "$app", selectedApp is null ? (object)DBNull.Value : selectedApp);

                if (!useRollup)
                {
                    AddParameter(command, "$cutoffTime", cutoff);
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

            await db.Database.CloseConnectionAsync();

            return dataByBucket;
        }

        public static TimeSpan BucketSizeFor(double hours, int trafficIntervalSeconds = 5)
        {
            TimeSpan result;

            if (hours <= 5.0 / 60.0)
            {
                result = TimeSpan.FromSeconds(trafficIntervalSeconds);
            }
            else if (hours <= 1)
            {
                result = TimeSpan.FromMinutes(1);
            }
            else if (hours <= 6)
            {
                result = TimeSpan.FromMinutes(6);
            }
            else if (hours <= 24)
            {
                result = TimeSpan.FromMinutes(24);
            }
            else if (hours <= 168)
            {
                result = TimeSpan.FromMinutes(168);
            }
            else
            {
                result = TimeSpan.FromHours(12);
            }

            return result;
        }

        private static void AddParameter(DbCommand command, string name, object value)
        {
            DbParameter parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;

            command.Parameters.Add(parameter);
        }

    }
}
