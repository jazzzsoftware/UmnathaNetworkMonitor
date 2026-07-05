using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using NetworkMonitor.Data;
using NetworkMonitor.Models;
using NetworkMonitor.Services.Common;
using NetworkMonitor.Services.Platform;

namespace NetworkMonitor.Services.Digest
{
    public class DigestWorker(
        DigestGenerator generator,
        Settings settings,
        IDbContextFactory<AppDbContext> dbFactory) : BackgroundService
    {
        private static readonly TimeSpan CycleTimeout = TimeSpan.FromMinutes(5);

        public async Task<DigestReport> GenerateNowAsync(CancellationToken ct = default)
        {
            DateTime endUtc = DateTime.UtcNow;
            DateTime startUtc = endUtc.AddDays(-1);
            DigestReport report = await generator.GenerateAsync(startUtc, endUtc, isScheduled: false, ct);

            return report;
        }

        protected override async Task ExecuteAsync(CancellationToken ct)
        {

            try
            {
                await Watchdog.RunAsync(token => RunCycleAsync(isStartup: true, token), CycleTimeout, ct);
            }
            catch (OperationCanceledException)
            {
            }
            catch (TimeoutException)
            {
                AppLog.Info($"Digest cycle timed out after {CycleTimeout.TotalSeconds:0} seconds and was aborted; it will retry on the next cycle.");
            }
            catch (Exception exception)
            {
                AppLog.Error("DigestWorker.ExecuteAsync", exception);
            }

            while (!ct.IsCancellationRequested)
            {

                try
                {
                    DateTime now = DateTime.Now;
                    DateTime nextRunLocal = DigestSchedule.NextRunLocal(now, settings.DigestGenerationHour);
                    TimeSpan delay = nextRunLocal - now;

                    if (delay > TimeSpan.Zero)
                    {
                        await Task.Delay(delay, ct);
                    }

                    await Watchdog.RunAsync(token => RunCycleAsync(isStartup: false, token), CycleTimeout, ct);
                }
                catch (OperationCanceledException)
                {
                }
                catch (TimeoutException)
                {
                    AppLog.Info($"Digest cycle timed out after {CycleTimeout.TotalSeconds:0} seconds and was aborted; it will retry on the next cycle.");
                }
                catch (Exception exception)
                {
                    AppLog.Error("DigestWorker.ExecuteAsync", exception);
                }

            }

        }

        private async Task RunCycleAsync(bool isStartup, CancellationToken ct)
        {
            await CatchUpAsync(isStartup, ct);

            await PurgeOldReportsAsync(ct);
        }

        private async Task CatchUpAsync(bool isStartup, CancellationToken ct)
        {

            if (isStartup)
            {
                bool hasAnyReport = await HasAnyReportAsync(ct);

                if (!hasAnyReport)
                {
                    DateTime nowLocal = DateTime.Now;
                    DateTime todayRunLocal = new DateTime(nowLocal.Year, nowLocal.Month, nowLocal.Day, settings.DigestGenerationHour, 0, 0, DateTimeKind.Local);
                    DateTime boundaryLocal = nowLocal >= todayRunLocal ? todayRunLocal : todayRunLocal.AddDays(-1);
                    DateTime startUtc = boundaryLocal.ToUniversalTime();
                    DateTime endUtc = DateTime.UtcNow;
                    bool hasData = await HasDataAsync(startUtc, endUtc, ct);

                    if (hasData)
                    {
                        await generator.GenerateAsync(startUtc, endUtc, isScheduled: false, ct);
                    }

                }

            }
            else
            {
                DateTime? lastEndUtc = await GetLastPeriodEndUtcAsync(ct);
                List<(DateTime StartUtc, DateTime EndUtc)> windows = DigestSchedule.MissedWindows(
                    lastEndUtc, DateTime.Now, settings.DigestGenerationHour, settings.DigestPurgeDays);

                foreach ((DateTime StartUtc, DateTime EndUtc) window in windows)
                {
                    await generator.GenerateAsync(window.StartUtc, window.EndUtc, isScheduled: true, ct);
                }

            }

        }

        private async Task<bool> HasAnyReportAsync(CancellationToken ct)
        {
            await using AppDbContext db = await dbFactory.CreateDbContextAsync(ct);
            bool hasAny = await db.DigestReports.AnyAsync(ct);

            return hasAny;
        }

        private async Task<bool> HasDataAsync(DateTime startUtc, DateTime endUtc, CancellationToken ct)
        {
            await using AppDbContext db = await dbFactory.CreateDbContextAsync(ct);
            long startEpoch = (long)(startUtc - DateTime.UnixEpoch).TotalSeconds;
            long endEpoch = (long)(endUtc - DateTime.UnixEpoch).TotalSeconds;

            bool hasTraffic = await db.TrafficRollups
                .AnyAsync(rollup => rollup.MinuteEpoch >= startEpoch && rollup.MinuteEpoch < endEpoch, ct);
            bool hasEvents = await db.DeviceEvents
                .AnyAsync(deviceEvent => deviceEvent.Timestamp >= startUtc && deviceEvent.Timestamp < endUtc, ct);
            bool hasDevices = await db.Devices
                .AnyAsync(device => device.LastSeen >= startUtc && device.FirstSeen < endUtc, ct);
            bool hasData = hasTraffic || hasEvents || hasDevices;

            return hasData;
        }

        private async Task<DateTime?> GetLastPeriodEndUtcAsync(CancellationToken ct)
        {
            await using AppDbContext db = await dbFactory.CreateDbContextAsync(ct);
            DateTime? lastEnd = await db.DigestReports
                .Where(report => report.IsScheduled)
                .OrderByDescending(report => report.PeriodEnd)
                .Select(report => (DateTime?)report.PeriodEnd)
                .FirstOrDefaultAsync(ct);

            return lastEnd;
        }

        private async Task PurgeOldReportsAsync(CancellationToken ct)
        {

            if (settings.DigestPurgeDays > 0)
            {
                await using AppDbContext db = await dbFactory.CreateDbContextAsync(ct);
                DateTime cutoff = DateTime.UtcNow.AddDays(-settings.DigestPurgeDays);
                await db.DigestReports
                    .Where(report => report.GeneratedAt < cutoff)
                    .ExecuteDeleteAsync(ct);
            }

        }
    }
}
