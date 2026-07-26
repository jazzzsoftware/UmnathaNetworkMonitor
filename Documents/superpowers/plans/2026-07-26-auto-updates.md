# Auto-Updates Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an in-app auto-update capability that checks GitHub Releases, notifies the user via a non-modal InfoBar, and on the user's click downloads the installer (with progress + SHA-256 verify) and launches it silently to self-update.

**Architecture:** Pure, testable logic (version compare, release-JSON parse, checksum) lives in `NetworkMonitor.Core/Update`; DTOs in `NetworkMonitor.Models/Update`; network/file/process orchestration in `NetworkMonitor.Services/Update`; a non-modal InfoBar banner and Settings controls in the app. The app already runs elevated, so the launched installer inherits admin with no extra UAC prompt. Update artifacts (installer `.exe` + companion `.sha256`) are published on GitHub Releases; the public Releases API is the feed.

**Tech Stack:** .NET 10, C#, WinUI 3 (Windows App SDK), CommunityToolkit.Mvvm, `System.Text.Json`, `System.Net.Http`, `Microsoft.Extensions.Hosting` `BackgroundService`, xunit v3, Inno Setup 6, PowerShell.

## Global Constraints

- **Coding conventions (CLAUDE.md) apply to every code sample verbatim:** no `var`; always curly braces; single exit point (one `return` at method end, value assigned to a local first with a blank line above the `return`); blank lines around every block; class member order Fields → Constructor → Properties → Public methods → Override methods → Private methods; a property's backing field sits directly above it in the Properties section and observable properties are hand-written with `SetProperty(ref _field, value)` (never `[ObservableProperty]`); property `{ get; set; }` each on its own line; `string.Empty` not `""`; no underscores in identifiers except the leading underscore on private fields; no single-character names; no comments unless the WHY is non-obvious.
- **XAML conventions (CLAUDE.md) apply to every XAML sample:** blank line after `<?xml?>`; one attribute per line indented 4 spaces; attribute order = simple assignments, then events/`Command`, then value bindings; blank line above/below every element; `DevicesPage.xaml` is the reference.
- **Test project boundary:** `NetworkMonitor.Tests` references **Models + Core only** (no Services, no App). All unit tests target Models/Core. Services/App/build tasks are verified by `dotnet build` + manual smoke, exactly as the existing workers (`ScanWorker`, `SpeedTestWorker`) are.
- **Layering:** Models ← Core ← Services ← App. Never make a lower layer reference a higher one.
- **Repository (update feed):** owner/repo = `jazzzsoftware/UmnathaNetworkMonitor`; latest-release endpoint = `https://api.github.com/repos/jazzzsoftware/UmnathaNetworkMonitor/releases/latest`.
- **Every new doc file created by this plan must be registered in `NetworkMonitor.slnx`** in the same commit.
- **Build:** x64 platform (WinUI 3 has no Any CPU). Run tests with `dotnet test NetworkMonitor.Tests/NetworkMonitor.Tests.csproj`.

---

### Task 1: Update DTOs (Models)

**Files:**
- Create: `NetworkMonitor.Models/Update/UpdateAvailability.cs`
- Create: `NetworkMonitor.Models/Update/AvailableUpdate.cs`
- Create: `NetworkMonitor.Models/Update/UpdateCheckResult.cs`
- Test: `NetworkMonitor.Tests/Update/UpdateCheckResultTests.cs`

**Interfaces:**
- Produces: `enum UpdateAvailability { UpToDate, UpdateAvailable, CheckFailed }`; `record AvailableUpdate(string VersionTag, string NormalizedVersion, string InstallerUrl, string ChecksumUrl, long SizeBytes)`; `record UpdateCheckResult` with `UpdateAvailability Availability`, `AvailableUpdate? Update`, `string? ErrorMessage`, and static factories `UpToDate()`, `Available(AvailableUpdate update)`, `Failed(string errorMessage)`.

- [ ] **Step 1: Write the failing test**

`NetworkMonitor.Tests/Update/UpdateCheckResultTests.cs`:

```csharp
using Xunit;
using NetworkMonitor.Models.Update;

namespace NetworkMonitor.Tests.Update
{
    public class UpdateCheckResultTests
    {
        [Fact]
        public void AvailableCarriesUpdateAndSetsAvailability()
        {
            AvailableUpdate update = new AvailableUpdate("v0.0.9", "0.0.9", "https://x/app.exe", "https://x/app.exe.sha256", 123);

            UpdateCheckResult result = UpdateCheckResult.Available(update);

            Assert.Equal(UpdateAvailability.UpdateAvailable, result.Availability);
            Assert.Same(update, result.Update);
            Assert.Null(result.ErrorMessage);
        }

        [Fact]
        public void UpToDateHasNoUpdateOrError()
        {
            UpdateCheckResult result = UpdateCheckResult.UpToDate();

            Assert.Equal(UpdateAvailability.UpToDate, result.Availability);
            Assert.Null(result.Update);
            Assert.Null(result.ErrorMessage);
        }

        [Fact]
        public void FailedCarriesMessage()
        {
            UpdateCheckResult result = UpdateCheckResult.Failed("no network");

            Assert.Equal(UpdateAvailability.CheckFailed, result.Availability);
            Assert.Equal("no network", result.ErrorMessage);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test NetworkMonitor.Tests/NetworkMonitor.Tests.csproj --filter UpdateCheckResultTests`
Expected: FAIL — types `UpdateAvailability` / `AvailableUpdate` / `UpdateCheckResult` do not exist (compile error).

- [ ] **Step 3: Write minimal implementation**

`NetworkMonitor.Models/Update/UpdateAvailability.cs`:

```csharp
namespace NetworkMonitor.Models.Update
{
    public enum UpdateAvailability
    {
        UpToDate,
        UpdateAvailable,
        CheckFailed
    }
}
```

`NetworkMonitor.Models/Update/AvailableUpdate.cs`:

```csharp
namespace NetworkMonitor.Models.Update
{
    public record AvailableUpdate(
        string VersionTag,
        string NormalizedVersion,
        string InstallerUrl,
        string ChecksumUrl,
        long SizeBytes);
}
```

`NetworkMonitor.Models/Update/UpdateCheckResult.cs`:

```csharp
namespace NetworkMonitor.Models.Update
{
    public record UpdateCheckResult(
        UpdateAvailability Availability,
        AvailableUpdate? Update,
        string? ErrorMessage)
    {
        public static UpdateCheckResult UpToDate()
        {
            UpdateCheckResult result = new UpdateCheckResult(UpdateAvailability.UpToDate, null, null);

            return result;
        }

        public static UpdateCheckResult Available(AvailableUpdate update)
        {
            UpdateCheckResult result = new UpdateCheckResult(UpdateAvailability.UpdateAvailable, update, null);

            return result;
        }

        public static UpdateCheckResult Failed(string errorMessage)
        {
            UpdateCheckResult result = new UpdateCheckResult(UpdateAvailability.CheckFailed, null, errorMessage);

            return result;
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test NetworkMonitor.Tests/NetworkMonitor.Tests.csproj --filter UpdateCheckResultTests`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add NetworkMonitor.Models/Update/ NetworkMonitor.Tests/Update/UpdateCheckResultTests.cs
git commit -m "Add update DTOs (AvailableUpdate, UpdateCheckResult, UpdateAvailability)."
```

---

### Task 2: SemanticVersion (Core)

**Files:**
- Create: `NetworkMonitor.Core/Update/SemanticVersion.cs`
- Test: `NetworkMonitor.Tests/Update/SemanticVersionTests.cs`

**Interfaces:**
- Produces: `sealed class SemanticVersion : IComparable<SemanticVersion>` with `int Major/Minor/Patch { get; }`, `static bool TryParse(string text, out SemanticVersion version)` (tolerates a leading `v`/`V` and a `-prerelease` suffix which it drops), and `int CompareTo(SemanticVersion? other)`.

- [ ] **Step 1: Write the failing test**

`NetworkMonitor.Tests/Update/SemanticVersionTests.cs`:

```csharp
using Xunit;
using NetworkMonitor.Core.Update;

