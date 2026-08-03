using Xunit;
using NetworkMonitor.Core.Common;

namespace NetworkMonitor.Tests
{
    public class WatchdogTests
    {
        [Fact(Timeout = 5000)]
        public async Task RunAsyncCompletesWhenOperationFinishesInTime()
        {
            bool didRun = false;

            await Watchdog.RunAsync(
                async token =>
                {
                    await Task.Delay(10, token);
                    didRun = true;
                },
                TimeSpan.FromSeconds(5),
                CancellationToken.None);

            Assert.True(didRun);
        }

        [Fact(Timeout = 5000)]
        public async Task RunAsyncThrowsTimeoutWhenOperationExceedsBudget()
        {
            await Assert.ThrowsAsync<TimeoutException>(() =>
                Watchdog.RunAsync(
                    token => Task.Delay(Timeout.Infinite, token),
                    TimeSpan.FromMilliseconds(100),
                    CancellationToken.None));
        }

        [Fact(Timeout = 5000)]
        public async Task RunAsyncRecoversWhenOperationIgnoresCancellation()
        {
            TaskCompletionSource neverReleased = new();

            Task IgnoresToken(CancellationToken token)
            {
                return neverReleased.Task;
            }

            await Assert.ThrowsAsync<TimeoutException>(() =>
                Watchdog.RunAsync(IgnoresToken, TimeSpan.FromMilliseconds(100), CancellationToken.None));

            neverReleased.TrySetResult();
        }

        // Shutting the app down mid-operation used to surface as a TimeoutException, which had the
        // speed test worker logging "timed out after 180 seconds" a tenth of a second into the run.
        [Fact(Timeout = 5000)]
        public async Task RunAsyncReportsCallerCancellationAsCancellationRatherThanTimeout()
        {
            using CancellationTokenSource cancellation = new CancellationTokenSource();

            Task run = Watchdog.RunAsync(
                token => Task.Delay(Timeout.Infinite, token),
                TimeSpan.FromMinutes(5),
                cancellation.Token);

            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        }

        [Fact(Timeout = 5000)]
        public async Task RunAsyncSurfacesOperationFailure()
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                Watchdog.RunAsync(
                    token => throw new InvalidOperationException("boom"),
                    TimeSpan.FromSeconds(5),
                    CancellationToken.None));
        }
    }
}
