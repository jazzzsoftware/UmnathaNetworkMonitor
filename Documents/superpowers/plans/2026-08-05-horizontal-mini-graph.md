# Horizontal Mini Graph Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a horizontal orientation to the existing mini graph widget — the same four sections laid out left-to-right in a short, wide strip sized to sit on the Windows taskbar.

**Architecture:** One window, one `Grid`, reconfigured in code. `MiniGraphWindow.ApplyLayout()` gains an orientation branch that swaps `RowDefinitions` for `ColumnDefinitions` and reassigns `Grid.Row` / `Grid.Column` on the existing children — no second window class and no duplicate `MiniTrafficSection` instances. Orientation lives on `MiniGraphState` beside `Opacity`; placement is stored per orientation. The derived-width calculation and the short speed-test string are pure and live in Core and Models respectively, where the test project can reach them.

**Tech Stack:** .NET 10, WinUI 3 (Windows App SDK 2.2.0), CommunityToolkit.Mvvm, xunit.

**Spec:** [`Documents/superpowers/specs/2026-08-05-horizontal-mini-graph-design.md`](../specs/2026-08-05-horizontal-mini-graph-design.md)

## Global Constraints

- **Coding conventions are non-negotiable and enforced on review.** No `var`. No single-character identifiers, including in lambdas and pattern matches. Curly braces on every `if` / `else` / `for` / `foreach` / `while` / `using`, even single-line bodies. `string.Empty` over `""`. Single exit point per method — exactly one `return`, at the end. Returns stand alone — assign to a local, blank line, then `return` that local. Blank lines above and below every block, at every nesting level, including immediately after a method's opening `{` when the first statement is a block and immediately before its closing `}` when the last statement ends with `}`. No comments unless the WHY is non-obvious. One type per file, named exactly after the type.
- **Class member order:** Fields → Constructor → Properties → Public methods → Override methods → Private methods. A property's backing field sits immediately above that property in the Properties section, not with the other fields. Hand-write `SetProperty(ref _field, value)`; never use `[ObservableProperty]`.
- **XAML formatting:** one attribute per line indented 4 spaces, blank line above and below every element, blank line after an opening tag and before a closing tag, attribute order = simple assignments, then event handlers and `Command`, then `{x:Bind}` value assignments last. `DevicesPage.xaml` is the canonical reference.
- **Layering:** Models ← Core ← Services ← App. `NetworkMonitor.Tests` references **Models and Core only** — nothing in Services or the App project can be unit tested. Tasks 3 through 7 are therefore build-and-manual verified, and say so explicitly.
- **Every sub-folder is its own namespace.** `NetworkMonitor.Models/Widget/` → `NetworkMonitor.Models.Widget`. `NetworkMonitor.Core/Widget/` → `NetworkMonitor.Core.Widget`.
- **Database impact: none.** No entity, `DbSet`, column or index changes anywhere in this plan. No EF Core migration. Orientation and strip placement are `settings.json` preferences only. State this in the final commit message.
- **Platform is x64.** WinUI 3 does not support Any CPU.
- Build: `dotnet build NetworkMonitor.slnx -p:Platform=x64`
- Test: `dotnet test NetworkMonitor.Tests/NetworkMonitor.Tests.csproj --nologo`
- **Baseline, measured on master at `025cdbd` before any of this work: 292 passed, 0 failed, 0 skipped.** Every task's test step must end at 292 plus that task's new tests. A drop below 292 is a regression, not a flake.

## Refinement of the spec, agreed before implementation

The spec says cell widths are "measured from the rendered text, not assumed". This plan instead uses **nominal width constants** on `HorizontalStripMetrics`. The strings are fixed-format (`Internet`, `Peak 4.2 MB/s`, `↓94 ↑12 Mb/s  18 ms`, `⚠ 3 unknown devices`), and a runtime text-measurement pass would make the width calculation untestable and give the window two competing sources of truth for its own size. The constants are tunable in one place and covered by tests. If a string ever overflows its cell, the fix is to tune the constant.

## File Structure

| File | Responsibility |
|---|---|
| `NetworkMonitor.Models/Widget/MiniGraphOrientation.cs` | **Create.** Two-value enum. |
| `NetworkMonitor.Core/Widget/HorizontalStripMetrics.cs` | **Create.** Pure derived width, font scale, height clamp, peak-visibility threshold. |
| `NetworkMonitor.Models/Formatting/MiniGraphFormatter.cs` | **Modify.** Add `SpeedTestShort`. |
| `NetworkMonitor.Services/Data/Settings.cs` | **Modify.** Four new keys. |
| `NetworkMonitor.Services/Platform/MiniGraphState.cs` | **Modify.** `Orientation`, `SaveStripPlacement`. |
| `NetworkMonitor/MiniGraphWindow.xaml` | **Modify.** Name the close button's host so it can move between a floating overlay and a grid column. |
| `NetworkMonitor/MiniGraphWindow.xaml.cs` | **Modify.** Orientation branch in layout, sizing, placement, font scale, menu. |
| `NetworkMonitor/ViewModels/SettingsViewModel.cs` | **Modify.** `MiniGraphHorizontal` property. |
| `NetworkMonitor/Views/SettingsPage.xaml` | **Modify.** Orientation toggle. |
| `NetworkMonitor.Tests/HorizontalStripMetricsTests.cs` | **Create.** |
| `NetworkMonitor.Tests/MiniGraphFormatterTests.cs` | **Modify.** Short-form cases. |

---

### Task 1: Orientation enum and the derived-width calculation

Pure, fully unit tested, no UI. Nothing later in the plan can be built without the constants this task fixes.

**Files:**
- Create: `NetworkMonitor.Models/Widget/MiniGraphOrientation.cs`
- Create: `NetworkMonitor.Core/Widget/HorizontalStripMetrics.cs`
- Test: `NetworkMonitor.Tests/HorizontalStripMetricsTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `NetworkMonitor.Models.Widget.MiniGraphOrientation` — `Vertical`, `Horizontal`.
  - `NetworkMonitor.Core.Widget.HorizontalStripMetrics`, a static class with:
    - `public static double FontScale(double height)`
    - `public static double Width(bool showInternet, bool showLocal, bool showSpeedTest, bool showUnknownDevices, double fontScale)`
    - `public static bool ShowsPeak(double height)`
    - `public static double ClampHeight(double height)`
    - constants `MinimumHeight = 28.0`, `MaximumHeight = 120.0`, `DefaultHeight = 40.0`.

- [ ] **Step 1: Create the enum**

Create `NetworkMonitor.Models/Widget/MiniGraphOrientation.cs`:

```csharp
namespace NetworkMonitor.Models.Widget
{
    public enum MiniGraphOrientation
    {
        Vertical,
        Horizontal
    }
}
```

- [ ] **Step 2: Write the failing tests**

Create `NetworkMonitor.Tests/HorizontalStripMetricsTests.cs`:

```csharp
using Xunit;
using NetworkMonitor.Core.Widget;

namespace NetworkMonitor.Tests
{
    public class HorizontalStripMetricsTests
    {
        [Fact]
        public void FontScaleIsOneAtTheReferenceHeight()
        {
            double scale = HorizontalStripMetrics.FontScale(40.0);

            Assert.Equal(1.0, scale, 3);
        }

