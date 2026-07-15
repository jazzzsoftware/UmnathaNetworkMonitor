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
        private Dictionary<string, (long Upload, long Download)> _windowDeviceTotals = new();
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

        private string? _selectedEndpoint;

        public string? SelectedEndpoint
        {
            get => _selectedEndpoint;
            set => SetProperty(ref _selectedEndpoint, value);
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

        private ObservableCollection<LocalTrafficDeviceRow> _devices = [];

        public ObservableCollection<LocalTrafficDeviceRow> Devices
        {
            get => _devices;
            set => SetProperty(ref _devices, value);
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
                string? selectedEndpoint = SelectedEndpoint;
                DateTime? selectedBucketStart = SelectedBucketStart;

                LocalLoadResult result = await Task.Run(() => BuildDataAsync(timeRangeHours, selectedEndpoint, selectedBucketStart));

                ChartPoints = new ObservableCollection<ChartPoint>(result.ChartPoints);
                SeedWindowState(result);

                if (refreshList)
                {
                    Devices = new ObservableCollection<LocalTrafficDeviceRow>(result.DisplayRows);
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
                    ApplyFlushToWindow(deltas);
                }

            }

        }

        private void SeedWindowState(LocalLoadResult result)
        {
            _windowCutoffEpoch = result.CutoffEpoch;
            _windowBucketSeconds = result.BucketSeconds;
            _windowChartPoints = new List<ChartPoint>(result.ChartPoints);
            _windowDeviceTotals = new Dictionary<string, (long Upload, long Download)>();

            foreach (LocalTrafficDeviceRow row in result.DisplayRows)
            {

                if (!row.IsAllDevices)
                {
                    _windowDeviceTotals[row.RemoteIp] = (row.BytesUploaded, row.BytesDownloaded);
                }

            }

        }

        private void ApplyFlushToWindow(IReadOnlyList<LocalTrafficDelta> deltas)
        {
            string? selectedEndpoint = SelectedEndpoint;
            long chartDeltaUpload = 0;
            long chartDeltaDownload = 0;

            foreach (LocalTrafficDelta delta in deltas)
            {

                if (selectedEndpoint is null || delta.RemoteIp == selectedEndpoint)
                {
                    chartDeltaUpload += delta.BytesUploaded;
                    chartDeltaDownload += delta.BytesDownloaded;
                }

                (long Upload, long Download) current = _windowDeviceTotals.TryGetValue(delta.RemoteIp, out (long Upload, long Download) existing)
                    ? existing
                    : (0L, 0L);
                _windowDeviceTotals[delta.RemoteIp] = (current.Upload + delta.BytesUploaded, current.Download + delta.BytesDownloaded);
            }

            int lastIndex = _windowChartPoints.Count - 1;
            ChartPoint last = _windowChartPoints[lastIndex];
            _windowChartPoints[lastIndex] = last with
            {
                BytesUploaded = last.BytesUploaded + chartDeltaUpload,
                BytesDownloaded = last.BytesDownloaded + chartDeltaDownload
            };

            ChartPoints = new ObservableCollection<ChartPoint>(_windowChartPoints);

            RebuildDeviceRows();
        }

        private void RebuildDeviceRows()
        {
            long totalUpload = 0;
            long totalDownload = 0;
            List<LocalTrafficDeviceRow> perDeviceRows = new List<LocalTrafficDeviceRow>(_windowDeviceTotals.Count);

            foreach (KeyValuePair<string, (long Upload, long Download)> pair in _windowDeviceTotals)
            {
                string displayName = LocalTrafficNameResolver.Resolve(pair.Key, _namesByIp);

                perDeviceRows.Add(new LocalTrafficDeviceRow(pair.Key, displayName, pair.Value.Upload, pair.Value.Download));
                totalUpload += pair.Value.Upload;
                totalDownload += pair.Value.Download;
            }

            perDeviceRows.Sort((left, right) => right.TotalBytes.CompareTo(left.TotalBytes));

            LocalTrafficDeviceRow allDevicesRow = new LocalTrafficDeviceRow(string.Empty, "All Devices", totalUpload, totalDownload);
            List<LocalTrafficDeviceRow> displayRows = new List<LocalTrafficDeviceRow> { allDevicesRow };
            displayRows.AddRange(perDeviceRows);

            string statusText = $"{perDeviceRows.Count} device{(perDeviceRows.Count == 1 ? string.Empty : "s")} · {ByteSizeFormatter.Format(allDevicesRow.TotalBytes)} total";

            Devices = new ObservableCollection<LocalTrafficDeviceRow>(displayRows);
            StatusText = statusText;
        }

        private async Task<LocalLoadResult> BuildDataAsync(double timeRangeHours, string? selectedEndpoint, DateTime? selectedBucketStart)
        {
            TimeSpan bucketSize = InternetViewModel.BucketSizeFor(timeRangeHours, _settings.TrafficIntervalSeconds);
            long bucketSeconds = Math.Max(1L, (long)bucketSize.TotalSeconds);
            int totalBuckets = (int)Math.Ceiling(timeRangeHours * 3600.0 / bucketSize.TotalSeconds);
            long nowEpoch = (long)(DateTime.UtcNow - DateTime.UnixEpoch).TotalSeconds;
            long cutoffEpoch = TrafficWindow.AlignedCutoffEpoch(nowEpoch, bucketSeconds, totalBuckets);

            DateTime? bucketRangeStart = selectedBucketStart;
            DateTime? bucketRangeEnd = selectedBucketStart.HasValue ? selectedBucketStart.Value + bucketSize : null;

            await using AppDbContext db = await _dbFactory.CreateDbContextAsync();

            Dictionary<string, string> namesByIp = await BuildNameMapAsync(db);
            _namesByIp = namesByIp;

            List<LocalTrafficDeviceRow> perDeviceRows = await LoadDeviceRowsAsync(db, cutoffEpoch, bucketRangeStart, bucketRangeEnd, namesByIp);

            Dictionary<int, (long Upload, long Download)> dataByBucket =
                await LoadChartBucketsAsync(db, cutoffEpoch, bucketSeconds, selectedEndpoint);

            List<ChartPoint> chartPoints = Enumerable
                .Range(0, totalBuckets)
                .Select(bucketIndex =>
                {
                    DateTime bucketStart = DateTime.UnixEpoch.AddSeconds(cutoffEpoch) + TimeSpan.FromTicks((long)bucketIndex * bucketSize.Ticks);
                    dataByBucket.TryGetValue(bucketIndex, out (long Upload, long Download) trafficData);
                    ChartPoint point = new ChartPoint(bucketStart, trafficData.Upload, trafficData.Download);

                    return point;
                })
                .ToList();

            long totalUpload = perDeviceRows.Sum(row => row.BytesUploaded);
            long totalDownload = perDeviceRows.Sum(row => row.BytesDownloaded);
            LocalTrafficDeviceRow allDevicesRow = new LocalTrafficDeviceRow(string.Empty, "All Devices", totalUpload, totalDownload);

            List<LocalTrafficDeviceRow> displayRows = new List<LocalTrafficDeviceRow> { allDevicesRow };
            displayRows.AddRange(perDeviceRows);

            string scopeText = selectedBucketStart.HasValue
                ? $"at {selectedBucketStart.Value.ToLocalTime():dd MMM HH:mm:ss}"
                : "total";
            string statusText = $"{perDeviceRows.Count} device{(perDeviceRows.Count == 1 ? string.Empty : "s")} · {ByteSizeFormatter.Format(allDevicesRow.TotalBytes)} {scopeText}";

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

        private async Task<List<LocalTrafficDeviceRow>> LoadDeviceRowsAsync(
            AppDbContext db,
            long cutoffEpoch,
            DateTime? bucketRangeStart,
            DateTime? bucketRangeEnd,
            IReadOnlyDictionary<string, string> namesByIp)
        {
            List<LocalTrafficDeviceRow> rows = new List<LocalTrafficDeviceRow>();
            string whereClause = "MinuteEpoch >= $cutoffEpoch";

            if (bucketRangeStart.HasValue && bucketRangeEnd.HasValue)
            {
                whereClause += " AND MinuteEpoch >= $bucketStartEpoch AND MinuteEpoch < $bucketEndEpoch";
            }

            await db.Database.OpenConnectionAsync();

            DbConnection connection = db.Database.GetDbConnection();

            await using (DbCommand command = connection.CreateCommand())
            {
                command.CommandText = $"""
                    SELECT RemoteIp,
                           SUM(BytesUploaded)   AS Upload,
                           SUM(BytesDownloaded) AS Download
                    FROM LocalTrafficRollups
                    WHERE {whereClause}
                    GROUP BY RemoteIp
                    ORDER BY SUM(BytesUploaded) + SUM(BytesDownloaded) DESC
                    """;

                DbParameter cutoffParameter = command.CreateParameter();
                cutoffParameter.ParameterName = "$cutoffEpoch";
                cutoffParameter.Value = cutoffEpoch;
                command.Parameters.Add(cutoffParameter);

                if (bucketRangeStart.HasValue && bucketRangeEnd.HasValue)
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

                await using (DbDataReader reader = await command.ExecuteReaderAsync())
                {

                    while (await reader.ReadAsync())
                    {
                        string remoteIp = reader.GetString(0);
                        long upload = reader.GetInt64(1);
                        long download = reader.GetInt64(2);
                        string displayName = LocalTrafficNameResolver.Resolve(remoteIp, namesByIp);
                        LocalTrafficDeviceRow row = new LocalTrafficDeviceRow(remoteIp, displayName, upload, download);
                        rows.Add(row);
                    }

                }

            }

            return rows;
        }

        private async Task<Dictionary<int, (long Upload, long Download)>> LoadChartBucketsAsync(
            AppDbContext db,
            long cutoffEpoch,
            long bucketSeconds,
            string? selectedEndpoint)
        {
            Dictionary<int, (long Upload, long Download)> dataByBucket = new Dictionary<int, (long Upload, long Download)>();

            await db.Database.OpenConnectionAsync();

            DbConnection connection = db.Database.GetDbConnection();

            await using (DbCommand command = connection.CreateCommand())
            {
                command.CommandText = """
                    SELECT
                        CAST((MinuteEpoch - $cutoffEpoch) / $bucketSeconds AS INTEGER) AS BucketIndex,
                        SUM(BytesUploaded)   AS Upload,
                        SUM(BytesDownloaded) AS Download
                    FROM LocalTrafficRollups
                    WHERE MinuteEpoch >= $cutoffEpoch
                        AND ($endpoint IS NULL OR RemoteIp = $endpoint)
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

                DbParameter endpointParameter = command.CreateParameter();
                endpointParameter.ParameterName = "$endpoint";
                endpointParameter.Value = selectedEndpoint is null ? (object)DBNull.Value : selectedEndpoint;
                command.Parameters.Add(endpointParameter);

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

    internal record LocalLoadResult(
        List<ChartPoint> ChartPoints,
        List<LocalTrafficDeviceRow> DisplayRows,
        string StatusText,
        long CutoffEpoch,
        long BucketSeconds);
}
