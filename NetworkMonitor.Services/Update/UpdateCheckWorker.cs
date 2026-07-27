using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using NetworkMonitor.Services.Data;
using NetworkMonitor.Services.Platform;

namespace NetworkMonitor.Services.Update
{
    public sealed class UpdateCheckWorker(IUpdateService updateService, Settings settings) : BackgroundService
    {
        private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {

            try
            {
                await Task.Delay(InitialDelay, stoppingToken);

                // A previous run's installer is spent once we are running again; nothing else
                // clears the folder until the next download starts.
                updateService.CleanUpDownloads();

                await CheckIfEnabledAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                AppLog.Error("UpdateCheckWorker.Initial", exception);
            }

            while (!stoppingToken.IsCancellationRequested)
            {

                try
                {
                    await Task.Delay(CheckInterval, stoppingToken);
                    await CheckIfEnabledAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception exception)
                {
                    AppLog.Error("UpdateCheckWorker.Loop", exception);
                }

            }

        }

        private async Task CheckIfEnabledAsync(CancellationToken cancellationToken)
        {

            if (settings.AutoCheckForUpdates)
            {
                await updateService.CheckAsync(cancellationToken);
            }

        }
    }
}