        // The vertical widget learned this the hard way: letting the scale fall below one made the
        // text illegible at small sizes rather than merely small. The strip keeps the same floor.
        [Fact]
        public void FontScaleNeverFallsBelowOne()
        {
            double scale = HorizontalStripMetrics.FontScale(28.0);

            Assert.Equal(1.0, scale, 3);
        }

        [Fact]
        public void FontScaleGrowsWithHeightAndCapsAtTwo()
        {
            double middle = HorizontalStripMetrics.FontScale(60.0);
            double capped = HorizontalStripMetrics.FontScale(400.0);

            Assert.Equal(1.5, middle, 3);
            Assert.Equal(2.0, capped, 3);
        }

        [Fact]
        public void ClampHeightHoldsTheStripBetweenItsBounds()
        {
            double low = HorizontalStripMetrics.ClampHeight(4.0);
            double high = HorizontalStripMetrics.ClampHeight(900.0);
            double inside = HorizontalStripMetrics.ClampHeight(55.0);

            Assert.Equal(HorizontalStripMetrics.MinimumHeight, low, 3);
            Assert.Equal(HorizontalStripMetrics.MaximumHeight, high, 3);
            Assert.Equal(55.0, inside, 3);
        }

        // 170 + 170 + 196 + 146 + 22 cells, four gaps of 4, padding of 4 either side.
        [Fact]
        public void WidthSumsEveryVisibleCellPlusGapsAndPadding()
        {
            double width = HorizontalStripMetrics.Width(true, true, true, true, 1.0);

            Assert.Equal(728.0, width, 3);
        }

        [Fact]
        public void TurningASectionOffNarrowsTheStrip()
        {
            double all = HorizontalStripMetrics.Width(true, true, true, true, 1.0);
            double withoutLocal = HorizontalStripMetrics.Width(true, false, true, true, 1.0);

            Assert.Equal(all - 170.0 - 4.0, withoutLocal, 3);
        }

        // The close column is not a section and cannot be switched off, so it is present even when
        // the state has been reduced to its single mandatory section.
        [Fact]
        public void TheCloseColumnIsAlwaysCounted()
        {
            double width = HorizontalStripMetrics.Width(true, false, false, false, 1.0);

            Assert.Equal(4.0 + 170.0 + 4.0 + 22.0 + 4.0, width, 3);
        }

        // Cells scale with the font but the gaps and padding do not, so width is not a flat multiple
        // of the scale. Getting this wrong leaves the text clipped at large heights.
        [Fact]
        public void CellsScaleWithTheFontButGapsAndPaddingDoNot()
        {
            double width = HorizontalStripMetrics.Width(true, false, false, false, 2.0);

            Assert.Equal(4.0 + 340.0 + 4.0 + 44.0 + 4.0, width, 3);
        }

        [Fact]
        public void ThePeakFigureIsDroppedOnlyBelowThirtyFour()
        {
            Assert.False(HorizontalStripMetrics.ShowsPeak(30.0));
            Assert.True(HorizontalStripMetrics.ShowsPeak(34.0));
            Assert.True(HorizontalStripMetrics.ShowsPeak(48.0));
        }
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test NetworkMonitor.Tests/NetworkMonitor.Tests.csproj --nologo`
Expected: compile failure — `HorizontalStripMetrics` does not exist.

- [ ] **Step 4: Write the implementation**

Create `NetworkMonitor.Core/Widget/HorizontalStripMetrics.cs`:

```csharp
namespace NetworkMonitor.Core.Widget
{
    // The strip's width is not something the user drags: it is the sum of whatever sections are
    // switched on. Keeping that sum here rather than in the window is what makes it testable, and
    // the nominal cell widths below are the one place to tune if a string ever overflows.
    public static class HorizontalStripMetrics
    {
        public const double MinimumHeight = 28.0;
        public const double MaximumHeight = 120.0;
        public const double DefaultHeight = 40.0;

        private const double Padding = 4.0;
        private const double Gap = 4.0;
        private const double InternetCellWidth = 170.0;
        private const double LocalCellWidth = 170.0;
        private const double SpeedCellWidth = 196.0;
        private const double UnknownDevicesCellWidth = 146.0;
        private const double CloseCellWidth = 22.0;
        private const double MinimumFontScale = 1.0;
        private const double MaximumFontScale = 2.0;

        // The label and the peak share one baseline row and the chart needs whatever is left. Below
        // this the two collide, so the peak goes rather than being allowed to overlap the label.
        private const double PeakMinimumHeight = 34.0;

        public static double FontScale(double height)
        {
            double scale = Math.Clamp(height / DefaultHeight, MinimumFontScale, MaximumFontScale);

            return scale;
        }

        public static double ClampHeight(double height)
        {
            double clamped = Math.Clamp(height, MinimumHeight, MaximumHeight);

            return clamped;
        }

        public static bool ShowsPeak(double height)
        {
            bool showsPeak = height >= PeakMinimumHeight;

            return showsPeak;
        }

