using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using NetworkMonitor.Core.Update;
using NetworkMonitor.Models.Update;
using NetworkMonitor.UITests.Runner;

namespace NetworkMonitor.UITests.Fixtures
{
    // Amendment C from the 2026-08-20 Task 7 checkpoint: the suite must never assume the app is
    // installed. When Preflight finds no install, it calls EnsureInstalledAsync instead of just
    // refusing — the latest GitHub release is downloaded, its SHA-256 is verified against the
    // release's own .sha256 asset before the installer is ever executed, and only then is it run
    // silently. Every other Preflight refusal stays a refusal; "not installed" is the one case the
    // runner now fixes for itself. Reuses ReleaseInfoParser/UpdateDownloader/ChecksumVerifier from
    // NetworkMonitor.Core/Update rather than re-implementing asset resolution or hashing.
    public static class ReleaseInstaller
    {
        private const string LatestReleaseUrl = "https://api.github.com/repos/jazzzsoftware/UmnathaNetworkMonitor/releases/latest";
        private const string UserAgent = "UmnathaNetworkMonitor-UITests";
        private const string InstallArguments = "/SILENT /SUPPRESSMSGBOXES /NORESTART";
        private const string DownloadsFolderName = "umnatha-uitests-install";

        // GitHub's API answers a JSON GET well under a second; this only bounds a server that
        // hangs rather than refusing, mirroring UpdateService.CheckTimeout for the same call.
        private static readonly TimeSpan ReleaseCheckTimeout = TimeSpan.FromSeconds(20);

        // Inno Setup's silent install of the ~75 MB payload finishes in well under a minute on any
        // machine that already passed Preflight's 3 GB free-space check; five minutes leaves
        // headroom for a slow disk or an antivirus scan without hanging the run forever.
        private static readonly TimeSpan InstallProcessTimeout = TimeSpan.FromMinutes(5);

        public static async Task<(bool Installed, string Message)> EnsureInstalledAsync(CancellationToken cancellationToken)
        {
            (bool Installed, string Message) outcome;

            using (HttpClient httpClient = new HttpClient())
            {
                httpClient.DefaultRequestHeaders.Add("User-Agent", UserAgent);

                try
                {
                    string releaseJson = await FetchReleaseJsonAsync(httpClient, cancellationToken);
                    AvailableUpdate? update = ReleaseInfoParser.Parse(releaseJson);

                    if (update is null)
                    {
                        outcome = (
                            false,
                            "Umnatha Network Monitor is not installed, and the latest GitHub release could not be "
                            + "resolved (no usable tag, installer asset or matching .sha256 asset). Install it by "
                            + "hand — see Tools/UITests/README.md.");
                    }
                    else
                    {
                        outcome = await DownloadVerifyAndInstallAsync(httpClient, update, cancellationToken);
                    }

                }
                catch (Exception exception)
                {
                    outcome = (
                        false,
                        "Umnatha Network Monitor is not installed, and acquiring the latest release failed: "
                        + $"{exception.Message}. Install it by hand — see Tools/UITests/README.md.");
                }

            }

            return outcome;
        }

        // Installs a release the caller has already resolved, rather than whatever GitHub calls
        // latest. UpdateLifecyclePhase needs the SECOND-newest release — the baseline it updates
        // from — and everything below it (download, SHA-256 verification against the release's own
        // .sha256 asset, silent install, exit-code check) is identical whichever release that is.
        public static async Task<(bool Installed, string Message)> InstallAsync(AvailableUpdate update, CancellationToken cancellationToken)
        {
            (bool Installed, string Message) outcome;

            using (HttpClient httpClient = new HttpClient())
            {
                httpClient.DefaultRequestHeaders.Add("User-Agent", UserAgent);

                try
                {
                    outcome = await DownloadVerifyAndInstallAsync(httpClient, update, cancellationToken);
                }
                catch (Exception exception)
                {
                    outcome = (false, $"Installing release {update.VersionTag} failed: {exception.Message}");
                }

            }

            return outcome;
        }

