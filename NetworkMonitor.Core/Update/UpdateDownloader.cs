using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NetworkMonitor.Models.Update;

namespace NetworkMonitor.Core.Update
{
    public sealed class UpdateDownloader
    {
        private const int BufferSize = 81920;

        private readonly Func<string, CancellationToken, Task<string>> _fetchText;
        private readonly Func<string, CancellationToken, Task<UpdateDownloadStream>> _openStream;
        private readonly Action<string, Exception>? _logError;

        public UpdateDownloader(
            Func<string, CancellationToken, Task<string>> fetchText,
            Func<string, CancellationToken, Task<UpdateDownloadStream>> openStream,
            Action<string, Exception>? logError = null)
        {
            _fetchText = fetchText;
            _openStream = openStream;
            _logError = logError;
        }

        public async Task<string> DownloadAndVerifyAsync(AvailableUpdate update, string updatesFolder, IProgress<double> progress, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(updatesFolder);
            CleanFolder(updatesFolder);

            string installerPath = Path.Combine(updatesFolder, $"UmnathaNetworkMonitor-{update.NormalizedVersion}.exe");

            try
            {
                string checksumText = await _fetchText(update.ChecksumUrl, cancellationToken);
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

        public void CleanFolder(string folder)
        {

            if (Directory.Exists(folder))
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
                    _logError?.Invoke("UpdateDownloader.CleanFolder", exception);
                }

            }

        }

        private async Task DownloadToFileAsync(string url, string destinationPath, long expectedSize, IProgress<double> progress, CancellationToken cancellationToken)
        {
            await using UpdateDownloadStream source = await _openStream(url, cancellationToken);
            await using FileStream destination = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);

            long totalBytes = source.ContentLength ?? expectedSize;
            byte[] buffer = new byte[BufferSize];
            long receivedBytes = 0;
            int lastReportedPercent = -1;
            int read;

            while ((read = await source.Content.ReadAsync(buffer, cancellationToken)) > 0)
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

        private void TryDelete(string path)
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
                _logError?.Invoke("UpdateDownloader.TryDelete", exception);
            }

        }
    }
}
