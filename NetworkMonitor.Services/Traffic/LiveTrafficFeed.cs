using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using NetworkMonitor.Core.Traffic;
using NetworkMonitor.Models.Charting;
using NetworkMonitor.Models.SpeedTest;
using NetworkMonitor.Models.Traffic;
using NetworkMonitor.Services.Data;
using NetworkMonitor.Services.Platform;
using NetworkMonitor.Services.Scanning;
using NetworkMonitor.Services.SpeedTest;

namespace NetworkMonitor.Services.Traffic
{
    // Feeds the floating mini graph. This runs from startup whether or not the widget is open, which
    // is what lets the widget open with five minutes already drawn rather than an empty chart. It
    // costs roughly 15 KB held permanently and performs exactly two database reads, both at startup.
    //
    // A fault in here must never propagate into the flush loop or the scan loop the rest of the app
    // depends on, so every handler is wrapped.
    public sealed class LiveTrafficFeed(
        TrafficTracker tracker,
        SpeedTestWorker speedTestWorker,
        ScanWorker scanWorker,
        Settings settings,
        IDbContextFactory<AppDbContext> dbFactory) : IHostedService
    {
        private const int WindowSeconds = 300;

        private readonly TrafficTracker _tracker = tracker;
        private readonly SpeedTestWorker _speedTestWorker = speedTestWorker;
        private readonly ScanWorker _scanWorker = scanWorker;
        private readonly Settings _settings = settings;
        private readonly IDbContextFactory<AppDbContext> _dbFactory = dbFactory;
        private readonly LiveRateBuffer _wanBuffer = new LiveRateBuffer(WindowSeconds);
        private readonly LiveRateBuffer _lanBuffer = new LiveRateBuffer(WindowSeconds);
        private readonly object _gate = new object();
        private readonly CancellationTokenSource _stopping = new CancellationTokenSource();
        private DateTime _lastFlushUtc = DateTime.MinValue;

        public event EventHandler? Updated;

        private SpeedTestResult? _latestSpeedTest;

        public SpeedTestResult? LatestSpeedTest
        {
            get
            {
                SpeedTestResult? latestSpeedTest;

                lock (_gate)
                {
                    latestSpeedTest = _latestSpeedTest;
                }

                return latestSpeedTest;
            }
        }

        private int _unapprovedDeviceCount;

        public int UnapprovedDeviceCount
        {
            get
            {
                int unapprovedDeviceCount;

                lock (_gate)
                {
                    unapprovedDeviceCount = _unapprovedDeviceCount;
                }

                return unapprovedDeviceCount;
            }
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await SeedAsync(cancellationToken);

            _tracker.Flushed += OnFlushed;
            _speedTestWorker.SpeedTestCompleted += OnSpeedTestCompleted;
            _scanWorker.ScanCompleted += OnScanCompleted;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _tracker.Flushed -= OnFlushed;
            _speedTestWorker.SpeedTestCompleted -= OnSpeedTestCompleted;
            _scanWorker.ScanCompleted -= OnScanCompleted;

            _stopping.Cancel();

            Task completed = Task.CompletedTask;

            return completed;
        }

        public IReadOnlyList<ChartPoint> WanSnapshot()
        {
            IReadOnlyList<ChartPoint> points;

            lock (_gate)
            {
                points = _wanBuffer.Snapshot(DateTime.UtcNow);
            }

            return points;
        }

        public IReadOnlyList<ChartPoint> LanSnapshot()
        {
            IReadOnlyList<ChartPoint> points;

            lock (_gate)
            {
                points = _lanBuffer.Snapshot(DateTime.UtcNow);
            }

            return points;
        }

        private static async Task<int> CountUnapprovedAsync(AppDbContext db, CancellationToken cancellationToken)
        {
            DateTime cutoff = DateTime.UtcNow.AddHours(-24);

            int count = await db.Devices
                .AsNoTracking()
                .CountAsync(device => !device.IsApproved && (device.IsOnline || device.LastSeen >= cutoff), cancellationToken);

            return count;
        }

        private async Task SeedAsync(CancellationToken cancellationToken)
        {

            try
            {
                await using AppDbContext db = await _dbFactory.CreateDbContextAsync(cancellationToken);

                SpeedTestResult? latestSpeedTest = await db.SpeedTestResults
                    .AsNoTracking()
                    .Where(result => result.Success)
                    .OrderByDescending(result => result.Timestamp)
                    .FirstOrDefaultAsync(cancellationToken);

                int unapprovedDeviceCount = await CountUnapprovedAsync(db, cancellationToken);

                lock (_gate)
                {
                    _latestSpeedTest = latestSpeedTest;
                    _unapprovedDeviceCount = unapprovedDeviceCount;
                }

            }
            catch (Exception exception)
            {
                AppLog.Error("LiveTrafficFeed.Seed", exception);
            }

        }

        private void OnFlushed(object? sender, TrafficFlushedEventArgs args)
        {

            try
            {
                long wanDownload = 0;
                long wanUpload = 0;

                foreach (TrafficEntry entry in args.Entries)
                {

                    // The Internet tab hides System, so including it here would put the widget and the
                    // tab permanently out of step.
                    if (entry.ProcessName == "System")
                    {
                        continue;
                    }

                    wanDownload += entry.BytesDownloaded;
                    wanUpload += entry.BytesUploaded;
                }

                long lanDownload = 0;
                long lanUpload = 0;

                foreach (LocalTrafficDelta delta in args.LocalDeltas)
                {

                    // The Local tab's chart excludes discovery traffic (NOT DiscoverySqlPredicate) and
                    // so must this one. mDNS, SSDP, NetBIOS and DHCP tick over on every device on the
                    // segment, so counting them here drew a dense sawtooth in the widget beside a
                    // near-flat line on the tab — the same two minutes of the same network.
                    FlowClassification classification = LocalFlowClassifier.Classify(delta.Protocol, delta.RemotePort);

                    if (classification.Category != FlowCategory.Data)
                    {
                        continue;
                    }

                    lanDownload += delta.BytesDownloaded;
                    lanUpload += delta.BytesUploaded;
                }

                DateTime nowUtc = DateTime.UtcNow;
                DateTime intervalStartUtc = _lastFlushUtc == DateTime.MinValue
                    ? nowUtc.AddSeconds(-Math.Max(1, _settings.TrafficIntervalSeconds))
                    : _lastFlushUtc;
                _lastFlushUtc = nowUtc;

                lock (_gate)
                {
                    _wanBuffer.AddInterval(intervalStartUtc, nowUtc, wanDownload, wanUpload);
                    _lanBuffer.AddInterval(intervalStartUtc, nowUtc, lanDownload, lanUpload);
                }

                Updated?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception exception)
            {
                AppLog.Error("LiveTrafficFeed.OnFlushed", exception);
            }

        }

        private void OnSpeedTestCompleted(object? sender, SpeedTestCompletedEventArgs args)
        {

            try
            {

                if (args.Result.Success)
                {

                    lock (_gate)
                    {
                        _latestSpeedTest = args.Result;
                    }

                    Updated?.Invoke(this, EventArgs.Empty);
                }

            }
            catch (Exception exception)
            {
                AppLog.Error("LiveTrafficFeed.OnSpeedTestCompleted", exception);
            }

        }

        private void OnScanCompleted(object? sender, ScanCompletedEventArgs args)
        {
            _ = RefreshUnapprovedCountAsync();
        }

        private async Task RefreshUnapprovedCountAsync()
        {

            try
            {
                CancellationToken cancellationToken = _stopping.Token;

                await using AppDbContext db = await _dbFactory.CreateDbContextAsync(cancellationToken);
                int count = await CountUnapprovedAsync(db, cancellationToken);

                lock (_gate)
                {
                    _unapprovedDeviceCount = count;
                }

                Updated?.Invoke(this, EventArgs.Empty);
            }
            catch (OperationCanceledException)
            {
                // A scan landing during shutdown. Expected, and previously logged as an error every
                // time it happened because the read raced the context factory's disposal.
            }
            catch (ObjectDisposedException)
            {
            }
            catch (Exception exception)
            {
                AppLog.Error("LiveTrafficFeed.RefreshUnapprovedCount", exception);
            }

        }

    }
}