        public static double Width(bool showInternet, bool showLocal, bool showSpeedTest, bool showUnknownDevices, double fontScale)
        {
            double cells = CloseCellWidth;
            int cellCount = 1;

            if (showInternet)
            {
                cells += InternetCellWidth;
                cellCount++;
            }

            if (showLocal)
            {
                cells += LocalCellWidth;
                cellCount++;
            }

            if (showSpeedTest)
            {
                cells += SpeedCellWidth;
                cellCount++;
            }

            if (showUnknownDevices)
            {
                cells += UnknownDevicesCellWidth;
                cellCount++;
            }

            double width = (cells * fontScale) + ((cellCount - 1) * Gap) + (Padding * 2.0);

            return width;
        }
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test NetworkMonitor.Tests/NetworkMonitor.Tests.csproj --nologo`
Expected: PASS, including every pre-existing test.

- [ ] **Step 6: Commit**

```bash
git add NetworkMonitor.Models/Widget/MiniGraphOrientation.cs NetworkMonitor.Core/Widget/HorizontalStripMetrics.cs NetworkMonitor.Tests/HorizontalStripMetricsTests.cs
git commit
```

Subject: `Add the horizontal strip's orientation enum and width metrics.` Body opens with **Context** (the strip's width is derived rather than dragged, so the sum needs to live somewhere testable), then **Change**. State: no DB impact.

---

### Task 2: Short-form speed-test string

**Files:**
- Modify: `NetworkMonitor.Models/Formatting/MiniGraphFormatter.cs`
- Test: `NetworkMonitor.Tests/MiniGraphFormatterTests.cs`

**Interfaces:**
- Consumes: nothing from Task 1.
- Produces: `public static string MiniGraphFormatter.SpeedTestShort(SpeedTestResult? latest, RateUnitMode mode)` — returns `↓94 ↑12 Mb/s  18 ms` on success and `not run yet` otherwise. **No leading gap**, unlike `SpeedTest`: the horizontal cell renders its bold `Speed` label as a separate element with its own margin, so a leading gap would double the spacing.

- [ ] **Step 1: Write the failing tests**

Append to `NetworkMonitor.Tests/MiniGraphFormatterTests.cs`, inside the existing class:

```csharp
        // The horizontal cell draws its own bold "Speed" label with a margin, so unlike the vertical
        // widget's line this string must not carry a leading gap of its own.
        [Fact]
        public void ShortSpeedTestOpensWithTheDownloadRateAndNoLeadingGap()
        {
            SpeedTestResult result = new SpeedTestResult
            {
                Timestamp = new DateTime(2026, 8, 2, 6, 0, 0, DateTimeKind.Utc),
                DownloadMbps = 94.0,
                UploadMbps = 12.0,
                LatencyMs = 18.0,
                JitterMs = 4.0,
                Success = true
            };

            string text = MiniGraphFormatter.SpeedTestShort(result, RateUnitMode.Bits);

            Assert.Equal("↓94 ↑12 Mb/s  18 ms", text);
        }

        [Fact]
        public void ShortSpeedTestDropsJitterAndTheTimestamp()
        {
            SpeedTestResult result = new SpeedTestResult
            {
                Timestamp = new DateTime(2026, 8, 2, 6, 0, 0, DateTimeKind.Utc),
                DownloadMbps = 94.0,
                UploadMbps = 12.0,
                LatencyMs = 18.0,
                JitterMs = 4.0,
                Success = true
            };

            string text = MiniGraphFormatter.SpeedTestShort(result, RateUnitMode.Bits);

            Assert.DoesNotContain("Jitter", text);
            Assert.DoesNotContain(result.LocalTimestamp.ToString("HH:mm"), text);
        }

        [Fact]
        public void ShortSpeedTestHonoursByteMode()
        {
            SpeedTestResult result = new SpeedTestResult
            {
                Timestamp = new DateTime(2026, 8, 2, 6, 0, 0, DateTimeKind.Utc),
                DownloadMbps = 512.0,
                UploadMbps = 48.0,
                LatencyMs = 9.0,
                JitterMs = 3.0,
                Success = true
            };

            string text = MiniGraphFormatter.SpeedTestShort(result, RateUnitMode.Bytes);

            Assert.Contains("MB/s", text);
            Assert.DoesNotContain("Mb/s", text);
        }

        // Below ten a rate must keep its decimal or a slow link reads as zero, and at or above ten the
        // decimal carries nothing and costs width the cell does not have.
        [Fact]
        public void ShortSpeedTestKeepsADecimalOnlyBelowTen()
        {
            SpeedTestResult slow = new SpeedTestResult
            {
                Timestamp = new DateTime(2026, 8, 2, 6, 0, 0, DateTimeKind.Utc),
                DownloadMbps = 5.6,
                UploadMbps = 3.0,
                LatencyMs = 40.0,
                Success = true
            };

            string text = MiniGraphFormatter.SpeedTestShort(slow, RateUnitMode.Bits);

            Assert.Equal("↓5.6 ↑3 Mb/s  40 ms", text);
        }

        [Fact]
        public void ShortSpeedTestReadsAsAPromptWhenNothingHasRun()
        {
            string missing = MiniGraphFormatter.SpeedTestShort(null, RateUnitMode.Bits);

            Assert.Equal("not run yet", missing);
        }

        [Fact]
        public void ShortSpeedTestTreatsAFailedRunAsNoResult()
        {
            SpeedTestResult failed = new SpeedTestResult
            {
                Timestamp = DateTime.UtcNow,
                Success = false,
                Error = "No internet"
            };

            string text = MiniGraphFormatter.SpeedTestShort(failed, RateUnitMode.Bits);

            Assert.Equal("not run yet", text);
        }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test NetworkMonitor.Tests/NetworkMonitor.Tests.csproj --nologo`
Expected: compile failure — `SpeedTestShort` does not exist.

- [ ] **Step 3: Write the implementation**

In `NetworkMonitor.Models/Formatting/MiniGraphFormatter.cs`, add a constant beside `ElementGap` and a method immediately after the existing `SpeedTest`, leaving `SpeedTest` untouched:

```csharp
        // One em space. The short line carries two readings rather than four, so it needs separation
        // between the rates and the ping but not the wide gap the full line uses.
        private const string ShortGap = "  ";
```

```csharp
        // The horizontal strip has room for the rates and the ping and nothing else. Jitter and the
        // timestamp are dropped rather than shrunk: a cell wide enough for all four readings would be
        // twice the width of a traffic section and would dominate the strip.
        public static string SpeedTestShort(SpeedTestResult? latest, RateUnitMode mode)
        {
            string text = "not run yet";

            if (latest is not null && latest.Success)
            {
                bool inBytes = TrafficRateFormatter.SingleUnit(mode) == RateUnitMode.Bytes;
                string unit = inBytes ? "MB/s" : "Mb/s";
                double download = inBytes ? latest.DownloadMBps : latest.DownloadMbps;
                double upload = inBytes ? latest.UploadMBps : latest.UploadMbps;

                text = $"↓{Scaled(download)} ↑{Scaled(upload)} {unit}{ShortGap}{latest.LatencyMs:F0} ms";
            }

            return text;
        }
```

Note: the expected strings in Step 1 use two U+2002 en spaces between the unit and the ping. If the test literals were typed with ordinary spaces they will fail — make the test literals `  ` to match, or paste the same characters.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test NetworkMonitor.Tests/NetworkMonitor.Tests.csproj --nologo`
Expected: PASS, all tests including the six pre-existing formatter tests.

- [ ] **Step 5: Commit**

```bash
git add NetworkMonitor.Models/Formatting/MiniGraphFormatter.cs NetworkMonitor.Tests/MiniGraphFormatterTests.cs
git commit
```

Subject: `Add a short-form speed test line for the horizontal strip.` Body: **Context** (the full line is roughly 320px and would dominate a strip), then **Change**. State: no DB impact.

---

### Task 3: Settings keys and `MiniGraphState.Orientation`

**No unit tests are possible here** — `NetworkMonitor.Tests` references Models and Core only, and both files are in `NetworkMonitor.Services`. Verification is the build plus the manual check in Step 5.

**Files:**
- Modify: `NetworkMonitor.Services/Data/Settings.cs` (beside the existing `MiniGraph*` properties, around lines 194-255)
- Modify: `NetworkMonitor.Services/Platform/MiniGraphState.cs`

**Interfaces:**
- Consumes: `NetworkMonitor.Models.Widget.MiniGraphOrientation` and `NetworkMonitor.Core.Widget.HorizontalStripMetrics` from Task 1.
- Produces:
  - `Settings.MiniGraphHorizontal` (`bool`, default `false`), `Settings.MiniGraphStripX` / `MiniGraphStripY` (`int`, default `int.MinValue`), `Settings.MiniGraphStripHeight` (`int`, default `40`).
  - `MiniGraphState.Orientation` (`MiniGraphOrientation`, raises `Changed`), `MiniGraphState.IsHorizontal` (`bool`), `MiniGraphState.SaveStripPlacement(int positionX, int positionY, int height)`.

`Settings` persists a `bool` rather than the enum so `settings.json` stays readable and an unrecognised value can never throw on load.

- [ ] **Step 1: Add the settings keys**

In `NetworkMonitor.Services/Data/Settings.cs`, immediately after the existing `MiniGraphOpacity` property:

```csharp
        public bool MiniGraphHorizontal
        {
            get;
            set;
        } = false;

        public int MiniGraphStripX
        {
            get;
            set;
        } = int.MinValue;

        public int MiniGraphStripY
        {
            get;
            set;
        } = int.MinValue;

