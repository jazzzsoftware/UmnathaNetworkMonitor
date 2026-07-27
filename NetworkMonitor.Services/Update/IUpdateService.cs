using System;
using System.Threading;
using System.Threading.Tasks;
using NetworkMonitor.Models.Update;

namespace NetworkMonitor.Services.Update
{
    public interface IUpdateService
    {
        event EventHandler<UpdateCheckResult>? CheckCompleted;

        UpdateCheckResult? LastResult
        {
            get;
        }

        Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken);

        Task<string> DownloadAndVerifyAsync(AvailableUpdate update, IProgress<double> progress, CancellationToken cancellationToken);

        void LaunchInstaller(string installerPath, Action? beforeExit);

        void CleanUpDownloads();
    }
}
