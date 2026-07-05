using NetworkMonitor.Services.Common;
using Xunit;

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