        public int MiniGraphStripHeight
        {
            get;
            set;
        } = 40;
```

- [ ] **Step 2: Add the state property**

In `NetworkMonitor.Services/Platform/MiniGraphState.cs`, add `using NetworkMonitor.Core.Widget;` and `using NetworkMonitor.Models.Widget;` at the top, then add after the existing `Opacity` property:

```csharp
        public MiniGraphOrientation Orientation
        {
            get => _settings.MiniGraphHorizontal ? MiniGraphOrientation.Horizontal : MiniGraphOrientation.Vertical;
            set
            {
                bool horizontal = value == MiniGraphOrientation.Horizontal;

                Apply(_settings.MiniGraphHorizontal != horizontal, () => _settings.MiniGraphHorizontal = horizontal);
            }
        }

        public bool IsHorizontal => _settings.MiniGraphHorizontal;
```

- [ ] **Step 3: Add the strip placement writer**

In the same file, immediately after the existing `SavePlacement`:

```csharp
        // The strip and the floating widget keep separate positions. Sharing one would drop a 700-wide
        // strip at the floating widget's coordinates on every orientation change, and the user would
        // have to reposition it each time.
        public void SaveStripPlacement(int positionX, int positionY, int height)
        {
            _settings.MiniGraphStripX = positionX;
            _settings.MiniGraphStripY = positionY;
            _settings.MiniGraphStripHeight = (int)Math.Round(HorizontalStripMetrics.ClampHeight(height));
            _settings.Save();
        }
```

- [ ] **Step 4: Build**

Run: `dotnet build NetworkMonitor.slnx -p:Platform=x64`
Expected: succeeds with no new warnings. If `NetworkMonitor.Services` cannot see `NetworkMonitor.Core.Widget`, confirm the existing Core → Services `ProjectReference`; do not add a new one, the layering already provides it.

- [ ] **Step 5: Manual check**

Run the app, open the mini graph, close the app, and open `%LOCALAPPDATA%\UmnathaNetworkMonitor\settings.json`.
Expected: the four new keys are present with defaults `false`, `-2147483648`, `-2147483648`, `40`, and every pre-existing key is unchanged.

- [ ] **Step 6: Commit**

```bash
git add NetworkMonitor.Services/Data/Settings.cs NetworkMonitor.Services/Platform/MiniGraphState.cs
git commit
```

Subject: `Store the mini graph orientation and strip placement.` Body: **Context** (why placement is per-orientation), then **Change**. State explicitly: `settings.json` only, no schema change, no EF Core migration.

---

### Task 4: Horizontal layout in the widget

The visual half. Sizing and placement are Task 5 — at the end of this task the strip lays out correctly but the window is still sized as though it were vertical.

**Files:**
- Modify: `NetworkMonitor/MiniGraphWindow.xaml`
- Modify: `NetworkMonitor/MiniGraphWindow.xaml.cs` (`ApplyLayout` at 384-403, `SectionsPanelSizeChanged` at 297-308)

**No unit tests are possible** — the App project is not referenced by `NetworkMonitor.Tests`. Verification is the build plus the manual checks in Step 5.

**Interfaces:**
- Consumes: `MiniGraphState.IsHorizontal` and `MiniGraphState.Orientation` from Task 3; `HorizontalStripMetrics.FontScale` and `ShowsPeak` from Task 1; `MiniGraphFormatter.SpeedTestShort` from Task 2.
- Produces: `MiniGraphWindow.ApplyLayout()` handling both orientations; a `CloseColumn` element in the XAML that the close glyph occupies when horizontal.

- [ ] **Step 1: Give the close glyph a grid position in the XAML**

In `NetworkMonitor/MiniGraphWindow.xaml`, `SectionsPanel` currently declares `RowDefinitions="*,*,Auto,Auto"` and the close `Button` floats over `RootLayer`. Move the close `Button` inside `SectionsPanel` so it can take a column, and let code-behind decide whether it floats or occupies a cell. Replace the `SectionsPanel` opening tag and the trailing `CloseGlyph` button with:

```xml
        <Grid
            x:Name="SectionsPanel"
            Padding="4,4,4,0"
            SizeChanged="SectionsPanelSizeChanged">

            <Grid.RowDefinitions>

                <RowDefinition
                    Height="*" />

                <RowDefinition
                    Height="*" />

                <RowDefinition
                    Height="Auto" />

                <RowDefinition
                    Height="Auto" />

            </Grid.RowDefinitions>
```

Keep the four existing children exactly as they are, and place the close button as the last child of `SectionsPanel`:

```xml
            <Button
                x:Name="CloseGlyph"
                HorizontalAlignment="Right"
                VerticalAlignment="Top"
                Margin="0,2,2,0"
                Padding="6,2"
                FontSize="11"
                Background="Transparent"
                BorderThickness="0"
                Opacity="0"
                Content="&#x2715;"
                Click="CloseGlyphClick" />
```

`RowDefinitions` moves from the attribute form to the element form because code-behind now clears and rebuilds both `RowDefinitions` and `ColumnDefinitions`, and the attribute shorthand is harder to reason about beside that.

- [ ] **Step 2: Split `ApplyLayout` by orientation**

In `NetworkMonitor/MiniGraphWindow.xaml.cs`, add `using NetworkMonitor.Core.Widget;` and `using NetworkMonitor.Models.Widget;`, then replace `ApplyLayout` (lines 384-403) with:

```csharp
        private void ApplyLayout()
        {
            InternetSection.Visibility = _state.ShowInternet ? Visibility.Visible : Visibility.Collapsed;
            LocalSection.Visibility = _state.ShowLocal ? Visibility.Visible : Visibility.Collapsed;
            SpeedTestBand.Visibility = _state.ShowSpeedTest ? Visibility.Visible : Visibility.Collapsed;
            UnknownDevicesBand.Visibility = _state.ShowUnknownDevices ? Visibility.Visible : Visibility.Collapsed;
            EmptyHint.Visibility = _state.HasAnySection ? Visibility.Collapsed : Visibility.Visible;

            if (_state.IsHorizontal)
            {
                ApplyHorizontalLayout();
            }
            else
            {
                ApplyVerticalLayout();
            }

            ApplySpeedTestText();
        }

        private void ApplyVerticalLayout()
        {
            SectionsPanel.ColumnDefinitions.Clear();
            SectionsPanel.Padding = new Thickness(4, 4, 4, 0);

            GridLength fill = new GridLength(1, GridUnitType.Star);
            GridLength none = new GridLength(0);

            // The strips are fixed height and the charts share everything left over, so switching Local
            // off makes Internet twice as tall rather than shrinking the window. With both charts off
            // row 0 stays a star and acts as a spacer, otherwise the footer would pin to the top edge
            // with the empty space below it.
            bool spacerNeeded = !_state.ShowInternet && !_state.ShowLocal;

            SectionsPanel.RowDefinitions[0].Height = _state.ShowInternet || spacerNeeded ? fill : none;
            SectionsPanel.RowDefinitions[1].Height = _state.ShowLocal ? fill : none;

            Grid.SetRow(InternetSection, 0);
            Grid.SetColumn(InternetSection, 0);
            Grid.SetRow(LocalSection, 1);
            Grid.SetColumn(LocalSection, 0);
            Grid.SetRow(SpeedTestBand, 2);
            Grid.SetColumn(SpeedTestBand, 0);
            Grid.SetRow(UnknownDevicesBand, 3);
            Grid.SetColumn(UnknownDevicesBand, 0);

            Grid.SetRow(CloseGlyph, 0);
            Grid.SetColumn(CloseGlyph, 0);
            CloseGlyph.HorizontalAlignment = HorizontalAlignment.Right;
            CloseGlyph.VerticalAlignment = VerticalAlignment.Top;
            CloseGlyph.Margin = new Thickness(0, 2, 2, 0);

            InternetSection.Margin = new Thickness(0, 0, 0, 4);
            LocalSection.Margin = new Thickness(0, 0, 0, 4);
            SpeedTestBand.Margin = new Thickness(0, 0, 0, 4);
            UnknownDevicesBand.Margin = new Thickness(0, 0, 0, 4);
        }

