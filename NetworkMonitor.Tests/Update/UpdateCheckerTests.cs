using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using NetworkMonitor.Core.Update;
using NetworkMonitor.Models.Update;

namespace NetworkMonitor.Tests.Update
{
    public class UpdateCheckerTests
    {
        private const string ReleaseJson = """
        {
          "tag_name": "v0.0.9",
          "assets": [
            { "name": "Umnatha.Network.Monitor.v0.0.9.exe", "browser_download_url": "https://example/app.exe", "size": 15000000 },
            { "name": "Umnatha.Network.Monitor.v0.0.9.exe.sha256", "browser_download_url": "https://example/app.exe.sha256", "size": 64 }
          ]
        }
        """;

        [Fact]
        public async Task CheckReportsAnAvailableUpdateWhenTheReleaseIsNewer()
        {
            string requestedUrl = string.Empty;
            UpdateChecker checker = new UpdateChecker((url, token) =>
            {
                requestedUrl = url;

                return Task.FromResult(ReleaseJson);
            });

            UpdateCheckOutcome outcome = await checker.CheckAsync("https://example/latest", "0.0.8", TestContext.Current.CancellationToken);

            Assert.Equal("https://example/latest", requestedUrl);
            Assert.False(outcome.Cancelled);
            Assert.Equal(UpdateAvailability.UpdateAvailable, outcome.Result.Availability);
            Assert.Equal("0.0.9", outcome.Result.Update!.NormalizedVersion);
        }

        [Fact]
        public async Task CheckReportsUpToDateWhenTheReleaseIsNotNewer()
        {
            UpdateChecker checker = new UpdateChecker((url, token) => Task.FromResult(ReleaseJson));

            UpdateCheckOutcome outcome = await checker.CheckAsync("https://example/latest", "0.0.9", TestContext.Current.CancellationToken);

            Assert.False(outcome.Cancelled);
            Assert.Equal(UpdateAvailability.UpToDate, outcome.Result.Availability);
            Assert.Null(outcome.Result.Update);
        }

        [Fact]
        public async Task CheckFailsWhenTheReleaseTagCannotBeRead()
        {
            UpdateChecker checker = new UpdateChecker((url, token) => Task.FromResult("not json"));

            UpdateCheckOutcome outcome = await checker.CheckAsync("https://example/latest", "0.0.8", TestContext.Current.CancellationToken);

            Assert.False(outcome.Cancelled);
            Assert.Equal(UpdateAvailability.CheckFailed, outcome.Result.Availability);
            Assert.NotNull(outcome.Result.ErrorMessage);
        }

        [Fact]
        public async Task CheckFailsWhenTheInstalledVersionCannotBeRead()
        {
            UpdateChecker checker = new UpdateChecker((url, token) => Task.FromResult(ReleaseJson));

            UpdateCheckOutcome outcome = await checker.CheckAsync("https://example/latest", "unknown", TestContext.Current.CancellationToken);

            Assert.False(outcome.Cancelled);
            Assert.Equal(UpdateAvailability.CheckFailed, outcome.Result.Availability);
            Assert.Contains("unknown", outcome.Result.ErrorMessage!, StringComparison.Ordinal);
        }

        [Fact]
        public async Task CheckFailsWhenANewerReleaseHasNoUsableDownload()
        {
            string json = """
            { "tag_name": "v1.0.0", "assets": [ { "name": "notes.txt", "browser_download_url": "https://example/notes.txt", "size": 1 } ] }
            """;

            UpdateChecker checker = new UpdateChecker((url, token) => Task.FromResult(json));

            UpdateCheckOutcome outcome = await checker.CheckAsync("https://example/latest", "0.0.9", TestContext.Current.CancellationToken);

            Assert.False(outcome.Cancelled);
            Assert.Equal(UpdateAvailability.CheckFailed, outcome.Result.Availability);
            Assert.Contains("v1.0.0", outcome.Result.ErrorMessage!, StringComparison.Ordinal);
        }

        [Fact]
        public async Task CheckFailsWithoutCancellingWhenTheFetchThrows()
        {
            string loggedSource = string.Empty;
            UpdateChecker checker = new UpdateChecker(
                (url, token) => throw new InvalidOperationException("no network"),
                (source, exception) => loggedSource = source);

            UpdateCheckOutcome outcome = await checker.CheckAsync("https://example/latest", "0.0.8", TestContext.Current.CancellationToken);

            Assert.False(outcome.Cancelled);
            Assert.Equal(UpdateAvailability.CheckFailed, outcome.Result.Availability);
            Assert.NotEmpty(loggedSource);
        }

        [Fact]
        public async Task CheckReportsCancellationWhenTheFetchIsCancelled()
        {
            UpdateChecker checker = new UpdateChecker((url, token) => throw new OperationCanceledException());

            UpdateCheckOutcome outcome = await checker.CheckAsync("https://example/latest", "0.0.8", TestContext.Current.CancellationToken);

            Assert.True(outcome.Cancelled);
            Assert.Equal(UpdateAvailability.CheckFailed, outcome.Result.Availability);
        }
    }
}
