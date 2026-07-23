using System.Collections.ObjectModel;
using System.Data.Common;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.EntityFrameworkCore;
using NetworkMonitor.Services.Data;
using NetworkMonitor.Models.Charting;
using NetworkMonitor.Models.Devices;
using NetworkMonitor.Models.Formatting;
using NetworkMonitor.Models.Traffic;
using NetworkMonitor.Services.Traffic;
using NetworkMonitor.Core.Traffic;

namespace NetworkMonitor.ViewModels
{
    public partial class LocalViewModel : ObservableObject
    {
        private const int MinimumSpinnerMs = 500;
        private const int RateSampleCount = 5;
        private const string AllRateKey = "__all";

        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly Settings _settings;
        private long _windowCutoffEpoch;
        private long _windowBucketSeconds;
        private List<ChartPoint> _windowChartPoints = [];
        private Dictionary<(string ProcessName, string RemoteIp, int Protocol, int RemotePort), (long Upload, long Download)> _windowFlows = new();
        private readonly Dictionary<string, Queue<long>> _rateWindows = new();
        private Dictionary<string, string> _namesByIp = new();

        public LocalViewModel(IDbContextFactory<AppDbContext> dbFactory, Settings settings)
        {
            _dbFactory = dbFactory;
            _settings = settings;
            _timeRangeHours = settings.LocalTimeRangeHours;
            _lens = settings.LocalLens;
            _groupHeader = _lens == LocalLens.ByApp ? "App" : "Device";
            _childHeader = _lens == LocalLens.ByApp ? "Peers" : "Apps";
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

        private LocalLens _lens;

        public LocalLens Lens
        {
            get => _lens;
            set
            {

                if (SetProperty(ref _lens, value))
                {
                    _settings.LocalLens = value;
                    _settings.Save();
                    GroupHeader = value == LocalLens.ByApp ? "App" : "Device";
                    ChildHeader = value == LocalLens.ByApp ? "Peers" : "Apps";
                    SelectedGroupKey = null;
                    _ = LoadAsync(true);
                }

            }
        }

        private string _groupHeader = "App";

        public string GroupHeader
        {
            get => _groupHeader;
            set => SetProperty(ref _groupHeader, value);
        }

        private string _childHeader = "Peers";

        public string ChildHeader
        {
            get => _childHeader;
            set => SetProperty(ref _childHeader, value);
        }

        private string? _selectedGroupKey;

        public string? SelectedGroupKey
        {
            get => _selectedGroupKey;
            set => SetProperty(ref _selectedGroupKey, value);
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

        private ObservableCollection<LocalTrafficGroupRow> _groups = [];

        public ObservableCollection<LocalTrafficGroupRow> Groups
        {
            get => _groups;
            set => SetProperty(ref _groups, value);
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
                string? selectedGroupKey = SelectedGroupKey;
                DateTime? selectedBucketStart = SelectedBucketStart;

                LocalLoadResult result = await Task.Run(() => BuildDataAsync(timeRangeHours, selectedGroupKey, selectedBucketStart));

                ChartPoints = new ObservableCollection<ChartPoint>(result.ChartPoints);
                SeedWindowState(result);

                if (refreshList)
                {
                    ApplyGroups(result.Groups, true);
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

            AccumulateRateWindows(deltas);

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

            ApplyRates();
        }

        private void SeedWindowState(LocalLoadResult result)
        {
            _windowCutoffEpoch = result.CutoffEpoch;
            _windowBucketSeconds = result.BucketSeconds;
            _windowChartPoints = new List<ChartPoint>(result.ChartPoints);
            _windowFlows = new Dictionary<(string ProcessName, string RemoteIp, int Protocol, int RemotePort), (long Upload, long Download)>();

            foreach (LocalFlowMinute minute in result.Minutes)
            {
                (string ProcessName, string RemoteIp, int Protocol, int RemotePort) key = (minute.ProcessName, minute.RemoteIp, minute.Protocol, minute.RemotePort);
                _windowFlows.TryGetValue(key, out (long Upload, long Download) current);
                _windowFlows[key] = (current.Upload + minute.BytesUploaded, current.Download + minute.BytesDownloaded);
            }

        }

        private void ApplyFlushToWindow(IReadOnlyList<LocalTrafficDelta> deltas)
        {
            string? selectedKey = SelectedGroupKey;
            LocalLens lens = Lens;
            long chartDeltaUpload = 0;
            long chartDeltaDownload = 0;

            foreach (LocalTrafficDelta delta in deltas)
            {
                FlowClassification classification = LocalFlowClassifier.Classify(delta.Protocol, delta.RemotePort);

                if (classification.Category == FlowCategory.Data)
                {
                    string groupKey = lens == LocalLens.ByApp ? delta.ProcessName : delta.RemoteIp;

                    if (selectedKey is null || groupKey == selectedKey)
                    {
                        chartDeltaUpload += delta.BytesUploaded;
                        chartDeltaDownload += delta.BytesDownloaded;
                    }

                }

                (string ProcessName, string RemoteIp, int Protocol, int RemotePort) key = (delta.ProcessName, delta.RemoteIp, delta.Protocol, delta.RemotePort);
                _windowFlows.TryGetValue(key, out (long Upload, long Download) current);
                _windowFlows[key] = (current.Upload + delta.BytesUploaded, current.Download + delta.BytesDownloaded);
            }

            int lastIndex = _windowChartPoints.Count - 1;
            ChartPoint last = _windowChartPoints[lastIndex];
            _windowChartPoints[lastIndex] = last with
            {
                BytesUploaded = last.BytesUploaded + chartDeltaUpload,
                BytesDownloaded = last.BytesDownloaded + chartDeltaDownload
            };

            ChartPoints = new ObservableCollection<ChartPoint>(_windowChartPoints);

            RebuildGroups();
        }

        private void RebuildGroups()
        {
            List<LocalFlowMinute> minutes = new List<LocalFlowMinute>(_windowFlows.Count);

            foreach (KeyValuePair<(string ProcessName, string RemoteIp, int Protocol, int RemotePort), (long Upload, long Download)> flow in _windowFlows)
            {
                minutes.Add(new LocalFlowMinute(flow.Key.ProcessName, flow.Key.RemoteIp, flow.Key.Protocol, flow.Key.RemotePort, flow.Value.Upload, flow.Value.Download));
            }

            IReadOnlyList<LocalTrafficGroupRow> groups = LocalTrafficGrouper.Build(minutes, _namesByIp, Lens);
            int normalCount = 0;

            foreach (LocalTrafficGroupRow row in groups)
            {

                if (row.Kind == GroupKind.Normal)
                {
                    normalCount++;
                }

            }

            long totalBytes = groups.Count > 0 ? groups[0].TotalBytes : 0;
            string unit = Lens == LocalLens.ByApp ? "app" : "device";
            string statusText = $"{normalCount} {unit}{(normalCount == 1 ? string.Empty : "s")} · {ByteSizeFormatter.Format(totalBytes)} total";

            ApplyGroups(groups, false);
            StatusText = statusText;
        }

        private void AccumulateRateWindows(IReadOnlyList<LocalTrafficDelta> deltas)
        {
            LocalLens lens = Lens;
            Dictionary<string, long> flushByGroup = new Dictionary<string, long>();
            long flushTotal = 0;

            foreach (LocalTrafficDelta delta in deltas)
            {
                FlowClassification classification = LocalFlowClassifier.Classify(delta.Protocol, delta.RemotePort);

                if (classification.Category == FlowCategory.Data)
                {
                    string groupKey = lens == LocalLens.ByApp ? delta.ProcessName : delta.RemoteIp;
                    long bytes = delta.BytesUploaded + delta.BytesDownloaded;

                    flushByGroup.TryGetValue(groupKey, out long groupBytes);
                    flushByGroup[groupKey] = groupBytes + bytes;
                    flushTotal += bytes;
                }

            }

            flushByGroup[AllRateKey] = flushTotal;
            UpdateRateWindows(flushByGroup);
        }

        private void UpdateRateWindows(IReadOnlyDictionary<string, long> flushByGroup)
        {
            HashSet<string> keys = new HashSet<string>(_rateWindows.Keys);

            foreach (string key in flushByGroup.Keys)
            {
                keys.Add(key);
            }

            foreach (string key in keys)
            {
                flushByGroup.TryGetValue(key, out long bytes);

                if (!_rateWindows.TryGetValue(key, out Queue<long>? window))
                {
                    window = new Queue<long>();
                    _rateWindows[key] = window;
                }

                window.Enqueue(bytes);

                while (window.Count > RateSampleCount)
                {
                    window.Dequeue();
                }

                if (window.Count == RateSampleCount && window.Sum() == 0)
                {
                    _rateWindows.Remove(key);
                }

            }

        }

        private void ApplyRates()
        {
            double intervalSeconds = Math.Max(1.0, _settings.TrafficIntervalSeconds);

            foreach (LocalTrafficGroupRow row in _groups)
            {
                double rate = 0.0;

                if (row.Kind != GroupKind.Background)
                {
                    string rateKey = row.IsAll ? AllRateKey : row.Key ?? string.Empty;

                    if (_rateWindows.TryGetValue(rateKey, out Queue<long>? window) && window.Count > 0)
                    {
                        rate = window.Average() / intervalSeconds;
                    }

                }

                row.RateBytesPerSec = rate;
            }

        }

        public void SetRatesActive(bool active)
        {

            if (!active)
            {
                _rateWindows.Clear();

                foreach (LocalTrafficGroupRow row in _groups)
                {
                    row.RateBytesPerSec = 0.0;
                }

            }

        }

        private void ApplyGroups(IReadOnlyList<LocalTrafficGroupRow> incoming, bool reorder)
        {
            HashSet<string> incomingIdentities = new HashSet<string>();

            foreach (LocalTrafficGroupRow row in incoming)
            {
                incomingIdentities.Add(GroupIdentity(row));
            }

            for (int index = _groups.Count - 1; index >= 0; index--)
            {

                if (!incomingIdentities.Contains(GroupIdentity(_groups[index])))
                {
                    _groups.RemoveAt(index);
                }

            }

            Dictionary<string, LocalTrafficGroupRow> existingByIdentity = new Dictionary<string, LocalTrafficGroupRow>();

            foreach (LocalTrafficGroupRow row in _groups)
            {
                existingByIdentity[GroupIdentity(row)] = row;
            }

            for (int index = 0; index < incoming.Count; index++)
            {
                LocalTrafficGroupRow incomingRow = incoming[index];
                string identity = GroupIdentity(incomingRow);

                if (existingByIdentity.TryGetValue(identity, out LocalTrafficGroupRow? current))
                {
                    current.UpdateFrom(incomingRow);

                    if (reorder)
                    {
                        int currentIndex = _groups.IndexOf(current);

                        if (currentIndex != index)
                        {
                            _groups.Move(currentIndex, index);
                        }

                    }

                }
                else if (reorder)
                {
                    _groups.Insert(index, incomingRow);
                }
                else
                {
                    _groups.Add(incomingRow);
                }

            }

        }

        private static string GroupIdentity(LocalTrafficGroupRow row)
        {
            string identity = $"{(int)row.Kind}|{row.Key ?? string.Empty}";

            return identity;
        }

        private async Task<LocalLoadResult> BuildDataAsync(double timeRangeHours, string? selectedGroupKey, DateTime? selectedBucketStart)
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

            List<LocalFlowMinute> minutes = await LoadFlowMinutesAsync(db, useRollup, cutoff, cutoffEpoch, bucketRangeStart, bucketRangeEnd);
            IReadOnlyList<LocalTrafficGroupRow> groups = LocalTrafficGrouper.Build(minutes, namesByIp, Lens);

            Dictionary<int, (long Upload, long Download)> dataByBucket =
                await LoadChartBucketsAsync(db, useRollup, cutoff, cutoffEpoch, bucketSeconds, Lens, selectedGroupKey);

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

            int normalCount = 0;

            foreach (LocalTrafficGroupRow row in groups)
            {

                if (row.Kind == GroupKind.Normal)
                {
                    normalCount++;
                }

            }

            long totalBytes = groups.Count > 0 ? groups[0].TotalBytes : 0;
            string unit = Lens == LocalLens.ByApp ? "app" : "device";
            string scopeText = selectedBucketStart.HasValue
                ? $"at {selectedBucketStart.Value.ToLocalTime():dd MMM HH:mm:ss}"
                : "total";
            string statusText = $"{normalCount} {unit}{(normalCount == 1 ? string.Empty : "s")} · {ByteSizeFormatter.Format(totalBytes)} {scopeText}";

            LocalLoadResult result = new LocalLoadResult(chartPoints, groups.ToList(), minutes, statusText, cutoffEpoch, bucketSeconds);

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

        private async Task<List<LocalFlowMinute>> LoadFlowMinutesAsync(
            AppDbContext db,
            bool useRollup,
            DateTime cutoff,
            long cutoffEpoch,
            DateTime? bucketRangeStart,
            DateTime? bucketRangeEnd)
        {
            List<LocalFlowMinute> minutes = new List<LocalFlowMinute>();
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
                    SELECT ProcessName, RemoteIp, Protocol, RemotePort,
                           SUM(BytesUploaded)   AS Upload,
                           SUM(BytesDownloaded) AS Download
                    FROM {sourceTable}
                    WHERE {whereClause}
                    GROUP BY ProcessName, RemoteIp, Protocol, RemotePort
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
                        int protocol = reader.GetInt32(2);
                        int remotePort = reader.GetInt32(3);
                        long upload = reader.GetInt64(4);
                        long download = reader.GetInt64(5);
                        LocalFlowMinute minute = new LocalFlowMinute(processName, remoteIp, protocol, remotePort, upload, download);
                        minutes.Add(minute);
                    }

                }

            }

            return minutes;
        }