        // Every visible section takes a column of its own natural width, in the same order the vertical
        // widget stacks them, and the close glyph takes a narrow trailing column. Left floating over the
        // top-right corner it would land on the unknown-devices text: the 26px right reserve inside
        // MiniTrafficSection's header does not apply to the plain Border bands.
        private void ApplyHorizontalLayout()
        {
            SectionsPanel.ColumnDefinitions.Clear();
            SectionsPanel.Padding = new Thickness(4);

            GridLength single = new GridLength(1, GridUnitType.Star);

            SectionsPanel.RowDefinitions[0].Height = single;
            SectionsPanel.RowDefinitions[1].Height = new GridLength(0);
            SectionsPanel.RowDefinitions[2].Height = new GridLength(0);
            SectionsPanel.RowDefinitions[3].Height = new GridLength(0);

            int column = 0;

            column = PlaceHorizontalCell(InternetSection, _state.ShowInternet, column);
            column = PlaceHorizontalCell(LocalSection, _state.ShowLocal, column);
            column = PlaceHorizontalCell(SpeedTestBand, _state.ShowSpeedTest, column);
            column = PlaceHorizontalCell(UnknownDevicesBand, _state.ShowUnknownDevices, column);

            SectionsPanel.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = GridLength.Auto
            });

            Grid.SetRow(CloseGlyph, 0);
            Grid.SetColumn(CloseGlyph, column);
            CloseGlyph.HorizontalAlignment = HorizontalAlignment.Center;
            CloseGlyph.VerticalAlignment = VerticalAlignment.Center;
            CloseGlyph.Margin = new Thickness(0);
        }

        private int PlaceHorizontalCell(FrameworkElement cell, bool isVisible, int column)
        {
            int next = column;

            if (isVisible)
            {
                SectionsPanel.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = new GridLength(1, GridUnitType.Star)
                });

                Grid.SetRow(cell, 0);
                Grid.SetColumn(cell, column);
                cell.Margin = new Thickness(0, 0, 4, 0);
                next = column + 1;
            }

            return next;
        }
```

- [ ] **Step 3: Scale the font from height alone when horizontal**

Replace `SectionsPanelSizeChanged` (lines 297-308) with:

```csharp
        // Every size the widget can be dragged to is a legitimate one, and text fixed at 12 point looks
        // cramped at 600 wide and swamps the charts at 240. The reference size is the default placement.
        // Horizontal takes its scale from the height alone: the strip's width grows with every section
        // switched on, so a width term would inflate the text as sections were added.
        private void SectionsPanelSizeChanged(object sender, SizeChangedEventArgs args)
        {
            double scale;

            if (_state.IsHorizontal)
            {
                scale = HorizontalStripMetrics.FontScale(args.NewSize.Height);
            }
            else
            {
                double widthScale = args.NewSize.Width / ReferenceWidth;
                double heightScale = args.NewSize.Height / ReferenceHeight;

                scale = Math.Clamp(Math.Min(widthScale, heightScale), MinimumFontScale, MaximumFontScale);
            }

            InternetSection.FontScale = scale;
            LocalSection.FontScale = scale;
            SpeedTestLine.FontSize = FooterFontSize * scale;
            UnknownDevicesLine.FontSize = FooterFontSize * scale;
            CloseGlyph.FontSize = FooterFontSize * scale;

            InternetSection.ShowPeak = !_state.IsHorizontal || HorizontalStripMetrics.ShowsPeak(args.NewSize.Height);
            LocalSection.ShowPeak = InternetSection.ShowPeak;
        }
```

This introduces a `ShowPeak` dependency property on `MiniTrafficSection`. Add it in `NetworkMonitor/Views/Controls/MiniTrafficSection.xaml.cs`, following the shape of the four existing dependency properties:

```csharp
        public static readonly DependencyProperty ShowPeakProperty =
            DependencyProperty.Register(
                nameof(ShowPeak),
                typeof(bool),
                typeof(MiniTrafficSection),
                new PropertyMetadata(true, OnShowPeakChanged));
```

```csharp
        public bool ShowPeak
        {
            get => (bool)GetValue(ShowPeakProperty);
            set => SetValue(ShowPeakProperty, value);
        }
```

```csharp
        private static void OnShowPeakChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        {
            MiniTrafficSection section = (MiniTrafficSection)sender;

            section.PeakLabel.Visibility = (bool)args.NewValue ? Visibility.Visible : Visibility.Collapsed;
        }
```

Add `using Microsoft.UI.Xaml;` if it is not already present. The dependency property registrations sit in the Fields section; `ShowPeak` sits with the other properties; `OnShowPeakChanged` sits with the other private static handlers.

- [ ] **Step 4: Use the short speed-test line when horizontal**

Replace `ApplySpeedTestText` (lines 423-426):

```csharp
        private void ApplySpeedTestText()
        {
            SpeedTestDetail.Text = _state.IsHorizontal ? ViewModel.SpeedTestShortText : ViewModel.SpeedTestText;
        }
```

The bold `Run` reading `Speed Test` also has to shorten. In `MiniGraphWindow.xaml` give it a name so code-behind can retitle it — change the existing `<Run FontWeight="Bold" Text="Speed Test" />` to:

```xml
                    <Run
                        x:Name="SpeedTestLabel"
                        FontWeight="Bold"
                        Text="Speed Test" />
```

and set it in `ApplySpeedTestText`:

```csharp
        private void ApplySpeedTestText()
        {
            SpeedTestLabel.Text = _state.IsHorizontal ? "Speed " : "Speed Test";
            SpeedTestDetail.Text = _state.IsHorizontal ? ViewModel.SpeedTestShortText : ViewModel.SpeedTestText;
        }
```

Add `SpeedTestShortText` to `NetworkMonitor/ViewModels/MiniGraphViewModel.cs`, beside the existing `SpeedTestText` and following the same backing-field-above-property shape:

```csharp
        private string _speedTestShortText = "not run yet";

        public string SpeedTestShortText
        {
            get => _speedTestShortText;
            private set => SetProperty(ref _speedTestShortText, value);
        }
```

and assign it in `Refresh`, immediately after the existing `SpeedTestText` assignment:

```csharp
            SpeedTestShortText = MiniGraphFormatter.SpeedTestShort(_feed.LatestSpeedTest, mode);
```

Extend the property-changed guard in `MiniGraphWindow.OnViewModelPropertyChanged` so the short line refreshes too:

```csharp
            if (args.PropertyName is null
                || args.PropertyName == nameof(MiniGraphViewModel.SpeedTestText)
                || args.PropertyName == nameof(MiniGraphViewModel.SpeedTestShortText))
            {
                DispatcherQueue.TryEnqueue(ApplySpeedTestText);
            }
