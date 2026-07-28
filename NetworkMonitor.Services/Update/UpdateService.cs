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

        private readonly IInstallerLauncher _launcher;
        private readonly UpdateChecker _checker;
        private readonly UpdateDownloader _downloader;

        public UpdateService(HttpClient httpClient, IInstallerLauncher launcher)
        {
            _launcher = launcher;
            _checker = new UpdateChecker(
                (url, cancellationToken) => FetchReleaseJsonAsync(httpClient, url, cancellationToken),
                AppLog.Error);
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
            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Accept", "application/vnd.github+json");

            using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            string releaseJson = await response.Content.ReadAsStringAsync(cancellationToken);

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
