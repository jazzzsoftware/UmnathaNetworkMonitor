# UI Automated Testing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** One elevated command, `dotnet run --project Tools/UITests`, that drives the installed build through every page and control it exposes against seeded data, proves the uninstall → baseline install → update → verify lifecycle, and leaves an HTML report and a non-zero exit code behind on any failure.

**Architecture:** A console runner at `Tools/UITests/` drives the **installed** app through FlaUI (Windows UI Automation) in-process — no daemon, no Node. Three small additions to shipping code make the app observable: a data-folder override so the run never touches real history, a culture-invariant draw summary on the two charts, and automation identifiers on the controls the suite drives. Everything the runner needs that is *pure* (folder resolution, summary formatting) goes into `NetworkMonitor.Core` so the existing 501-test project covers it; everything else is proven by the suite driving the real app.

**Tech Stack:** .NET 10, FlaUI 5.0.0 (`FlaUI.Core` + `FlaUI.UIA3`), EF Core 10 + SQLite, WinUI 3 (the app under test), xunit (for the Core additions only).

**Spec:** `Documents/superpowers/specs/2026-08-20-ui-automated-testing-design.md`

---

## Prerequisite — RESOLVED 2026-08-20

**v0.0.12 is now installed** at `C:\Program Files\Umnatha Network Monitor\`, with the uninstall key `{7074c3a8-a61b-4e4a-9e6c-dedc9a62ae94}_is1` reporting `DisplayVersion 0.0.12`. The installer's SHA-256 was verified against the release's `.sha256` asset before it was run.

The operator's standing instruction is **"always assume it is not installed"** — so the runner must acquire and install what it needs rather than depending on this, or on any manual step. That is amendment C in Task 8. The section below is kept for the record of what the original plan assumed.

## Prerequisite as originally written — the app was not installed

**Verified on this machine on 2026-08-20:** `C:\Program Files\Umnatha Network Monitor` does not exist, and there is no uninstall entry under `HKLM`, `HKLM\WOW6432Node` or `HKCU` matching `*Umnatha*` or `*Network Monitor*`. `Tools/Installer/Output/` holds only `Umnatha Network Monitor v0.0.10.exe`.

The spec's decision #3 targets the installed release, so **the suite cannot run until v0.0.12 is installed**. Before Task 8, install it:

```powershell
gh release download v0.0.12 --repo jazzzsoftware/UmnathaNetworkMonitor --pattern "*.exe" --dir "$env:TEMP\umnatha-install"
& "$env:TEMP\umnatha-install\Umnatha.Network.Monitor.v0.0.12.exe" /SILENT /SUPPRESSMSGBOXES /NORESTART
```

Tasks 1–7 do not need it — Task 3's preflight is written specifically to *report* its absence rather than crash, and Task 3 Step 6 verifies exactly that.

---

## Global Constraints

- **No `var`.** Always explicit types.
- **No single-character variable names**, including pattern-match variables and lambda parameters.
- **Always curly braces** on `if`, `else`, `for`, `foreach`, `while`, `using` — even single-line bodies.
- **Blank lines around all blocks**, at every nesting level, including immediately after a method's opening `{` when the first statement is a block and immediately before its closing `}` when the last statement ends with `}`.
- **Single exit point** — exactly one `return` per method, at the end. `break` and `continue` are unaffected.
- **Returns stand alone** — assign to a local first, then `return` that local. Blank line above the `return`.
- **One type per file**, named exactly after the type. **This corrects the spec**, whose `Phases/01_Launch.cs` naming is both an invalid identifier and a breach of the no-underscores rule. Phase files are `LaunchPhase.cs`, `DevicesPhase.cs`, and so on; ordering lives in the `Program.cs` sequence, not in file names.
- **Class member order** — Fields → Constructor → Properties → Public methods → Override methods → Private methods. A property's backing field goes immediately above that property in the Properties section.
- **`string.Empty`**, not `""`.
- **No underscores in identifiers** except the leading underscore on private fields.
- **No comments unless the WHY is non-obvious.** No trailing summary comments after methods.
- **XAML formatting** — element name on its own line; every attribute on its own line indented 4 spaces from the opening `<`; blank line above and below every element; attribute order is simple assignments, then event handlers and `Command` bindings, then value-assignment bindings. `AllDevicesPage.xaml` is the canonical reference.
- **Platform x64.** WinUI 3 does not support Any CPU.
- **`Tools/UITests/` is NOT a solution project.** Register it in `NetworkMonitor.slnx` as a `<Folder>` of `<File>` entries, exactly as `Tools/MigrationVerify/` is (slnx lines 130–134). It uninstalls the app; a `<Project>` entry would let a routine `dotnet build`/`dotnet test` reach it.
- **DB impact: NONE.** No entity, `DbSet`, column or index changes anywhere in this plan, and therefore **no EF migration**. `SeedDatabase` runs the *existing* migrations against a throwaway file; it does not author new ones. The `UMNATHA_DATA_FOLDER` override changes *where* a database is opened, never its schema.
- **The test project references Models and Core only.** Anything this plan makes unit-testable must land in Core. Nothing in `NetworkMonitor.Services` or the app project gains a unit test.
- Build the app with `dotnet build NetworkMonitor.slnx -c Debug -p:Platform=x64`; test with `dotnet test NetworkMonitor.Tests/NetworkMonitor.Tests.csproj`. **Baseline to beat: 501 tests green** (verified 2026-08-20).
- Build the runner with `dotnet build Tools/UITests/UITests.csproj` — it is outside the slnx, so the solution build never touches it.

---

## Corrections to the spec, found while planning

Each is applied in the task noted. They are recorded here rather than silently fixed, per the house rule.

| # | Spec says | Actually | Fixed in |
|---|---|---|---|
| 1 | Step 5: "verify its SHA-256 **against the release notes**" | The hash is a **separate release asset**, `Umnatha.Network.Monitor.v0.0.11.exe.sha256` (64 bytes). Verified against the live v0.0.11 release. The notes contain no hash. | Task 12 |
| 2 | Step 8: "Click the update action" | The banner's buttons are **"Update now"** and **"Later"**, with a separate **"Cancel"** during download (`MainWindow.xaml:139-167`). There is no "Download" button — the download starts on "Update now". | Task 12 |
| 3 | Implies the update machinery must be written | `NetworkMonitor.Core/Update/` already has `ReleaseInfoParser`, `UpdateDownloader`, `ChecksumVerifier`, `SemanticVersion` and `UpdateChecker`. The runner references Core and reuses them rather than reimplementing asset resolution and hashing. | Task 12 |
| 4 | `Phases/01_Launch.cs` | Invalid C# identifier and a breach of one-type-per-file + no-underscores. | Global Constraints |
| 5 | Silent on it | `Tools/MigrationVerify` links `AppPaths.cs` via `<Compile Include>` and references **Models only**. Giving `AppPaths` a Core dependency breaks that build until MigrationVerify also references Core. | Task 1 |
| 6 | Target is the installed build | Nothing is installed on this machine. | Prerequisite |

---

## File Structure

**Created — shipping code**

| File | Responsibility |
|---|---|
| `NetworkMonitor.Core/Common/AppDataFolderResolver.cs` | Pure rule: override value + default → folder path |
| `NetworkMonitor.Core/Charting/ChartDrawSummary.cs` | Pure, culture-invariant draw-summary string |
| `NetworkMonitor.Tests/Common/AppDataFolderResolverTests.cs` | The override rule |
| `NetworkMonitor.Tests/Charting/ChartDrawSummaryTests.cs` | Summary format and culture invariance |

**Created — the runner** (all under `Tools/UITests/`)

| File | Responsibility |
|---|---|
| `UITests.csproj` | `net10.0-windows10.0.19041.0`, x64, outside the slnx |
| `Program.cs` | Preflight → phase sequence → report → exit code |
| `README.md` | How to run it, what it destroys, how to recover |
| `Runner/Preflight.cs` | Elevation, installed build, disk space, stranded backup |
| `Runner/PreflightResult.cs` | Ready flag + the reasons it is not |
| `Runner/Phase.cs` | Name, steps, abort policy |
| `Runner/PhaseRunner.cs` | Ordered execution, timing, failure capture |
| `Runner/StepResult.cs` | Pass / fail / skipped, message, evidence paths |
| `Runner/StepOutcome.cs` | The three-state enum |
| `Evidence/ScreenshotWriter.cs` | Window capture on failure |
| `Evidence/UiaTreeDumper.cs` | Automation subtree dump on failure |
| `Evidence/HtmlReport.cs` | The single-file report |
| `Evidence/RunEnvironment.cs` | OS build, DPI, theme, scheme, elevation, versions |
| `Environment/DataFolderFixture.cs` | Throwaway folder + env var |
| `Environment/SeedDatabase.cs` | Fixture `.db` from known rows |
| `Environment/SeedCounts.cs` | The known values every assertion is written against |
| `Environment/InstalledApp.cs` | Locate / launch / shut down the installed build |
| `Environment/RealDataGuard.cs` | Copy the live folder aside, restore in a `finally` |
| `Driving/AppSession.cs` | FlaUI `Application` + main and mini-graph windows |
| `Driving/Waits.cs` | The **only** place a delay is written |
| `Driving/Navigator.cs` | Nav routes via `SelectionItemPattern` |
| `Driving/GridReader.cs` | DataGrid row and cell reads |
| `Phases/LaunchPhase.cs` … `Phases/UpdateLifecyclePhase.cs` | Nine phases, one file each |

**Modified**

| File | Change |
|---|---|
| `NetworkMonitor.Services/Data/AppPaths.cs` | Delegate to the Core resolver, reading `UMNATHA_DATA_FOLDER` |
| `Tools/MigrationVerify/MigrationVerify.csproj` | Add the Core `ProjectReference` that `AppPaths` now needs |
| `NetworkMonitor/Views/Controls/TrafficAreaChart.xaml` | Name the root `Grid` |
| `NetworkMonitor/Views/Controls/TrafficAreaChart.xaml.cs` | Publish the summary after drawing |
| `NetworkMonitor/Views/Controls/SpeedTrendChart.xaml` | Name the root `Grid` |
| `NetworkMonitor/Views/Controls/SpeedTrendChart.xaml.cs` | Publish the summary after drawing |
| 11 page XAMLs + `MainWindow.xaml` + `MiniGraphWindow.xaml` | `AutomationProperties.AutomationId` on driven controls only |
| `NetworkMonitor.slnx` | Register `Tools/UITests/` and this plan |
| `CLAUDE.md` | `/Tools/` list, Key Files, the `UMNATHA_DATA_FOLDER` override |
| `Documents/To Do.txt` | Mark the item built |

---

### Task 1: Data folder override

Today `AppPaths.AppDataFolder` is a hardcoded `Path.Combine(LocalApplicationData, "UmnathaNetworkMonitor")` with no override, so any UI-driven run would drive the operator's real database and real `settings.json`. Four call sites depend on it — `AppDbContext.cs:24`, `Settings.cs:36`, `SortPreference.cs:11`, `AppLog.cs:13`, plus `UpdateService.cs:48` for the Updates folder — so overriding this one property redirects everything at once.

The *decision* is pure logic and belongs in Core where the test project can reach it; `AppPaths` keeps only the two environment reads.

**Files:**
- Create: `NetworkMonitor.Core/Common/AppDataFolderResolver.cs`
- Create: `NetworkMonitor.Tests/Common/AppDataFolderResolverTests.cs`
- Modify: `NetworkMonitor.Services/Data/AppPaths.cs`
- Modify: `Tools/MigrationVerify/MigrationVerify.csproj:11-13`

**Interfaces:**
- Consumes: nothing.
- Produces: `AppDataFolderResolver.Resolve(string? overrideValue, string localApplicationDataPath) → string`. Later tasks set the `UMNATHA_DATA_FOLDER` environment variable on a launched process and rely on the app honouring it.

- [ ] **Step 1: Write the failing tests**

Create `NetworkMonitor.Tests/Common/AppDataFolderResolverTests.cs`:

```csharp
using NetworkMonitor.Core.Common;