```

- [ ] **Step 5: Build and check by hand**

Run: `dotnet build NetworkMonitor.slnx -p:Platform=x64`
Expected: succeeds.

Temporarily set `"MiniGraphHorizontal": true` in `%LOCALAPPDATA%\UmnathaNetworkMonitor\settings.json` with the app closed, then run the app and show the mini graph.

Expected, accepting that the window is still sized as a vertical widget — that is Task 5:
- The four sections sit side by side, left to right: Internet, Local, Speed, Unknown devices, with the close glyph in a narrow trailing column rather than over the Internet chart.
- The speed cell reads `Speed ↓… ↑… Mb/s  … ms`, not the four-reading line.
- Switching a section off through the right-click menu removes its column and the remaining columns close up.
- Switching back to `false` and restarting restores the vertical widget exactly as before, including the close glyph over the top-right corner.

- [ ] **Step 6: Commit**

```bash
git add NetworkMonitor/MiniGraphWindow.xaml NetworkMonitor/MiniGraphWindow.xaml.cs NetworkMonitor/Views/Controls/MiniTrafficSection.xaml.cs NetworkMonitor/ViewModels/MiniGraphViewModel.cs
git commit
```

Subject: `Lay the mini graph out horizontally.` Body: **Context** (one grid reconfigured rather than a second window, and why), then **Change**. State: no DB impact.

---

### Task 5: Strip sizing and per-orientation placement

**Files:**
- Modify: `NetworkMonitor/MiniGraphWindow.xaml.cs` (`SaveCurrentPlacement` 244-258, `RestorePlacement` 339-382, `ClampMinimumSize` 457-472, `OnStateChanged` 439-443)

**No unit tests are possible** — App project. Verification is the build plus the manual matrix in Step 5.

**Interfaces:**
- Consumes: `HorizontalStripMetrics.Width`, `.FontScale`, `.ClampHeight`, `.MinimumHeight`, `.MaximumHeight`, `.DefaultHeight` from Task 1; `MiniGraphState.SaveStripPlacement` and `.IsHorizontal` from Task 3; `ApplyLayout` from Task 4.
- Produces: nothing consumed by later tasks.

- [ ] **Step 1: Derive the strip's width and enforce it**

Add these members to `MiniGraphWindow`, with the private methods in the private-methods section:

```csharp
        private double DerivedStripWidth()
        {
            double height = AppWindow.Size.Height / GetCurrentScale();
            double fontScale = HorizontalStripMetrics.FontScale(height);
            double width = HorizontalStripMetrics.Width(_state.ShowInternet, _state.ShowLocal, _state.ShowSpeedTest, _state.ShowUnknownDevices, fontScale);

            return width;
        }
```

Replace `ClampMinimumSize` (lines 457-472) with a method that branches. `OverlappedPresenter.IsResizable` cannot lock a single axis, so horizontal keeps the window resizable and forces the width back on every size change — dragging a side edge therefore snaps back, which is the accepted rough edge recorded in the spec:

```csharp
        private void ClampMinimumSize()
        {

            if (_state.IsHorizontal)
            {
                ClampStripSize();
            }
            else
            {
                ClampWidgetSize();
            }

        }

        // Width is derived from the visible sections rather than dragged, and the presenter cannot lock
        // one axis while leaving the other free, so a side-edge drag is undone here on the next change.
        private void ClampStripSize()
        {
            double scale = GetCurrentScale();
            SizeInt32 size = AppWindow.Size;
            double heightInDips = HorizontalStripMetrics.ClampHeight(size.Height / scale);
            int height = (int)Math.Round(heightInDips * scale);
            int width = (int)Math.Round(DerivedStripWidth() * scale);

            if (size.Width != width || size.Height != height)
            {
                AppWindow.Resize(new SizeInt32(width, height));
            }

        }

        private void ClampWidgetSize()
        {
            double scale = GetCurrentScale();
            int minimumWidth = (int)Math.Round(MinimumWidth * scale);
            int minimumHeight = (int)Math.Round(MinimumHeight * scale);
            SizeInt32 size = AppWindow.Size;

            if (size.Width < minimumWidth || size.Height < minimumHeight)
            {
                int width = Math.Max(minimumWidth, size.Width);
                int height = Math.Max(minimumHeight, size.Height);

                AppWindow.Resize(new SizeInt32(width, height));
            }

        }
```

`DerivedStripWidth` reads `AppWindow.Size.Height`, so `ClampStripSize` must compute the height first and the width from the clamped height — the code above does exactly that by calling `HorizontalStripMetrics.FontScale` on the raw height inside `DerivedStripWidth`. When the height is out of bounds the resize triggers another `Changed` event and the second pass settles on the clamped value. That convergence is intentional; do not try to make it single-pass by reading the pre-clamp height.

- [ ] **Step 2: Save placement to the right keys**

Replace `SaveCurrentPlacement` (lines 244-258):

```csharp
        // The size is stored in DIPs so the widget keeps the same apparent size across displays of
        // different scaling; the position stays in physical pixels because that is the coordinate
        // space of the virtual desktop that DisplayArea and AppWindow both work in.
        private void SaveCurrentPlacement()
        {
            double scale = GetCurrentScale();
            SizeInt32 size = AppWindow.Size;
            int width = (int)Math.Round(size.Width / scale);
            int height = (int)Math.Round(size.Height / scale);
            PointInt32 position = AppWindow.Position;

            if (_state.IsHorizontal)
            {
                _state.SaveStripPlacement(position.X, position.Y, height);
            }
            else if (width >= MinimumWidth && height >= MinimumHeight)
            {
                _state.SavePlacement(position.X, position.Y, width, height);
            }

        }
```

The strip has no width guard because its width is not the user's to set.

- [ ] **Step 3: Restore placement from the right keys**

Replace `RestorePlacement` (lines 339-382):

```csharp
        private void RestorePlacement()
        {
            bool horizontal = _state.IsHorizontal;
            int positionX = horizontal ? _settings.MiniGraphStripX : _settings.MiniGraphX;
            int positionY = horizontal ? _settings.MiniGraphStripY : _settings.MiniGraphY;
            DisplayArea? saved = null;

            if (positionX != int.MinValue && positionY != int.MinValue)
            {
                saved = DisplayArea.GetFromPoint(new PointInt32(positionX, positionY), DisplayAreaFallback.None);
            }

            DisplayArea target = saved ?? DisplayArea.Primary;
            RectInt32 workArea = target.WorkArea;

            // Without this the stored DIP size went to AppWindow verbatim, so on a 200% display the
            // widget came up at half the size it was asked for — small enough that the font scale
            // bottomed out and the sections were unreadable.
            int scaleSampleX = saved is null ? workArea.X : positionX;
            int scaleSampleY = saved is null ? workArea.Y : positionY;
            double scale = GetScaleForPoint(scaleSampleX, scaleSampleY);
            int width;
            int height;

            if (horizontal)
            {
                double heightInDips = HorizontalStripMetrics.ClampHeight(_settings.MiniGraphStripHeight);
                double fontScale = HorizontalStripMetrics.FontScale(heightInDips);
                double widthInDips = HorizontalStripMetrics.Width(_state.ShowInternet, _state.ShowLocal, _state.ShowSpeedTest, _state.ShowUnknownDevices, fontScale);

                width = (int)Math.Round(widthInDips * scale);
                height = (int)Math.Round(heightInDips * scale);
            }
            else
            {
                width = (int)Math.Round(Math.Max(MinimumWidth, _settings.MiniGraphWidth) * scale);
                height = (int)Math.Round(Math.Max(MinimumHeight, _settings.MiniGraphHeight) * scale);
            }

            if (saved is null)
            {
                int margin = (int)Math.Round(EdgeMargin * scale);

                positionX = workArea.X + workArea.Width - width - margin;
                positionY = workArea.Y + workArea.Height - height - margin;
            }

            // Only the top-left corner was ever tested against a display, so a widget saved near a
            // right or bottom edge could come back mostly off-screen — and scaling the size on
            // restore makes that easier to hit, because the widget can now be wider than it was
            // when the position was written.
            int maximumX = Math.Max(workArea.X, workArea.X + workArea.Width - width);
            int maximumY = Math.Max(workArea.Y, workArea.Y + workArea.Height - height);

            positionX = Math.Clamp(positionX, workArea.X, maximumX);
            positionY = Math.Clamp(positionY, workArea.Y, maximumY);

            AppWindow.MoveAndResize(new RectInt32(positionX, positionY, width, height));
            _placementRestored = true;
        }