namespace NetworkMonitor.Tests.Update
{
    public class SemanticVersionTests
    {
        [Theory]
        [InlineData("0.0.9", 0, 0, 9)]
        [InlineData("v0.0.9", 0, 0, 9)]
        [InlineData("V1.2.3", 1, 2, 3)]
        [InlineData("1.2", 1, 2, 0)]
        [InlineData("2", 2, 0, 0)]
        [InlineData("1.2.3-beta.1", 1, 2, 3)]
        public void TryParseParsesValidVersions(string text, int major, int minor, int patch)
        {
            bool parsed = SemanticVersion.TryParse(text, out SemanticVersion version);

            Assert.True(parsed);
            Assert.Equal(major, version.Major);
            Assert.Equal(minor, version.Minor);
            Assert.Equal(patch, version.Patch);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("abc")]
        [InlineData("1.x.3")]
        [InlineData("v")]
        public void TryParseRejectsInvalidVersions(string text)
        {
            bool parsed = SemanticVersion.TryParse(text, out SemanticVersion version);

            Assert.False(parsed);
        }

        [Fact]
        public void CompareToOrdersByPrecedence()
        {
            SemanticVersion.TryParse("0.0.9", out SemanticVersion newer);
            SemanticVersion.TryParse("0.0.8", out SemanticVersion older);

            Assert.True(newer.CompareTo(older) > 0);
            Assert.True(older.CompareTo(newer) < 0);
            Assert.Equal(0, newer.CompareTo(newer));
        }