        private static async Task<(bool Installed, string Message)> DownloadVerifyAndInstallAsync(
            HttpClient httpClient,
            AvailableUpdate update,
            CancellationToken cancellationToken)
        {
            (bool Installed, string Message) outcome;
            string downloadsFolder = Path.Combine(Path.GetTempPath(), DownloadsFolderName);
            UpdateDownloader downloader = new UpdateDownloader(
                (url, token) => httpClient.GetStringAsync(url, token),
                (url, token) => OpenStreamAsync(httpClient, url, token));

            // DownloadAndVerifyAsync computes the installer's own SHA-256 and compares it against
            // the hash fetched from update.ChecksumUrl before returning — the verify-before-execute
            // amendment requires, and the installer below is never started unless this succeeds.
            string installerPath = await downloader.DownloadAndVerifyAsync(
                update,
                downloadsFolder,
                new Progress<double>(),
                cancellationToken);

            outcome = await RunInstallerAsync(installerPath, update.NormalizedVersion, cancellationToken);

            return outcome;
        }

        private static async Task<(bool Installed, string Message)> RunInstallerAsync(
            string installerPath,
            string normalizedVersion,
            CancellationToken cancellationToken)
        {
            (bool Installed, string Message) outcome;
            ProcessStartInfo startInfo = new ProcessStartInfo(installerPath)
            {
                Arguments = InstallArguments,
                UseShellExecute = false
            };

            using (Process? installerProcess = Process.Start(startInfo))
            {

                if (installerProcess is null)
                {
                    outcome = (
                        false,
                        $"The verified installer at {installerPath} did not start. Install it by hand — see "
                        + "Tools/UITests/README.md.");
                }
                else
                {
                    outcome = await WaitForInstallerAsync(installerProcess, normalizedVersion, cancellationToken);
                }

            }

            return outcome;
        }

        private static async Task<(bool Installed, string Message)> WaitForInstallerAsync(
            Process installerProcess,
            string normalizedVersion,
            CancellationToken cancellationToken)
        {
            (bool Installed, string Message) outcome;

            using (CancellationTokenSource waitDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                waitDeadline.CancelAfter(InstallProcessTimeout);

                try
                {
                    await installerProcess.WaitForExitAsync(waitDeadline.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new TimeoutException(
                        $"The silent installer for v{normalizedVersion} did not finish within {InstallProcessTimeout.TotalMinutes:0} minutes.");
                }

            }

            if (installerProcess.ExitCode == 0)
            {
                string installedVersion = Preflight.ReadInstalledVersion();

                outcome = (
                    true,
                    $"No install was found, so the suite downloaded, verified and silently installed release "
                    + $"v{normalizedVersion} (now reporting v{installedVersion}).");
            }
            else
            {
                outcome = (
                    false,
                    $"The silent installer for v{normalizedVersion} exited with code {installerProcess.ExitCode}. "
                    + "Install it by hand — see Tools/UITests/README.md.");
            }

            return outcome;
        }

        private static async Task<string> FetchReleaseJsonAsync(HttpClient httpClient, CancellationToken cancellationToken)
        {
            string releaseJson;

            using (CancellationTokenSource attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                attempt.CancelAfter(ReleaseCheckTimeout);

                try
                {

                    using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl))
                    {
                        request.Headers.Add("Accept", "application/vnd.github+json");

                        using (HttpResponseMessage response = await httpClient.SendAsync(request, attempt.Token))
                        {
                            response.EnsureSuccessStatusCode();

                            releaseJson = await response.Content.ReadAsStringAsync(attempt.Token);
                        }

                    }

                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new TimeoutException($"The release check did not respond within {ReleaseCheckTimeout.TotalSeconds:0} seconds.");
                }

            }

            return releaseJson;
        }

        private static async Task<UpdateDownloadStream> OpenStreamAsync(HttpClient httpClient, string url, CancellationToken cancellationToken)
        {
            UpdateDownloadStream stream;
            HttpResponseMessage response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

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