```

- [ ] **Step 4: Relayout and reposition on an orientation change**

Replace `OnStateChanged` (lines 439-443). The current placement must be flushed **before** the orientation flips, or the new orientation's dimensions get written to the old orientation's keys:

```csharp
        private void OnStateChanged(object? sender, EventArgs args)
        {
            DispatcherQueue.TryEnqueue(OnStateChangedOnUiThread);
        }

        private void OnStateChangedOnUiThread()
        {

            if (_appliedOrientation != _state.Orientation)
            {
                _savePlacementTimer.Stop();
                _appliedOrientation = _state.Orientation;
                ApplyLayout();
                RestorePlacement();
            }
            else
            {
                ApplyLayout();
                ClampMinimumSize();
            }

            ApplyRestingOpacity();
        }
```

Add the field to the Fields section, beside `_placementRestored`:

```csharp
        private MiniGraphOrientation _appliedOrientation;
```

and seed it in the constructor immediately before the existing `ConfigureWindow()` call, so the first `OnStateChanged` after construction does not read the orientation as changed:

```csharp
            _appliedOrientation = _state.Orientation;
```

Flushing before the flip is handled by the existing debounce: `FlushPlacement` runs on a 400 ms timer that `SaveCurrentPlacement` reads `_state.IsHorizontal` from. Because the orientation has already changed by the time `OnStateChangedOnUiThread` runs, the pending save would write strip dimensions to widget keys. Stopping the timer without flushing, as above, discards at most the last 400 ms of dragging in the orientation being left — an acceptable trade against corrupting the other orientation's saved placement.

- [ ] **Step 5: Build and check the matrix by hand**

Run: `dotnet build NetworkMonitor.slnx -p:Platform=x64`
Expected: succeeds.

With the app running and the mini graph shown, flip orientation from the right-click menu (available after Task 6 — until then, edit `MiniGraphHorizontal` in `settings.json` with the app closed and restart). Check each of these:

| Check | Expected |
|---|---|
| Flip to horizontal | Window becomes a strip roughly 728 × 40 at 100% scaling |
| Toggle Local off | Strip narrows by ~174; cells close up; no gap left behind |
| Toggle Local back on | Strip returns to its previous width |
| Drag the top edge up | Height grows; text and charts scale with it |
| Drag past 120 DIP | Height stops at 120 |
| Drag below 28 DIP | Height stops at 28 |
| Drag below 34 DIP | `Peak …` disappears from both traffic cells; the label stays |
| Drag above 74 DIP | The charts' gridline values and time markers reappear |
| Drag a side edge | Width snaps back — expected, documented |
| Move the strip, flip to vertical, flip back | Strip returns to where it was moved to, not the widget's position |
| Move the widget, flip to horizontal, flip back | Widget returns to where it was moved to |
| Repeat the flip and drag checks on a 200% display | Strip opens at the intended apparent size, and drag tracks the cursor without diverging |

The 200% row is not optional. `RestorePlacement` and the drag path have each already produced a shipped defect on high-DPI displays (`8023ffa`, `abea7c8`).

- [ ] **Step 6: Commit**

```bash
git add NetworkMonitor/MiniGraphWindow.xaml.cs
git commit
```

Subject: `Size and place the horizontal mini graph strip.` Body: **Context** (derived width, dragged height, per-orientation placement, and the presenter's inability to lock one axis), then **Change**. State: no DB impact.

---

### Task 6: Orientation submenu on the widget

**Files:**
- Modify: `NetworkMonitor/MiniGraphWindow.xaml.cs` (`RootRightTapped` 673-698, `BuildOpacitySubmenu` 719-743)

**No unit tests are possible** — App project. Verification is the build plus the manual check in Step 4.

**Interfaces:**
- Consumes: `MiniGraphState.Orientation` from Task 3.
- Produces: nothing consumed by later tasks.

- [ ] **Step 1: Add the submenu builder**

Add beside `BuildOpacitySubmenu`, in the private-methods section:

```csharp
        private MenuFlyoutSubItem BuildOrientationSubmenu()
        {
            MenuFlyoutSubItem submenu = new MenuFlyoutSubItem
            {
                Text = "Orientation"
            };

            MiniGraphOrientation current = _state.Orientation;

            submenu.Items.Add(BuildOrientationItem("Vertical", MiniGraphOrientation.Vertical, current));
            submenu.Items.Add(BuildOrientationItem("Horizontal", MiniGraphOrientation.Horizontal, current));

            return submenu;
        }

        private RadioMenuFlyoutItem BuildOrientationItem(string text, MiniGraphOrientation orientation, MiniGraphOrientation current)
        {
            RadioMenuFlyoutItem item = new RadioMenuFlyoutItem
            {
                Text = text,
                GroupName = "MiniGraphOrientation",
                IsChecked = orientation == current
            };

            item.Click += (sender, args) => _state.Orientation = orientation;

            return item;
        }
```

- [ ] **Step 2: Add it to the menu**

In `RootRightTapped`, insert immediately after the `BuildOpacitySubmenu` line:

```csharp
            WidgetMenu.Items.Add(BuildOrientationSubmenu());