        private async Task<Dictionary<int, (long Upload, long Download)>> LoadChartBucketsAsync(
            AppDbContext db,
            bool useRollup,
            DateTime cutoff,
            long cutoffEpoch,
            long bucketSeconds,
            LocalLens lens,
            string? selectedGroupKey)
        {
            Dictionary<int, (long Upload, long Download)> dataByBucket = new Dictionary<int, (long Upload, long Download)>();
            string sourceTable = useRollup ? "LocalTrafficRollups" : "LocalTrafficEntries";
            string epochExpr = useRollup ? "MinuteEpoch" : "CAST(strftime('%s', Timestamp) AS INTEGER)";
            string whereClause = useRollup ? "MinuteEpoch >= $cutoffEpoch" : "Timestamp >= $cutoffTime";
            string selectionColumn = lens == LocalLens.ByApp ? "ProcessName" : "RemoteIp";

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
                        AND NOT {LocalFlowClassifier.DiscoverySqlPredicate}
                        AND ($key IS NULL OR {selectionColumn} = $key)
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

                DbParameter keyParameter = command.CreateParameter();
                keyParameter.ParameterName = "$key";
                keyParameter.Value = selectedGroupKey is null ? (object)DBNull.Value : selectedGroupKey;
                command.Parameters.Add(keyParameter);

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
