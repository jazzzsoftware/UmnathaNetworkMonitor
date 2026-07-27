using System;
using System.Text.Json;
using NetworkMonitor.Models.Update;

namespace NetworkMonitor.Core.Update
{
    public static class ReleaseInfoParser
    {
        // Separate from Parse so a release can be version-compared before its assets are validated —
        // a release we are already running must not raise an error just because it lacks a checksum.
        public static bool TryParseVersionTag(string releaseJson, out string versionTag)
        {
            versionTag = string.Empty;
            bool parsed = false;

            if (!string.IsNullOrWhiteSpace(releaseJson))
            {

                try
                {
                    using JsonDocument document = JsonDocument.Parse(releaseJson);
                    JsonElement root = document.RootElement;

                    if (root.ValueKind == JsonValueKind.Object
                        && root.TryGetProperty("tag_name", out JsonElement tagElement)
                        && tagElement.ValueKind == JsonValueKind.String)
                    {
                        string tag = tagElement.GetString() ?? string.Empty;

                        if (SemanticVersion.TryParse(tag, out SemanticVersion parsedVersion))
                        {
                            versionTag = tag;
                            parsed = true;
                        }

                    }

                }
                catch (JsonException)
                {
                    versionTag = string.Empty;
                }

            }

            return parsed;
        }

        public static AvailableUpdate? Parse(string releaseJson)
        {
            AvailableUpdate? result = null;

            if (!string.IsNullOrWhiteSpace(releaseJson))
            {

                try
                {
                    using JsonDocument document = JsonDocument.Parse(releaseJson);
                    JsonElement root = document.RootElement;

                    if (root.ValueKind == JsonValueKind.Object
                        && root.TryGetProperty("tag_name", out JsonElement tagElement)
                        && tagElement.ValueKind == JsonValueKind.String
                        && root.TryGetProperty("assets", out JsonElement assetsElement)
                        && assetsElement.ValueKind == JsonValueKind.Array)
                    {
                        string versionTag = tagElement.GetString() ?? string.Empty;
                        string installerName = string.Empty;
                        string installerUrl = string.Empty;
                        string checksumUrl = string.Empty;
                        long installerSize = 0;
                        Dictionary<string, string> checksumUrlsByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                        foreach (JsonElement asset in assetsElement.EnumerateArray())
                        {

                            if (asset.TryGetProperty("name", out JsonElement nameElement)
                                && asset.TryGetProperty("browser_download_url", out JsonElement urlElement))
                            {
                                string name = nameElement.GetString() ?? string.Empty;
                                string url = urlElement.GetString() ?? string.Empty;

                                if (name.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase))
                                {
                                    checksumUrlsByName[name] = url;
                                }
                                else if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && installerName.Length == 0)
                                {
                                    installerName = name;
                                    installerUrl = url;
                                    installerSize = asset.TryGetProperty("size", out JsonElement sizeElement)
                                        && sizeElement.TryGetInt64(out long parsedSize)
                                        ? parsedSize
                                        : 0;
                                }

                            }

                        }

                        // The checksum must be the one published for this installer. Taking any
                        // .sha256 in the release pairs the wrong hash with the wrong file the
                        // moment a release ships a second executable, and the download then
                        // always fails verification with no way to tell why.
                        if (installerName.Length > 0)
                        {

                            if (checksumUrlsByName.TryGetValue($"{installerName}.sha256", out string? matched))
                            {
                                checksumUrl = matched;
                            }
                            else if (checksumUrlsByName.Count == 1)
                            {

                                foreach (KeyValuePair<string, string> only in checksumUrlsByName)
                                {
                                    checksumUrl = only.Value;
                                }

                            }

                        }

                        if (SemanticVersion.TryParse(versionTag, out SemanticVersion parsedVersion)
                            && installerUrl.Length > 0
                            && checksumUrl.Length > 0)
                        {
                            string normalized = $"{parsedVersion.Major}.{parsedVersion.Minor}.{parsedVersion.Patch}";
                            result = new AvailableUpdate(versionTag, normalized, installerUrl, checksumUrl, installerSize);
                        }

                    }

                }
                catch (JsonException)
                {
                    result = null;
                }

            }

            return result;
        }
    }
}
