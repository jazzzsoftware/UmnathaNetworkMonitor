using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using NetworkMonitor.Core.Update;
using NetworkMonitor.Models.Update;

namespace NetworkMonitor.Tests.Update
{
    public class UpdateDownloaderTests
    {
        [Fact]
        public async Task DownloadWritesTheInstallerAndReturnsItsPathWhenTheChecksumMatches()
        {
            byte[] payload = CreatePayload(4096);
            string folder = CreateTempFolder();

            try
            {
                UpdateDownloader downloader = CreateDownloader(payload, HashOf(payload), payload.Length);

                string path = await downloader.DownloadAndVerifyAsync(CreateUpdate(payload.Length), folder, new IgnoredProgress(), TestContext.Current.CancellationToken);

                Assert.True(File.Exists(path));
                Assert.Equal(folder, Path.GetDirectoryName(path));
                Assert.Equal(payload, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
            }
            finally
            {
                Directory.Delete(folder, true);
            }

        }

        [Fact]
        public async Task DownloadDeletesThePartialInstallerWhenTheChecksumDoesNotMatch()
        {
            byte[] payload = CreatePayload(4096);
            string folder = CreateTempFolder();

            try
            {
                UpdateDownloader downloader = CreateDownloader(payload, "deadbeef", payload.Length);

                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    downloader.DownloadAndVerifyAsync(CreateUpdate(payload.Length), folder, new IgnoredProgress(), TestContext.Current.CancellationToken));

                Assert.Empty(Directory.GetFiles(folder));
            }
            finally
            {
                Directory.Delete(folder, true);
            }

        }

        [Fact]
        public async Task DownloadDeletesThePartialInstallerWhenTheTransferFails()
        {
            byte[] payload = CreatePayload(4096);
            string folder = CreateTempFolder();

            try
            {
                UpdateDownloader downloader = new UpdateDownloader(
                    (url, token) => Task.FromResult(HashOf(payload)),
                    (url, token) => Task.FromResult(new UpdateDownloadStream(new FailingStream(payload), payload.Length)),
                    null);

                await Assert.ThrowsAsync<IOException>(() =>
                    downloader.DownloadAndVerifyAsync(CreateUpdate(payload.Length), folder, new IgnoredProgress(), TestContext.Current.CancellationToken));

                Assert.Empty(Directory.GetFiles(folder));
            }
            finally
            {
                Directory.Delete(folder, true);
            }

        }

        [Fact]
        public async Task DownloadClearsFilesLeftBehindByAPreviousAttempt()
        {
            byte[] payload = CreatePayload(4096);
            string folder = CreateTempFolder();
            string stalePath = Path.Combine(folder, "UmnathaNetworkMonitor-0.0.1.exe");
            await File.WriteAllTextAsync(stalePath, "stale", TestContext.Current.CancellationToken);

            try
            {
                UpdateDownloader downloader = CreateDownloader(payload, HashOf(payload), payload.Length);

                await downloader.DownloadAndVerifyAsync(CreateUpdate(payload.Length), folder, new IgnoredProgress(), TestContext.Current.CancellationToken);

                Assert.False(File.Exists(stalePath));
                Assert.Single(Directory.GetFiles(folder));
            }
            finally
            {
                Directory.Delete(folder, true);
            }

        }

        [Fact]
        public async Task DownloadReportsEachWholePercentOnlyOnce()
        {
            byte[] payload = CreatePayload(1000);
            string folder = CreateTempFolder();
            RecordingProgress progress = new RecordingProgress();

            try
            {
                UpdateDownloader downloader = new UpdateDownloader(
                    (url, token) => Task.FromResult(HashOf(payload)),
                    (url, token) => Task.FromResult(new UpdateDownloadStream(new ChunkedStream(payload, 1), payload.Length)),
                    null);

                await downloader.DownloadAndVerifyAsync(CreateUpdate(payload.Length), folder, progress, TestContext.Current.CancellationToken);

                List<int> percents = progress.WholePercents();

                Assert.Equal(percents.Count, new HashSet<int>(percents).Count);
                Assert.True(percents.Count <= 101, $"expected at most 101 reports, got {percents.Count}");
                Assert.Equal(100, percents[percents.Count - 1]);
            }
            finally
            {
                Directory.Delete(folder, true);
            }

        }

        [Fact]
        public async Task DownloadFallsBackToTheReleaseSizeWhenContentLengthIsMissing()
        {
            byte[] payload = CreatePayload(1000);
            string folder = CreateTempFolder();
            RecordingProgress progress = new RecordingProgress();

            try
            {
                UpdateDownloader downloader = new UpdateDownloader(
                    (url, token) => Task.FromResult(HashOf(payload)),
                    (url, token) => Task.FromResult(new UpdateDownloadStream(new ChunkedStream(payload, 1), null)),
                    null);

                await downloader.DownloadAndVerifyAsync(CreateUpdate(payload.Length), folder, progress, TestContext.Current.CancellationToken);

                List<int> percents = progress.WholePercents();

                Assert.NotEmpty(percents);
                Assert.Equal(100, percents[percents.Count - 1]);
            }
            finally
            {
                Directory.Delete(folder, true);
            }

        }

