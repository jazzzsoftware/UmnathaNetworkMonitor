using System;
using System.Threading;
using System.Threading.Tasks;

namespace NetworkMonitor.Core.Common
{
    public static class Watchdog
    {
        public static async Task RunAsync(Func<CancellationToken, Task> operation, TimeSpan timeout, CancellationToken cancellationToken)
        {
            using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            Task operationTask = operation(timeoutSource.Token);
            Task timeoutTask = Task.Delay(timeout, timeoutSource.Token);

            Task completed = await Task.WhenAny(operationTask, timeoutTask);

            timeoutSource.Cancel();

            if (completed != operationTask)
            {
                ObserveInBackground(operationTask);

                // The delay runs on the linked token, so the caller cancelling completes it just as
                // surely as the timeout elapsing. Without this, shutting the app down mid-operation was
                // reported as "timed out after 180 seconds" a tenth of a second after it started.
                cancellationToken.ThrowIfCancellationRequested();

                throw new TimeoutException($"Operation did not complete within {timeout.TotalSeconds:0} seconds and was abandoned.");
            }

            await operationTask;
        }

        private static void ObserveInBackground(Task task)
        {
            _ = task.ContinueWith(
                finished => finished.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
        }
    }
}
