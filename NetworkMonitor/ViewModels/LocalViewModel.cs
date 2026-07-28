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

        // Fixed query shapes selected by an if, rather than a command string assembled per call.
        // Every part that used to be interpolated is an internal constant, so there is no injection
        // today; keeping the pattern out of the file is what stops a user-derived value reaching a
        // command string later. The optional bucket-range predicates are expressed as nullable
        // parameters, the same idiom the selection key already uses.
        private const string FlowBucketsRollupSql = """
            SELECT CAST((MinuteEpoch - $cutoffEpoch) / $bucketSeconds AS INTEGER) AS BucketIndex,
                   ProcessName, RemoteIp, Protocol, RemotePort,
                   SUM(BytesUploaded)   AS Upload,
                   SUM(BytesDownloaded) AS Download
            FROM LocalTrafficRollups
            WHERE MinuteEpoch >= $cutoffEpoch
              AND ($bucketStartEpoch IS NULL OR MinuteEpoch >= $bucketStartEpoch)
              AND ($bucketEndEpoch IS NULL OR MinuteEpoch < $bucketEndEpoch)
            GROUP BY BucketIndex, ProcessName, RemoteIp, Protocol, RemotePort
            """;

        private const string FlowBucketsEntriesSql = """
            SELECT CAST((CAST(strftime('%s', Timestamp) AS INTEGER) - $cutoffEpoch) / $bucketSeconds AS INTEGER) AS BucketIndex,
                   ProcessName, RemoteIp, Protocol, RemotePort,
                   SUM(BytesUploaded)   AS Upload,
                   SUM(BytesDownloaded) AS Download
            FROM LocalTrafficEntries
            WHERE Timestamp >= $cutoffTime
              AND ($bucketStartTime IS NULL OR Timestamp >= $bucketStartTime)
              AND ($bucketEndTime IS NULL OR Timestamp < $bucketEndTime)
            GROUP BY BucketIndex, ProcessName, RemoteIp, Protocol, RemotePort
            """;

        // The discovery predicate is composed from Core's port list, so the chart shapes are built
        // once at type initialisation instead of being declared const.
        private static readonly string ChartBucketsRollupByAppSql = $"""
            SELECT
                CAST((MinuteEpoch - $cutoffEpoch) / $bucketSeconds AS INTEGER) AS BucketIndex,
                SUM(BytesUploaded)   AS Upload,
                SUM(BytesDownloaded) AS Download
            FROM LocalTrafficRollups
            WHERE MinuteEpoch >= $cutoffEpoch
                AND NOT {LocalFlowClassifier.DiscoverySqlPredicate}
                AND ($key IS NULL OR ProcessName = $key)
            GROUP BY BucketIndex
            """;

        private static readonly string ChartBucketsRollupByDeviceSql = $"""
            SELECT
                CAST((MinuteEpoch - $cutoffEpoch) / $bucketSeconds AS INTEGER) AS BucketIndex,
                SUM(BytesUploaded)   AS Upload,
                SUM(BytesDownloaded) AS Download
            FROM LocalTrafficRollups
            WHERE MinuteEpoch >= $cutoffEpoch
                AND NOT {LocalFlowClassifier.DiscoverySqlPredicate}
                AND ($key IS NULL OR RemoteIp = $key)
            GROUP BY BucketIndex
            """;

        private static readonly string ChartBucketsEntriesByAppSql = $"""
            SELECT
                CAST((CAST(strftime('%s', Timestamp) AS INTEGER) - $cutoffEpoch) / $bucketSeconds AS INTEGER) AS BucketIndex,
                SUM(BytesUploaded)   AS Upload,
                SUM(BytesDownloaded) AS Download
            FROM LocalTrafficEntries
            WHERE Timestamp >= $cutoffTime
                AND NOT {LocalFlowClassifier.DiscoverySqlPredicate}
                AND ($key IS NULL OR ProcessName = $key)
            GROUP BY BucketIndex
            """;

        private static readonly string ChartBucketsEntriesByDeviceSql = $"""
            SELECT
                CAST((CAST(strftime('%s', Timestamp) AS INTEGER) - $cutoffEpoch) / $bucketSeconds AS INTEGER) AS BucketIndex,
                SUM(BytesUploaded)   AS Upload,
                SUM(BytesDownloaded) AS Download
            FROM LocalTrafficEntries
            WHERE Timestamp >= $cutoffTime
                AND NOT {LocalFlowClassifier.DiscoverySqlPredicate}
                AND ($key IS NULL OR RemoteIp = $key)
            GROUP BY BucketIndex
            """;

        private static readonly TimeSpan NameMapLifetime = TimeSpan.FromSeconds(60);

        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly Settings _settings;
        private readonly SemaphoreSlim _loadGate = new SemaphoreSlim(1, 1);
        private long _windowCutoffEpoch;
        private long _windowBucketSeconds;
        private List<ChartPoint> _windowChartPoints = [];
        private List<Dictionary<LocalFlowIdentity, LocalFlowTotals>> _windowFlowBuckets = new();
        private DateTime _lastFlushUtc = DateTime.MinValue;
        private Dictionary<LocalFlowIdentity, LocalFlowTotals> _windowFlows = new();
        private readonly Dictionary<string, RateWindow> _rateWindows = new();
        private Dictionary<string, string> _namesByIp = new();
        private DateTime _namesLoadedUtc = DateTime.MinValue;

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

            // Serialised: a live tick and an explicit reload overlapping would let the slower one
            // finish last and re-seed the window from a cutoff that is no longer on screen.
            await _loadGate.WaitAsync();

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
                _loadGate.Release();

                if (showLoading)
                {
                    IsLoading = false;
                }

            }

        }

        public async Task ApplyLiveFlushAsync(IReadOnlyList<LocalTrafficDelta> deltas)
        {

            AccumulateRateWindows(deltas);

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
                    ApplyFlushToWindow(deltas, intervalStartUtc, nowUtc);
                }
                else if (bucketsAdvanced > 0 && bucketsAdvanced < _windowChartPoints.Count)
                {
                    ShiftWindow(cutoffEpoch, (int)bucketsAdvanced);
                    ApplyFlushToWindow(deltas, intervalStartUtc, nowUtc);
                }
                else
                {
                    await LoadAsync();
                }

            }

            ApplyRates();
        }

        private void SeedWindowState(LocalLoadResult result)
        {
            _windowCutoffEpoch = result.CutoffEpoch;
            _windowBucketSeconds = result.BucketSeconds;
            _windowChartPoints = new List<ChartPoint>(result.ChartPoints);
            _windowFlowBuckets = result.FlowBuckets;
            _windowFlows = new Dictionary<LocalFlowIdentity, LocalFlowTotals>();

            foreach (LocalFlowMinute minute in result.Minutes)
            {
                LocalFlowIdentity key = new LocalFlowIdentity(minute.ProcessName, minute.RemoteIp, minute.Protocol, minute.RemotePort);
                _windowFlows.TryGetValue(key, out LocalFlowTotals current);
                _windowFlows[key] = new LocalFlowTotals(current.Upload + minute.BytesUploaded, current.Download + minute.BytesDownloaded);
            }

        }

        private void ShiftWindow(long cutoffEpoch, int bucketsAdvanced)
        {
            int totalBuckets = _windowChartPoints.Count;

            for (int shift = 0; shift < bucketsAdvanced; shift++)
            {
                Dictionary<LocalFlowIdentity, LocalFlowTotals> evicted = _windowFlowBuckets[0];

                foreach (KeyValuePair<LocalFlowIdentity, LocalFlowTotals> flow in evicted)
                {

                    if (_windowFlows.TryGetValue(flow.Key, out LocalFlowTotals current))
                    {
                        long upload = current.Upload - flow.Value.Upload;
                        long download = current.Download - flow.Value.Download;

                        if (upload > 0 || download > 0)
                        {
                            _windowFlows[flow.Key] = new LocalFlowTotals(Math.Max(0, upload), Math.Max(0, download));
                        }
                        else
                        {
                            _windowFlows.Remove(flow.Key);
                        }

                    }

                }

                _windowFlowBuckets.RemoveAt(0);
                _windowChartPoints.RemoveAt(0);
                _windowFlowBuckets.Add(new Dictionary<LocalFlowIdentity, LocalFlowTotals>());

                int appendedIndex = totalBuckets - bucketsAdvanced + shift;
                DateTime bucketStart = DateTime.UnixEpoch.AddSeconds(cutoffEpoch + (long)appendedIndex * _windowBucketSeconds);
                _windowChartPoints.Add(new ChartPoint(bucketStart, 0, 0));
            }

            _windowCutoffEpoch = cutoffEpoch;
        }

        private void SpreadAcrossBuckets(long upload, long download, DateTime intervalStartUtc, DateTime intervalEndUtc)
        {
            List<DateTime> bucketStarts = new List<DateTime>(_windowChartPoints.Count);

            foreach (ChartPoint point in _windowChartPoints)
            {
                bucketStarts.Add(point.BucketStart);
            }

            long[] uploadShares = FlushSpread.Distribute(upload, bucketStarts, _windowBucketSeconds, intervalStartUtc, intervalEndUtc);
            long[] downloadShares = FlushSpread.Distribute(download, bucketStarts, _windowBucketSeconds, intervalStartUtc, intervalEndUtc);

            for (int index = 0; index < _windowChartPoints.Count; index++)
            {

                if (uploadShares[index] != 0 || downloadShares[index] != 0)
                {
                    ChartPoint point = _windowChartPoints[index];
                    _windowChartPoints[index] = point with
                    {
                        BytesUploaded = point.BytesUploaded + uploadShares[index],
                        BytesDownloaded = point.BytesDownloaded + downloadShares[index]
                    };
                }

            }

        }

        private void ApplyFlushToWindow(IReadOnlyList<LocalTrafficDelta> deltas, DateTime intervalStartUtc, DateTime intervalEndUtc)
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

                LocalFlowIdentity key = new LocalFlowIdentity(delta.ProcessName, delta.RemoteIp, delta.Protocol, delta.RemotePort);
                _windowFlows.TryGetValue(key, out LocalFlowTotals current);
                _windowFlows[key] = new LocalFlowTotals(current.Upload + delta.BytesUploaded, current.Download + delta.BytesDownloaded);

                if (_windowFlowBuckets.Count > 0)
                {
                    Dictionary<LocalFlowIdentity, LocalFlowTotals> newest = _windowFlowBuckets[_windowFlowBuckets.Count - 1];
                    newest.TryGetValue(key, out LocalFlowTotals bucketTotals);
                    newest[key] = new LocalFlowTotals(bucketTotals.Upload + delta.BytesUploaded, bucketTotals.Download + delta.BytesDownloaded);
                }

            }

            SpreadAcrossBuckets(chartDeltaUpload, chartDeltaDownload, intervalStartUtc, intervalEndUtc);

            ChartPoints = new ObservableCollection<ChartPoint>(_windowChartPoints);

            RebuildGroups();
        }

        private void RebuildGroups()
        {
            List<LocalFlowMinute> minutes = new List<LocalFlowMinute>(_windowFlows.Count);

            foreach (KeyValuePair<LocalFlowIdentity, LocalFlowTotals> flow in _windowFlows)
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

            string statusText = StatusTextFor(groups, normalCount, null);

            // Freezing the order on every live tick kept the grid steady under an open drill-down,
            // but it also stripped the ordering of meaning: a row that starts talking later stays
            // wherever it was first inserted, so a NAS pulling gigabytes sits below an idle phone.
            // Re-sort while the user is just watching the list; hold the order once they drill in.
            bool reorder = SelectedGroupKey is null;

            ApplyGroups(groups, reorder);
            StatusText = statusText;
        }

        private string StatusTextFor(IReadOnlyList<LocalTrafficGroupRow> groups, int normalCount, DateTime? selectedBucketStart)
        {
            long totalBytes = groups.Count > 0 ? groups[0].TotalBytes : 0;
            string unit = Lens == LocalLens.ByApp ? "app" : "device";
            string scopeText = selectedBucketStart.HasValue
                ? $"at {selectedBucketStart.Value.ToLocalTime():dd MMM HH:mm:ss}"
                : "total";
            string statusText = $"{normalCount} {unit}{(normalCount == 1 ? string.Empty : "s")} · {ByteSizeFormatter.Format(totalBytes)} {scopeText}";

            return statusText;
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

        private void ApplyRates()
        {
            double intervalSeconds = Math.Max(1.0, _settings.TrafficIntervalSeconds);

            foreach (LocalTrafficGroupRow row in _groups)
            {
                double rate = 0.0;

                if (row.Kind != GroupKind.Background)
                {
                    string rateKey = row.IsAll ? AllRateKey : row.Key ?? string.Empty;

                    if (_rateWindows.TryGetValue(rateKey, out RateWindow? window) && window.Count > 0)
                    {
                        rate = window.Average / intervalSeconds;
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

                    // A live refresh must not re-sort the grid under an open drill-down, but it
                    // still has to show apps and devices that only started talking just now:
                    // append them in place, ahead of the trailing discovery row.
                    _groups.Insert(InsertPositionFor(incomingRow), incomingRow);
                }

            }

        }

        private int InsertPositionFor(LocalTrafficGroupRow incomingRow)
        {
            int position = _groups.Count;

            if (incomingRow.Kind != GroupKind.Background)
            {

                for (int index = 0; index < _groups.Count; index++)
                {

                    if (_groups[index].Kind == GroupKind.Background)
                    {
                        position = index;

                        break;
                    }

                }

            }

            return position;
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

            List<Dictionary<LocalFlowIdentity, LocalFlowTotals>> flowBuckets = await LoadFlowBucketsAsync(
                db,
                useRollup,
                cutoff,
                cutoffEpoch,
                bucketSeconds,
                totalBuckets,
                bucketRangeStart,
                bucketRangeEnd);

            List<LocalFlowMinute> minutes = AggregateFlows(flowBuckets);
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

            string statusText = StatusTextFor(groups, normalCount, selectedBucketStart);

            LocalLoadResult result = new LocalLoadResult(chartPoints, groups.ToList(), minutes, flowBuckets, statusText, cutoffEpoch, bucketSeconds);

            return result;
        }

        private static List<LocalFlowMinute> AggregateFlows(List<Dictionary<LocalFlowIdentity, LocalFlowTotals>> flowBuckets)
        {
            Dictionary<LocalFlowIdentity, LocalFlowTotals> totals = new Dictionary<LocalFlowIdentity, LocalFlowTotals>();

            foreach (Dictionary<LocalFlowIdentity, LocalFlowTotals> bucket in flowBuckets)
            {

                foreach (KeyValuePair<LocalFlowIdentity, LocalFlowTotals> flow in bucket)
                {
                    totals.TryGetValue(flow.Key, out LocalFlowTotals current);
                    totals[flow.Key] = new LocalFlowTotals(current.Upload + flow.Value.Upload, current.Download + flow.Value.Download);
                }

            }

            List<LocalFlowMinute> minutes = new List<LocalFlowMinute>(totals.Count);

            foreach (KeyValuePair<LocalFlowIdentity, LocalFlowTotals> flow in totals)
            {
                minutes.Add(new LocalFlowMinute(flow.Key.ProcessName, flow.Key.RemoteIp, flow.Key.Protocol, flow.Key.RemotePort, flow.Value.Upload, flow.Value.Download));
            }

            return minutes;
        }

        private async Task<Dictionary<string, string>> BuildNameMapAsync(AppDbContext db)
        {
            DateTime nowUtc = DateTime.UtcNow;
            Dictionary<string, string> namesByIp = _namesByIp;

            // The map only changes when a scan finishes (every few minutes); re-reading the whole
            // device table on each live tick was the most expensive part of a refresh.
            if (namesByIp.Count == 0 || nowUtc - _namesLoadedUtc >= NameMapLifetime)
            {
                namesByIp = new Dictionary<string, string>();

                List<Device> devices = await db.Devices.AsNoTracking().ToListAsync();

                foreach (Device device in devices)
                {

                    if (!string.IsNullOrWhiteSpace(device.IpAddress))
                    {
                        namesByIp[device.IpAddress] = device.DisplayName;
                    }

                }

                _namesLoadedUtc = nowUtc;
            }

            return namesByIp;
        }

        private async Task<List<Dictionary<LocalFlowIdentity, LocalFlowTotals>>> LoadFlowBucketsAsync(
            AppDbContext db,
            bool useRollup,
            DateTime cutoff,
            long cutoffEpoch,
            long bucketSeconds,
            int totalBuckets,
            DateTime? bucketRangeStart,
            DateTime? bucketRangeEnd)
        {
            List<Dictionary<LocalFlowIdentity, LocalFlowTotals>> buckets = new List<Dictionary<LocalFlowIdentity, LocalFlowTotals>>(totalBuckets);

            for (int index = 0; index < totalBuckets; index++)
            {
                buckets.Add(new Dictionary<LocalFlowIdentity, LocalFlowTotals>());
            }

            await db.Database.OpenConnectionAsync();

            DbConnection connection = db.Database.GetDbConnection();

            await using (DbCommand command = connection.CreateCommand())
            {
                command.CommandText = useRollup ? FlowBucketsRollupSql : FlowBucketsEntriesSql;

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
                        string remoteIp = reader.GetString(2);
                        int protocol = reader.GetInt32(3);
                        int remotePort = reader.GetInt32(4);
                        long upload = reader.GetInt64(5);
                        long download = reader.GetInt64(6);

                        // A row written between the cutoff calculation and this query can land one
                        // bucket past the end; clamp rather than drop, so no bytes go missing.
                        int slot = Math.Clamp(bucketIndex, 0, totalBuckets - 1);
                        LocalFlowIdentity key = new LocalFlowIdentity(processName, remoteIp, protocol, remotePort);
                        Dictionary<LocalFlowIdentity, LocalFlowTotals> bucket = buckets[slot];

                        bucket.TryGetValue(key, out LocalFlowTotals current);
                        bucket[key] = new LocalFlowTotals(current.Upload + upload, current.Download + download);
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
            LocalLens lens,
            string? selectedGroupKey)
        {
            Dictionary<int, (long Upload, long Download)> dataByBucket = new Dictionary<int, (long Upload, long Download)>();
            string commandText;

            if (useRollup)
            {
                commandText = lens == LocalLens.ByApp ? ChartBucketsRollupByAppSql : ChartBucketsRollupByDeviceSql;
            }
            else
            {
                commandText = lens == LocalLens.ByApp ? ChartBucketsEntriesByAppSql : ChartBucketsEntriesByDeviceSql;
            }

            await db.Database.OpenConnectionAsync();

            DbConnection connection = db.Database.GetDbConnection();

            await using (DbCommand command = connection.CreateCommand())
            {
                command.CommandText = commandText;

                AddParameter(command, "$cutoffEpoch", cutoffEpoch);
                AddParameter(command, "$bucketSeconds", bucketSeconds);
                AddParameter(command, "$key", selectedGroupKey is null ? (object)DBNull.Value : selectedGroupKey);

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

        private static void AddParameter(DbCommand command, string name, object value)
        {
            DbParameter parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;

            command.Parameters.Add(parameter);
        }
    }
}