```

- [ ] **Step 3: Build**

Run: `dotnet build NetworkMonitor.slnx -p:Platform=x64`
Expected: succeeds.

- [ ] **Step 4: Manual check**

Right-click the widget.
Expected: an **Orientation** submenu below **Opacity**, with Vertical and Horizontal as radio items and the current one ticked. Choosing the other relayouts the open window in place — it does not close and reopen — and the tick survives a reopen of the menu and a restart of the app.

- [ ] **Step 5: Commit**

```bash
git add NetworkMonitor/MiniGraphWindow.xaml.cs
git commit
```

Subject: `Add an orientation submenu to the mini graph.` Body: **Context**, then **Change**. State: no DB impact.

---

### Task 7: Settings page selector

**Files:**
- Modify: `NetworkMonitor/ViewModels/SettingsViewModel.cs` (mini graph properties around 320-420, `OnSettingChanged` 455-475, `SyncMiniGraphFromState` 477-492)
- Modify: `NetworkMonitor/Views/SettingsPage.xaml` (mini graph block, around 615-663)

**No unit tests are possible** — App project. Verification is the build plus the manual check in Step 4.

**Interfaces:**
- Consumes: `MiniGraphState.Orientation` from Task 3.
- Produces: nothing consumed by later tasks.

- [ ] **Step 1: Add the view model property**

In `NetworkMonitor/ViewModels/SettingsViewModel.cs`, add `using NetworkMonitor.Models.Widget;` and add immediately after the existing `MiniGraphOpacity` property, following the same backing-field-above-property shape:

```csharp
        private bool _miniGraphHorizontal;

        public bool MiniGraphHorizontal
        {
            get => _miniGraphHorizontal;
            set
            {

                if (SetProperty(ref _miniGraphHorizontal, value))
                {
                    _miniGraphState.Orientation = value ? MiniGraphOrientation.Horizontal : MiniGraphOrientation.Vertical;
                }

            }
        }
```

- [ ] **Step 2: Keep it out of the bulk persist and in the state sync**

`MiniGraphState` already saves and notifies on its own, so this property must be excluded from `PersistAll`, exactly as the other mini graph properties are. In `OnSettingChanged`, extend the `isPersistable` chain:

```csharp
                && args.PropertyName != nameof(MiniGraphOpacity)
                && args.PropertyName != nameof(MiniGraphHorizontal);
```

In `SyncMiniGraphFromState`, add the read and the notification:

```csharp
            _miniGraphHorizontal = _miniGraphState.IsHorizontal;
```

```csharp
            OnPropertyChanged(nameof(MiniGraphHorizontal));
```

- [ ] **Step 3: Add the control**

In `NetworkMonitor/Views/SettingsPage.xaml`, insert between the Unknown devices `CheckBox` and the opacity `StackPanel`:

```xml
                        <StackPanel
                            Spacing="4">

                            <ToggleSwitch
                                Header="Horizontal strip"
                                OffContent="Vertical"
                                OnContent="Horizontal"
                                IsOn="{x:Bind ViewModel.MiniGraphHorizontal, Mode=TwoWay}" />

                            <TextBlock
                                Text="Lays the sections out side by side in a short, wide strip that fits on the taskbar. Its width follows whichever sections you have switched on; drag its top or bottom edge to set the height."
                                FontSize="12"
                                Opacity="0.65"
                                TextWrapping="Wrap" />

                        </StackPanel>
```

- [ ] **Step 4: Build and check by hand**

Run: `dotnet build NetworkMonitor.slnx -p:Platform=x64`
Expected: succeeds.

Open Settings with the mini graph showing.
Expected: the **Horizontal strip** switch reflects the current orientation; flipping it relayouts the open widget immediately; flipping from the widget's own right-click menu and reopening Settings shows the switch already in step; no "Settings saved" toast fires for this change, matching the other mini graph controls.

- [ ] **Step 5: Commit**

```bash
git add NetworkMonitor/ViewModels/SettingsViewModel.cs NetworkMonitor/Views/SettingsPage.xaml
git commit
```

Subject: `Add a horizontal strip switch to Settings.` Body: **Context**, then **Change**. State: no DB impact.

---

### Task 8: Documentation and release notes

**Files:**
- Modify: `Project/GitHub/README.md` or wherever the roadmap lives — locate it first with `git log --oneline -20 -- '*README*'` and by searching the repo for the roadmap table; commit `1481789` marked the floating mini graph as shipped and is the model to follow.
- Modify: the release notes file used by commit `be8da1d`. Find it the same way.
- Modify: `Documents/To Do.txt` only if it carries an entry for this work. **It has uncommitted changes from before this plan began — do not stage them wholesale; stage only the lines this work touches, or leave the file alone.**

- [ ] **Step 1: Locate the roadmap and release notes**

```bash
git show --stat 1481789
git show --stat be8da1d
```

Expected: the exact paths of the roadmap and release-notes files.

- [ ] **Step 2: Add the entries**

Add the horizontal strip to the roadmap in the same style as the floating mini graph entry, and add a release-notes bullet describing it in user-facing terms: the mini graph can now be laid out as a horizontal strip that fits on the taskbar, its width follows the sections you switch on, and its height is set by dragging.

- [ ] **Step 3: Full regression run**

Run: `dotnet test NetworkMonitor.Tests/NetworkMonitor.Tests.csproj --nologo`
Expected: PASS, all tests.

Run: `dotnet build NetworkMonitor.slnx -p:Platform=x64`
Expected: succeeds with no new warnings.

Then run the app and confirm the vertical widget is untouched: default position and size, close glyph over the top-right corner, the full four-reading speed line, sections stacking and the row-0 spacer behaving as before with both charts off.

- [ ] **Step 4: Commit and push**

```bash
git add <roadmap> <release notes>
git commit
git push all master
```

Subject: `Document the horizontal mini graph strip.` Body: **Context**, then **Change**. State: no DB impact. Confirm in the result that both GitHub (master) and DevOps (mirror) were updated.

---

## Self-Review

**Spec coverage.** Orientation switch on one widget → Tasks 3, 4, 6, 7. Per-orientation placement → Tasks 3, 5. Derived width → Tasks 1, 5. Dragged and clamped height → Tasks 1, 5. Section order → Task 4. Chart-behind-text cells → inherited, no work needed. Short speed-test line → Tasks 2, 4. Unknown-devices cell → inherited unchanged. Close glyph as a trailing column → Task 4. Height-only font scale → Task 4. Peak dropped below 34 → Tasks 1, 4. `MiniGraphOrientation` in Models, `HorizontalStripMetrics` in Core → Task 1. Settings keys → Task 3. Right-click submenu → Task 6. Settings page → Task 7. Rough edge: side-drag snap-back → Task 5, Step 1, with the manual check that proves it. Error cases: orientation change while hidden → Task 5, Step 4 (`RestorePlacement` runs on the change; `ShowWidget` is unaffected); missing display → inherited `DisplayAreaFallback.None` path, unchanged; Alt+F4 → inherited, unchanged. No gaps found.

**Placeholder scan.** No "TBD", no "add error handling", no "similar to Task N". Every code step carries the actual code. Task 8 Step 1 deliberately locates two file paths at execution time rather than guessing them, and gives the exact commands that produce them.

**Type consistency.** `HorizontalStripMetrics.Width(bool, bool, bool, bool, double)`, `.FontScale(double)`, `.ClampHeight(double)`, `.ShowsPeak(double)` are used with those signatures in Tasks 3 and 5. `MiniGraphState.IsHorizontal`, `.Orientation`, `.SaveStripPlacement(int, int, int)` defined in Task 3 and consumed in Tasks 4, 5, 6, 7 under those names. `MiniGraphFormatter.SpeedTestShort` defined in Task 2 and consumed in Task 4. `MiniGraphViewModel.SpeedTestShortText` and `MiniTrafficSection.ShowPeak` are both defined and consumed inside Task 4. `MiniGraphOrientation.Vertical` / `.Horizontal` consistent throughout.

**One correction made during review.** Task 5 Step 4 originally relied on `FlushPlacement` running before the orientation flip. It cannot — the state has already changed by the time the window is notified, so the pending debounced save would write the new orientation's dimensions to the old orientation's keys. The step now stops the timer without flushing and says why.
