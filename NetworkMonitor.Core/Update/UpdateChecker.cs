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

        public UpdateChecker(
            Func<string, CancellationToken, Task<string>> fetchText,
            Action<string, Exception>? logError = null)
        {
            _fetchText = fetchText;
            _logError = logError;
        }

        public async Task<UpdateCheckOutcome> CheckAsync(string releaseUrl, string currentVersion, CancellationToken cancellationToken)
        {
            UpdateCheckResult result;
            bool cancelled = false;

            try
            {
                string releaseJson = await _fetchText(releaseUrl, cancellationToken);

                if (!ReleaseInfoParser.TryParseVersionTag(releaseJson, out string versionTag))
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
                    AvailableUpdate? update = ReleaseInfoParser.Parse(releaseJson);

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
                _logError?.Invoke("UpdateChecker.Check", exception);
                result = UpdateCheckResult.Failed("Couldn't check for updates — check your connection.");
            }

            UpdateCheckOutcome outcome = new UpdateCheckOutcome(result, cancelled);

            return outcome;
        }
    }
}
