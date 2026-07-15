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
    public partial class InternetViewModel : ObservableObject
    {
        private const int MinimumSpinnerMs = 500;

        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly Settings _settings;
        private long _windowCutoffEpoch;
        private long _windowBucketSeconds;
        private List<ChartPoint> _windowChartPoints = [];
        private Dictionary<string, (long Upload, long Download, string? Path)> _windowAppTotals = new();

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

                if (showLoading)
                {
                    IsLoading = false;
                }

            }

        }

        public async Task ApplyLiveFlushAsync(IReadOnlyList<TrafficEntry> entries)
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
                    ApplyFlushToWindow(entries);
                }

            }

        }

        private void SeedWindowState(InternetLoadResult result)
        {
            _windowCutoffEpoch = result.CutoffEpoch;
            _windowBucketSeconds = result.BucketSeconds;
            _windowChartPoints = new List<ChartPoint>(result.ChartPoints);
            _windowAppTotals = new Dictionary<string, (long Upload, long Download, string? Path)>();

            foreach (InternetTrafficAppRow row in result.DisplayRows)
            {

                if (!row.IsAllApps && row.ProcessName is not null)
                {
                    _windowAppTotals[row.ProcessName] = (row.BytesUploaded, row.BytesDownloaded, row.ProcessPath);
                }

            }

        }

        private void ApplyFlushToWindow(IReadOnlyList<TrafficEntry> entries)
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

                (long Upload, long Download, string? Path) current = _windowAppTotals.TryGetValue(entry.ProcessName, out (long Upload, long Download, string? Path) existing)
                    ? existing
                    : (0L, 0L, null);
                _windowAppTotals[entry.ProcessName] = (current.Upload + entry.BytesUploaded, current.Download + entry.BytesDownloaded, current.Path ?? entry.ProcessPath);
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

        private void RebuildAppRows()
        {
            long totalUpload = 0;
            long totalDownload = 0;
            List<InternetTrafficAppRow> perAppRows = new List<InternetTrafficAppRow>(_windowAppTotals.Count);

            foreach (KeyValuePair<string, (long Upload, long Download, string? Path)> pair in _windowAppTotals)
            {
                perAppRows.Add(new InternetTrafficAppRow(pair.Key, pair.Value.Upload, pair.Value.Download, pair.Value.Path));
                totalUpload += pair.Value.Upload;
                totalDownload += pair.Value.Download;
            }

            perAppRows.Sort((left, right) => (right.BytesUploaded + right.BytesDownloaded).CompareTo(left.BytesUploaded + left.BytesDownloaded));

            InternetTrafficAppRow allAppsRow = new InternetTrafficAppRow(null, totalUpload, totalDownload, null);
            List<InternetTrafficAppRow> displayRows = new List<InternetTrafficAppRow> { allAppsRow };
            displayRows.AddRange(perAppRows);

            string statusText = $"{perAppRows.Count} app{(perAppRows.Count == 1 ? string.Empty : "s")} · {ByteSizeFormatter.Format(allAppsRow.TotalBytes)} total";

            Apps = new ObservableCollection<InternetTrafficAppRow>(displayRows);
            StatusText = statusText;
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

            List<InternetTrafficAppRow> perAppRows = await LoadAppRowsAsync(db, useRollup, cutoff, cutoffEpoch, bucketRangeStart, bucketRangeEnd);

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

            InternetLoadResult result = new InternetLoadResult(chartPoints, displayRows, statusText, cutoffEpoch, bucketSeconds);

            return result;
        }

        private async Task<List<InternetTrafficAppRow>> LoadAppRowsAsync(
            AppDbContext db,
            bool useRollup,
            DateTime cutoff,
            long cutoffEpoch,
            DateTime? bucketRangeStart,
            DateTime? bucketRangeEnd)
        {
            List<InternetTrafficAppRow> rows = new List<InternetTrafficAppRow>();
            string sourceTable = useRollup ? "TrafficRollups" : "TrafficEntries";
            string whereClause = useRollup ? "MinuteEpoch >= $cutoffEpoch" : "Timestamp >= $cutoffTime";

            whereClause += " AND ProcessName <> 'System'";

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
                    SELECT ProcessName,
                           SUM(BytesUploaded)     AS Upload,
                           SUM(BytesDownloaded) AS Download,
                           MAX(ProcessPath)   AS Path
                    FROM {sourceTable}
                    WHERE {whereClause}
                    GROUP BY ProcessName
                    ORDER BY SUM(BytesUploaded) + SUM(BytesDownloaded) DESC
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
                        long upload = reader.GetInt64(1);
                        long download = reader.GetInt64(2);
                        string? processPath = reader.IsDBNull(3) ? null : reader.GetString(3);
                        InternetTrafficAppRow row = new InternetTrafficAppRow(processName, upload, download, processPath);
                        rows.Add(row);
                    }

                }

            }

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
            string sourceTable = useRollup ? "TrafficRollups" : "TrafficEntries";
            string epochExpr = useRollup ? "MinuteEpoch" : "CAST(strftime('%s', Timestamp) AS INTEGER)";
            string whereClause = useRollup ? "MinuteEpoch >= $cutoffEpoch" : "Timestamp >= $cutoffTime";

            whereClause += " AND ProcessName <> 'System'";

            await db.Database.OpenConnectionAsync();

            DbConnection connection = db.Database.GetDbConnection();

            await using (DbCommand command = connection.CreateCommand())
            {
                command.CommandText = $"""
                    SELECT
                        CAST(({epochExpr} - $cutoffEpoch) / $bucketSeconds AS INTEGER) AS BucketIndex,
                        SUM(BytesUploaded)     AS Upload,
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

    }
}
