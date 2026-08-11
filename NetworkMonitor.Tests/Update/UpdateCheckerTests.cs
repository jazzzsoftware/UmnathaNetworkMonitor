using System;
using System.Text.Json;
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
            string loggedError = string.Empty;
            string loggedInfo = string.Empty;
            UpdateChecker checker = new UpdateChecker(
                (url, token) => throw new InvalidOperationException("no network"),
                (source, exception) => loggedError = source,
                message => loggedInfo = message);

            UpdateCheckOutcome outcome = await checker.CheckAsync("https://example/latest", "0.0.8", TestContext.Current.CancellationToken);

            Assert.False(outcome.Cancelled);
            Assert.Equal(UpdateAvailability.CheckFailed, outcome.Result.Availability);

            // Being unable to reach the server is an ordinary condition — it must not be filed as an
            // error with a stack trace, which is what filled the log during an offline check.
            Assert.Empty(loggedError);
            Assert.NotEmpty(loggedInfo);
        }

        [Fact]
        public async Task CheckReportsCancellationOnlyWhenTheCallerCancelled()
        {
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            UpdateChecker checker = new UpdateChecker((url, token) => throw new OperationCanceledException());

            UpdateCheckOutcome outcome = await checker.CheckAsync("https://example/latest", "0.0.8", cancellation.Token);

            Assert.True(outcome.Cancelled);
            Assert.Equal(UpdateAvailability.CheckFailed, outcome.Result.Availability);
        }

        [Fact]
        public async Task ATimeoutIsNotMistakenForCancellation()
        {
            // HttpClient reports its own timeout as TaskCanceledException. Treating that as a
            // cancellation makes UpdateService suppress the result, so the user sees nothing at all.
            UpdateChecker checker = new UpdateChecker((url, token) => throw new TaskCanceledException());

            UpdateCheckOutcome outcome = await checker.CheckAsync("https://example/latest", "0.0.8", TestContext.Current.CancellationToken);

            Assert.False(outcome.Cancelled);
            Assert.Equal(UpdateAvailability.CheckFailed, outcome.Result.Availability);
        }

        [Fact]
        public async Task AnUnreadableResponseIsStillLoggedAsAnError()
        {
            string loggedError = string.Empty;
            Exception? loggedException = null;
            UpdateChecker checker = new UpdateChecker(
                (url, token) => Task.FromResult("{ \"tag_name\": "),
                (source, exception) =>
                {
                    loggedError = source;
                    loggedException = exception;
                });

            UpdateCheckOutcome outcome = await checker.CheckAsync("https://example/latest", "0.0.8", TestContext.Current.CancellationToken);

            Assert.False(outcome.Cancelled);
            Assert.Equal(UpdateAvailability.CheckFailed, outcome.Result.Availability);

            // This is the assertion the test was named for and never made. A publicly auto-updating
            // client that receives a corrupt payload must leave evidence behind.
            Assert.NotEmpty(loggedError);
            Assert.IsAssignableFrom<JsonException>(loggedException);
        }

        [Fact]
        public async Task AResponseWithNoTagNameIsLoggedAsAnError()
        {
            string loggedError = string.Empty;
            Exception? loggedException = null;
            UpdateChecker checker = new UpdateChecker(
                (url, token) => Task.FromResult("{ \"message\": \"API rate limit exceeded\" }"),
                (source, exception) =>
                {
                    loggedError = source;
                    loggedException = exception;
                });

            UpdateCheckOutcome outcome = await checker.CheckAsync("https://example/latest", "0.0.8", TestContext.Current.CancellationToken);

            Assert.Equal(UpdateAvailability.CheckFailed, outcome.Result.Availability);

            // Valid JSON, no usable tag — a different fault from malformed JSON, and the reason the
            // parser reports why it failed instead of just returning false.
            Assert.NotEmpty(loggedError);
            Assert.NotNull(loggedException);
            Assert.False(loggedException is JsonException);
        }
    }
}