namespace NetworkMonitor.Tests.Common
{
    public class AppDataFolderResolverTests
    {
        [Fact]
        public void NoOverrideFallsBackToTheProductFolderUnderLocalApplicationData()
        {
            string resolved = AppDataFolderResolver.Resolve(null, @"C:\Users\Someone\AppData\Local");

            Assert.Equal(@"C:\Users\Someone\AppData\Local\UmnathaNetworkMonitor", resolved);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void AnEmptyOrWhitespaceOverrideIsTreatedAsAbsent(string overrideValue)
        {
            string resolved = AppDataFolderResolver.Resolve(overrideValue, @"C:\Local");

            Assert.Equal(@"C:\Local\UmnathaNetworkMonitor", resolved);
        }

        [Fact]
        public void AnOverrideIsUsedExactlyAndDoesNotGainTheProductFolder()
        {
            string resolved = AppDataFolderResolver.Resolve(@"D:\uitest\data", @"C:\Local");

            Assert.Equal(@"D:\uitest\data", resolved);
        }

        [Fact]
        public void AnOverrideIsTrimmedSoATrailingSpaceCannotCreateASecondFolder()
        {
            string resolved = AppDataFolderResolver.Resolve(@"  D:\uitest\data  ", @"C:\Local");

            Assert.Equal(@"D:\uitest\data", resolved);
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test NetworkMonitor.Tests/NetworkMonitor.Tests.csproj --filter AppDataFolderResolverTests`
Expected: FAIL — `The type or namespace name 'AppDataFolderResolver' could not be found`.

- [ ] **Step 3: Write the resolver**

Create `NetworkMonitor.Core/Common/AppDataFolderResolver.cs`:

```csharp
namespace NetworkMonitor.Core.Common
{
    public static class AppDataFolderResolver
    {
        public const string OverrideVariableName = "UMNATHA_DATA_FOLDER";

        private const string ProductFolderName = "UmnathaNetworkMonitor";

        public static string Resolve(string? overrideValue, string localApplicationDataPath)
        {
            string resolved;

            if (string.IsNullOrWhiteSpace(overrideValue))
            {
                resolved = Path.Combine(localApplicationDataPath, ProductFolderName);
            }
            else
            {
                resolved = overrideValue.Trim();
            }

            return resolved;
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test NetworkMonitor.Tests/NetworkMonitor.Tests.csproj --filter AppDataFolderResolverTests`
Expected: PASS, 5 tests.

- [ ] **Step 5: Point `AppPaths` at the resolver**

Replace the whole of `NetworkMonitor.Services/Data/AppPaths.cs`:

```csharp
using NetworkMonitor.Core.Common;

namespace NetworkMonitor.Services.Data
{
    public static class AppPaths
    {
        public static string AppDataFolder =>
            AppDataFolderResolver.Resolve(
                Environment.GetEnvironmentVariable(AppDataFolderResolver.OverrideVariableName),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
    }
}
```

- [ ] **Step 6: Give MigrationVerify the Core reference it now needs**

`Tools/MigrationVerify` compiles `AppPaths.cs` in via `<Compile Include>` (line 33) and references **Models only**, so it will not build until it can see Core. In `Tools/MigrationVerify/MigrationVerify.csproj`, replace the `ItemGroup` at lines 11–13 with:

```xml
  <ItemGroup>
    <ProjectReference Include="..\..\NetworkMonitor.Models\NetworkMonitor.Models.csproj" />
    <!-- AppPaths.cs, linked below, resolves the data folder through Core. -->
    <ProjectReference Include="..\..\NetworkMonitor.Core\NetworkMonitor.Core.csproj" />
  </ItemGroup>
```

- [ ] **Step 7: Verify everything still builds and passes**

Run: `dotnet build NetworkMonitor.slnx -c Debug -p:Platform=x64`
Expected: Build succeeded, 0 warnings.

Run: `dotnet build Tools/MigrationVerify/MigrationVerify.csproj`
Expected: Build succeeded. (This is the check that Step 6 mattered — it fails with `CS0246: AppDataFolderResolver` if the reference was missed.)

Run: `dotnet test NetworkMonitor.Tests/NetworkMonitor.Tests.csproj`
Expected: PASS, **506 tests** (501 + 5).

- [ ] **Step 8: Prove the override actually redirects the app**

This is the one behaviour a unit test cannot reach, and it is the whole point of the task.

```powershell
$env:UMNATHA_DATA_FOLDER = "$env:TEMP\umnatha-override-check"
dotnet run --project Tools/MigrationVerify
Get-ChildItem "$env:TEMP\umnatha-override-check" -ErrorAction SilentlyContinue
Remove-Item Env:\UMNATHA_DATA_FOLDER
```

Expected: MigrationVerify still exits 0 with all checks passing (it works in its own `%TEMP%` sandbox regardless), **and** `%LOCALAPPDATA%\UmnathaNetworkMonitor` is untouched. Confirm by comparing `(Get-Item "$env:LOCALAPPDATA\UmnathaNetworkMonitor\networkmonitor.db").LastWriteTime` before and after.

- [ ] **Step 9: Commit**

```bash
git add NetworkMonitor.Core/Common/AppDataFolderResolver.cs NetworkMonitor.Tests/Common/AppDataFolderResolverTests.cs NetworkMonitor.Services/Data/AppPaths.cs Tools/MigrationVerify/MigrationVerify.csproj
git commit -m "Let the data folder be pointed somewhere other than the user's real one."
```

DB impact: **none.** The override changes which file is opened, never its schema.

---

### Task 2: Chart draw summary

`TrafficAreaChart` renders into a Win2D `CanvasControl` with `IsHitTestVisible="False"`. To UI Automation that is one opaque bitmap with no children, so the *data* behind the chart is currently unassertable. Publishing a compact, stable, culture-invariant string to `AutomationProperties.Name` on the chart's root `Grid` makes the input data assertable without pixel comparison.

**This proves the data arrived. It does not prove the drawing is correct** — that limitation is real, is stated in the spec's boundary section, and Task 13 puts it in the report's "Not covered" list. Do not blur it.

**Files:**
- Create: `NetworkMonitor.Core/Charting/ChartDrawSummary.cs`
- Create: `NetworkMonitor.Tests/Charting/ChartDrawSummaryTests.cs`
- Modify: `NetworkMonitor/Views/Controls/TrafficAreaChart.xaml:9-10`
- Modify: `NetworkMonitor/Views/Controls/TrafficAreaChart.xaml.cs` (in `ChartCanvasDraw`, line 587)
- Modify: `NetworkMonitor/Views/Controls/SpeedTrendChart.xaml:8-9`
- Modify: `NetworkMonitor/Views/Controls/SpeedTrendChart.xaml.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `ChartDrawSummary.Format(int buckets, string series, long peak, long scale, string range) → string`, emitting exactly `buckets=300 series=down,up peak=2411520 scale=4194304 range=5m`; `ChartDrawSummary.TryParse(string candidate, out ChartDrawValues values) → bool`; `ChartDrawRange.FromBucketSeconds(double bucketSeconds) → string`. Task 9 parses the published string with `TryParse`.

**`peak` and `scale` are different numbers.** `peak` is the largest value in the data; `scale` is the axis maximum the chart scaled to. They are usually close and never identical, and conflating them would make the assertion in Task 9 tautological.

- [ ] **Step 1: Write the failing tests**

Create `NetworkMonitor.Tests/Charting/ChartDrawSummaryTests.cs`:

```csharp
using System.Globalization;
using NetworkMonitor.Core.Charting;

namespace NetworkMonitor.Tests.Charting
{
    public class ChartDrawSummaryTests
    {
        [Fact]
        public void TheSummaryHasTheExactShapeTheSuiteParses()
        {
            string summary = ChartDrawSummary.Format(300, "down,up", 2411520L, 4194304L, "5m");

            Assert.Equal("buckets=300 series=down,up peak=2411520 scale=4194304 range=5m", summary);
        }

        [Fact]
        public void LargeNumbersCarryNoThousandsSeparatorInAnyCulture()
        {
            CultureInfo previous = CultureInfo.CurrentCulture;

            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");

                string summary = ChartDrawSummary.Format(1, "down", 1234567L, 2000000L, "6h");

                Assert.Contains("peak=1234567", summary);
                Assert.Contains("scale=2000000", summary);
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }

        [Fact]
        public void AnEmptyChartIsStillReportedRatherThanLeftBlank()
        {
            string summary = ChartDrawSummary.Format(0, "down,up", 0L, 0L, "5m");

            Assert.Equal("buckets=0 series=down,up peak=0 scale=0 range=5m", summary);
        }

        [Fact]
        public void ASummaryRoundTripsThroughTryParse()
        {
            string summary = ChartDrawSummary.Format(300, "down,up", 2411520L, 4194304L, "5m");

            bool parsed = ChartDrawSummary.TryParse(summary, out ChartDrawValues values);

            Assert.True(parsed);
            Assert.Equal(300, values.Buckets);
            Assert.Equal("down,up", values.Series);
            Assert.Equal(2411520L, values.Peak);
            Assert.Equal(4194304L, values.Scale);
            Assert.Equal("5m", values.Range);
        }

        [Theory]
        [InlineData("")]
        [InlineData("buckets=300")]
        [InlineData("buckets=abc series=down peak=1 scale=1 range=5m")]
        public void TextThatIsNotASummaryFailsToParseRatherThanThrowing(string candidate)
        {
            bool parsed = ChartDrawSummary.TryParse(candidate, out ChartDrawValues values);

            Assert.False(parsed);
            Assert.Equal(0, values.Buckets);
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test NetworkMonitor.Tests/NetworkMonitor.Tests.csproj --filter ChartDrawSummaryTests`
Expected: FAIL — `ChartDrawSummary` and `ChartDrawValues` not found.

- [ ] **Step 3: Write the value type**

Create `NetworkMonitor.Core/Charting/ChartDrawValues.cs`:

```csharp
namespace NetworkMonitor.Core.Charting
{
    public readonly record struct ChartDrawValues(
        int Buckets,
        string Series,
        long Peak,
        long Scale,
        string Range);
}
```

- [ ] **Step 4: Write the formatter**

Create `NetworkMonitor.Core/Charting/ChartDrawSummary.cs`:

```csharp
using System.Globalization;

namespace NetworkMonitor.Core.Charting
{
    public static class ChartDrawSummary
    {
        public static string Format(int buckets, string series, long peak, long scale, string range)
        {
            string summary = string.Create(
                CultureInfo.InvariantCulture,
                $"buckets={buckets} series={series} peak={peak} scale={scale} range={range}");

            return summary;
        }

        public static bool TryParse(string candidate, out ChartDrawValues values)
        {
            values = default;

            bool parsed = false;

            if (!string.IsNullOrWhiteSpace(candidate))
            {
                Dictionary<string, string> fields = new Dictionary<string, string>(StringComparer.Ordinal);

                foreach (string pair in candidate.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    int separator = pair.IndexOf('=');

                    if (separator > 0)
                    {
                        fields[pair[..separator]] = pair[(separator + 1)..];
                    }
                }

                bool hasEvery =
                    fields.ContainsKey("buckets")
                    && fields.ContainsKey("series")
                    && fields.ContainsKey("peak")
                    && fields.ContainsKey("scale")
                    && fields.ContainsKey("range");

                if (hasEvery)
                {
                    bool numbersRead =
                        int.TryParse(fields["buckets"], NumberStyles.Integer, CultureInfo.InvariantCulture, out int buckets)
                        && long.TryParse(fields["peak"], NumberStyles.Integer, CultureInfo.InvariantCulture, out long peak)
                        && long.TryParse(fields["scale"], NumberStyles.Integer, CultureInfo.InvariantCulture, out long scale);

                    if (numbersRead)
                    {
                        values = new ChartDrawValues(buckets, fields["series"], peak, scale, fields["range"]);
                        parsed = true;
                    }
                }
            }

            return parsed;
        }
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test NetworkMonitor.Tests/NetworkMonitor.Tests.csproj --filter ChartDrawSummaryTests`
Expected: PASS, 7 tests (four facts plus the three-case theory).

- [ ] **Step 6: Write the range mapper**

`ChartDrawRange` converts the bucket width the chart is actually drawing into the range token the report asserts on. It is written before the wiring because Step 8 calls it. Create `NetworkMonitor.Core/Charting/ChartDrawRange.cs`:

```csharp
namespace NetworkMonitor.Core.Charting
{
    public static class ChartDrawRange
    {
        public static string FromBucketSeconds(double bucketSeconds)
        {
            string range;

            if (bucketSeconds <= 1.5)
            {
                range = "5m";
            }
            else if (bucketSeconds <= 90.0)
            {
                range = "1h";
            }
            else
            {
                range = "6h";
            }

            return range;
        }
    }
}
```

Add three cases to `ChartDrawSummaryTests` pinning the boundaries — `1.0 → "5m"`, `60.0 → "1h"`, `300.0 → "6h"` — so a later change to bucket widths fails a test rather than silently mislabelling the report.

Run: `dotnet test NetworkMonitor.Tests/NetworkMonitor.Tests.csproj --filter ChartDrawSummaryTests`
Expected: PASS, 10 tests.

- [ ] **Step 7: Name the two chart roots**

`TrafficAreaChart.xaml` lines 9–10 are currently:

```xml
    <Grid
        Background="Transparent">
```

Replace with:

```xml
    <Grid
        x:Name="ChartRoot"
        Background="Transparent">
```

`SpeedTrendChart.xaml` lines 8–9 are currently:

```xml
    <Grid
        SizeChanged="OnSizeChanged">
```

Replace with:

```xml
    <Grid
        x:Name="ChartRoot"
        SizeChanged="OnSizeChanged">
```

Per the XAML attribute-order rule, `x:Name` leads — the same correction C4-7 made to `TrafficHostPage`.

- [ ] **Step 8: Publish the summary from `TrafficAreaChart`**

At the **end** of `ChartCanvasDraw` (the method starting at line 587), after all drawing, add:

```csharp
                PublishDrawSummary(peakValue);
```

`peakValue` is the largest data value this frame — the same number `UpdatePeakLabels(long maxValue, double bucketSeconds)` (line 997) is already given. Use that existing local; do **not** add new state to feed the summary.

Then add this private method, placed with the other private methods:

```csharp
        private void PublishDrawSummary(long peakValue)
        {
            string range = ChartDrawRange.FromBucketSeconds(_bucketSeconds);

            string summary = ChartDrawSummary.Format(
                _count,
                "down,up",
                peakValue,
                (long)_targetMax,
                range);

            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(ChartRoot, summary);
        }
```

`_count`, `_targetMax` and `_bucketSeconds` are existing fields (lines 58–61); `_targetMax` is the axis maximum (assigned at line 908 from `axisMax`), which is what `scale` means. Do **not** pass `_displayMax` — that is the eased value mid-animation and would make the summary flicker between frames, which is exactly the flake the suite must not have.

Add `using NetworkMonitor.Core.Charting;` — the file already imports `NetworkMonitor.Models.Charting`, which is a different namespace.

- [ ] **Step 9: Publish the summary from `SpeedTrendChart`**

`SpeedTrendChart` draws XAML shapes onto a `Canvas` rather than Win2D, so publish at the end of its redraw method (the one `OnSizeChanged` calls):

```csharp
        private void PublishDrawSummary(int pointCount, long peakBitsPerSecond, long axisMax)
        {
            string summary = ChartDrawSummary.Format(
                pointCount,
                "download,upload",
                peakBitsPerSecond,
                axisMax,
                "speed");

            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(ChartRoot, summary);
        }
```

Call it with the counts and maxima that method already computes. If the local names differ, use the existing ones — do not introduce new state to feed the summary.

- [ ] **Step 10: Build and run the full suite**

Run: `dotnet build NetworkMonitor.slnx -c Debug -p:Platform=x64`
Expected: Build succeeded, 0 warnings.

Run: `dotnet test NetworkMonitor.Tests/NetworkMonitor.Tests.csproj`
Expected: PASS, **516 tests** (501 baseline + 5 from Task 1 + 10 from this task).

- [ ] **Step 11: Commit**

```bash
git add NetworkMonitor.Core/Charting/ NetworkMonitor.Tests/Charting/ChartDrawSummaryTests.cs "NetworkMonitor/Views/Controls/"
git commit -m "Let the charts report what they drew."
```

DB impact: **none.** Presentation and a pure formatter.

---

### Task 3: The runner skeleton

A console app that starts, refuses to run when the machine is not ready, says exactly why, and exits non-zero. Nothing drives the app yet. This task exists on its own because preflight is what stops the destructive phases running on a machine that cannot survive them, and it must be right before anything else is built on top.

**Files:**
- Create: `Tools/UITests/UITests.csproj`
- Create: `Tools/UITests/Program.cs`
- Create: `Tools/UITests/Runner/Preflight.cs`
- Create: `Tools/UITests/Runner/PreflightResult.cs`
- Create: `Tools/UITests/Runner/StepOutcome.cs`
- Create: `Tools/UITests/Runner/StepResult.cs`
- Create: `Tools/UITests/Runner/Phase.cs`
- Create: `Tools/UITests/Runner/PhaseContext.cs`
- Create: `Tools/UITests/Runner/PhaseResult.cs`
- Create: `Tools/UITests/Runner/PhaseRunner.cs`
- Create: `Tools/UITests/Runner/RunOutcome.cs`

**Interfaces:**
- Consumes: `AppDataFolderResolver.OverrideVariableName` (Task 1).
- Produces: `StepResult.Pass(string name)`, `StepResult.Fail(string name, string expected, string actual)`, `StepResult.Skip(string name, string why)`; `Phase(string name, bool abortsRun, Func<PhaseContext, Task<IReadOnlyList<StepResult>>> run)`; `PhaseRunner.RunAsync(IReadOnlyList<Phase> phases, PhaseContext context) → RunOutcome`; `Preflight.Check() → PreflightResult` with `Ready` and `Blockers`.
- `PhaseContext` carries what every phase needs and nothing more: `AppSession Session` (set by Task 6, `null` until then), `string DataFolder`, `string ArtifactFolder`, `SeedCounts Seed` (Task 5). It is a mutable class, not a record — `Session` is replaced when a phase restarts the app.
- `PhaseResult` is `(string Name, TimeSpan Duration, bool Aborted, IReadOnlyList<StepResult> Steps)`. `RunOutcome` is `(IReadOnlyList<PhaseResult> Phases, TimeSpan TotalDuration)` with computed `PassedCount`, `FailedCount`, `SkippedCount` and `ExitCode` — `0` only when nothing failed and nothing aborted.

- [ ] **Step 1: Create the project file**

Create `Tools/UITests/UITests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
    <OutputType>Exe</OutputType>
    <Platforms>x64</Platforms>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>NetworkMonitor.UITests</RootNamespace>
    <UseWindowsForms>false</UseWindowsForms>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\NetworkMonitor.Models\NetworkMonitor.Models.csproj" />
    <ProjectReference Include="..\..\NetworkMonitor.Core\NetworkMonitor.Core.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="FlaUI.Core" Version="5.0.0" />
    <PackageReference Include="FlaUI.UIA3" Version="5.0.0" />
    <PackageReference Include="System.Drawing.Common" Version="10.*" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="10.*" />
    <!-- Pinned the way the app pins it; the transitive 2.x bundle carries a known advisory. -->
    <PackageReference Include="SQLitePCLRaw.bundle_e_sqlite3" Version="3.0.3" />
  </ItemGroup>

  <!--
    NetworkMonitor.Services is net10.0-windows with UseWinUI, which this plain console host
    cannot reference without dragging WinUI in. These files plus the Migrations folder are the
    whole of the DB layer, are platform-neutral, and are what SeedDatabase needs to build a
    fixture with the app's own schema. Same approach, and same reason, as Tools/MigrationVerify.
  -->
  <ItemGroup>
    <Compile Include="..\..\NetworkMonitor.Services\Data\AppDbContext.cs" Link="Linked\AppDbContext.cs" />
    <Compile Include="..\..\NetworkMonitor.Services\Data\AppPaths.cs" Link="Linked\AppPaths.cs" />
    <Compile Include="..\..\NetworkMonitor.Services\Data\AppDbContextDesignTimeFactory.cs" Link="Linked\AppDbContextDesignTimeFactory.cs" />
    <Compile Include="..\..\NetworkMonitor.Services\Data\DatabaseInitializer.cs" Link="Linked\DatabaseInitializer.cs" />
    <Compile Include="..\..\NetworkMonitor.Services\Platform\AppLog.cs" Link="Linked\AppLog.cs" />
    <Compile Include="..\..\NetworkMonitor.Services\Data\Migrations\**\*.cs" LinkBase="Linked\Migrations" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Verify FlaUI 5.0.0 actually resolves**

The spec names FlaUI 5.0.0. Confirm before building anything on it — a wrong version number here stalls every later task.

Run: `dotnet restore Tools/UITests/UITests.csproj`
Expected: Restore succeeded.

If it fails with `NU1102: Unable to find package FlaUI.Core with version (>= 5.0.0)`, run `dotnet package search FlaUI.Core --exact-match --take 1`, use the highest released version, and **record the substitution at the top of `Tools/UITests/README.md`** in Step 8 rather than leaving the spec's number standing unremarked.

- [ ] **Step 3: Write the result types**

Create `Tools/UITests/Runner/StepOutcome.cs`:

```csharp
namespace NetworkMonitor.UITests.Runner
{
    public enum StepOutcome
    {
        Passed,
        Failed,
        Skipped
    }
}
```

Create `Tools/UITests/Runner/StepResult.cs`:

```csharp
namespace NetworkMonitor.UITests.Runner
{
    public sealed class StepResult
    {
        private StepResult(StepOutcome outcome, string name, string message)
        {
            Outcome = outcome;
            Name = name;
            Message = message;
        }

        public StepOutcome Outcome
        {
            get;
        }

        public string Name
        {
            get;
        }

        public string Message
        {
            get;
        }

        public string ScreenshotPath
        {
            get;
            set;
        } = string.Empty;

        public string TreeDumpPath
        {
            get;
            set;
        } = string.Empty;

        public static StepResult Pass(string name)
        {
            StepResult result = new StepResult(StepOutcome.Passed, name, string.Empty);

            return result;
        }

        public static StepResult Fail(string name, string expected, string actual)
        {
            string message = $"Expected: {expected}\nActual:   {actual}";

            StepResult result = new StepResult(StepOutcome.Failed, name, message);

            return result;
        }

        public static StepResult Skip(string name, string why)
        {
            StepResult result = new StepResult(StepOutcome.Skipped, name, why);

            return result;
        }
    }
}
```

- [ ] **Step 4: Write preflight**

Create `Tools/UITests/Runner/PreflightResult.cs`:

```csharp
namespace NetworkMonitor.UITests.Runner
{
    public sealed class PreflightResult
    {
        public PreflightResult(IReadOnlyList<string> blockers)
        {
            Blockers = blockers;
        }

        public IReadOnlyList<string> Blockers
        {
            get;
        }

        public bool Ready => Blockers.Count == 0;
    }
}
```

Create `Tools/UITests/Runner/Preflight.cs`:

```csharp
using System.Security.Principal;
using Microsoft.Win32;

namespace NetworkMonitor.UITests.Runner
{
    public static class Preflight
    {
        public const string UninstallKeyPath =
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{7074c3a8-a61b-4e4a-9e6c-dedc9a62ae94}_is1";

        private const long RequiredFreeBytes = 3L * 1024L * 1024L * 1024L;

        public static PreflightResult Check()
        {
            List<string> blockers = new List<string>();

            if (!IsElevated())
            {
                blockers.Add("Not elevated. The suite installs and uninstalls the app; start it from an elevated terminal.");
            }

            string installedVersion = ReadInstalledVersion();

            if (installedVersion.Length == 0)
            {
                blockers.Add(
                    "Umnatha Network Monitor is not installed. The suite drives the installed release, "
                    + "not a dev build. Install the latest release first — see Tools/UITests/README.md.");
            }

            string strandedBackup = FindStrandedBackup();

            if (strandedBackup.Length > 0)
            {
                blockers.Add(
                    $"A previous run left a data-folder backup at {strandedBackup}. "
                    + "Restore or delete it before running again — this suite will not run while your history is parked.");
            }

            DriveInfo systemDrive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\");

            if (systemDrive.AvailableFreeSpace < RequiredFreeBytes)
            {
                blockers.Add(
                    $"Only {systemDrive.AvailableFreeSpace / 1024 / 1024} MB free on {systemDrive.Name}. "
                    + "The update phase downloads two ~75 MB installers and copies the data folder aside; 3 GB is the floor.");
            }

            PreflightResult result = new PreflightResult(blockers);

            return result;
        }

        public static string ReadInstalledVersion()
        {
            string version = string.Empty;

            foreach (RegistryView view in new RegistryView[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                using (RegistryKey? key = baseKey.OpenSubKey(UninstallKeyPath))
                {
                    if (key is not null && version.Length == 0)
                    {
                        version = key.GetValue("DisplayVersion") as string ?? string.Empty;
                    }
                }
            }

            return version;
        }

        private static bool IsElevated()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                WindowsPrincipal principal = new WindowsPrincipal(identity);

                bool elevated = principal.IsInRole(WindowsBuiltInRole.Administrator);

                return elevated;
            }
        }

        private static string FindStrandedBackup()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            string[] candidates = Directory.GetDirectories(localAppData, "UmnathaNetworkMonitor.uitest-backup-*");

            string stranded = candidates.Length > 0 ? candidates[0] : string.Empty;

            return stranded;
        }
    }
}
```

The registry key name is the Inno `AppId` from `Tools/Installer/NetworkMonitor.iss:24` with Inno's `_is1` suffix. Both registry views are read because an admin install writes the 64-bit view but the suffix convention is shared.

- [ ] **Step 5: Write the phase runner and entry point**

Create `PhaseContext.cs`, `Phase.cs`, `PhaseResult.cs`, `RunOutcome.cs` and `PhaseRunner.cs` to the shapes in **Interfaces** above, implementing the two failure classes from the spec:

- **Step failure** — an assertion fails. Evidence is captured, the step is marked failed, and the phase **continues**. Later steps in that phase may fail as a consequence; the report groups them.
- **Phase abort** — the app crashed, a window vanished, or the environment is wrong. The phase **stops** and the runner moves to the next phase after restarting the app. A phase declared with `abortsRun: true` (only `LaunchPhase`) ends the whole run instead.

`PhaseRunner` records each phase's wall-clock duration for the 15-minute budget check in Task 13. It catches every exception a phase throws — including the `TimeoutException` from `Waits` — and converts it to an aborted `PhaseResult` rather than letting it escape into `Program.cs`, because an unhandled throw would skip the report and, worse, skip Task 12's restore.

Create `Tools/UITests/Program.cs`:

```csharp
using NetworkMonitor.UITests.Runner;

PreflightResult preflight = Preflight.Check();

if (!preflight.Ready)
{
    Console.WriteLine("Preflight failed. The suite did not start:");
    Console.WriteLine();

    foreach (string blocker in preflight.Blockers)
    {
        Console.WriteLine($"  - {blocker}");
    }

    Console.WriteLine();

    return 2;
}

Console.WriteLine($"Preflight passed. Installed version: {Preflight.ReadInstalledVersion()}");

return 0;
```

Exit code 2 is preflight refusal, distinct from 1 (a real failure) and 0 (everything passed).

- [ ] **Step 6: Run it un-elevated and confirm it refuses clearly**

Run: `dotnet run --project Tools/UITests`
Expected: Exit code 2, listing "Not elevated" **and** "Umnatha Network Monitor is not installed" — both true on this machine today. This is the task's real test: the runner reports the machine's state instead of crashing on it.

- [ ] **Step 7: Run it elevated and confirm the elevation blocker clears**

From an elevated terminal, run the same command.
Expected: Exit code 2, with only the not-installed blocker remaining.

- [ ] **Step 8: Write the README**

Create `Tools/UITests/README.md` covering: what the suite is, the one command, that it **must** be elevated, that phase 09 really uninstalls the app, where the report lands, where a stranded backup would be and how to restore it by hand, and the FlaUI version actually in use if Step 2 forced a substitution.

- [ ] **Step 9: Commit**

```bash
git add Tools/UITests/
git commit -m "Add the UI test runner's skeleton and its preflight refusals."
```

DB impact: **none.**

---

### Task 4: Evidence — screenshots, tree dumps and the report

Built before any phase, because a phase that fails without evidence wastes the run. The spec is specific about why the tree dump matters: it turns "control not found" from a mystery into a five-second diagnosis.

**Files:**
- Create: `Tools/UITests/Evidence/ScreenshotWriter.cs`
- Create: `Tools/UITests/Evidence/UiaTreeDumper.cs`
- Create: `Tools/UITests/Evidence/HtmlReport.cs`
- Create: `Tools/UITests/Evidence/RunEnvironment.cs`

**Interfaces:**
- Consumes: `StepResult` (Task 3).
- Produces: `ScreenshotWriter.Write(AutomationElement element, string artifactFolder, string stepName) → string` (the path written, or empty); `UiaTreeDumper.Dump(AutomationElement root, string artifactFolder, string stepName) → string`; `HtmlReport.Write(RunOutcome outcome, RunEnvironment environment, string artifactFolder) → string`; `RunEnvironment.Read() → RunEnvironment`.

Our method is `Write`, not `Capture`, because FlaUI's own screenshot helper is the static class `FlaUI.Core.Capturing.Capture` — a method of the same name in a class that calls `Capture.Element(...)` reads ambiguously. `RunEnvironment.Read()` for the same reason: it does not capture anything.

- [ ] **Step 1: Write the screenshot writer**

```csharp
using System.Drawing.Imaging;
using FlaUI.Core.Capturing;
using FlaUI.Core.AutomationElements;

namespace NetworkMonitor.UITests.Evidence
{
    public static class ScreenshotWriter
    {
        public static string Write(AutomationElement element, string artifactFolder, string stepName)
        {
            string path = string.Empty;

            try
            {
                Directory.CreateDirectory(artifactFolder);

                string fileName = $"{Sanitise(stepName)}.png";
                string fullPath = Path.Combine(artifactFolder, fileName);

                using (CaptureImage image = Capture.Element(element))
                {
                    image.Bitmap.Save(fullPath, ImageFormat.Png);
                }

                path = fullPath;
            }
            catch (Exception failure)
            {
                Console.WriteLine($"Could not capture a screenshot for '{stepName}': {failure.Message}");
            }

            return path;
        }

        private static string Sanitise(string stepName)
        {
            string cleaned = string.Join("-", stepName.Split(Path.GetInvalidFileNameChars()));

            return cleaned;
        }
    }
}
```

Evidence capture never throws into the run — a failed screenshot must not mask the failure it was documenting.

- [ ] **Step 2: Write the tree dumper**

`UiaTreeDumper.Dump` walks the automation subtree from a given root, depth-limited to 12, writing one indented line per element: `ControlType | AutomationId | Name | IsEnabled | IsOffscreen`. Rooted at the element the failing step was looking for, or the window if the step never found one.

- [ ] **Step 3: Write the environment read**

`RunEnvironment.Read()` records: app version before and after the run, OS build (`Environment.OSVersion` + the `CurrentBuildNumber` registry value), primary-monitor DPI scale, the app's theme and chart colour scheme read from the fixture `settings.json`, and whether the process is elevated. All of it goes in the report's Environment section.

- [ ] **Step 4: Write the HTML report**

A single self-contained file — inline CSS, screenshots embedded as `data:` URIs so the report survives being moved. Sections in the spec's order: **Verdict** (passed / failed / aborted, counts, total wall-clock), **Phase timeline** (each phase, duration, step results), **Each failure** (assertion, expected, actual, inline screenshot, collapsible tree dump), **Not covered by this run** (Task 13 fills the list), **Environment**.

- [ ] **Step 5: Prove the report renders from fabricated results**

Add a temporary `--selftest` argument to `Program.cs` that builds three fake `StepResult`s — one passed, one failed with a screenshot of the desktop and a tree dump, one skipped — and writes a report. Run it, open the HTML, confirm the failure's screenshot renders inline and the tree dump expands.

Run: `dotnet run --project Tools/UITests -- --selftest`
Expected: A report opens showing 1 passed, 1 failed, 1 skipped.

Keep `--selftest` — it is how the report gets changed later without a 15-minute run.

- [ ] **Step 6: Commit**

```bash
git add Tools/UITests/Evidence/ Tools/UITests/Program.cs
git commit -m "Capture evidence and render the UI test report."
```

DB impact: **none.**

---

### Task 5: Environment — the throwaway data folder and the fixture database

The run must never touch the operator's real 74 MB database. This task builds the sandbox and the known data every later assertion is written against.

**Files:**
- Create: `Tools/UITests/Environment/DataFolderFixture.cs`
- Create: `Tools/UITests/Environment/SeedDatabase.cs`
- Create: `Tools/UITests/Environment/SeedCounts.cs`
- Create: `Tools/UITests/Environment/InstalledApp.cs`
- Create: `Tools/UITests/Environment/RealDataGuard.cs`

**Interfaces:**
- Consumes: `AppDataFolderResolver.OverrideVariableName` (Task 1); the linked `AppDbContext` and `DatabaseInitializer`; `Models` entity types.
- Produces: `DataFolderFixture.CreateAsync() → DataFolderFixture` with `FolderPath`; `SeedDatabase.BuildAsync(string dbPath) → SeedCounts`; `InstalledApp.Launch(string dataFolder) → Application`; `InstalledApp.ShutDown(Application app)`; `RealDataGuard.CopyAside() → string`, `RealDataGuard.Restore(string backupPath)`.

- [ ] **Step 1: Write the seed counts**

`SeedCounts` is the single source of the known values. From the spec's fixture list:

```csharp
namespace NetworkMonitor.UITests.Environment
{
    public sealed record SeedCounts(
        int KnownDevices,
        int ApprovedDevices,
        int UnapprovedDevices,
        int DeviceEvents,
        int SpeedTestResults,
        int DigestReports);
}
```

The spec's fixture: **12 known devices** across approved and unapproved, one renamed, one with notes; **48 hours of device events** including arrivals and departures; traffic and rollup rows across the 5-minute, 1-hour and 6-hour windows for both WAN and LAN; local traffic across data and discovery classifications; **30 speed-test results** with a visible trend; **three generated digests**.

- [ ] **Step 2: Build the fixture through the app's own migrations**

`SeedDatabase.BuildAsync` creates an empty file, runs `DatabaseInitializer.InitializeAsync` against it — the app's real migration path — then inserts the known rows through `AppDbContext`. Using the real migrations rather than a checked-in `.db` means **the fixture cannot drift from the schema, and a broken migration fails the suite loudly**.

Timestamps are computed relative to a `DateTime nowUtc` parameter, never `DateTime.UtcNow` inline, so the same fixture is reproducible and the 5-minute / 1-hour / 6-hour windows land where the assertions expect.

- [ ] **Step 3: Write the data folder fixture**

`DataFolderFixture.CreateAsync` makes `%TEMP%\umnatha-uitests\<timestamp>\`, seeds `networkmonitor.db` into it via `SeedDatabase`, writes a known `settings.json`, and exposes `FolderPath` for `InstalledApp` to pass as `UMNATHA_DATA_FOLDER`.

- [ ] **Step 4: Write the real-data guard**

Per spec step 2, `CopyAside` **copies** `%LOCALAPPDATA%\UmnathaNetworkMonitor` to `UmnathaNetworkMonitor.uitest-backup-<timestamp>` and leaves the original in place — a hard kill mid-phase must leave the original where the app expects it. It records the row counts of the live database before copying, so `Restore` can verify them afterwards.

`Restore` deletes whatever data folder now exists, restores the backup over it, verifies the database opens and reports the same row counts, then deletes the backup. **If it cannot complete, it writes the backup location in large letters to the console and returns false** — the runner never leaves the operator guessing where their history went.

- [ ] **Step 5: Write the installed-app launcher**

`InstalledApp.Launch` reads `InstallLocation` from the uninstall key, starts `NetworkMonitor.exe` with `UMNATHA_DATA_FOLDER` set in its environment, and returns the FlaUI `Application`. `ShutDown` uses the tray Exit path where possible and falls back to `Close()` then `Kill()`, because the graceful exit is what checkpoints the WAL.

- [ ] **Step 6: Prove the fixture builds and the real folder is untouched**

Extend `--selftest` to build a fixture and print the seeded counts.

Run (elevated): `dotnet run --project Tools/UITests -- --selftest`
Expected: prints the seeded counts matching `SeedCounts`, and `%LOCALAPPDATA%\UmnathaNetworkMonitor\networkmonitor.db` has an unchanged `LastWriteTime`. Verify that timestamp explicitly before and after — it is the claim the whole suite rests on.

- [ ] **Step 7: Commit**

```bash
git add Tools/UITests/Environment/
git commit -m "Seed a throwaway database for the UI tests and guard the real one."
```

DB impact: **none.** `SeedDatabase` runs the existing migrations against a temporary file. No new migration is authored.

---

### Task 6: Driving — waits, sessions, navigation and grids

**Files:**
- Create: `Tools/UITests/Driving/Waits.cs`
- Create: `Tools/UITests/Driving/AppSession.cs`
- Create: `Tools/UITests/Driving/Navigator.cs`
- Create: `Tools/UITests/Driving/GridReader.cs`

**Interfaces:**
- Consumes: `InstalledApp` (Task 5).
- Produces: `Waits.Until(Func<bool> condition, TimeSpan timeout, string whatWeWereWaitingFor)`; `Waits.UntilFound<T>(Func<T?> find, TimeSpan timeout, string what) → T`; `AppSession.MainWindow`, `AppSession.MiniGraphWindow`, `AppSession.ByAutomationId(string id)`; `Navigator.GoTo(NavRoute route)`; `GridReader.RowCount(AutomationElement grid) → int`, `GridReader.CellText(AutomationElement grid, int row, int column) → string`.

- [ ] **Step 1: Write `Waits` — the only place a delay exists**

Per the spec's flake policy: **no `Thread.Sleep` as a synchronisation device**, every wait is a condition poll with a timeout and a message naming what it was waiting for, **no retry-until-green**, and every timeout is named and justified where it is declared.

```csharp
namespace NetworkMonitor.UITests.Driving
{
    public static class Waits
    {
        private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

        public static void Until(Func<bool> condition, TimeSpan timeout, string whatWeWereWaitingFor)
        {
            DateTime deadline = DateTime.UtcNow + timeout;

            bool satisfied = false;

            while (DateTime.UtcNow < deadline)
            {
                if (condition())
                {
                    satisfied = true;

                    break;
                }

                Thread.Sleep(PollInterval);
            }

            if (!satisfied)
            {
                throw new TimeoutException(
                    $"Waited {timeout.TotalSeconds:0.#}s for {whatWeWereWaitingFor} and it never happened.");
            }
        }

        public static TFound UntilFound<TFound>(Func<TFound?> find, TimeSpan timeout, string whatWeWereLookingFor)
            where TFound : class
        {
            TFound? found = null;

            Until(
                () =>
                {
                    found = find();

                    bool present = found is not null;

                    return present;
                },
                timeout,
                whatWeWereLookingFor);

            return found!;
        }
    }
}
```

`UntilFound` is the form nearly every phase uses — poll until a control exists, then act on it — and it exists so no phase writes its own loop. The generic parameter is `TFound` rather than `T`: single-character identifiers are banned, and that includes type parameters.

The single `Thread.Sleep` in the codebase is the poll interval inside `Until`, and that is the point of concentrating it here. Any phase that reaches for its own `Thread.Sleep` is a review rejection.

- [ ] **Step 2: Write `AppSession`**

Wraps the FlaUI `Application` and `UIA3Automation`, exposes the main window and the mini-graph window (a separate top-level window, found by class or title), and offers `ByAutomationId` returning null rather than throwing so callers can assert absence.

- [ ] **Step 3: Write `Navigator`**

The shell is a `NavigationView` with three items tagged `traffic`, `devices` and `reports` (`MainWindow.xaml:22-59`), plus Settings. **Select nav items with `SelectionItemPattern.Select()`, not `Invoke()`** — the spec is explicit, and invoking a `NavigationViewItem` does not reliably change selection.

- [ ] **Step 4: Write `GridReader`**

Uses the DataGrid's UIA `Grid` and `GridItem` patterns. Per the spec's risk table: **assert on row count via the grid's UIA pattern rather than by enumerating children**, and scroll to realise rows before reading them, because virtualisation hides unrealised rows from UIA. Verify this during implementation against the seeded 12 devices — do not assume it.

- [ ] **Step 5: Commit**

```bash
git add Tools/UITests/Driving/
git commit -m "Add the UI test driving helpers."
```

DB impact: **none.**

---

### Task 7: Automation identifiers

There are currently **zero** `AutomationProperties.AutomationId` values in the entire XAML — verified 2026-08-20 by a repo-wide grep, which returns hits only inside the spec document. Every control would otherwise be found by display text, which breaks whenever a label changes; this session already renamed "Resting opacity (%)" to "Resting opacity", exactly that kind of edit.

Add identifiers to **only the controls the suite drives**, not exhaustively. Identifiers are PascalCase and named for the thing, not the layout — `ScanNowButton`, not `TopRightButton`. This is additive attached XAML with no visual or layout effect, and it improves screen-reader navigation.

The control census across the driven surfaces (counted 2026-08-20): 66 buttons, 10 toggle switches, 5 combo boxes, 6 check boxes, 3 radio buttons, 1 slider, 1 text box. `SettingsPage.xaml` alone is 1,366 lines and holds 16 buttons, 9 toggles, 5 combos, 6 checks, 3 radios and the slider.

**Files:**
- Modify: `NetworkMonitor/MainWindow.xaml`, `NetworkMonitor/MiniGraphWindow.xaml`, and the 11 files in `NetworkMonitor/Views/`

- [ ] **Step 1: Confirm the starting point**

Run: `rg "AutomationProperties.AutomationId" NetworkMonitor/`
Expected: no matches.

- [ ] **Step 2: Add identifiers page by page**

Work one file at a time. For each control the suite drives, add the attached property in the **simple-assignment group**, before event handlers and bindings:

```xml
            <Button
                AutomationProperties.AutomationId="ScanNowButton"
                Content="Scan Network"
                Style="{StaticResource AccentButtonStyle}"
                Click="ScanNowClick" />
```

Minimum set per page — nav and shell first (`MainWindow`: the three nav items, Settings, the update banner and its "Update now" / "Later" / "Cancel" buttons), then Devices (the four tabs, scan, search, CSV import/export, edit, delete, the four grids), Traffic (Internet and Local, range selector, lens toggle, both charts' roots, drill-down), Speed Test (grid, trend chart root, run), Reports (digest list, render, PDF export), Settings (every setting the round-trip test touches), Mini graph (both orientations, section toggles, opacity slider).

- [ ] **Step 3: Verify each page with the tree dumper, not by eye**

This is why Task 4 came first. After each page, launch the app and dump that page's subtree:

Run: `dotnet run --project Tools/UITests -- --dump-tree <page>`
Expected: every identifier added appears in the dump with the exact spelling the phases will use. A typo found here costs a minute; found in Task 10 it costs a 15-minute run.

Add `--dump-tree` to `Program.cs` as a standing diagnostic — it is the tool for adding identifiers later.

- [ ] **Step 4: Confirm nothing moved**

Run: `dotnet build NetworkMonitor.slnx -c Debug -p:Platform=x64`
Expected: Build succeeded, 0 warnings.

Run: `dotnet test NetworkMonitor.Tests/NetworkMonitor.Tests.csproj`
Expected: PASS, 516 tests.

Then launch the app and look at each touched page. Attached properties cannot move anything, but the spec's risk table promises a manual pass and this is it.

- [ ] **Step 5: Commit**

```bash
git add NetworkMonitor/ Tools/UITests/Program.cs
git commit -m "Give the controls the UI tests drive stable automation identifiers."
```

DB impact: **none.**

---

### Task 8: Phases 01 Launch and 02 Devices

**Amended after the Task 7 checkpoint, 2026-08-20.** Three things were learned by driving the real app that the original plan did not know. All three land in this task, before any phase is written, because each one can silently point the suite at the operator's real data or leave their machine worse than it found it.

**A. The suite must refuse to run while the operator's own instance is up.** `App.xaml.cs:62` takes a named mutex, `NetworkMonitor.App.SingleInstance.Mutex`, and signals an activation event. A second launch therefore hands off to the running instance and exits — so `InstalledApp.Launch` would return a dead process and every subsequent lookup would resolve against **the operator's instance, which points at their real database**. That is the exact failure this design exists to prevent, and nothing currently detects it.

`Preflight.Check` gains a check: if any `NetworkMonitor` process is running, refuse, naming the process id and telling the operator to exit it from the tray. **The runner must not close it for them** — see B for why killing this app is not safe. Operator's decision, recorded 2026-08-20.

**B. An orphaned ETW session stops the app starting.** `TrafficCollector.cs:13` names its kernel session `NetworkMonitorTraffic`. If the app is hard-killed, that session survives, and the next launch hangs before it reaches its shell — reproduced twice at the checkpoint, and the reason Task 7 could never dump a live tree. `TrafficCollector.cs:164-169` already tries to clean up a leftover session by attaching and disposing, but it does not succeed after a kill.

Two consequences:

- `InstalledApp.ShutDown` falls back to `Kill()`, so **a failed run poisons the next one.** Teardown must stop the session — the equivalent of `logman stop NetworkMonitorTraffic -ets` — whenever it had to kill rather than exit gracefully, and say in the report that it did.
- `Preflight.Check` gains a stale-session check: a `NetworkMonitorTraffic` session running with no `NetworkMonitor` process behind it is a blocker, with the stop command in the message.

Note that closing the main window does **not** exit this app — it closes to tray. Any graceful shutdown has to go through the tray Exit item.

**C. Never assume the app is installed.** Operator's instruction, 2026-08-20: the suite acquires what it needs rather than refusing. When no install is present, the runner downloads the latest release, **verifies its SHA-256 against the `.sha256` asset before running it**, and installs it `/SILENT /SUPPRESSMSGBOXES /NORESTART`. Preflight's not-installed case becomes something it fixes, not something it reports. Keep every other preflight refusal as a refusal.

**Also now settled:** `UMNATHA_DATA_FOLDER` is proven end to end. Launching the real app with it set created `networkmonitor.db`, `Backups` and `Logs` inside the sandbox and left `%LOCALAPPDATA%\UmnathaNetworkMonitor` untouched. Task 1's verification gap is closed.

**D. The app under test is a LOCAL BUILD, not the installed release. Added 2026-08-20 after the first real run, which failed and exposed a contradiction in this plan.**

Spec decision 3 says drive the installed release. But this plan's own three production changes — the `UMNATHA_DATA_FOLDER` override, the chart draw summaries, and the 105 automation identifiers — exist only in this branch's source. **The installed v0.0.12 predates all of them**, verified by scanning the installed binary for `AllDevicesScanNowButton` and finding it absent. Two consequences, both observed on the first run:

- The shell can never be located by automation identifier, so `LaunchPhase` waited 45 seconds and aborted.
- Far worse: **the installed build ignores `UMNATHA_DATA_FOLDER` because it predates it**, so the fixture was silently bypassed and the run drove the operator's real data folder. The fixture folder held only the seeded `.db` and `settings.json` — no `-wal`, `-shm`, `Logs` or `Backups` — while the real folder's `-wal` and `-shm` were written during the run. No data was lost; these were the app's own ordinary writes. But every safeguard Task 5 built was bypassed, because it protects a folder the app under test was never using.

Driving the installed release requires a release that already ships the test hooks, which cannot exist until the suite that needs them has run. Operator's decision, 2026-08-20 — **option 1**:

- **Phases 01 through 08 drive a locally built binary** — `NetworkMonitor/bin/x64/Debug/net10.0-windows10.0.19041.0/win-x64/NetworkMonitor.exe`, built from the working tree. That binary has the override, the summaries and the identifiers by construction.
- **Only `UpdateLifecyclePhase` (Task 12) uses installed builds**, which is correct on its own terms: that phase is *about* installing an old release and updating it, so the installed build is the subject rather than the instrument.

**Two guards are required, because this failure was silent.** A run that drives the wrong folder must never again look like a run that merely failed:

1. **Static, at preflight** — the binary about to be driven must contain the override marker. Scan the app folder's assemblies for `UMNATHA_DATA_FOLDER` (it lives in `NetworkMonitor.Core.dll`, from `AppDataFolderResolver.OverrideVariableName`). Absent means refuse, naming the binary and saying it predates the override.
2. **Behavioural, immediately after launch** — assert the fixture folder gains `networkmonitor.db-wal` within a short, justified timeout. If the app is running and the fixture folder stays inert, it is using a different folder: **abort the whole run at once**, do not continue into the phases. This is the check that would have caught the first run in seconds rather than at teardown.

Rename `InstalledApp` to reflect that it now launches whichever build is under test, and make the choice explicit at the call site rather than implicit.

**Files:**
- Create: `Tools/UITests/Phases/LaunchPhase.cs`
- Create: `Tools/UITests/Phases/DevicesPhase.cs`
- Modify: `Tools/UITests/Runner/Preflight.cs` (A, B, C)
- Modify: `Tools/UITests/Fixtures/InstalledApp.cs` (B)
- Modify: `Tools/UITests/Program.cs`

- [ ] **Step 1: Write `LaunchPhase`**

Cold start against the seeded fixture: the splash appears and closes, the main window reaches the ready state, the mini-graph window exists if settings say it should, the title carries the expected version, and no error dialog is present. `abortsRun` is **true** — if the app will not start, nothing after it means anything.

- [ ] **Step 2: Write `DevicesPhase`**

All / Approved / Unapproved / History tabs. Against the seeded 12 devices: the All grid's row count matches `SeedCounts.KnownDevices`, the renamed device shows its name, the device with notes shows them, Approved and Unapproved split as seeded, History shows the 48 hours of events with arrivals and departures. Then CSV export to the fixture folder and re-import, edit a device, delete a device and confirm the count drops by one.

- [ ] **Step 3: Wire both into the sequence and run**

Run (elevated): `dotnet run --project Tools/UITests`
Expected: Exit 0, report shows both phases green. If a step fails, the report has the screenshot and the tree dump — diagnose from those, not by re-running.

- [ ] **Step 4: Commit**

```bash
git add Tools/UITests/
git commit -m "Drive launch and the device pages from the UI test suite."
```

DB impact: **none.**

---

### Task 9: Phases 03 Traffic and 04 Speed Test

**Files:**
- Create: `Tools/UITests/Phases/TrafficPhase.cs`
- Create: `Tools/UITests/Phases/SpeedTestPhase.cs`

- [ ] **Step 1: Write `TrafficPhase`**

Internet and Local tabs. Per range (5m, 1h, 6h): read the chart root's `AutomationProperties.Name`, parse it with `ChartDrawSummary.TryParse` (Task 2), and assert `Buckets > 0`, `Range` matches the selected range, and `Peak` matches the seeded maximum for that window. Then the Local lens toggle (By app ↔ By device), the service / discovery / rate chips, drill-down expansion staying open, and a chart-bucket click filtering the grid.

- [ ] **Step 2: Write `SpeedTestPhase`**

The seeded 30 results: grid row count, the trend chart's summary parsing with `Range == "speed"` and `Buckets == 30`, and the visible trend. **A real speed test is not run** — it depends on the operator's internet at that moment, is non-deterministic and slow. That exclusion goes in the report's "Not covered" list in Task 13.

- [ ] **Step 3: Run and commit**

Run (elevated): `dotnet run --project Tools/UITests`
Expected: Exit 0, four phases green.

```bash
git add Tools/UITests/Phases/
git commit -m "Drive the traffic and speed test pages from the UI test suite."
```

DB impact: **none.**

---

### Task 10: Phases 05 Reports and 06 Settings

**Files:**
- Create: `Tools/UITests/Phases/ReportsPhase.cs`
- Create: `Tools/UITests/Phases/SettingsPhase.cs`

- [ ] **Step 1: Write `ReportsPhase`**

The three seeded digests: the list shows them, one renders, PDF export writes a file to the fixture folder and the file is non-empty and starts with `%PDF`. The **24-hour digest schedule is not tested** — it is bound to wall-clock time; only the output is reachable.

- [ ] **Step 2: Write `SettingsPhase`**

Every setting the UI exposes, round-tripped through `settings.json` **on disk in the fixture folder**, not just read back from the control. For each: change it, wait for the save, read the JSON, assert the new value, restore it. This is the phase that catches a setting that appears to save but does not — the class of defect commit `3a822b8` fixed.

Cover the 9 toggles, 5 combos, 6 checks, 3 radios and the opacity slider. The chart colour scheme combo drives the v0.0.12 feature and is worth an explicit assertion that the scheme id lands in the JSON.

- [ ] **Step 3: Run and commit**

Run (elevated): `dotnet run --project Tools/UITests`
Expected: Exit 0, six phases green.

```bash
git add Tools/UITests/Phases/
git commit -m "Drive reports and every setting from the UI test suite."
```

DB impact: **none.**

---

### Task 11: Phases 07 Mini Graph and 08 Purge

**Files:**
- Create: `Tools/UITests/Phases/MiniGraphPhase.cs`
- Create: `Tools/UITests/Phases/PurgePhase.cs`

- [ ] **Step 1: Write `MiniGraphPhase`**

Both orientations (panel and horizontal strip), section toggles, the opacity slider, the last-section invariant (the final section cannot be turned off), and placement persisting across a hide and show. **Assert the strip's height is unchanged across five orientation switches** — that is U-1 from the 2026-08-12 manual run, where the strip grew ~7 DIP per switch until it hit the 120 DIP ceiling. It is pinned by a unit test on the arithmetic; this pins it through the real save/restore round trip, which is where it actually broke.

**Mixed-DPI is out of reach** and stays in `Documents/Code Review/2026-08-10/manual-test-plan.md` Part 1 — C2-2 and C2-5 still need a second monitor at a different scale factor. Say so in the report's "Not covered" list; this suite does not close them.

- [ ] **Step 2: Write `PurgePhase`**

The retention purge and the one-way-door paths: purge traffic data, purge device history, and the confirmation dialogs on each. Runs against the fixture, so a purge destroys only seeded rows. Assert the row counts drop as expected by opening the fixture database directly afterwards.

- [ ] **Step 3: Run and commit**

Run (elevated): `dotnet run --project Tools/UITests`
Expected: Exit 0, eight phases green.

```bash
git add Tools/UITests/Phases/
git commit -m "Drive the mini graph and the purge paths from the UI test suite."
```

DB impact: **none.** The purge deletes seeded rows from a temporary database.

---

### Task 12: Phase 09 — the update lifecycle

**This is the phase with real consequences.** It is last, it is the only phase that mutates the machine outside the throwaway folder, and it really does uninstall the operator's app.

**Files:**
- Create: `Tools/UITests/Phases/UpdateLifecyclePhase.cs`
- Create: `Tools/UITests/Environment/ReleaseResolver.cs`

**Interfaces:**
- Consumes: `RealDataGuard` (Task 5); `ReleaseInfoParser`, `UpdateDownloader`, `ChecksumVerifier`, `SemanticVersion` from `NetworkMonitor.Core/Update/` — **spec correction 3**, this machinery already exists and is reused rather than reimplemented.
- Produces: `ReleaseResolver.ResolveAsync() → (string targetTag, string baselineTag, AvailableUpdate baseline)`.

- [ ] **Step 1: Write `ReleaseResolver`**

Query `https://api.github.com/repos/jazzzsoftware/UmnathaNetworkMonitor/releases`, take the newest as the target and the second-newest as the baseline. **Verified 2026-08-20:** that is v0.0.12 and v0.0.11. Fail the phase if fewer than two releases exist.

`ReleaseInfoParser.Parse` takes a **single** release object while `/releases` returns an array — index element `[1]` and pass that element's raw JSON to `Parse`.

GitHub's unauthenticated limit is 60 requests an hour and this makes one. If it does fail, report the limit rather than a confusing parse error.

- [ ] **Step 2: Write the guards (spec step 1)**

Refuse if a previous run left a stranded data-folder backup (already in `Preflight`), refuse if not elevated, refuse if the working copy has uncommitted installer changes.

- [ ] **Step 3: Copy the live data folder aside (spec step 2)**

`RealDataGuard.CopyAside()`, wrapped so that **everything from here to Step 10 sits inside a `try` whose `finally` restores**.

- [ ] **Step 4: Uninstall the current build (spec step 4)**

Read the uninstall string from the registry key in `Preflight.UninstallKeyPath` and run:

```
"C:\Program Files\Umnatha Network Monitor\unins000.exe" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART
```

Wait for the uninstall entry to disappear, using `Waits.Until` with a named timeout.

- [ ] **Step 5: Download and install the baseline (spec step 5)**

**Spec correction 1:** the SHA-256 is a **separate release asset**, not in the release notes. Verified on the live v0.0.11 release, whose assets are exactly:

```
Umnatha.Network.Monitor.v0.0.11.exe          74,613,468 bytes
Umnatha.Network.Monitor.v0.0.11.exe.sha256           64 bytes
```

`ReleaseInfoParser.Parse` already resolves both URLs into `AvailableUpdate.DownloadUrl` and `AvailableUpdate.ChecksumUrl`, and `UpdateDownloader` already downloads and verifies against it. Reuse both, then run the installer `/SILENT /SUPPRESSMSGBOXES /NORESTART`.

- [ ] **Step 6: Assert the baseline is installed (spec step 6)**

Registry `DisplayVersion` equals the baseline version.

- [ ] **Step 7: Launch the baseline and wait for the banner (spec step 7)**

**The old build ignores `UMNATHA_DATA_FOLDER`** — it predates the override — so it uses the real folder. That is precisely why Step 3 exists.

Wait for the `InfoBar` in `MainWindow` and assert its message names the target version. `UpdateService` has a 20-second check deadline, so **the wait is 45 seconds** before failing — that reasoning lives next to the number, per the flake policy.

- [ ] **Step 8: Drive the update (spec step 8)**

**Spec correction 2:** the banner's buttons are **"Update now"** and **"Later"**, with **"Cancel"** appearing during download (`MainWindow.xaml:139-167`). There is no "Download" button — the download starts on "Update now".

Click **Update now**, wait for the progress bar to reach 100 and for SHA-256 verification, then let `InstallerLauncher` run the installer `/SILENT` and exit the app.

Because the baseline build cannot be found by automation id (it predates Task 7), find these three buttons **by name** — and say so in a comment, so nobody later "fixes" it to use an id that does not exist in the old build.

- [ ] **Step 9: Assert the target is installed (spec step 9)**

Registry `DisplayVersion` equals the target version; relaunch and assert the in-app version matches.

- [ ] **Step 10: Restore (spec step 10)**

In the `finally`: delete whatever data folder now exists, restore the backup copy over it, and verify the restored database opens and reports the same row counts recorded in Step 3.

**If restore cannot complete, write the backup location in large letters into the report and to the console, and exit non-zero.** This is the single most important line of error handling in the suite.

- [ ] **Step 11: Rehearse the destructive path before trusting it**

Do **not** let the first execution of this phase be a real run against the operator's install.

1. Run Steps 1–3 and 10 alone, with the uninstall and install stubbed out, and confirm the copy-aside and restore round-trip the live folder byte-for-byte with matching row counts.
2. Kill the process between copy-aside and restore, then confirm the original folder is still in place and preflight refuses the next run with the stranded-backup message.
3. Only then run the phase whole.

- [ ] **Step 12: Commit**

```bash
git add Tools/UITests/
git commit -m "Prove the uninstall, install and update cycle from the UI test suite."
```

DB impact: **none to the schema.** The phase copies, deletes and restores the user's database file; it never alters its shape. The baseline build runs its own migrations against the real folder, which is exactly the upgrade path being tested.

---

### Task 13: Honest reporting, docs and registration

A green run must not read as total coverage. This task makes the report say what it did not do, and puts the suite into the repo's paperwork.

**Files:**
- Modify: `Tools/UITests/Evidence/HtmlReport.cs`
- Create: `Tools/UITests/Evidence/NotCovered.cs`
- Modify: `Tools/UITests/README.md`, `NetworkMonitor.slnx`, `CLAUDE.md`, `Documents/To Do.txt`

- [ ] **Step 1: Write the "Not covered by this run" list**

Fixed, from the spec's boundary section plus what Task 11 found:

- **Chart pixels** — line shape, colours, smoothing. The draw summary proves the data arrived; it cannot prove the drawing is correct.
- **The 24-hour digest schedule** — bound to wall-clock time.
- **The 6-hour and 1-hour live windows** — seeded, not lived.
- **A device genuinely going offline** — needs real hardware to stop responding.
- **A real speed test** — non-deterministic and slow.
- **ETW capture of real traffic** — needs real packets through a kernel session.
- **Mixed-DPI multi-monitor** — C2-2 and C2-5 remain open in `Documents/Code Review/2026-08-10/`, and only `manual-test-plan.md` Part 1 closes them.

It renders as a section of the report on **every** run, pass or fail.

- [ ] **Step 2: Flag the time budget**

`PhaseRunner` already records per-phase durations. If the total exceeds 15 minutes, the report says so and names the slowest phase.

- [ ] **Step 3: Register in the slnx**

Add after the `/Tools/RetentionProbe/` folder, matching the `MigrationVerify` shape — **`<File>` entries, never `<Project>`**:

```xml
  <Folder Name="/Tools/UITests/">
    <File Path="Tools/UITests/Program.cs" />
    <File Path="Tools/UITests/README.md" />
    <File Path="Tools/UITests/UITests.csproj" />
  </Folder>
```

This plan itself is already registered under `/Documents/superpowers/plans/` — it was added when the plan was written, per the standing rule that the slnx tracks `Documents/` as it changes.

- [ ] **Step 4: Confirm the solution build never touches the runner**

Run: `dotnet build NetworkMonitor.slnx -c Debug -p:Platform=x64`
Expected: Build succeeded, 0 warnings, and **no UITests output** in the log. This is the guarantee that a routine build cannot uninstall the operator's app.

- [ ] **Step 5: Update CLAUDE.md**

Add `Tools/UITests/` to the `/Tools/` list with one line on what it does and that it is destructive. Add the `UMNATHA_DATA_FOLDER` override to the Notes section — it is now the supported way to point an install at a copied database for diagnosis, and it is what `Tools/HistoryRestore` should target rather than the live file. Add `Tools/UITests/Program.cs` to the Key Files table.

- [ ] **Step 6: Update the To Do**

Change the `UI automated testing - spec written` line to `Done - UI automated testing`, with a note that mixed-DPI is still manual.

- [ ] **Step 7: Full verification**

Run: `dotnet build NetworkMonitor.slnx -c Debug -p:Platform=x64` → 0 warnings.
Run: `dotnet test NetworkMonitor.Tests/NetworkMonitor.Tests.csproj` → 516 tests pass.
Run (elevated): `dotnet run --project Tools/UITests` → exit 0, nine phases green, report under 15 minutes, "Not covered" section present.

- [ ] **Step 8: Commit**

```bash
git add Tools/UITests/ NetworkMonitor.slnx CLAUDE.md "Documents/To Do.txt"
git commit -m "Say what the UI test suite did not cover, and register it."
```

DB impact: **none.**

---

## Verification summary

| Gate | Command | Expected |
|---|---|---|
| App builds | `dotnet build NetworkMonitor.slnx -c Debug -p:Platform=x64` | 0 errors, 0 warnings, no UITests output |
| Unit tests | `dotnet test NetworkMonitor.Tests/NetworkMonitor.Tests.csproj` | 516 pass (501 baseline + 15 new) |
| MigrationVerify still builds | `dotnet build Tools/MigrationVerify/MigrationVerify.csproj` | Succeeded — catches the Task 1 Step 6 trap |
| Runner builds | `dotnet build Tools/UITests/UITests.csproj` | Succeeded |
| Suite | `dotnet run --project Tools/UITests` (elevated) | Exit 0, under 15 minutes |
| Real data intact | `LastWriteTime` of `%LOCALAPPDATA%\UmnathaNetworkMonitor\networkmonitor.db` | Unchanged across Tasks 1–11; restored with matching row counts after Task 12 |

## Out of scope

Carried from the spec, unchanged:

- **CI.** The suite needs an interactive desktop session and elevation.
- **Tier 1 headless unit tests for view models and Services.** Retargeting `NetworkMonitor.Tests` to `net10.0-windows10.0.19041.0` would unblock 4,401 lines of view model across 15 files — worthwhile, separate, and not this suite.
- **Screenshot baseline comparison for chart pixels.** Too brittle across DPI, theme and the five colour schemes.
- **Accessibility scanning with Axe.Windows.** A natural follow-on once the identifiers from Task 7 exist.
