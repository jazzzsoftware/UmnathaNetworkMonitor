using System.Net.Http.Headers;
using System.Text.Json;
using NetworkMonitor.Core.Update;
using NetworkMonitor.Models.Update;

namespace NetworkMonitor.UITests.Fixtures
{
    // Resolves the two releases the update lifecycle needs: the newest as the target, the one
    // before it as the baseline to install and then update FROM. Both come from one call to
    // GitHub's /releases, which returns them newest first.
    //
    // ReleaseInfoParser.Parse takes a SINGLE release object while /releases returns an array, so
    // each element's raw JSON is handed to it individually rather than the whole document — the
    // plan calls this out because passing the array produces a null with no explanation.
    //
    // Lives beside ReleaseInstaller rather than in the Environment/ folder the plan named: nothing
    // else was ever put there, and these two are the same subject — which release, and installing
    // it. Recorded as a deviation in the plan's Task 12 amendments.
    public static class ReleaseResolver
    {
        private const string ReleasesUrl = "https://api.github.com/repos/jazzzsoftware/UmnathaNetworkMonitor/releases";

        // GitHub rejects an API request with no User-Agent outright.
        private const string UserAgent = "UmnathaNetworkMonitor-UITests";

        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

        public static async Task<ReleasePair> ResolveAsync(CancellationToken cancellationToken)
        {
            string releasesJson = await FetchReleasesJsonAsync(cancellationToken);

            using JsonDocument document = JsonDocument.Parse(releasesJson);

            JsonElement root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException(
                    "GitHub's /releases did not return an array. Unauthenticated requests are limited to 60 an hour, "
                    + "and the limit is reported as a JSON object rather than a list — if this run has been repeated "
                    + "often, that is the likely cause. The body started: "
                    + releasesJson[..Math.Min(releasesJson.Length, 200)]);
            }

            List<JsonElement> releases = root.EnumerateArray().ToList();

            if (releases.Count < 2)
            {
                throw new InvalidOperationException(
                    $"The update lifecycle needs two releases to work with — the newest to update TO and the one "
                    + $"before it to install and update FROM — but /releases returned {releases.Count}.");
            }

            AvailableUpdate target = ParseOrThrow(releases[0], "the newest release (the update target)");
            AvailableUpdate baseline = ParseOrThrow(releases[1], "the second-newest release (the baseline to update from)");
            ReleasePair pair = new ReleasePair(target, baseline);

            return pair;
        }

        private static AvailableUpdate ParseOrThrow(JsonElement release, string description)
        {
            AvailableUpdate? parsed = ReleaseInfoParser.Parse(release.GetRawText());

            if (parsed is null)
            {
                throw new InvalidOperationException(
                    $"Could not read {description} from GitHub's response. A release only parses when it carries both "
                    + "an installer asset and its .sha256 sibling; one of those is missing.");
            }

            return parsed;
        }

        private static async Task<string> FetchReleasesJsonAsync(CancellationToken cancellationToken)
        {
            string json;

            using (HttpClient httpClient = new HttpClient())
            {
                httpClient.Timeout = RequestTimeout;
                httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(UserAgent, "1.0"));

                json = await httpClient.GetStringAsync(ReleasesUrl, cancellationToken);
            }

            return json;
        }
    }
}
