using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NetworkMonitor.Core.Update;
using NetworkMonitor.Models.Update;
using NetworkMonitor.Services.Data;
using NetworkMonitor.Services.Platform;

namespace NetworkMonitor.Services.Update
{
    // A thin adapter over the Core orchestration: this class owns only the HTTP transport,
    // the app-data location and the check-result notification. The decision ladder, the
    // download/verify sequence and the folder housekeeping live in Core so they are testable.
    public sealed class UpdateService : IUpdateService
    {
        private const string LatestReleaseUrl =
            "https://api.github.com/repos/jazzzsoftware/UmnathaNetworkMonitor/releases/latest";

        private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(20);

        private readonly IInstallerLauncher _launcher;
        private readonly UpdateChecker _checker;
        private readonly UpdateDownloader _downloader;

        public UpdateService(HttpClient httpClient, IInstallerLauncher launcher)
        {
            _launcher = launcher;
            _checker = new UpdateChecker(
                (url, cancellationToken) => FetchReleaseJsonAsync(httpClient, url, cancellationToken),
                AppLog.Error,
                AppLog.Info);
            _downloader = new UpdateDownloader(
                (url, cancellationToken) => httpClient.GetStringAsync(url, cancellationToken),
                (url, cancellationToken) => OpenStreamAsync(httpClient, url, cancellationToken),
                AppLog.Error);
        }

        public event EventHandler<UpdateCheckResult>? CheckCompleted;

        public UpdateCheckResult? LastResult
        {
            get;
            private set;
        }

        private static string UpdatesFolder => Path.Combine(AppPaths.AppDataFolder, "Updates");

        public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken)
        {
            string currentVersion = AppInfo.GetVersion();
            UpdateCheckOutcome outcome = await _checker.CheckAsync(LatestReleaseUrl, currentVersion, cancellationToken);

            AppLog.Info($"Update check completed: installed=v{currentVersion}, result={outcome.Result.Availability}, cancelled={outcome.Cancelled}.");

            // A check cancelled by host shutdown is not a failure the user needs to see.
            if (!outcome.Cancelled)
            {
                LastResult = outcome.Result;
                CheckCompleted?.Invoke(this, outcome.Result);
            }

            return outcome.Result;
        }

        public void CleanUpDownloads()
        {
            _downloader.CleanFolder(UpdatesFolder);
        }

        public async Task<string> DownloadAndVerifyAsync(AvailableUpdate update, IProgress<double> progress, CancellationToken cancellationToken)
        {
            string installerPath = await _downloader.DownloadAndVerifyAsync(update, UpdatesFolder, progress, cancellationToken);

            return installerPath;
        }

        public void LaunchInstaller(string installerPath, Action? beforeExit)
        {
            _launcher.LaunchAndExit(installerPath, beforeExit);
        }

        private static async Task<string> FetchReleaseJsonAsync(HttpClient httpClient, string url, CancellationToken cancellationToken)
        {
            // The shared client carries a long timeout for downloads. A check is one small JSON GET,
            // so bound it separately — otherwise an unreachable server that hangs rather than
            // refusing leaves the user waiting on the client's full download budget.
            using CancellationTokenSource attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            attempt.CancelAfter(CheckTimeout);

            string releaseJson;

            try
            {
                using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("Accept", "application/vnd.github+json");

                using HttpResponseMessage response = await httpClient.SendAsync(request, attempt.Token);
                response.EnsureSuccessStatusCode();

                releaseJson = await response.Content.ReadAsStringAsync(attempt.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Our own deadline, not the caller's. Surfacing this as a cancellation would make
                // UpdateChecker mark the outcome cancelled and the caller suppress it entirely,
                // leaving the user with no message at all.
                throw new TimeoutException($"The update check did not respond within {CheckTimeout.TotalSeconds:0} seconds.");
            }

            return releaseJson;
        }

        private static async Task<UpdateDownloadStream> OpenStreamAsync(HttpClient httpClient, string url, CancellationToken cancellationToken)
        {
            UpdateDownloadStream stream;
            HttpResponseMessage response = await httpClient.GetAsync(
                url,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            try
            {
                response.EnsureSuccessStatusCode();

                Stream content = await response.Content.ReadAsStreamAsync(cancellationToken);
                stream = new UpdateDownloadStream(content, response.Content.Headers.ContentLength, response);
            }
            catch (Exception)
            {
                response.Dispose();

                throw;
            }

            return stream;
        }
    }
}
