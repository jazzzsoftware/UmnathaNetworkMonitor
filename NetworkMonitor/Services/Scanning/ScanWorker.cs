using System.Net.NetworkInformation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using NetworkMonitor.Data;
using NetworkMonitor.Models;
using NetworkMonitor.Services.Common;
using NetworkMonitor.Services.Platform;

namespace NetworkMonitor.Services.Scanning
{
    public class ScanWorker(
        NetworkScanner scanner,
        DeviceTracker tracker,
        Settings settings,
        IDbContextFactory<AppDbContext> dbFactory) : BackgroundService
    {
        private static readonly TimeSpan NetworkChangeDebounce = TimeSpan.FromSeconds(5);

        private readonly SemaphoreSlim _scanGate = new(1, 1);
        private CancellationTokenSource? _networkChangeCts;
        private CancellationToken _stoppingToken;

        public event EventHandler<ScanCompletedEventArgs>? ScanCompleted;
        public event EventHandler<DeviceStatusChangedEventArgs>? DeviceStatusChanged;
        public event EventHandler<NetworkChangedEventArgs>? NetworkChanged;

        public TimeSpan ScanTimeout
        {
            get;
            set;
        } = TimeSpan.FromMinutes(2);

        public async Task ScanNowAsync(CancellationToken ct = default)
        {
            await RunScanAsync(ct, true);
        }

        protected override async Task ExecuteAsync(CancellationToken ct)
        {
            _stoppingToken = ct;
            NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;

            try
            {
                await Task.WhenAll(RunScanLoopAsync(ct), RunPurgeLoopAsync(ct));
            }
            finally
            {
                NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;
            }

        }

        public override void Dispose()
        {
            _networkChangeCts?.Cancel();
            _networkChangeCts?.Dispose();
            _scanGate.Dispose();

            base.Dispose();
        }

        private async Task RunScanLoopAsync(CancellationToken ct)
        {

            try
            {
                await RunScanAsync(ct);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                AppLog.Error("ScanWorker.RunScanLoop", exception);
            }

            while (!ct.IsCancellationRequested)
            {

                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(settings.IntervalMinutes), ct);
                    await RunScanAsync(ct);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception exception)
                {
                    AppLog.Error("ScanWorker.RunScanLoop", exception);
                }

            }

        }

        private async Task RunPurgeLoopAsync(CancellationToken ct)
        {

            try
            {
                await PurgeOldHistoryAsync(ct);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                AppLog.Error("ScanWorker.RunPurgeLoop", exception);
            }

            while (!ct.IsCancellationRequested)
            {

                try
                {
                    await Task.Delay(TimeSpan.FromHours(24), ct);
                    await PurgeOldHistoryAsync(ct);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception exception)
                {
                    AppLog.Error("ScanWorker.RunPurgeLoop", exception);
                }

            }

        }

        private async Task PurgeOldHistoryAsync(CancellationToken ct)
        {
            await using AppDbContext db = await dbFactory.CreateDbContextAsync(ct);

            if (settings.TrafficPurgeDays > 0)
            {
                DateTime trafficCutoff = DateTime.UtcNow.AddDays(-settings.TrafficPurgeDays);

                await db.TrafficEntries
                    .Where(entry => entry.Timestamp < trafficCutoff)
                    .ExecuteDeleteAsync(ct);

                long rollupCutoffEpoch = (long)(trafficCutoff - DateTime.UnixEpoch).TotalSeconds;

                await db.Database.ExecuteSqlRawAsync(
                    "DELETE FROM TrafficRollups WHERE MinuteEpoch < {0}",
                    new object[] { rollupCutoffEpoch },
                    ct);

                await db.Database.ExecuteSqlRawAsync(
                    "DELETE FROM LocalTrafficRollups WHERE MinuteEpoch < {0}",
                    new object[] { rollupCutoffEpoch },
                    ct);

                await db.LocalTrafficEntries
                    .Where(entry => entry.Timestamp < trafficCutoff)
                    .ExecuteDeleteAsync(ct);

                await db.SpeedTestResults
                    .Where(result => result.Timestamp < trafficCutoff)
                    .ExecuteDeleteAsync(ct);
            }

            if (settings.HistoryPurgeDays > 0)
            {
                DateTime deviceCutoff = DateTime.UtcNow.AddDays(-settings.HistoryPurgeDays);
                await db.DeviceEvents
                    .Where(deviceEvent => deviceEvent.Timestamp < deviceCutoff)
                    .ExecuteDeleteAsync(ct);
                await db.ScanSessions
                    .Where(session => session.CompletedAt.HasValue && session.CompletedAt.Value < deviceCutoff)
                    .ExecuteDeleteAsync(ct);
            }

        }

        private async Task RunScanAsync(CancellationToken ct, bool isManual = false)
        {
            await _scanGate.WaitAsync(ct);

            try
            {
                await Watchdog.RunAsync(token => ExecuteScanAsync(token, isManual), ScanTimeout, ct);
            }
            catch (TimeoutException)
            {
                AppLog.Info($"Scan timed out after {ScanTimeout.TotalSeconds:0} seconds and was aborted; scanning will resume on the next cycle.");
            }
            finally
            {
                _scanGate.Release();
            }

        }

        private void OnNetworkAddressChanged(object? sender, EventArgs args)
        {

            if (settings.AutoDetectSubnet && !_stoppingToken.IsCancellationRequested)
            {
                CancellationTokenSource newCts = CancellationTokenSource.CreateLinkedTokenSource(_stoppingToken);
                CancellationTokenSource? oldCts = Interlocked.Exchange(ref _networkChangeCts, newCts);
                oldCts?.Cancel();
                oldCts?.Dispose();
                _ = ScanAfterNetworkChangeAsync(newCts.Token);
            }

        }

        private async Task ScanAfterNetworkChangeAsync(CancellationToken ct)
        {

            try
            {
                await Task.Delay(NetworkChangeDebounce, ct);
                AppLog.Info("Network address change detected; starting scan.");
                await RunScanAsync(ct);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                AppLog.Error("ScanWorker.ScanAfterNetworkChange", exception);
            }

        }

        private void RefreshSubnetBase()
        {

            if (settings.AutoDetectSubnet)
            {
                string? detected = Settings.TryDetectSubnetBase();

                if (detected is not null && detected != settings.SubnetBase)
                {
                    string oldSubnetBase = settings.SubnetBase;
                    AppLog.Info($"Network changed: subnet {oldSubnetBase} -> {detected}; scanning the new subnet.");
                    settings.SubnetBase = detected;
                    settings.Save();
                    NetworkChanged?.Invoke(this, new NetworkChangedEventArgs(oldSubnetBase, detected));
                }

            }

        }

        private async Task ExecuteScanAsync(CancellationToken ct, bool isManual)
        {
            AppLog.Info("Scan started.");

            RefreshSubnetBase();

            IReadOnlyList<ScannedDevice> results = await scanner.ScanAsync(settings, ct);

            ct.ThrowIfCancellationRequested();

            (ScanSession session, List<DeviceNotification> notifications) = await tracker.MergeAsync(results, ct);

            foreach (DeviceNotification notification in notifications)
            {
                DeviceStatusChanged?.Invoke(this, new DeviceStatusChangedEventArgs(notification));
            }

            ScanCompleted?.Invoke(this, new ScanCompletedEventArgs(session, isManual));
            AppLog.Info($"Scan completed: {session.DevicesFound} found, {session.NewDevices} new, {session.DevicesGone} gone.");
        }
    }
}
