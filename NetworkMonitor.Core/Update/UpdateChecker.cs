using System;
using System.Threading;
using System.Threading.Tasks;
using NetworkMonitor.Models.Update;

namespace NetworkMonitor.Core.Update
{
    public sealed class UpdateChecker
    {
        private readonly Func<string, CancellationToken, Task<string>> _fetchText;
        private readonly Action<string, Exception>? _logError;
        private readonly Action<string>? _logInfo;

        public UpdateChecker(
            Func<string, CancellationToken, Task<string>> fetchText,
            Action<string, Exception>? logError = null,
            Action<string>? logInfo = null)
        {
            _fetchText = fetchText;
            _logError = logError;
            _logInfo = logInfo;
        }

        public async Task<UpdateCheckOutcome> CheckAsync(string releaseUrl, string currentVersion, CancellationToken cancellationToken)
        {
            UpdateCheckResult result;
            bool cancelled = false;
            string? releaseJson = null;

            try
            {
                releaseJson = await _fetchText(releaseUrl, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Only a cancellation we actually asked for. An HttpClient timeout also surfaces as
                // TaskCanceledException, and treating that as "cancelled" would make the caller
                // suppress the result entirely, leaving the user staring at a dead button.
                cancelled = true;
            }
            catch (Exception exception)
            {
                // The fetch delegate is pure transport, so anything it throws is a connectivity
                // problem — an ordinary condition when offline, not a fault worth a stack trace.
                _logInfo?.Invoke($"Update check could not reach the release server: {exception.Message}");
            }

            if (cancelled)
            {
                result = UpdateCheckResult.Failed("The update check was cancelled.");
            }
            else if (releaseJson is null)
            {
                result = UpdateCheckResult.Failed("Couldn't check for updates — check your connection.");
            }
            else
            {
                result = Evaluate(releaseJson, currentVersion);
            }

            UpdateCheckOutcome outcome = new UpdateCheckOutcome(result, cancelled);

            return outcome;
        }

        private UpdateCheckResult Evaluate(string releaseJson, string currentVersion)
        {
            UpdateCheckResult result;

            try
            {

                if (!ReleaseInfoParser.TryParseVersionTag(releaseJson, out string versionTag, out Exception parseFailure))
                {
                    // The server answered, so this is a fault worth recording — the parser handles
                    // malformed JSON internally and never throws, so without this the one realistic
                    // corrupt-payload case reached no log line at all.
                    _logError?.Invoke("UpdateChecker.Evaluate", parseFailure);
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
                    AvailableUpdate? update = ReleaseInfoParser.Parse(releaseJson);

                    if (update is null)
                    {
                        // The tag already carries its own v, so prefixing this read "Version v0.0.9".
                        result = UpdateCheckResult.Failed($"{versionTag} is available, but its download is incomplete. Please try again later.");
                    }
                    else
                    {
                        result = UpdateCheckResult.Available(update);
                    }

                }

            }
            catch (Exception exception)
            {
                // A genuine fault: the server answered but we could not make sense of it.
                _logError?.Invoke("UpdateChecker.Evaluate", exception);
                result = UpdateCheckResult.Failed("The latest release could not be read.");
            }

            return result;
        }
    }
}
