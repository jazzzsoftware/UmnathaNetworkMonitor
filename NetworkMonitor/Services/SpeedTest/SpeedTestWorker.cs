using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using NetworkMonitor.Data;
using NetworkMonitor.Models;
using NetworkMonitor.Services.Common;
using NetworkMonitor.Services.Platform;

namespace NetworkMonitor.Services.SpeedTest
{
    public class SpeedTestWorker(
        SpeedTestService service,
        Settings settings,
        IDbContextFactory<AppDbContext> dbFactory) : BackgroundService
    {
        private readonly SemaphoreSlim _runGate = new(1, 1);

        private static readonly TimeSpan RunTimeout = TimeSpan.FromMinutes(3);

        public event EventHandler<SpeedTestCompletedEventArgs>? SpeedTestCompleted;

        public async Task RunNowAsync(CancellationToken ct = default)
        {
            await RunTestAsync(ct);
        }

        protected override async Task ExecuteAsync(CancellationToken ct)
        {

            while (!ct.IsCancellationRequested)
            {

                try
                {
                    TimeSpan delay = GetDelayUntilNextHour();

                    await Task.Delay(delay, ct);

                    if (settings.SpeedTestEnabled)
                    {
                        await RunTestAsync(ct);
                    }

                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception exception)
                {
                    AppLog.Error("SpeedTestWorker.Execute", exception);
                }

            }

        }

        public override void Dispose()
        {
            _runGate.Dispose();

            base.Dispose();
        }

        private static TimeSpan GetDelayUntilNextHour()
        {
            DateTime now = DateTime.Now;
            DateTime nextHour = now.Date.AddHours(now.Hour + 1);
            TimeSpan delay = nextHour - now;

            return delay;
        }

        private async Task RunTestAsync(CancellationToken ct)
        {
            await _runGate.WaitAsync(ct);

            try
            {
                await Watchdog.RunAsync(ExecuteTestAsync, RunTimeout, ct);
            }
            catch (OperationCanceledException)
            {
            }
            catch (TimeoutException)
            {
                AppLog.Info($"Speed test timed out after {RunTimeout.TotalSeconds:0} seconds and was aborted; it will retry on the next cycle.");
            }
            catch (Exception exception)
            {
                AppLog.Error("SpeedTestWorker.RunTest", exception);
            }
            finally
            {
                _runGate.Release();
            }

        }

        private async Task ExecuteTestAsync(CancellationToken ct)
        {
            SpeedTestResult result = await service.RunAsync(ct);

            await using AppDbContext db = await dbFactory.CreateDbContextAsync(ct);
            db.SpeedTestResults.Add(result);
            await db.SaveChangesAsync(ct);

            SpeedTestCompleted?.Invoke(this, new SpeedTestCompletedEventArgs(result));
            AppLog.Info($"Speed test completed: {result.DownloadMbps:0.0} down / {result.UploadMbps:0.0} up Mbps, ping={result.LatencyMs:0.0} ms, jitter={result.JitterMs:0.0} ms, success={result.Success}.");
        }
    }
}