        [Fact]
        public void CompareToRanksMinorAbovePatch()
        {
            SemanticVersion.TryParse("0.1.0", out SemanticVersion minorBump);
            SemanticVersion.TryParse("0.0.99", out SemanticVersion patchOnly);

            Assert.True(minorBump.CompareTo(patchOnly) > 0);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test NetworkMonitor.Tests/NetworkMonitor.Tests.csproj --filter SemanticVersionTests`
Expected: FAIL — `SemanticVersion` does not exist (compile error).

- [ ] **Step 3: Write minimal implementation**

`NetworkMonitor.Core/Update/SemanticVersion.cs`:

```csharp
using System;

namespace NetworkMonitor.Core.Update
{
    public sealed class SemanticVersion : IComparable<SemanticVersion>
    {
        private SemanticVersion(int major, int minor, int patch)
        {
            Major = major;
            Minor = minor;
            Patch = patch;
        }

        public int Major
        {
            get;
        }

        public int Minor
        {
            get;
        }

        public int Patch
        {
            get;
        }

        public static bool TryParse(string text, out SemanticVersion version)
        {
            version = new SemanticVersion(0, 0, 0);
            bool parsed = false;

            if (!string.IsNullOrWhiteSpace(text))
            {
                string candidate = text.Trim();

                if (candidate.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                {
                    candidate = candidate.Substring(1);
                }

                int dashIndex = candidate.IndexOf('-');

                if (dashIndex >= 0)
                {
                    candidate = candidate.Substring(0, dashIndex);
                }

                string[] parts = candidate.Split('.');

                if (parts.Length >= 1 && parts.Length <= 3)
                {
                    int major = 0;
                    int minor = 0;
                    int patch = 0;
                    bool componentsValid = TryParseComponent(parts, 0, ref major)
                        && TryParseComponent(parts, 1, ref minor)
                        && TryParseComponent(parts, 2, ref patch);

                    if (componentsValid)
                    {
                        version = new SemanticVersion(major, minor, patch);
                        parsed = true;
                    }

                }

            }

            return parsed;
        }

        public int CompareTo(SemanticVersion? other)
        {
            int result;

            if (other is null)
            {
                result = 1;
            }
            else if (Major != other.Major)
            {
                result = Major.CompareTo(other.Major);
            }
            else if (Minor != other.Minor)
            {
                result = Minor.CompareTo(other.Minor);
            }
            else
            {
                result = Patch.CompareTo(other.Patch);
            }

            return result;
        }

        private static bool TryParseComponent(string[] parts, int index, ref int value)
        {
            bool valid;

            if (index >= parts.Length)
            {
                value = 0;
                valid = true;
            }
            else
            {
                valid = int.TryParse(parts[index], out int parsedValue) && parsedValue >= 0;

                if (valid)
                {
                    value = parsedValue;
                }

            }

            return valid;
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test NetworkMonitor.Tests/NetworkMonitor.Tests.csproj --filter SemanticVersionTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add NetworkMonitor.Core/Update/SemanticVersion.cs NetworkMonitor.Tests/Update/SemanticVersionTests.cs
git commit -m "Add SemanticVersion parse/compare for update checks."
```

---

### Task 3: UpdateDecision (Core)

**Files:**
- Create: `NetworkMonitor.Core/Update/UpdateDecision.cs`
- Test: `NetworkMonitor.Tests/Update/UpdateDecisionTests.cs`

**Interfaces:**
- Consumes: `SemanticVersion` (Task 2).
- Produces: `static class UpdateDecision` with `static bool IsNewer(string currentVersion, string candidateVersion)` — returns true only when both parse and candidate > current.

- [ ] **Step 1: Write the failing test**

`NetworkMonitor.Tests/Update/UpdateDecisionTests.cs`:

```csharp
using Xunit;
using NetworkMonitor.Core.Update;

namespace NetworkMonitor.Tests.Update
{
    public class UpdateDecisionTests
    {
        [Theory]
        [InlineData("0.0.8", "0.0.9", true)]
        [InlineData("0.0.8", "v0.0.9", true)]
        [InlineData("0.0.9", "0.0.9", false)]
        [InlineData("0.0.9", "0.0.8", false)]
        [InlineData("1.0.0", "0.9.9", false)]
        public void IsNewerComparesVersions(string current, string candidate, bool expected)
        {
            bool actual = UpdateDecision.IsNewer(current, candidate);

            Assert.Equal(expected, actual);
        }

        [Theory]
        [InlineData("garbage", "0.0.9")]
        [InlineData("0.0.8", "garbage")]
        public void IsNewerReturnsFalseWhenEitherVersionUnparseable(string current, string candidate)
        {
            bool actual = UpdateDecision.IsNewer(current, candidate);

            Assert.False(actual);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test NetworkMonitor.Tests/NetworkMonitor.Tests.csproj --filter UpdateDecisionTests`
Expected: FAIL — `UpdateDecision` does not exist.

- [ ] **Step 3: Write minimal implementation**

`NetworkMonitor.Core/Update/UpdateDecision.cs`:

```csharp
namespace NetworkMonitor.Core.Update
{
    public static class UpdateDecision
    {
        public static bool IsNewer(string currentVersion, string candidateVersion)
        {
            bool isNewer = false;

            if (SemanticVersion.TryParse(currentVersion, out SemanticVersion current)
                && SemanticVersion.TryParse(candidateVersion, out SemanticVersion candidate))
            {
                isNewer = candidate.CompareTo(current) > 0;
            }

            return isNewer;
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test NetworkMonitor.Tests/NetworkMonitor.Tests.csproj --filter UpdateDecisionTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add NetworkMonitor.Core/Update/UpdateDecision.cs NetworkMonitor.Tests/Update/UpdateDecisionTests.cs
git commit -m "Add UpdateDecision.IsNewer version gate."
```

---

### Task 4: ReleaseInfoParser (Core)

**Files:**
- Create: `NetworkMonitor.Core/Update/ReleaseInfoParser.cs`
- Test: `NetworkMonitor.Tests/Update/ReleaseInfoParserTests.cs`

**Interfaces:**
- Consumes: `SemanticVersion` (Task 2), `AvailableUpdate` (Task 1).
- Produces: `static class ReleaseInfoParser` with `static AvailableUpdate? Parse(string releaseJson)` — reads `tag_name`, finds the asset whose name ends with `.exe` (installer) and the asset whose name ends with `.sha256` (checksum), and returns an `AvailableUpdate`; returns `null` if the JSON is malformed, has no valid `tag_name`, or is missing either asset.

- [ ] **Step 1: Write the failing test**

`NetworkMonitor.Tests/Update/ReleaseInfoParserTests.cs`:

```csharp
using Xunit;
using NetworkMonitor.Core.Update;
using NetworkMonitor.Models.Update;

namespace NetworkMonitor.Tests.Update
{
    public class ReleaseInfoParserTests
    {
        private const string ValidJson = """
        {
          "tag_name": "v0.0.9",
          "assets": [
            { "name": "Umnatha.Network.Monitor.v0.0.9.exe", "browser_download_url": "https://example/app.exe", "size": 15000000 },
            { "name": "Umnatha.Network.Monitor.v0.0.9.exe.sha256", "browser_download_url": "https://example/app.exe.sha256", "size": 64 }
          ]
        }
        """;

        [Fact]
        public void ParseReadsTagVersionAndAssetUrls()
        {
            AvailableUpdate? update = ReleaseInfoParser.Parse(ValidJson);

            Assert.NotNull(update);
            Assert.Equal("v0.0.9", update!.VersionTag);
            Assert.Equal("0.0.9", update.NormalizedVersion);
            Assert.Equal("https://example/app.exe", update.InstallerUrl);
            Assert.Equal("https://example/app.exe.sha256", update.ChecksumUrl);
            Assert.Equal(15000000, update.SizeBytes);
        }

        [Fact]
        public void ParseReturnsNullWhenExeAssetMissing()
        {
            string json = """
            { "tag_name": "v0.0.9", "assets": [ { "name": "notes.txt", "browser_download_url": "https://x/notes.txt", "size": 1 } ] }
            """;

            AvailableUpdate? update = ReleaseInfoParser.Parse(json);

            Assert.Null(update);
        }

        [Fact]
        public void ParseReturnsNullWhenChecksumAssetMissing()
        {
            string json = """
            { "tag_name": "v0.0.9", "assets": [ { "name": "app.exe", "browser_download_url": "https://x/app.exe", "size": 10 } ] }
            """;

            AvailableUpdate? update = ReleaseInfoParser.Parse(json);

            Assert.Null(update);
        }

        [Theory]
        [InlineData("")]
        [InlineData("not json")]
        [InlineData("{ \"assets\": [] }")]
        public void ParseReturnsNullForBadInput(string json)
        {
            AvailableUpdate? update = ReleaseInfoParser.Parse(json);

            Assert.Null(update);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test NetworkMonitor.Tests/NetworkMonitor.Tests.csproj --filter ReleaseInfoParserTests`
Expected: FAIL — `ReleaseInfoParser` does not exist.

- [ ] **Step 3: Write minimal implementation**

`NetworkMonitor.Core/Update/ReleaseInfoParser.cs`:

```csharp
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

                    if (root.TryGetProperty("tag_name", out JsonElement tagElement)
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
```

Note: the `installerSize = ... ? ... : ...` ternary is an assignment expression, not a `return`, so it complies with the "returns stand alone" rule.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test NetworkMonitor.Tests/NetworkMonitor.Tests.csproj --filter ReleaseInfoParserTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add NetworkMonitor.Core/Update/ReleaseInfoParser.cs NetworkMonitor.Tests/Update/ReleaseInfoParserTests.cs
git commit -m "Add ReleaseInfoParser for GitHub latest-release JSON."
```

---

### Task 5: ChecksumVerifier (Core)

**Files:**
- Create: `NetworkMonitor.Core/Update/ChecksumVerifier.cs`
- Test: `NetworkMonitor.Tests/Update/ChecksumVerifierTests.cs`

**Interfaces:**
- Produces: `static class ChecksumVerifier` with `static string ParseHashFromChecksumFile(string content)` (returns the leading hex token, lower-cased, or `string.Empty`), `static bool Verify(string expectedHashHex, string actualHashHex)` (case-insensitive, trims, both non-empty), `static Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken)` (returns lower-case hex).

- [ ] **Step 1: Write the failing test**

`NetworkMonitor.Tests/Update/ChecksumVerifierTests.cs`:

```csharp
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using NetworkMonitor.Core.Update;

namespace NetworkMonitor.Tests.Update
{
    public class ChecksumVerifierTests
    {
        [Theory]
        [InlineData("ABC123", "abc123")]
        [InlineData("abc123  *Umnatha.Network.Monitor.v0.0.9.exe", "abc123")]
        [InlineData("  deadBEEF \n", "deadbeef")]
        [InlineData("", "")]
        public void ParseHashExtractsLeadingHexTokenLowerCased(string content, string expected)
        {
            string actual = ChecksumVerifier.ParseHashFromChecksumFile(content);

            Assert.Equal(expected, actual);
        }

        [Fact]
        public void VerifyIsCaseInsensitive()
        {
            Assert.True(ChecksumVerifier.Verify("ABC123", "abc123"));
            Assert.False(ChecksumVerifier.Verify("abc123", "abc124"));
            Assert.False(ChecksumVerifier.Verify("", "abc123"));
        }

        [Fact]
        public async Task ComputeSha256MatchesKnownHashOfEmptyFile()
        {
            string path = Path.Combine(Path.GetTempPath(), $"nm-update-test-{System.Guid.NewGuid():N}.bin");
            await File.WriteAllBytesAsync(path, System.Array.Empty<byte>());

            try
            {
                string hash = await ChecksumVerifier.ComputeSha256Async(path, CancellationToken.None);

                Assert.Equal("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", hash);
            }
            finally
            {
                File.Delete(path);
            }

        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test NetworkMonitor.Tests/NetworkMonitor.Tests.csproj --filter ChecksumVerifierTests`
Expected: FAIL — `ChecksumVerifier` does not exist.

- [ ] **Step 3: Write minimal implementation**

`NetworkMonitor.Core/Update/ChecksumVerifier.cs`:

```csharp
using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace NetworkMonitor.Core.Update
{
    public static class ChecksumVerifier
    {
        public static string ParseHashFromChecksumFile(string content)
        {
            string hash = string.Empty;

            if (!string.IsNullOrWhiteSpace(content))
            {
                string[] tokens = content.Trim().Split(
                    new[] { ' ', '\t', '\r', '\n', '*' },
                    StringSplitOptions.RemoveEmptyEntries);

                if (tokens.Length > 0)
                {
                    hash = tokens[0].ToLowerInvariant();
                }

            }

            return hash;
        }

        public static bool Verify(string expectedHashHex, string actualHashHex)
        {
            bool verified = false;

            if (!string.IsNullOrWhiteSpace(expectedHashHex) && !string.IsNullOrWhiteSpace(actualHashHex))
            {
                verified = string.Equals(
                    expectedHashHex.Trim(),
                    actualHashHex.Trim(),
                    StringComparison.OrdinalIgnoreCase);
            }

            return verified;
        }

        public static async Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken)
        {
            await using FileStream stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);

            using SHA256 sha256 = SHA256.Create();
            byte[] hashBytes = await sha256.ComputeHashAsync(stream, cancellationToken);
            string hash = Convert.ToHexString(hashBytes).ToLowerInvariant();

            return hash;
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test NetworkMonitor.Tests/NetworkMonitor.Tests.csproj --filter ChecksumVerifierTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add NetworkMonitor.Core/Update/ChecksumVerifier.cs NetworkMonitor.Tests/Update/ChecksumVerifierTests.cs
git commit -m "Add ChecksumVerifier (parse, compare, SHA-256 compute)."
```

---

### Task 6: AutoCheckForUpdates setting (Services)

**Files:**
- Modify: `NetworkMonitor.Services/Data/Settings.cs` (add property after `RateUnitMode`, before `Save()`)
- Modify: `NetworkMonitor/appsettings.json` (add key under the `Scanner` section)

**Interfaces:**
- Produces: `Settings.AutoCheckForUpdates` (bool, default `true`), persisted by the existing `Save()`.

- [ ] **Step 1: Add the setting property**

In `NetworkMonitor.Services/Data/Settings.cs`, immediately after the `RateUnitMode` property (line ~180) and before `public void Save()`:

```csharp
        public bool AutoCheckForUpdates
        {
            get;
            set;
        } = true;
```

- [ ] **Step 2: Add the default to appsettings.json**

In `NetworkMonitor/appsettings.json`, add `"AutoCheckForUpdates": true` inside the `Scanner` object (match existing key style/comma placement).

- [ ] **Step 3: Build to verify**

Run: `dotnet build NetworkMonitor.Services/NetworkMonitor.Services.csproj -p:Platform=x64`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add NetworkMonitor.Services/Data/Settings.cs NetworkMonitor/appsettings.json
git commit -m "Add AutoCheckForUpdates setting (default on)."
```

---

### Task 7: InstallerLauncher (Services)

**Files:**
- Create: `NetworkMonitor.Services/Update/IInstallerLauncher.cs`
- Create: `NetworkMonitor.Services/Update/InstallerLauncher.cs`

**Interfaces:**
- Produces: `interface IInstallerLauncher { void LaunchAndExit(string installerPath); }`, and `InstallerLauncher` which starts the installer with `/SILENT /SUPPRESSMSGBOXES /NORESTART` via `ShellExecute` (inherits the app's elevation — no extra UAC), then calls `Environment.Exit(0)` so the single-instance mutex releases and files unlock before the installer replaces them.

Note: no unit test — this launches a process and terminates the app; verified by build + Task 16 smoke.

- [ ] **Step 1: Create the interface**

`NetworkMonitor.Services/Update/IInstallerLauncher.cs`:

```csharp
namespace NetworkMonitor.Services.Update
{
    public interface IInstallerLauncher
    {
        void LaunchAndExit(string installerPath);
    }
}
```

- [ ] **Step 2: Create the implementation**

`NetworkMonitor.Services/Update/InstallerLauncher.cs`:

```csharp
using System;
using System.Diagnostics;
using NetworkMonitor.Core.Common;

namespace NetworkMonitor.Services.Update
{
    public sealed class InstallerLauncher : IInstallerLauncher
    {
        public void LaunchAndExit(string installerPath)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = installerPath,
                Arguments = "/SILENT /SUPPRESSMSGBOXES /NORESTART",
                UseShellExecute = true
            };

            AppLog.Info($"Launching update installer: {installerPath}");
            Process.Start(startInfo);

            Environment.Exit(0);
        }
    }
}
```

Note: `/NORESTART` prevents Inno from rebooting; the app relaunch is handled by the `.iss` `[Run]` entry (Task 15), not by `/RESTART`.

- [ ] **Step 3: Build to verify**

Run: `dotnet build NetworkMonitor.Services/NetworkMonitor.Services.csproj -p:Platform=x64`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add NetworkMonitor.Services/Update/IInstallerLauncher.cs NetworkMonitor.Services/Update/InstallerLauncher.cs
git commit -m "Add InstallerLauncher (silent launch + app exit)."
```

---

### Task 8: UpdateService (Services)

**Files:**
- Create: `NetworkMonitor.Services/Update/IUpdateService.cs`
- Create: `NetworkMonitor.Services/Update/UpdateService.cs`

**Interfaces:**
- Consumes: `UpdateCheckResult`/`AvailableUpdate` (Task 1), `ReleaseInfoParser`/`UpdateDecision`/`ChecksumVerifier` (Tasks 3-5), `IInstallerLauncher` (Task 7), `AppInfo.GetVersion()` (`NetworkMonitor.Services.Platform`), `AppLog` (`NetworkMonitor.Core.Common`), `AppPaths.AppDataFolder` (the helper `Settings` already uses).
- Produces:
  - `interface IUpdateService` with `event EventHandler<UpdateCheckResult>? CheckCompleted;`, `Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken)`, `Task<string> DownloadAndVerifyAsync(AvailableUpdate update, IProgress<double> progress, CancellationToken cancellationToken)` (returns the verified installer path; throws on download failure or checksum mismatch, deleting the partial file), `void LaunchInstaller(string installerPath)`.
  - `UpdateService(HttpClient httpClient, IInstallerLauncher launcher)`.

Note: no unit test (network/file/process, and Tests can't reference Services); verified by build + Task 16 smoke.

- [ ] **Step 1: Create the interface**

`NetworkMonitor.Services/Update/IUpdateService.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using NetworkMonitor.Models.Update;

namespace NetworkMonitor.Services.Update
{
    public interface IUpdateService
    {
        event EventHandler<UpdateCheckResult>? CheckCompleted;

        Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken);

        Task<string> DownloadAndVerifyAsync(AvailableUpdate update, IProgress<double> progress, CancellationToken cancellationToken);

        void LaunchInstaller(string installerPath);
    }
}
```

- [ ] **Step 2: Create the implementation**

`NetworkMonitor.Services/Update/UpdateService.cs`:

```csharp
using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NetworkMonitor.Core.Common;
using NetworkMonitor.Core.Update;
using NetworkMonitor.Models.Update;
using NetworkMonitor.Services.Data;
using NetworkMonitor.Services.Platform;

namespace NetworkMonitor.Services.Update
{
    public sealed class UpdateService : IUpdateService
    {
        private const string LatestReleaseUrl =
            "https://api.github.com/repos/jazzzsoftware/UmnathaNetworkMonitor/releases/latest";

        private readonly HttpClient _httpClient;
        private readonly IInstallerLauncher _launcher;

        public UpdateService(HttpClient httpClient, IInstallerLauncher launcher)
        {
            _httpClient = httpClient;
            _launcher = launcher;
        }

        public event EventHandler<UpdateCheckResult>? CheckCompleted;

        public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken)
        {
            UpdateCheckResult result;

            try
            {
                using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);
                request.Headers.Add("Accept", "application/vnd.github+json");
                request.Headers.Add("User-Agent", "UmnathaNetworkMonitor");

                using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
                response.EnsureSuccessStatusCode();

                string json = await response.Content.ReadAsStringAsync(cancellationToken);
                AvailableUpdate? update = ReleaseInfoParser.Parse(json);

                if (update is null)
                {
                    result = UpdateCheckResult.Failed("The latest release could not be read.");
                }
                else if (UpdateDecision.IsNewer(AppInfo.GetVersion(), update.NormalizedVersion))
                {
                    result = UpdateCheckResult.Available(update);
                }
                else
                {
                    result = UpdateCheckResult.UpToDate();
                }

            }
            catch (OperationCanceledException)
            {
                result = UpdateCheckResult.Failed("The update check was cancelled.");
            }
            catch (Exception exception)
            {
                AppLog.Error("UpdateService.Check", exception);
                result = UpdateCheckResult.Failed("Couldn't check for updates — check your connection.");
            }

            CheckCompleted?.Invoke(this, result);

            return result;
        }

        public async Task<string> DownloadAndVerifyAsync(AvailableUpdate update, IProgress<double> progress, CancellationToken cancellationToken)
        {
            string updatesFolder = Path.Combine(AppPaths.AppDataFolder, "Updates");
            Directory.CreateDirectory(updatesFolder);
            CleanFolder(updatesFolder);

            string installerPath = Path.Combine(updatesFolder, $"UmnathaNetworkMonitor-{update.NormalizedVersion}.exe");

            try
            {
                string checksumText = await _httpClient.GetStringAsync(update.ChecksumUrl, cancellationToken);
                string expectedHash = ChecksumVerifier.ParseHashFromChecksumFile(checksumText);

                await DownloadToFileAsync(update.InstallerUrl, installerPath, update.SizeBytes, progress, cancellationToken);

                string actualHash = await ChecksumVerifier.ComputeSha256Async(installerPath, cancellationToken);

                if (!ChecksumVerifier.Verify(expectedHash, actualHash))
                {
                    throw new InvalidOperationException("The downloaded update failed its checksum check.");
                }

            }
            catch (Exception)
            {
                TryDelete(installerPath);

                throw;
            }

            return installerPath;
        }

        public void LaunchInstaller(string installerPath)
        {
            _launcher.LaunchAndExit(installerPath);
        }

        private async Task DownloadToFileAsync(string url, string destinationPath, long expectedSize, IProgress<double> progress, CancellationToken cancellationToken)
        {
            using HttpResponseMessage response = await _httpClient.GetAsync(
                url,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            response.EnsureSuccessStatusCode();

            long totalBytes = response.Content.Headers.ContentLength ?? expectedSize;
            await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using FileStream destination = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);

            byte[] buffer = new byte[81920];
            long receivedBytes = 0;
            int read;

            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                receivedBytes += read;

                if (totalBytes > 0)
                {
                    double fraction = (double)receivedBytes / totalBytes;
                    progress.Report(fraction);
                }

            }

        }

        private static void CleanFolder(string folder)
        {

            try
            {
                foreach (string file in Directory.EnumerateFiles(folder))
                {
                    TryDelete(file);
                }

            }
            catch (Exception exception)
            {
                AppLog.Error("UpdateService.CleanFolder", exception);
            }

        }

        private static void TryDelete(string path)
        {

            try
            {

                if (File.Exists(path))
                {
                    File.Delete(path);
                }

            }
            catch (Exception exception)
            {
                AppLog.Error("UpdateService.TryDelete", exception);
            }

        }
    }
}
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build NetworkMonitor.Services/NetworkMonitor.Services.csproj -p:Platform=x64`
Expected: Build succeeded. (If `AppPaths` is in a different namespace than imported, add the correct `using` — it is the same helper `Settings.cs` references for `AppDataFolder`.)

- [ ] **Step 4: Commit**

```bash
git add NetworkMonitor.Services/Update/IUpdateService.cs NetworkMonitor.Services/Update/UpdateService.cs
git commit -m "Add UpdateService (check, download+verify, launch)."
```

---

### Task 9: UpdateCheckWorker (Services)

**Files:**
- Create: `NetworkMonitor.Services/Update/UpdateCheckWorker.cs`

**Interfaces:**
- Consumes: `IUpdateService` (Task 8), `Settings` (Task 6), `AppLog`.
- Produces: `UpdateCheckWorker(IUpdateService updateService, Settings settings) : BackgroundService` — waits ~10s after start, then loops every 24h; each iteration calls `updateService.CheckAsync` only when `settings.AutoCheckForUpdates` is true (the UI subscribes to `CheckCompleted`). Mirrors `ScanWorker`'s try/catch/`AppLog` loop shape.

Note: no unit test; verified by build + Task 16 smoke.

- [ ] **Step 1: Create the worker**

`NetworkMonitor.Services/Update/UpdateCheckWorker.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using NetworkMonitor.Core.Common;
using NetworkMonitor.Services.Data;

namespace NetworkMonitor.Services.Update
{
    public sealed class UpdateCheckWorker(IUpdateService updateService, Settings settings) : BackgroundService
    {
        private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {

            try
            {
                await Task.Delay(InitialDelay, stoppingToken);
                await CheckIfEnabledAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                AppLog.Error("UpdateCheckWorker.Initial", exception);
            }

            while (!stoppingToken.IsCancellationRequested)
            {

                try
                {
                    await Task.Delay(CheckInterval, stoppingToken);
                    await CheckIfEnabledAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception exception)
                {
                    AppLog.Error("UpdateCheckWorker.Loop", exception);
                }

            }

        }

        private async Task CheckIfEnabledAsync(CancellationToken cancellationToken)
        {

            if (settings.AutoCheckForUpdates)
            {
                await updateService.CheckAsync(cancellationToken);
            }

        }
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build NetworkMonitor.Services/NetworkMonitor.Services.csproj -p:Platform=x64`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add NetworkMonitor.Services/Update/UpdateCheckWorker.cs
git commit -m "Add UpdateCheckWorker periodic update-check loop."
```

---

### Task 10: UpdateViewModel (App)

**Files:**
- Create: `NetworkMonitor/ViewModels/UpdateViewModel.cs`

**Interfaces:**
- Consumes: `IUpdateService` (Task 8), `UpdateCheckResult`/`AvailableUpdate`/`UpdateAvailability` (Task 1), `DispatcherQueue`.
- Produces: `UpdateViewModel(IUpdateService updateService) : ObservableObject` exposing hand-written observable properties `IsBannerOpen`, `Message`, `Severity` (`Microsoft.UI.Xaml.Controls.InfoBarSeverity`), `IsBusy`, `DownloadProgress`, and commands `CheckNowCommand`, `UpdateNowCommand`, `DismissCommand`. Subscribes to `updateService.CheckCompleted` and marshals updates to the UI thread.

Note: no unit test (UI thread + ObservableObject); verified by build + Task 16 smoke.

- [ ] **Step 1: Create the view model**

`NetworkMonitor/ViewModels/UpdateViewModel.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using NetworkMonitor.Models.Update;
using NetworkMonitor.Services.Update;

namespace NetworkMonitor.ViewModels
{
    public sealed class UpdateViewModel : ObservableObject
    {
        private readonly IUpdateService _updateService;
        private readonly DispatcherQueue _dispatcher;
        private AvailableUpdate? _pendingUpdate;

        public UpdateViewModel(IUpdateService updateService)
        {
            _updateService = updateService;
            _dispatcher = DispatcherQueue.GetForCurrentThread();
            CheckNowCommand = new AsyncRelayCommand(CheckNowAsync);
            UpdateNowCommand = new AsyncRelayCommand(UpdateNowAsync);
            DismissCommand = new RelayCommand(Dismiss);

            _updateService.CheckCompleted += OnCheckCompleted;
        }

        private bool _isBannerOpen;

        public bool IsBannerOpen
        {
            get => _isBannerOpen;
            set
            {
                SetProperty(ref _isBannerOpen, value);
            }
        }

        private string _message = string.Empty;

        public string Message
        {
            get => _message;
            set
            {
                SetProperty(ref _message, value);
            }
        }

        private InfoBarSeverity _severity = InfoBarSeverity.Informational;

        public InfoBarSeverity Severity
        {
            get => _severity;
            set
            {
                SetProperty(ref _severity, value);
            }
        }

        private bool _isBusy;

        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                SetProperty(ref _isBusy, value);
            }
        }

        private double _downloadProgress;

        public double DownloadProgress
        {
            get => _downloadProgress;
            set
            {
                SetProperty(ref _downloadProgress, value);
            }
        }

        public IAsyncRelayCommand CheckNowCommand
        {
            get;
        }

        public IAsyncRelayCommand UpdateNowCommand
        {
            get;
        }

        public IRelayCommand DismissCommand
        {
            get;
        }

        private async Task CheckNowAsync()
        {
            await _updateService.CheckAsync(CancellationToken.None);
        }

        private async Task UpdateNowAsync()
        {
            AvailableUpdate? update = _pendingUpdate;

            if (update is not null && !IsBusy)
            {
                IsBusy = true;
                DownloadProgress = 0;
                Message = $"Downloading version {update.NormalizedVersion}…";
                Severity = InfoBarSeverity.Informational;

                Progress<double> progress = new Progress<double>(fraction =>
                {
                    DownloadProgress = fraction * 100.0;
                });

                try
                {
                    string installerPath = await _updateService.DownloadAndVerifyAsync(update, progress, CancellationToken.None);
                    _updateService.LaunchInstaller(installerPath);
                }
                catch (Exception)
                {
                    IsBusy = false;
                    Severity = InfoBarSeverity.Error;
                    Message = "The update could not be downloaded or verified. Please try again later.";
                    IsBannerOpen = true;
                }

            }

        }

        private void Dismiss()
        {
            IsBannerOpen = false;
        }

        private void OnCheckCompleted(object? sender, UpdateCheckResult result)
        {
            _dispatcher.TryEnqueue(() =>
            {
                Apply(result);
            });
        }

        private void Apply(UpdateCheckResult result)
        {

            if (result.Availability == UpdateAvailability.UpdateAvailable && result.Update is not null)
            {
                _pendingUpdate = result.Update;
                Severity = InfoBarSeverity.Informational;
                Message = $"Version {result.Update.NormalizedVersion} is available.";
                IsBannerOpen = true;
            }
            else if (result.Availability == UpdateAvailability.CheckFailed)
            {
                _pendingUpdate = null;
                Severity = InfoBarSeverity.Error;
                Message = result.ErrorMessage ?? "Couldn't check for updates.";
                IsBannerOpen = true;
            }
            else
            {
                _pendingUpdate = null;
                IsBannerOpen = false;
            }

        }
    }
}
```

Note: `AsyncRelayCommand`/`RelayCommand` (CommunityToolkit.Mvvm.Input) are used directly — only `[ObservableProperty]` is banned by the conventions, and all observable properties here are hand-written with `SetProperty`.

- [ ] **Step 2: Build to verify**

Run: `dotnet build NetworkMonitor/NetworkMonitor.csproj -p:Platform=x64`
Expected: FAIL at DI resolution is not tested here; the project should **compile**. Expected: Build succeeded. (It will not run until Task 11 registers it.)

- [ ] **Step 3: Commit**

```bash
git add NetworkMonitor/ViewModels/UpdateViewModel.cs
git commit -m "Add UpdateViewModel for the update banner."
```

---

### Task 11: Register update services in DI (App)

**Files:**
- Modify: `NetworkMonitor/App.xaml.cs` (add registrations inside `ConfigureServices`, after the `SpeedTestWorker` block ~line 131; add `using NetworkMonitor.Services.Update;`)

**Interfaces:**
- Consumes: `UpdateService`/`IUpdateService`/`IInstallerLauncher`/`InstallerLauncher`/`UpdateCheckWorker` (Tasks 7-9), `UpdateViewModel` (Task 10).
- Produces: DI graph — `IInstallerLauncher`→`InstallerLauncher` (singleton), `IUpdateService`→`UpdateService` with a dedicated `HttpClient` (singleton), `UpdateCheckWorker` hosted service, `UpdateViewModel` (singleton).

- [ ] **Step 1: Add the `using`**

At the top of `NetworkMonitor/App.xaml.cs`, with the other `NetworkMonitor.Services.*` usings:

```csharp
using NetworkMonitor.Services.Update;
```

- [ ] **Step 2: Add the registrations**

In `ConfigureServices`, immediately after the `services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<SpeedTestWorker>());` line:

```csharp
                        services.AddSingleton<IInstallerLauncher, InstallerLauncher>();
                        services.AddSingleton<IUpdateService>(serviceProvider =>
                        {
                            HttpClient updateHttpClient = new HttpClient
                            {
                                Timeout = TimeSpan.FromMinutes(10)
                            };

                            IInstallerLauncher installerLauncher = serviceProvider.GetRequiredService<IInstallerLauncher>();
                            UpdateService updateService = new UpdateService(updateHttpClient, installerLauncher);

                            return updateService;
                        });
                        services.AddSingleton<UpdateCheckWorker>();
                        services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<UpdateCheckWorker>());
                        services.AddSingleton<UpdateViewModel>();
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build NetworkMonitor.slnx -p:Platform=x64`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add NetworkMonitor/App.xaml.cs
git commit -m "Register update services and view model in DI."
```

---

### Task 12: Update banner InfoBar (App)

**Files:**
- Modify: `NetworkMonitor/MainWindow.xaml` (add row layout + `InfoBar` above the `NavigationView`)
- Modify: `NetworkMonitor/MainWindow.xaml.cs` (resolve `UpdateViewModel`, expose it for `x:Bind`)

**Interfaces:**
- Consumes: `UpdateViewModel` (Task 10) from DI.
- Produces: a non-modal `InfoBar` bound to `UpdateViewModel` with **Update now** / **Later** actions and a download `ProgressBar`.

- [ ] **Step 1: Expose the view model from code-behind**

In `NetworkMonitor/MainWindow.xaml.cs`, add a `using Microsoft.Extensions.DependencyInjection;` (if absent) and a property assigned in the constructor **before** `InitializeComponent()`:

```csharp
        public UpdateViewModel UpdateViewModel
        {
            get;
        }
```

In the constructor, before `InitializeComponent();`:

```csharp
            UpdateViewModel = App.AppHost.Services.GetRequiredService<UpdateViewModel>();
```

(Add `using NetworkMonitor.ViewModels;` if not already present.)

- [ ] **Step 2: Add the InfoBar to the XAML**

In `NetworkMonitor/MainWindow.xaml`, replace the opening `<Grid>` (line 9) with a two-row grid, place the existing `NavigationView` in row 1 and the existing `ToastBorder` in row 1, and add the `InfoBar` in row 0. The new top of the `<Grid>` becomes:

```xml
    <Grid>

        <Grid.RowDefinitions>

            <RowDefinition
                Height="Auto" />

            <RowDefinition
                Height="*" />

        </Grid.RowDefinitions>

        <InfoBar
            Grid.Row="0"
            Title="Software update"
            IsClosable="True"
            CloseButtonCommand="{x:Bind UpdateViewModel.DismissCommand}"
            IsOpen="{x:Bind UpdateViewModel.IsBannerOpen, Mode=OneWay}"
            Severity="{x:Bind UpdateViewModel.Severity, Mode=OneWay}"
            Message="{x:Bind UpdateViewModel.Message, Mode=OneWay}">

            <InfoBar.ActionButton>

                <StackPanel
                    Orientation="Horizontal"
                    Spacing="8">

                    <ProgressBar
                        Width="140"
                        VerticalAlignment="Center"
                        Visibility="{x:Bind UpdateViewModel.IsBusy, Mode=OneWay}"
                        Value="{x:Bind UpdateViewModel.DownloadProgress, Mode=OneWay}" />

                    <Button
                        Content="Update now"
                        IsEnabled="{x:Bind UpdateViewModel.IsBusy, Mode=OneWay, Converter={StaticResource InverseBoolConverter}}"
                        Command="{x:Bind UpdateViewModel.UpdateNowCommand}" />

                </StackPanel>

            </InfoBar.ActionButton>

        </InfoBar>
```

Then set `Grid.Row="1"` on the existing `NavigationView` (add it as the first attribute after `x:Name="NavView"`) and add `Grid.Row="1"` to the existing `ToastBorder` (as the first attribute after `x:Name="ToastBorder"`).

Note on the converter: if the project has no `InverseBoolConverter` resource in scope for this window, replace the `IsEnabled` line with an equivalent already used in the codebase, or drop the `IsEnabled` attribute entirely (the command still no-ops while `IsBusy` because `UpdateNowAsync` guards on `!IsBusy`). Confirm by grepping `InverseBool` before wiring; prefer removing the attribute if no converter exists.

- [ ] **Step 3: Build to verify**

Run: `dotnet build NetworkMonitor.slnx -p:Platform=x64`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add NetworkMonitor/MainWindow.xaml NetworkMonitor/MainWindow.xaml.cs
git commit -m "Add non-modal update banner to the main window."
```

---

### Task 13: Settings — auto-check toggle, manual check, status (App)

**Files:**
- Modify: `NetworkMonitor/ViewModels/SettingsViewModel.cs` (add `AutoCheckForUpdates` observable property that persists, and expose the `UpdateViewModel`)
- Modify: `NetworkMonitor/Views/SettingsPage.xaml` (add a toggle + "Check for updates" button near the About section)
- Modify: `NetworkMonitor/Views/SettingsPage.xaml.cs` (if the manual-check wiring needs a handler; prefer command binding)

**Interfaces:**
- Consumes: `Settings` (Task 6), `UpdateViewModel` (Task 10) via DI.
- Produces: a persisted `SettingsViewModel.AutoCheckForUpdates` toggle and a manual **Check for updates** button bound to `UpdateViewModel.CheckNowCommand`.

- [ ] **Step 1: Add the toggle to SettingsViewModel**

In `NetworkMonitor/ViewModels/SettingsViewModel.cs`, add a hand-written observable property in the Properties section that writes through to `Settings` and saves (match the existing toggle pattern already in this file — e.g. how `EnableLogging`/`ShowToasts` persist). Concretely:

```csharp
        public bool AutoCheckForUpdates
        {
            get => _settings.AutoCheckForUpdates;
            set
            {

                if (_settings.AutoCheckForUpdates != value)
                {
                    _settings.AutoCheckForUpdates = value;
                    _settings.Save();
                    OnPropertyChanged();
                }

            }
        }
```

(If `SettingsViewModel` holds its injected `Settings` under a different field name than `_settings`, use that name. Confirm by reading the top of the file first.)

- [ ] **Step 2: Expose UpdateViewModel for the manual button**

Two options — pick whichever matches how `SettingsPage` already reaches services:
- If `SettingsPage.xaml.cs` resolves things from `App.AppHost.Services` (it already resolves `SettingsViewModel` there): add `public UpdateViewModel UpdateViewModel { get; }` to `SettingsPage.xaml.cs` and assign `UpdateViewModel = App.AppHost.Services.GetRequiredService<UpdateViewModel>();` in the constructor.

- [ ] **Step 3: Add the UI to SettingsPage.xaml**

Near the existing About button (`AboutClick`, ~line 818-820), add — following the XAML conventions and matching the surrounding toggle rows:

```xml
                <ToggleSwitch
                    Header="Automatically check for updates"
                    IsOn="{x:Bind ViewModel.AutoCheckForUpdates, Mode=TwoWay}" />

                <Button
                    Content="Check for updates"
                    Command="{x:Bind UpdateViewModel.CheckNowCommand}" />

                <InfoBar
                    IsClosable="False"
                    IsOpen="{x:Bind UpdateViewModel.IsBannerOpen, Mode=OneWay}"
                    Severity="{x:Bind UpdateViewModel.Severity, Mode=OneWay}"
                    Message="{x:Bind UpdateViewModel.Message, Mode=OneWay}" />
```

- [ ] **Step 4: Build to verify**

Run: `dotnet build NetworkMonitor.slnx -p:Platform=x64`
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add NetworkMonitor/ViewModels/SettingsViewModel.cs NetworkMonitor/Views/SettingsPage.xaml NetworkMonitor/Views/SettingsPage.xaml.cs
git commit -m "Add auto-update toggle and manual check to Settings."
```

---

### Task 14: build-installer.ps1 emits a SHA-256 companion (Build)

**Files:**
- Modify: `Installer/build-installer.ps1` (after the "Installer built" line)

**Interfaces:**
- Produces: alongside `Output/Umnatha Network Monitor v{Version}.exe`, a `.sha256` file containing the lower-case SHA-256 hash of the installer, named `<installer>.exe.sha256`.

- [ ] **Step 1: Append checksum emission**

At the end of `Installer/build-installer.ps1`, after the existing `Write-Host "Installer built: $outFile"` line, add:

```powershell
$hash = (Get-FileHash -Algorithm SHA256 -Path $outFile).Hash.ToLower()
$sha256File = "$outFile.sha256"
Set-Content -Path $sha256File -Value $hash -Encoding ascii -NoNewline
Write-Host "Checksum written: $sha256File ($hash)" -ForegroundColor Green
```

- [ ] **Step 2: Verify (dry check without a full publish)**

If a prior publish exists, run: `Installer\build-installer.ps1 -SkipPublish`
Expected: prints `Installer built: ...` then `Checksum written: ...<64-hex>...`; a `.sha256` file appears next to the installer in `Installer/Output`. Confirm the hash matches: `(Get-FileHash -Algorithm SHA256 -Path "<outFile>").Hash.ToLower()`.

- [ ] **Step 3: Commit**

```bash
git add Installer/build-installer.ps1
git commit -m "Emit SHA-256 companion file when building the installer."
```

---

### Task 15: Installer silent-relaunch fix (Build)

**Files:**
- Modify: `Installer/NetworkMonitor.iss` (`[Setup]` + `[Run]`)

**Interfaces:**
- Produces: an installer that, when run with `/SILENT`, closes the running app, replaces files, and **relaunches** the app — so the in-app update completes without user clicks.

- [ ] **Step 1: Add close/restart handling to `[Setup]`**

In `Installer/NetworkMonitor.iss`, add these two lines to the `[Setup]` section (e.g. after `PrivilegesRequired=admin`):

```
CloseApplications=yes
RestartApplications=no
```

- [ ] **Step 2: Add a silent-safe relaunch `[Run]` entry**

The existing `[Run]` entry uses `skipifsilent`, so it does not relaunch during a silent update. Add a second relaunch entry that runs **only** when silent (so a normal interactive install still uses the existing `postinstall` entry, and a silent update gets exactly one relaunch):

```
Filename: "{app}\{#MyAppExeName}"; Parameters: "--minimized"; Flags: nowait skipifnotsilent
```

Place it in the `[Run]` section after the existing lines. Result: interactive installs relaunch via the existing `postinstall`+`skipifsilent` line; silent updates relaunch via the new `skipifnotsilent` line, minimized (matching the startup-task launch style).

- [ ] **Step 3: Verify it compiles**

Run: `Installer\build-installer.ps1 -SkipPublish` (or invoke ISCC directly per the script). 
Expected: ISCC compiles with no errors; the installer builds. (Full behaviour is validated in Task 16 smoke.)

- [ ] **Step 4: Commit**

```bash
git add Installer/NetworkMonitor.iss
git commit -m "Relaunch app after silent update; close running app during install."
```

---

### Task 16: Release-process docs + end-to-end smoke (Docs)

**Files:**
- Modify: `CONTRIBUTING.md` (release section: upload both `.exe` and `.exe.sha256`)
- Modify: `NetworkMonitor.slnx` only if a **new** doc file is created (none expected here — `CONTRIBUTING.md` is already registered)

**Interfaces:**
- Produces: documented release steps and a verified end-to-end update.

- [ ] **Step 1: Document the two-asset release**

In `CONTRIBUTING.md`, in the release/maintainer section, add a short subsection stating: build with `Installer\build-installer.ps1 -Version X.Y.Z`; the GitHub release for tag `vX.Y.Z` must include **both** `Umnatha Network Monitor vX.Y.Z.exe` **and** its `.exe.sha256` companion; the in-app updater reads `tag_name` and both assets from the latest release.

- [ ] **Step 2: End-to-end smoke test (manual)**

Perform against a test release whose version is higher than the installed build:
1. Temporarily set `<Version>` to a value **lower** than the latest published release (or publish a higher test release), build, install, and run.
2. Confirm the InfoBar banner appears: *"Version X.Y.Z is available."*
3. Click **Later** → banner closes; reappears after restart.
4. Click **Update now** → progress bar advances; app closes; installer runs silently (progress visible); app relaunches minimized on the updated version.
5. Confirm the About box shows the new version.
6. Failure path: point the checksum asset at a wrong hash (or disconnect mid-download) → confirm the error InfoBar shows and no installer launches.
7. Settings: toggle **Automatically check for updates** off → confirm `settings.json` shows `"AutoCheckForUpdates": false` and the startup/24h checks stop; the manual **Check for updates** button still works.

- [ ] **Step 3: Commit**

```bash
git add CONTRIBUTING.md
git commit -m "Document two-asset release process for auto-updates."
```

---

## Self-Review

**Spec coverage:**
- Check triggers (startup / 24h / manual) → Tasks 9 (worker), 13 (manual button). ✓
- InfoBar notify, mirrored in Settings → Tasks 12, 13. ✓
- Later/cancel, download only on Update now → Task 10 (`UpdateNowAsync` guard, `Dismiss`). ✓
- Download with progress → Tasks 8 (`DownloadToFileAsync` + `IProgress`), 10, 12. ✓
- SHA-256 verify before launch, companion `.sha256` → Tasks 5, 8, 14. ✓
- Silent `/SILENT` install + auto-relaunch + exit to unlock → Tasks 7, 15. ✓
- Failures never silent (auto + manual) → Task 10 `Apply`/`UpdateNowAsync` error branches, Task 8 `Failed` results. ✓
- `AutoCheckForUpdates` setting (default on) → Tasks 6, 9, 13. ✓
- Layering Models/Core/Services/App → Tasks 1-13 placed accordingly. ✓
- GitHub Releases feed, no auth → Task 8. ✓
- Download folder + cleanup → Task 8 (`Updates` folder, `CleanFolder`). ✓
- Testing on Core/Models → Tasks 1-5. Service/App unit tests are **out** by the Tests-project boundary (Global Constraints); the spec's "fake HttpMessageHandler" service test is replaced by Core coverage + Task 16 smoke — a deliberate, documented deviation, not a gap.

**Placeholder scan:** No TBD/TODO; every code step contains complete code; the two "confirm existing pattern" notes (converter in Task 12, `_settings` field name in Task 13) give an explicit fallback action rather than deferring work.

**Type consistency:** `AvailableUpdate.NormalizedVersion` used consistently in Tasks 4, 8, 10; `IUpdateService.CheckCompleted`/`CheckAsync`/`DownloadAndVerifyAsync`/`LaunchInstaller` names match across Tasks 8, 9, 10; `ChecksumVerifier.ParseHashFromChecksumFile`/`Verify`/`ComputeSha256Async` match across Tasks 5, 8; `IInstallerLauncher.LaunchAndExit` matches Tasks 7, 8.
