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
    public sealed class UpdateService : IUpdateService
    {
        private const string LatestReleaseUrl =
            "https://api.github.com/repos/jazzzsoftware/UmnathaNetworkMonitor/releases/latest";

        private readonly HttpClient _httpClient;
        private readonly IInstallerLauncher _launcher;

        public UpdateService(HttpClient httpClient, IInstallerLauncher launcher)
        {
            _httpClient = httpClient;
            _launcher = launcher;
        }

        public event EventHandler<UpdateCheckResult>? CheckCompleted;

        public UpdateCheckResult? LastResult
        {
            get;
            private set;
        }

        public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken)
        {
            UpdateCheckResult result;
            bool cancelled = false;

            try
            {
                using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);
                request.Headers.Add("Accept", "application/vnd.github+json");

                using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
                response.EnsureSuccessStatusCode();

                string json = await response.Content.ReadAsStringAsync(cancellationToken);
                string currentVersion = AppInfo.GetVersion();

                if (!ReleaseInfoParser.TryParseVersionTag(json, out string versionTag))
                {
                    result = UpdateCheckResult.Failed("The latest release could not be read.");
                }
                else if (!SemanticVersion.TryParse(currentVersion, out SemanticVersion _))
                {
                    // Without this the comparison below would silently fail closed and the user
                    // would be told they are up to date on every check, for good.
                    result = UpdateCheckResult.Failed($"Couldn't read the installed version ({currentVersion}), so updates can't be compared.");
                }
                else if (!UpdateDecision.IsNewer(currentVersion, versionTag))
                {
                    result = UpdateCheckResult.UpToDate();
                }
                else
                {
                    AvailableUpdate? update = ReleaseInfoParser.Parse(json);

                    if (update is null)
                    {
                        result = UpdateCheckResult.Failed($"Version {versionTag} is available, but its download is incomplete. Please try again later.");
                    }
                    else
                    {
                        result = UpdateCheckResult.Available(update);
                    }

                }

            }
            catch (OperationCanceledException)
            {
                cancelled = true;
                result = UpdateCheckResult.Failed("The update check was cancelled.");
            }
            catch (Exception exception)
            {
                AppLog.Error("UpdateService.Check", exception);
                result = UpdateCheckResult.Failed("Couldn't check for updates — check your connection.");
            }

            // A check cancelled by host shutdown is not a failure the user needs to see.
            if (!cancelled)
            {
                LastResult = result;
                CheckCompleted?.Invoke(this, result);
            }

            return result;
        }

        public void CleanUpDownloads()
        {
            string updatesFolder = Path.Combine(AppPaths.AppDataFolder, "Updates");

            if (Directory.Exists(updatesFolder))
            {
                CleanFolder(updatesFolder);
            }

        }

        public async Task<string> DownloadAndVerifyAsync(AvailableUpdate update, IProgress<double> progress, CancellationToken cancellationToken)
        {
            string updatesFolder = Path.Combine(AppPaths.AppDataFolder, "Updates");
            Directory.CreateDirectory(updatesFolder);
            CleanFolder(updatesFolder);

            string installerPath = Path.Combine(updatesFolder, $"UmnathaNetworkMonitor-{update.NormalizedVersion}.exe");

            try
            {
                string checksumText = await _httpClient.GetStringAsync(update.ChecksumUrl, cancellationToken);
                string expectedHash = ChecksumVerifier.ParseHashFromChecksumFile(checksumText);

                await DownloadToFileAsync(update.InstallerUrl, installerPath, update.SizeBytes, progress, cancellationToken);

                string actualHash = await ChecksumVerifier.ComputeSha256Async(installerPath, cancellationToken);

                if (!ChecksumVerifier.Verify(expectedHash, actualHash))
                {
                    throw new InvalidOperationException("The downloaded update failed its checksum check.");
                }

            }
            catch (Exception)
            {
                TryDelete(installerPath);

                throw;
            }

            return installerPath;
        }

        public void LaunchInstaller(string installerPath, Action? beforeExit)
        {
            _launcher.LaunchAndExit(installerPath, beforeExit);
        }

        private async Task DownloadToFileAsync(string url, string destinationPath, long expectedSize, IProgress<double> progress, CancellationToken cancellationToken)
        {
            using HttpResponseMessage response = await _httpClient.GetAsync(
                url,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            response.EnsureSuccessStatusCode();

            long totalBytes = response.Content.Headers.ContentLength ?? expectedSize;
            await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using FileStream destination = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);

            byte[] buffer = new byte[81920];
            long receivedBytes = 0;
            int lastReportedPercent = -1;
            int read;

            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                receivedBytes += read;

                if (totalBytes > 0)
                {
                    double fraction = (double)receivedBytes / totalBytes;
                    int percent = (int)(fraction * 100.0);

                    // Reporting every 80 KB chunk marshals ~1300 updates to the UI thread for a
                    // 100 MB installer; the bar only shows whole percent.
                    if (percent != lastReportedPercent)
                    {
                        lastReportedPercent = percent;
                        progress.Report(fraction);
                    }

                }

            }

        }

        private static void CleanFolder(string folder)
        {

            try
            {
                foreach (string file in Directory.EnumerateFiles(folder))
                {
                    TryDelete(file);
                }

            }
            catch (Exception exception)
            {
                AppLog.Error("UpdateService.CleanFolder", exception);
            }

        }

        private static void TryDelete(string path)
        {

            try
            {

                if (File.Exists(path))
                {
                    File.Delete(path);
                }

            }
            catch (Exception exception)
            {
                AppLog.Error("UpdateService.TryDelete", exception);
            }

        }
    }
}