        [Fact]
        public async Task DownloadStillCompletesWhenNeitherSizeIsKnown()
        {
            byte[] payload = CreatePayload(1000);
            string folder = CreateTempFolder();
            RecordingProgress progress = new RecordingProgress();

            try
            {
                UpdateDownloader downloader = new UpdateDownloader(
                    (url, token) => Task.FromResult(HashOf(payload)),
                    (url, token) => Task.FromResult(new UpdateDownloadStream(new ChunkedStream(payload, 1), null)),
                    null);

                string path = await downloader.DownloadAndVerifyAsync(CreateUpdate(0), folder, progress, TestContext.Current.CancellationToken);

                Assert.True(File.Exists(path));
                Assert.Empty(progress.WholePercents());
            }
            finally
            {
                Directory.Delete(folder, true);
            }

        }

        [Fact]
        public void CleanFolderRemovesEveryFileItFinds()
        {
            string folder = CreateTempFolder();
            File.WriteAllText(Path.Combine(folder, "one.exe"), "a");
            File.WriteAllText(Path.Combine(folder, "two.exe.sha256"), "b");

            try
            {
                UpdateDownloader downloader = CreateDownloader(CreatePayload(8), "hash", 8);

                downloader.CleanFolder(folder);

                Assert.Empty(Directory.GetFiles(folder));
            }
            finally
            {
                Directory.Delete(folder, true);
            }

        }

        [Fact]
        public void CleanFolderIgnoresAFolderThatDoesNotExist()
        {
            string missing = Path.Combine(Path.GetTempPath(), $"nm-update-missing-{Guid.NewGuid():N}");
            string loggedSource = string.Empty;
            UpdateDownloader downloader = new UpdateDownloader(
                (url, token) => Task.FromResult(string.Empty),
                (url, token) => Task.FromResult(new UpdateDownloadStream(new MemoryStream(), 0)),
                (source, exception) => loggedSource = source);

            downloader.CleanFolder(missing);

            Assert.Empty(loggedSource);
        }

        private static UpdateDownloader CreateDownloader(byte[] payload, string checksum, long size)
        {
            UpdateDownloader downloader = new UpdateDownloader(
                (url, token) => Task.FromResult(checksum),
                (url, token) => Task.FromResult(new UpdateDownloadStream(new MemoryStream(payload, false), size)),
                null);

            return downloader;
        }

        private static AvailableUpdate CreateUpdate(long sizeBytes)
        {
            AvailableUpdate update = new AvailableUpdate(
                "v1.2.3",
                "1.2.3",
                "https://example/app.exe",
                "https://example/app.exe.sha256",
                sizeBytes);

            return update;
        }

        private static byte[] CreatePayload(int length)
        {
            byte[] payload = new byte[length];

            for (int index = 0; index < length; index++)
            {
                payload[index] = (byte)(index % 251);
            }

            return payload;
        }

        private static string HashOf(byte[] payload)
        {
            string hash = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();

            return hash;
        }

        private static string CreateTempFolder()
        {
            string folder = Path.Combine(Path.GetTempPath(), $"nm-update-{Guid.NewGuid():N}");
            Directory.CreateDirectory(folder);

            return folder;
        }

        private sealed class RecordingProgress : IProgress<double>
        {
            private readonly List<double> _fractions = new List<double>();

            public void Report(double value)
            {
                _fractions.Add(value);
            }

            public List<int> WholePercents()
            {
                List<int> percents = new List<int>();

                foreach (double fraction in _fractions)
                {
                    percents.Add((int)(fraction * 100.0));
                }

                return percents;
            }
        }

        private sealed class IgnoredProgress : IProgress<double>
        {
            public void Report(double value)
            {
            }
        }

        private sealed class ChunkedStream : MemoryStream
        {
            private readonly int _maxChunk;

            public ChunkedStream(byte[] payload, int maxChunk)
                : base(payload, false)
            {
                _maxChunk = maxChunk;
            }

            public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                int size = Math.Min(_maxChunk, buffer.Length);
                ValueTask<int> read = base.ReadAsync(buffer.Slice(0, size), cancellationToken);

                return read;
            }
        }

        private sealed class FailingStream : MemoryStream
        {
            public FailingStream(byte[] payload)
                : base(payload, false)
            {
            }

            public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                throw new IOException("The connection dropped.");
            }
        }
    }
}
