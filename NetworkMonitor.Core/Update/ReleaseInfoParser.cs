using System;
using System.Text.Json;
using NetworkMonitor.Models.Update;

namespace NetworkMonitor.Core.Update
{
    public static class ReleaseInfoParser
    {
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
                        string installerUrl = string.Empty;
                        string checksumUrl = string.Empty;
                        long installerSize = 0;

                        foreach (JsonElement asset in assetsElement.EnumerateArray())
                        {

                            if (asset.TryGetProperty("name", out JsonElement nameElement)
                                && asset.TryGetProperty("browser_download_url", out JsonElement urlElement))
                            {
                                string name = nameElement.GetString() ?? string.Empty;
                                string url = urlElement.GetString() ?? string.Empty;

                                if (name.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase))
                                {
                                    checksumUrl = url;
                                }
                                else if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                                {
                                    installerUrl = url;
                                    installerSize = asset.TryGetProperty("size", out JsonElement sizeElement)
                                        && sizeElement.TryGetInt64(out long parsedSize)
                                        ? parsedSize
                                        : 0;
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
