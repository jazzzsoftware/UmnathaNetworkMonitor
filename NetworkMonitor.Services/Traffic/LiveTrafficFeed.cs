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
            // StartAsync is awaited from OnLaunched, and Microsoft.Data.Sqlite is synchronous
            // underneath, so seeding inline ran both queries on the UI thread and blocked every later
            // hosted service plus MainWindow creation. CountUnapprovedAsync scans Devices, which
            // grows with the device list.
            await Task.Run(() => SeedAsync(cancellationToken), cancellationToken);

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
                // The two filters that keep these totals in step with the Internet and Local tabs now
                // live in Core, where they are tested. See WidgetTrafficTotals.
                TrafficTotals wan = WidgetTrafficTotals.Wan(args.Entries);
                TrafficTotals lan = WidgetTrafficTotals.Lan(args.LocalDeltas);

                long wanDownload = wan.BytesDownloaded;
                long wanUpload = wan.BytesUploaded;
                long lanDownload = lan.BytesDownloaded;
                long lanUpload = lan.BytesUploaded;

                DateTime nowUtc = DateTime.UtcNow;

                lock (_gate)
                {

                    // The clock went backwards between flushes. Feeding an inverted interval to
                    // AddInterval falls through to a single Add on a bucket that still holds
                    // pre-jump bytes, so this flush is dropped and the baseline restarts from here.
                    if (nowUtc < _lastFlushUtc)
                    {
                        _lastFlushUtc = nowUtc;
                    }
                    else
                    {
                        DateTime intervalStartUtc = _lastFlushUtc == DateTime.MinValue
                            ? nowUtc.AddSeconds(-Math.Max(1, _settings.TrafficIntervalSeconds))
                            : _lastFlushUtc;
                        _lastFlushUtc = nowUtc;

                        _wanBuffer.AddInterval(intervalStartUtc, nowUtc, wanDownload, wanUpload);
                        _lanBuffer.AddInterval(intervalStartUtc, nowUtc, lanDownload, lanUpload);
                    }

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
