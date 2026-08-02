# Floating Mini Graph Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an always-on-top desktop widget showing live Internet and Local traffic, the last speed test and the unknown-device count, fed entirely from events the app already raises.

**Architecture:** A pure ring buffer of one-second buckets (`NetworkMonitor.Core`) is filled by a singleton hosted service (`NetworkMonitor.Services`) that subscribes to the existing `TrafficTracker.Flushed`, `SpeedTestWorker.SpeedTestCompleted` and `ScanWorker.ScanCompleted` events. The service runs from startup whether or not the widget is open, so the widget opens with five minutes already drawn. A frameless `Window` in the app project binds to a singleton view model that reads snapshots from the feed; its charts never touch the database.

**Tech Stack:** WinUI 3 (Windows App SDK), Win2D `CanvasControl`, CommunityToolkit.Mvvm `ObservableObject`, `Microsoft.Extensions.Hosting`, xunit.v3.

**Source spec:** `Documents/superpowers/specs/2026-07-31-floating-mini-graph-design.md`

## Global Constraints

- Layering is **Models ← Core ← Services ← App**. New pure, testable logic goes in Core or Models — never in Services or the app project.
- `NetworkMonitor.Tests` references `NetworkMonitor.Models` and `NetworkMonitor.Core` by `ProjectReference` only. There are **no source links**. Anything a test touches must live in one of those two projects.
- Every project uses one namespace per sub-folder (e.g. `NetworkMonitor.Services.Traffic`).
- **One type per file**, named exactly after the type. Nested types and a cohesive block of P/Invoke declarations in the file of the API that consumes them are the only exemptions.
- Coding conventions are non-negotiable and are enforced by review, not by a linter: no `var`; no single-character identifiers anywhere including lambda and pattern-match variables; braces on every block; single exit point per method with the `return` last and standing alone (assign to a local first); blank lines above and below every block, including immediately after a method's opening `{` when the first statement is a block and immediately before its closing `}` when the last statement ends with `}`; `string.Empty` over `""`; no comments unless the WHY is non-obvious; class member order Fields → Constructor → Properties → Public methods → Override methods → Private methods; a property's backing field sits immediately above the property inside the Properties section, and properties are hand-written with `SetProperty(ref _field, value)` — **never** the `[ObservableProperty]` source generator.
- XAML formatting follows `DevicesPage.xaml`: blank line after the `<?xml?>` declaration; element name on its own line; every attribute on its own line indented 4 spaces; attribute order is simple assignments, then event handlers and `Command`, then value-assignment bindings; blank line above and below every element and after every opening tag / before every closing tag.
- Settings live on the `Settings` singleton (`NetworkMonitor.Services/Data/Settings.cs`) and persist to `settings.json` via `Save()`. No `appsettings.json` seeding is needed for new fields.
- **No EF migrations.** The schema is created by `EnsureCreated`. This feature adds no tables and no columns, so **no database delete is required on upgrade.**
- Test baseline before this work: **274 passing, 0 failing.** Every task that adds tests must leave the suite green.
- Any file added under `Documents/` must be added to `NetworkMonitor.slnx` in the same commit.

**Commands used throughout:**

- Tests: `dotnet test NetworkMonitor.Tests/NetworkMonitor.Tests.csproj -v q --nologo`
- Compile check of the whole solution: `dotnet build NetworkMonitor.slnx -c Debug --nologo`
- Running the app: open `NetworkMonitor.slnx` in Visual Studio 2026, set platform to **x64**, F5. WinUI 3 does not support Any CPU.

---

### Task 1: `LiveRateBuffer` — the pure ring of one-second buckets

The chart data structure. Fixed capacity, zero-fills idle gaps, and spreads a flush across the interval it covers rather than charging it all to one bucket.

**Why the spread matters:** commit 28be399 ("Fix live chart peaks reading above the physical line rate") fixed exactly this bug on the Traffic page. A flush drains everything the collector accumulated since the previous drain, so its bytes belong to the whole interval. At the default 1-second traffic interval charging them to a single bucket is harmless, but the interval is user-configurable to 60 seconds, at which point a single-bucket write would draw a 60× spike followed by 59 zeros. The spec sketched `Add(timestamp, download, upload)`; this plan adds `AddInterval` alongside it and has the feed use `AddInterval`, reusing the already-tested `FlushSpread.Distribute` from Core rather than reimplementing the distribution.

**Files:**
- Create: `NetworkMonitor.Core/Traffic/LiveRateBuffer.cs`
- Test: `NetworkMonitor.Tests/LiveRateBufferTests.cs`

**Interfaces:**
- Consumes: `NetworkMonitor.Models.Charting.ChartPoint` — `record ChartPoint(DateTime BucketStart, long BytesUploaded, long BytesDownloaded)`. **Upload is the second positional argument, download the third.** `NetworkMonitor.Core.Traffic.FlushSpread.Distribute(long totalBytes, IReadOnlyList<DateTime> bucketStartsUtc, double bucketSeconds, DateTime intervalStartUtc, DateTime intervalEndUtc) → long[]`.
- Produces:
  - `LiveRateBuffer(int capacitySeconds)`
  - `int Capacity { get; }`
  - `void Add(DateTime timestampUtc, long bytesDownloaded, long bytesUploaded)`
  - `void AddInterval(DateTime intervalStartUtc, DateTime intervalEndUtc, long bytesDownloaded, long bytesUploaded)`
  - `IReadOnlyList<ChartPoint> Snapshot(DateTime nowUtc)`
  - `void Clear()`

- [ ] **Step 1: Write the failing tests**

Create `NetworkMonitor.Tests/LiveRateBufferTests.cs`:

```csharp
using Xunit;
using NetworkMonitor.Core.Traffic;
using NetworkMonitor.Models.Charting;

namespace NetworkMonitor.Tests
{
    public class LiveRateBufferTests
    {
        private static readonly DateTime Origin = new DateTime(2026, 8, 2, 12, 0, 0, DateTimeKind.Utc);

        [Fact]
        public void SnapshotIsEmptyBeforeAnythingIsAdded()
        {
            LiveRateBuffer buffer = new LiveRateBuffer(5);

            IReadOnlyList<ChartPoint> points = buffer.Snapshot(Origin);

            Assert.Empty(points);
        }

        [Fact]
        public void SamplesInTheSameSecondAccumulateIntoOneBucket()
        {
            LiveRateBuffer buffer = new LiveRateBuffer(5);

            buffer.Add(Origin, 100, 10);
            buffer.Add(Origin.AddMilliseconds(400), 50, 5);

            IReadOnlyList<ChartPoint> points = buffer.Snapshot(Origin);
            ChartPoint newest = points[points.Count - 1];

            Assert.Equal(150, newest.BytesDownloaded);
            Assert.Equal(15, newest.BytesUploaded);
        }

        [Fact]
        public void SnapshotIsOldestFirstAndExactlyCapacityLong()
        {
            LiveRateBuffer buffer = new LiveRateBuffer(4);

            buffer.Add(Origin, 1, 0);

            IReadOnlyList<ChartPoint> points = buffer.Snapshot(Origin);

            Assert.Equal(4, points.Count);
            Assert.Equal(Origin.AddSeconds(-3), points[0].BucketStart);
            Assert.Equal(Origin, points[3].BucketStart);
        }

        [Fact]
        public void AnIdleGapReadsAsZeroesRatherThanStaleBytes()
        {
            LiveRateBuffer buffer = new LiveRateBuffer(10);

            buffer.Add(Origin, 900, 90);
            buffer.Add(Origin.AddSeconds(4), 100, 10);

            IReadOnlyList<ChartPoint> points = buffer.Snapshot(Origin.AddSeconds(4));

            Assert.Equal(900, points[points.Count - 5].BytesDownloaded);
            Assert.Equal(0, points[points.Count - 4].BytesDownloaded);
            Assert.Equal(0, points[points.Count - 3].BytesDownloaded);
            Assert.Equal(0, points[points.Count - 2].BytesDownloaded);
            Assert.Equal(100, points[points.Count - 1].BytesDownloaded);
        }

        [Fact]
        public void SnapshotZeroFillsForwardToNowWhenNothingHasArrivedSince()
        {
            LiveRateBuffer buffer = new LiveRateBuffer(6);

            buffer.Add(Origin, 500, 50);

            IReadOnlyList<ChartPoint> points = buffer.Snapshot(Origin.AddSeconds(3));

            Assert.Equal(6, points.Count);
            Assert.Equal(Origin.AddSeconds(3), points[5].BucketStart);
            Assert.Equal(0, points[5].BytesDownloaded);
            Assert.Equal(500, points[2].BytesDownloaded);
        }

        [Fact]
        public void WritingPastCapacityEvictsTheOldestBucket()
        {
            LiveRateBuffer buffer = new LiveRateBuffer(3);

            buffer.Add(Origin, 111, 0);
            buffer.Add(Origin.AddSeconds(1), 222, 0);
            buffer.Add(Origin.AddSeconds(2), 333, 0);
            buffer.Add(Origin.AddSeconds(3), 444, 0);

            IReadOnlyList<ChartPoint> points = buffer.Snapshot(Origin.AddSeconds(3));

            Assert.Equal(3, points.Count);
            Assert.Equal(Origin.AddSeconds(1), points[0].BucketStart);
            Assert.Equal(222, points[0].BytesDownloaded);
            Assert.Equal(444, points[2].BytesDownloaded);
        }

        [Fact]
        public void AWholeWindowOfSilenceLeavesNothingBehind()
        {
            LiveRateBuffer buffer = new LiveRateBuffer(3);

            buffer.Add(Origin, 999, 99);
            buffer.Add(Origin.AddSeconds(30), 0, 0);

            IReadOnlyList<ChartPoint> points = buffer.Snapshot(Origin.AddSeconds(30));

            Assert.Equal(3, points.Count);

            foreach (ChartPoint point in points)
            {
                Assert.Equal(0, point.BytesDownloaded);
                Assert.Equal(0, point.BytesUploaded);
            }

        }

        [Fact]
        public void SamplesOlderThanTheWindowAreDropped()
        {
            LiveRateBuffer buffer = new LiveRateBuffer(3);

            buffer.Add(Origin.AddSeconds(10), 100, 0);
            buffer.Add(Origin, 7777, 0);

            IReadOnlyList<ChartPoint> points = buffer.Snapshot(Origin.AddSeconds(10));
            long total = 0;

            foreach (ChartPoint point in points)
            {
                total += point.BytesDownloaded;
            }

            Assert.Equal(100, total);
        }

        [Fact]
        public void AnIntervalIsSpreadAcrossEverySecondItCovers()
        {
            LiveRateBuffer buffer = new LiveRateBuffer(10);

            buffer.AddInterval(Origin, Origin.AddSeconds(4), 400, 40);

            IReadOnlyList<ChartPoint> points = buffer.Snapshot(Origin.AddSeconds(3));
            long total = 0;

            foreach (ChartPoint point in points)
            {
                total += point.BytesDownloaded;
                Assert.True(point.BytesDownloaded <= 100);
            }

            Assert.Equal(400, total);
        }

        [Fact]
        public void ClearDiscardsEverythingHeld()
        {
            LiveRateBuffer buffer = new LiveRateBuffer(4);

            buffer.Add(Origin, 100, 10);
            buffer.Clear();

            IReadOnlyList<ChartPoint> points = buffer.Snapshot(Origin);

            Assert.Empty(points);
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test NetworkMonitor.Tests/NetworkMonitor.Tests.csproj -v q --nologo`

Expected: compile failure — `The type or namespace name 'LiveRateBuffer' could not be found`.

- [ ] **Step 3: Write the implementation**

Create `NetworkMonitor.Core/Traffic/LiveRateBuffer.cs`:

```csharp
using NetworkMonitor.Models.Charting;

namespace NetworkMonitor.Core.Traffic
{
    // The chart behind the floating mini graph. It fills from app startup whether or not the widget
    // is open, which is what lets the widget open with five minutes already drawn instead of an
    // empty chart, and it costs a fixed ~15 KB to leave running.
    //
    // Idle seconds must read as a flat zero line rather than a hole in the trace, so advancing the
    // ring zeroes every bucket it skips over.
    public sealed class LiveRateBuffer
    {
        private readonly long[] _download;
        private readonly long[] _upload;
        private long _lastEpoch = -1;

        public LiveRateBuffer(int capacitySeconds)
        {

            if (capacitySeconds < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(capacitySeconds));
            }

            _capacity = capacitySeconds;
            _download = new long[capacitySeconds];
            _upload = new long[capacitySeconds];
        }

        private readonly int _capacity;

        public int Capacity => _capacity;

        public void Add(DateTime timestampUtc, long bytesDownloaded, long bytesUploaded)
        {
            long epoch = ToEpochSeconds(timestampUtc);

            Advance(epoch);
            Accumulate(epoch, bytesDownloaded, bytesUploaded);
        }

        // A flush drains everything accumulated since the previous drain, so its bytes belong to the
        // whole interval rather than to the instant the drain happened. Charging them to one bucket
        // draws a spike above the physical line rate at any traffic interval above one second.
        public void AddInterval(DateTime intervalStartUtc, DateTime intervalEndUtc, long bytesDownloaded, long bytesUploaded)
        {

            if (intervalEndUtc <= intervalStartUtc)
            {
                Add(intervalEndUtc, bytesDownloaded, bytesUploaded);
            }
            else
            {
                long endEpoch = ToEpochSeconds(intervalEndUtc);

                Advance(endEpoch);

                long firstEpoch = ToEpochSeconds(intervalStartUtc);
                long oldestHeld = _lastEpoch - _capacity + 1;

                if (firstEpoch < oldestHeld)
                {
                    firstEpoch = oldestHeld;
                }

                List<DateTime> bucketStarts = new List<DateTime>();

                for (long epoch = firstEpoch; epoch <= endEpoch; epoch++)
                {
                    bucketStarts.Add(DateTime.UnixEpoch.AddSeconds(epoch));
                }

                long[] downloadShares = FlushSpread.Distribute(bytesDownloaded, bucketStarts, 1.0, intervalStartUtc, intervalEndUtc);
                long[] uploadShares = FlushSpread.Distribute(bytesUploaded, bucketStarts, 1.0, intervalStartUtc, intervalEndUtc);

                for (int index = 0; index < bucketStarts.Count; index++)
                {
                    Accumulate(firstEpoch + index, downloadShares[index], uploadShares[index]);
                }

            }

        }

        public IReadOnlyList<ChartPoint> Snapshot(DateTime nowUtc)
        {
            List<ChartPoint> points = new List<ChartPoint>();

            if (_lastEpoch >= 0)
            {
                long nowEpoch = ToEpochSeconds(nowUtc);
                long endEpoch = nowEpoch > _lastEpoch ? nowEpoch : _lastEpoch;
                long startEpoch = endEpoch - _capacity + 1;

                for (long epoch = startEpoch; epoch <= endEpoch; epoch++)
                {
                    long download = 0;
                    long upload = 0;

                    if (IsHeld(epoch))
                    {
                        int slot = Slot(epoch);
                        download = _download[slot];
                        upload = _upload[slot];
                    }

                    points.Add(new ChartPoint(DateTime.UnixEpoch.AddSeconds(epoch), upload, download));
                }

            }

            return points;
        }

        public void Clear()
        {
            Array.Clear(_download);
            Array.Clear(_upload);
            _lastEpoch = -1;
        }

        private void Advance(long epoch)
        {

            if (_lastEpoch < 0)
            {
                _lastEpoch = epoch;
                int slot = Slot(epoch);
                _download[slot] = 0;
                _upload[slot] = 0;
            }
            else if (epoch > _lastEpoch)
            {
                long first = _lastEpoch + 1;

                if (epoch - first >= _capacity)
                {
                    first = epoch - _capacity + 1;
                }

                for (long skipped = first; skipped <= epoch; skipped++)
                {
                    int slot = Slot(skipped);
                    _download[slot] = 0;
                    _upload[slot] = 0;
                }

                _lastEpoch = epoch;
            }

        }

        private void Accumulate(long epoch, long bytesDownloaded, long bytesUploaded)
        {

            if (IsHeld(epoch))
            {
                int slot = Slot(epoch);
                _download[slot] += bytesDownloaded;
                _upload[slot] += bytesUploaded;
            }

        }

        private bool IsHeld(long epoch)
        {
            bool held = epoch <= _lastEpoch && epoch > _lastEpoch - _capacity;

            return held;
        }

        private int Slot(long epoch)
        {
            int slot = (int)(epoch % _capacity);

            return slot;
        }

        private static long ToEpochSeconds(DateTime timestampUtc)
        {
            long epoch = (long)(timestampUtc - DateTime.UnixEpoch).TotalSeconds;

            return epoch;
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test NetworkMonitor.Tests/NetworkMonitor.Tests.csproj -v q --nologo`

Expected: `Failed: 0, Passed: 284` (274 baseline + 10 new).

- [ ] **Step 5: Commit**

```bash
git add NetworkMonitor.Core/Traffic/LiveRateBuffer.cs NetworkMonitor.Tests/LiveRateBufferTests.cs
git commit -m "Add the mini graph's live one-second ring buffer."
```

---

### Task 2: `MiniGraphFormatter` — the widget's three text lines

Every string the widget shows other than chart pixels. Pure and testable, so it goes in Models beside the existing `TrafficRateFormatter`.

**Files:**
- Create: `NetworkMonitor.Models/Formatting/MiniGraphFormatter.cs`
- Test: `NetworkMonitor.Tests/MiniGraphFormatterTests.cs`

**Interfaces:**
- Consumes: `NetworkMonitor.Models.Formatting.RateUnitMode` — note the namespace is **Formatting**, not Charting, even though `ChartPoint` lives in Charting (values `Both = 0`, `Bits = 1`, `Bytes = 2`); `NetworkMonitor.Models.SpeedTest.SpeedTestResult` with `DateTime LocalTimestamp`, `double DownloadMbps`, `double UploadMbps`, `double DownloadMBps`, `double UploadMBps`, `double LatencyMs`, `bool Success`.
- Produces:
  - `static string Rate(double downloadBytesPerSecond, double uploadBytesPerSecond, RateUnitMode mode)`
  - `static string SpeedTest(SpeedTestResult? latest, RateUnitMode mode)`
  - `static string UnknownDevices(int count)`

**Rules from the spec:**
- `Both` renders as Mb/s only — there is not room for two units in this window.
- When the **combined** throughput is below 0.5 Mb/s the whole rate reads `—`. That is 62 500 B/s, the same threshold `InternetTrafficAppRow.HasRate` already uses for the live rate badge.
- Speed strip reads `Speed 06:00 ↓512 ↑48 Mb/s · 9 ms`, or `No speed test yet` on an empty database.
- Devices strip reads `⚠ 2 unknown devices` or `✓ no unknown devices`.

- [ ] **Step 1: Write the failing tests**

Create `NetworkMonitor.Tests/MiniGraphFormatterTests.cs`:

```csharp
using Xunit;
using NetworkMonitor.Models.Formatting;
using NetworkMonitor.Models.SpeedTest;

namespace NetworkMonitor.Tests
{
    public class MiniGraphFormatterTests
    {
        [Fact]
        public void RateReadsAsADashBelowTheHalfMegabitThreshold()
        {
            string text = MiniGraphFormatter.Rate(30_000.0, 20_000.0, RateUnitMode.Bits);

            Assert.Equal("—", text);
        }

        [Fact]
        public void RateShowsBothArrowsWithASingleSharedUnit()
        {
            string text = MiniGraphFormatter.Rate(14_750_000.0, 375_000.0, RateUnitMode.Bits);

            Assert.Equal("↓118 ↑3 Mb/s", text);
        }

        [Fact]
        public void BothModeRendersAsMegabitsOnlyBecauseThereIsNoRoomForTwoUnits()
        {
            string bits = MiniGraphFormatter.Rate(14_750_000.0, 375_000.0, RateUnitMode.Bits);
            string both = MiniGraphFormatter.Rate(14_750_000.0, 375_000.0, RateUnitMode.Both);

            Assert.Equal(bits, both);
        }

        [Fact]
        public void ByteModeRendersMegabytesPerSecond()
        {
            string text = MiniGraphFormatter.Rate(14_000_000.0, 2_000_000.0, RateUnitMode.Bytes);

            Assert.Equal("↓14 ↑2 MB/s", text);
        }

        [Fact]
        public void SmallRatesKeepOneDecimalSoTheyDoNotCollapseToZero()
        {
            string text = MiniGraphFormatter.Rate(700_000.0, 100_000.0, RateUnitMode.Bits);

            Assert.Equal("↓5.6 ↑0.8 Mb/s", text);
        }

        [Fact]
        public void SpeedTestReadsAsAPromptWhenTheDatabaseIsEmpty()
        {
            string text = MiniGraphFormatter.SpeedTest(null, RateUnitMode.Bits);

            Assert.Equal("No speed test yet", text);
        }

        [Fact]
        public void SpeedTestShowsTimeRatesAndPing()
        {
            SpeedTestResult result = new SpeedTestResult
            {
                Timestamp = new DateTime(2026, 8, 2, 6, 0, 0, DateTimeKind.Utc).ToUniversalTime(),
                DownloadMbps = 512.0,
                UploadMbps = 48.0,
                LatencyMs = 9.0,
                Success = true
            };

            string text = MiniGraphFormatter.SpeedTest(result, RateUnitMode.Bits);

            Assert.StartsWith("Speed ", text);
            Assert.Contains("↓512 ↑48 Mb/s", text);
            Assert.EndsWith("· 9 ms", text);
        }

        [Fact]
        public void AFailedSpeedTestIsTreatedAsNoResult()
        {
            SpeedTestResult result = new SpeedTestResult
            {
                Timestamp = DateTime.UtcNow,
                Success = false,
                Error = "No internet"
            };

            string text = MiniGraphFormatter.SpeedTest(result, RateUnitMode.Bits);

            Assert.Equal("No speed test yet", text);
        }

        [Fact]
        public void UnknownDevicesReadsAsATickAtZero()
        {
            string text = MiniGraphFormatter.UnknownDevices(0);

            Assert.Equal("✓ no unknown devices", text);
        }

        [Fact]
        public void UnknownDevicesWarnsAndAgreesInNumber()
        {
            string one = MiniGraphFormatter.UnknownDevices(1);
            string many = MiniGraphFormatter.UnknownDevices(2);

            Assert.Equal("⚠ 1 unknown device", one);
            Assert.Equal("⚠ 2 unknown devices", many);
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test NetworkMonitor.Tests/NetworkMonitor.Tests.csproj -v q --nologo`

Expected: compile failure — `'MiniGraphFormatter' does not exist`.

- [ ] **Step 3: Write the implementation**

Create `NetworkMonitor.Models/Formatting/MiniGraphFormatter.cs`:

```csharp
using NetworkMonitor.Models.SpeedTest;

namespace NetworkMonitor.Models.Formatting
{
    public static class MiniGraphFormatter
    {
        // 0.5 Mb/s, the same floor InternetTrafficAppRow.HasRate uses for the live rate badge.
        private const double RateThresholdBytesPerSecond = 62_500.0;

        public static string Rate(double downloadBytesPerSecond, double uploadBytesPerSecond, RateUnitMode mode)
        {
            string text = "—";

            if (downloadBytesPerSecond + uploadBytesPerSecond >= RateThresholdBytesPerSecond)
            {
                bool inBytes = mode == RateUnitMode.Bytes;
                double divisor = inBytes ? 1_000_000.0 : 125_000.0;
                string unit = inBytes ? "MB/s" : "Mb/s";
                string download = Scaled(downloadBytesPerSecond / divisor);
                string upload = Scaled(uploadBytesPerSecond / divisor);

                text = $"↓{download} ↑{upload} {unit}";
            }

            return text;
        }

        public static string SpeedTest(SpeedTestResult? latest, RateUnitMode mode)
        {
            string text = "No speed test yet";

            if (latest is not null && latest.Success)
            {
                bool inBytes = mode == RateUnitMode.Bytes;
                string unit = inBytes ? "MB/s" : "Mb/s";
                double download = inBytes ? latest.DownloadMBps : latest.DownloadMbps;
                double upload = inBytes ? latest.UploadMBps : latest.UploadMbps;
                string time = latest.LocalTimestamp.ToString("HH:mm");

                text = $"Speed {time} ↓{Scaled(download)} ↑{Scaled(upload)} {unit} · {latest.LatencyMs:F0} ms";
            }

            return text;
        }

        public static string UnknownDevices(int count)
        {
            string text = count switch
            {
                <= 0 => "✓ no unknown devices",
                1 => "⚠ 1 unknown device",
                _ => $"⚠ {count} unknown devices"
            };

            return text;
        }

        // "0.#" rather than "F1" below ten: 5.6 has to keep its decimal or a slow link reads as
        // zero, but 3.0 must render as "3" — there is no room in this window for a decimal that
        // carries no information.
        private static string Scaled(double value)
        {
            string text = value >= 10.0 ? value.ToString("F0") : value.ToString("0.#");

            return text;
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test NetworkMonitor.Tests/NetworkMonitor.Tests.csproj -v q --nologo`

Expected: `Failed: 0, Passed: 294`.

- [ ] **Step 5: Commit**

```bash
git add NetworkMonitor.Models/Formatting/MiniGraphFormatter.cs NetworkMonitor.Tests/MiniGraphFormatterTests.cs
git commit -m "Add the mini graph's rate, speed and device text formatting."
```

---

### Task 3: Settings fields and the shared `MiniGraphState`

Three entry points (tray menu, Traffic toolbar, Settings page) all drive the same booleans, and the window itself must follow whichever one the user touched. A tiny observable singleton in Services is the single writer to `Settings` for those fields; everything else reads and writes it.

**Files:**
- Modify: `NetworkMonitor.Services/Data/Settings.cs` (append new fields before `Save()`)
- Create: `NetworkMonitor.Services/Platform/MiniGraphState.cs`
- Modify: `NetworkMonitor/App.xaml.cs:98` (register the singleton next to the other `AddSingleton` calls)

**Interfaces:**
- Consumes: `NetworkMonitor.Services.Data.Settings` and its `Save()`.
- Produces: `MiniGraphState` with `bool IsVisible`, `bool ShowInternet`, `bool ShowLocal`, `bool ShowSpeedTest`, `bool ShowUnknownDevices`, `int Opacity`, `event EventHandler? Changed`, and `void SavePlacement(int x, int y, int width, int height)`. Every setter persists and raises `Changed` only when the value actually differs.

- [ ] **Step 1: Add the settings fields**

In `NetworkMonitor.Services/Data/Settings.cs`, insert immediately after the `AutoCheckForUpdates` property (line 186) and before `public void Save()`:

```csharp
        public bool ShowMiniGraph
        {
            get;
            set;
        } = false;

        public bool MiniGraphShowInternet
        {
            get;
            set;
        } = true;

        public bool MiniGraphShowLocal
        {
            get;
            set;
        } = true;

        public bool MiniGraphShowSpeedTest
        {
            get;
            set;
        } = true;

        public bool MiniGraphShowUnknownDevices
        {
            get;
            set;
        } = true;

        public int MiniGraphX
        {
            get;
            set;
        } = -1;

        public int MiniGraphY
        {
            get;
            set;
        } = -1;

        public int MiniGraphWidth
        {
            get;
            set;
        } = 320;

        public int MiniGraphHeight
        {
            get;
            set;
        } = 230;

        public int MiniGraphOpacity
        {
            get;
            set;
        } = 100;
```

- [ ] **Step 2: Create `MiniGraphState`**

Create `NetworkMonitor.Services/Platform/MiniGraphState.cs`:

```csharp
using NetworkMonitor.Services.Data;

namespace NetworkMonitor.Services.Platform
{
    // The tray menu, the Traffic toolbar and the Settings page all drive the same booleans, so they
    // share one writer rather than each poking Settings and hoping the others notice.
    public sealed class MiniGraphState(Settings settings)
    {
        private const int MinimumOpacity = 50;
        private const int MaximumOpacity = 100;

        private readonly Settings _settings = settings;

        public event EventHandler? Changed;

        public bool IsVisible
        {
            get => _settings.ShowMiniGraph;
            set => Apply(_settings.ShowMiniGraph != value, () => _settings.ShowMiniGraph = value);
        }

        public bool ShowInternet
        {
            get => _settings.MiniGraphShowInternet;
            set => Apply(_settings.MiniGraphShowInternet != value, () => _settings.MiniGraphShowInternet = value);
        }

        public bool ShowLocal
        {
            get => _settings.MiniGraphShowLocal;
            set => Apply(_settings.MiniGraphShowLocal != value, () => _settings.MiniGraphShowLocal = value);
        }

        public bool ShowSpeedTest
        {
            get => _settings.MiniGraphShowSpeedTest;
            set => Apply(_settings.MiniGraphShowSpeedTest != value, () => _settings.MiniGraphShowSpeedTest = value);
        }

        public bool ShowUnknownDevices
        {
            get => _settings.MiniGraphShowUnknownDevices;
            set => Apply(_settings.MiniGraphShowUnknownDevices != value, () => _settings.MiniGraphShowUnknownDevices = value);
        }

        public int Opacity
        {
            get => Math.Clamp(_settings.MiniGraphOpacity, MinimumOpacity, MaximumOpacity);
            set
            {
                int clamped = Math.Clamp(value, MinimumOpacity, MaximumOpacity);

                Apply(_settings.MiniGraphOpacity != clamped, () => _settings.MiniGraphOpacity = clamped);
            }
        }

        public bool HasAnySection => ShowInternet || ShowLocal || ShowSpeedTest || ShowUnknownDevices;

        public void SavePlacement(int x, int y, int width, int height)
        {
            _settings.MiniGraphX = x;
            _settings.MiniGraphY = y;
            _settings.MiniGraphWidth = width;
            _settings.MiniGraphHeight = height;
            _settings.Save();
        }

        private void Apply(bool changed, Action assign)
        {

            if (changed)
            {
                assign();
                _settings.Save();
                Changed?.Invoke(this, EventArgs.Empty);
            }

        }
    }
}
```

- [ ] **Step 3: Register it**

In `NetworkMonitor/App.xaml.cs`, immediately after `services.AddSingleton(scannerSettings);` (line 98):

```csharp
                        services.AddSingleton<MiniGraphState>();
```

`NetworkMonitor.Services.Platform` is already imported at line 18, so no new `using` is needed.

- [ ] **Step 4: Verify it compiles and the suite is still green**

Run: `dotnet build NetworkMonitor.slnx -c Debug --nologo`
Expected: build succeeded, 0 errors.

Run: `dotnet test NetworkMonitor.Tests/NetworkMonitor.Tests.csproj -v q --nologo`
Expected: `Failed: 0, Passed: 294`.

- [ ] **Step 5: Commit**

```bash
git add NetworkMonitor.Services/Data/Settings.cs NetworkMonitor.Services/Platform/MiniGraphState.cs NetworkMonitor/App.xaml.cs
git commit -m "Add the mini graph settings and their shared state holder."
```

---

### Task 4: `LiveTrafficFeed` — the always-running event feed

A singleton `IHostedService` that runs from startup whether or not the widget is open. It holds the two ring buffers, the two smoothed rate windows, the last speed test and the unapproved-device count, and raises one `Updated` event whenever any of them move.

**Files:**
- Create: `NetworkMonitor.Services/Traffic/LiveTrafficFeed.cs`
- Modify: `NetworkMonitor/App.xaml.cs:112` (register after `TrafficTracker`)

**Interfaces:**
- Consumes:
  - `TrafficTracker.Flushed` → `TrafficFlushedEventArgs` with `IReadOnlyList<TrafficEntry> Entries` and `IReadOnlyList<LocalTrafficDelta> LocalDeltas`. `TrafficEntry` has `string ProcessName`, `long BytesUploaded`, `long BytesDownloaded`. `LocalTrafficDelta` is `record LocalTrafficDelta(string ProcessName, string? ProcessPath, string RemoteIp, int Protocol, int RemotePort, long BytesUploaded, long BytesDownloaded)`.
  - `SpeedTestWorker.SpeedTestCompleted` → `record SpeedTestCompletedEventArgs(SpeedTestResult Result)`.
  - `ScanWorker.ScanCompleted` → `record ScanCompletedEventArgs(ScanSession Session, bool IsManual)`.
  - `LiveRateBuffer` and `RateWindow` from Task 1 / Core.
  - `AppLog.Error(string context, Exception exception)` from `NetworkMonitor.Services.Platform`.
- Produces:
  - `event EventHandler? Updated`
  - `SpeedTestResult? LatestSpeedTest { get; }`
  - `int UnapprovedDeviceCount { get; }`
  - `IReadOnlyList<ChartPoint> WanSnapshot()` / `IReadOnlyList<ChartPoint> LanSnapshot()`
  - `double WanDownloadBytesPerSecond` / `WanUploadBytesPerSecond` / `LanDownloadBytesPerSecond` / `LanUploadBytesPerSecond`

**Two things to get right:**
1. **WAN excludes `ProcessName == "System"`**, matching `InternetViewModel.AccumulateRateWindows` — the Internet tab does not show System, so the widget must not either or the two charts will disagree.
2. **`Flushed` is raised on the tracker's background thread** and the snapshots are read from the UI thread. Every buffer touch is inside `lock (_gate)`.

- [ ] **Step 1: Write the implementation**

Create `NetworkMonitor.Services/Traffic/LiveTrafficFeed.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using NetworkMonitor.Core.Traffic;
using NetworkMonitor.Models.Charting;
using NetworkMonitor.Models.SpeedTest;
using NetworkMonitor.Models.Traffic;
using NetworkMonitor.Services.Data;
using NetworkMonitor.Services.Platform;
using NetworkMonitor.Services.Scanning;
using NetworkMonitor.Services.SpeedTest;

namespace NetworkMonitor.Services.Traffic
{
    // Feeds the floating mini graph. This runs from startup whether or not the widget is open, which
    // is what lets the widget open with five minutes already drawn rather than an empty chart. It
    // costs roughly 15 KB held permanently and performs exactly two database reads, both at startup.
    //
    // A fault in here must never propagate into the flush loop or the scan loop the rest of the app
    // depends on, so every handler is wrapped.
    public sealed class LiveTrafficFeed(
        TrafficTracker tracker,
        SpeedTestWorker speedTestWorker,
        ScanWorker scanWorker,
        Settings settings,
        IDbContextFactory<AppDbContext> dbFactory) : IHostedService
    {
        private const int WindowSeconds = 300;
        private const int RateSampleCount = 5;

        private readonly TrafficTracker _tracker = tracker;
        private readonly SpeedTestWorker _speedTestWorker = speedTestWorker;
        private readonly ScanWorker _scanWorker = scanWorker;
        private readonly Settings _settings = settings;
        private readonly IDbContextFactory<AppDbContext> _dbFactory = dbFactory;
        private readonly LiveRateBuffer _wanBuffer = new LiveRateBuffer(WindowSeconds);
        private readonly LiveRateBuffer _lanBuffer = new LiveRateBuffer(WindowSeconds);
        private readonly RateWindow _wanDownloadRate = new RateWindow();
        private readonly RateWindow _wanUploadRate = new RateWindow();
        private readonly RateWindow _lanDownloadRate = new RateWindow();
        private readonly RateWindow _lanUploadRate = new RateWindow();
        private readonly object _gate = new object();
        private DateTime _lastFlushUtc = DateTime.MinValue;

        public event EventHandler? Updated;

        public SpeedTestResult? LatestSpeedTest
        {
            get;
            private set;
        }

        public int UnapprovedDeviceCount
        {
            get;
            private set;
        }

        public double WanDownloadBytesPerSecond => RateOf(_wanDownloadRate);

        public double WanUploadBytesPerSecond => RateOf(_wanUploadRate);

        public double LanDownloadBytesPerSecond => RateOf(_lanDownloadRate);

        public double LanUploadBytesPerSecond => RateOf(_lanUploadRate);

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await SeedAsync(cancellationToken);

            _tracker.Flushed += OnFlushed;
            _speedTestWorker.SpeedTestCompleted += OnSpeedTestCompleted;
            _scanWorker.ScanCompleted += OnScanCompleted;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _tracker.Flushed -= OnFlushed;
            _speedTestWorker.SpeedTestCompleted -= OnSpeedTestCompleted;
            _scanWorker.ScanCompleted -= OnScanCompleted;

            Task completed = Task.CompletedTask;

            return completed;
        }

        public IReadOnlyList<ChartPoint> WanSnapshot()
        {
            IReadOnlyList<ChartPoint> points;

            lock (_gate)
            {
                points = _wanBuffer.Snapshot(DateTime.UtcNow);
            }

            return points;
        }

        public IReadOnlyList<ChartPoint> LanSnapshot()
        {
            IReadOnlyList<ChartPoint> points;

            lock (_gate)
            {
                points = _lanBuffer.Snapshot(DateTime.UtcNow);
            }

            return points;
        }

        private async Task SeedAsync(CancellationToken cancellationToken)
        {

            try
            {
                await using AppDbContext db = await _dbFactory.CreateDbContextAsync(cancellationToken);

                LatestSpeedTest = await db.SpeedTestResults
                    .AsNoTracking()
                    .Where(result => result.Success)
                    .OrderByDescending(result => result.Timestamp)
                    .FirstOrDefaultAsync(cancellationToken);

                DateTime cutoff = DateTime.UtcNow.AddHours(-24);

                UnapprovedDeviceCount = await db.Devices
                    .AsNoTracking()
                    .CountAsync(device => !device.IsApproved && (device.IsOnline || device.LastSeen >= cutoff), cancellationToken);
            }
            catch (Exception exception)
            {
                AppLog.Error("LiveTrafficFeed.Seed", exception);
            }

        }

        private void OnFlushed(object? sender, TrafficFlushedEventArgs args)
        {

            try
            {
                long wanDownload = 0;
                long wanUpload = 0;

                foreach (TrafficEntry entry in args.Entries)
                {

                    // The Internet tab hides System, so including it here would put the widget and the
                    // tab permanently out of step.
                    if (entry.ProcessName == "System")
                    {
                        continue;
                    }

                    wanDownload += entry.BytesDownloaded;
                    wanUpload += entry.BytesUploaded;
                }

                long lanDownload = 0;
                long lanUpload = 0;

                foreach (LocalTrafficDelta delta in args.LocalDeltas)
                {
                    lanDownload += delta.BytesDownloaded;
                    lanUpload += delta.BytesUploaded;
                }

                DateTime nowUtc = DateTime.UtcNow;
                DateTime intervalStartUtc = _lastFlushUtc == DateTime.MinValue
                    ? nowUtc.AddSeconds(-Math.Max(1, _settings.TrafficIntervalSeconds))
                    : _lastFlushUtc;
                _lastFlushUtc = nowUtc;

                lock (_gate)
                {
                    _wanBuffer.AddInterval(intervalStartUtc, nowUtc, wanDownload, wanUpload);
                    _lanBuffer.AddInterval(intervalStartUtc, nowUtc, lanDownload, lanUpload);
                    _wanDownloadRate.Add(wanDownload, RateSampleCount);
                    _wanUploadRate.Add(wanUpload, RateSampleCount);
                    _lanDownloadRate.Add(lanDownload, RateSampleCount);
                    _lanUploadRate.Add(lanUpload, RateSampleCount);
                }

                Updated?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception exception)
            {
                AppLog.Error("LiveTrafficFeed.OnFlushed", exception);
            }

        }

        private void OnSpeedTestCompleted(object? sender, SpeedTestCompletedEventArgs args)
        {

            try
            {

                if (args.Result.Success)
                {
                    LatestSpeedTest = args.Result;
                    Updated?.Invoke(this, EventArgs.Empty);
                }

            }
            catch (Exception exception)
            {
                AppLog.Error("LiveTrafficFeed.OnSpeedTestCompleted", exception);
            }

        }

        private void OnScanCompleted(object? sender, ScanCompletedEventArgs args)
        {
            _ = RefreshUnapprovedCountAsync();
        }

        private async Task RefreshUnapprovedCountAsync()
        {

            try
            {
                await using AppDbContext db = await _dbFactory.CreateDbContextAsync();
                DateTime cutoff = DateTime.UtcNow.AddHours(-24);

                int count = await db.Devices
                    .AsNoTracking()
                    .CountAsync(device => !device.IsApproved && (device.IsOnline || device.LastSeen >= cutoff));

                UnapprovedDeviceCount = count;
                Updated?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception exception)
            {
                AppLog.Error("LiveTrafficFeed.RefreshUnapprovedCount", exception);
            }

        }

        private double RateOf(RateWindow window)
        {
            double intervalSeconds = Math.Max(1.0, _settings.TrafficIntervalSeconds);
            double rate = 0.0;

            lock (_gate)
            {

                if (window.Count > 0)
                {
                    rate = window.Average / intervalSeconds;
                }

            }

            return rate;
        }
    }
}
```

The unapproved predicate is copied verbatim from `UnapprovedDevicesViewModel.cs:91` — `!IsApproved && (IsOnline || LastSeen >= cutoff)` with a 24-hour cutoff. If that page's rule ever changes, this must change with it.

- [ ] **Step 2: Register the feed**

In `NetworkMonitor/App.xaml.cs`, immediately after the `TrafficTracker` hosted-service registration (line 112):

```csharp
                        services.AddSingleton<LiveTrafficFeed>();
                        services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<LiveTrafficFeed>());
```

- [ ] **Step 3: Verify it builds and starts**

Run: `dotnet build NetworkMonitor.slnx -c Debug --nologo`
Expected: build succeeded, 0 errors.

Then run the app from Visual Studio (x64) and confirm it starts normally, the Internet and Local tabs still update, and no `LiveTrafficFeed` errors appear in the log (Settings → Data folder → `log.txt`). Nothing is visible yet — this step is confirming the feed is inert and harmless.

- [ ] **Step 4: Commit**

```bash
git add NetworkMonitor.Services/Traffic/LiveTrafficFeed.cs NetworkMonitor/App.xaml.cs
git commit -m "Add the always-on live traffic feed behind the mini graph."
```

---

### Task 5: `Compact` mode on `TrafficAreaChart`

One new dependency property that collapses everything that does not fit at widget size: the axis-label panel, the hover card, the crosshair and the input layer. No other behaviour changes, so the Traffic page is unaffected.

**Files:**
- Modify: `NetworkMonitor/Views/Controls/TrafficAreaChart.xaml` (name the axis panel)
- Modify: `NetworkMonitor/Views/Controls/TrafficAreaChart.xaml.cs`

**Interfaces:**
- Produces: `TrafficAreaChart.Compact` (`bool`, default `false`) and `TrafficAreaChart.CompactProperty`.

- [ ] **Step 1: Name the axis-label panel**

In `NetworkMonitor/Views/Controls/TrafficAreaChart.xaml`, add `x:Name` to the `StackPanel` that starts at line 18, as its first attribute:

```xaml
        <StackPanel
            x:Name="AxisLabelPanel"
            HorizontalAlignment="Right"
```

- [ ] **Step 2: Add the dependency property**

In `NetworkMonitor/Views/Controls/TrafficAreaChart.xaml.cs`, add after the `SelectedBucketStartProperty` registration (line 88):

```csharp
        public static readonly DependencyProperty CompactProperty =
            DependencyProperty.Register(
                nameof(Compact),
                typeof(bool),
                typeof(TrafficAreaChart),
                new PropertyMetadata(false, OnCompactChanged));
```

Add the property after `SelectedBucketStart` (line 122):

```csharp
        public bool Compact
        {
            get => (bool)GetValue(CompactProperty);
            set => SetValue(CompactProperty, value);
        }
```

Add the handler beside the other static change handlers (after `OnSelectedBucketStartChanged`):

```csharp
        private static void OnCompactChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        {
            TrafficAreaChart chart = (TrafficAreaChart)sender;
            bool compact = (bool)args.NewValue;
            Visibility visibility = compact ? Visibility.Collapsed : Visibility.Visible;

            chart.AxisLabelPanel.Visibility = visibility;
            chart.InputLayer.Visibility = visibility;

            if (compact)
            {
                chart.CrosshairLine.Visibility = Visibility.Collapsed;
                chart.HoverPanel.Visibility = Visibility.Collapsed;
            }

        }
```

- [ ] **Step 3: Suppress the in-chart axis text when compact**

`DrawAxisLabels` writes the peak-scale text onto the Win2D surface; at 320 px wide it is unreadable clutter. In `ChartCanvasDraw`, guard the call at line 385:

```csharp
                if (!Compact)
                {
                    DrawAxisLabels(args.DrawingSession, width, height, _axisTextFormat);
                }
```

- [ ] **Step 4: Verify the Traffic page is unchanged**

Run: `dotnet build NetworkMonitor.slnx -c Debug --nologo`
Expected: build succeeded.

Run the app (x64) and confirm the Internet and Local charts still show axis labels, the crosshair on hover, the hover card and click-to-select. `Compact` defaults to `false`, so nothing should differ.

- [ ] **Step 5: Commit**

```bash
git add NetworkMonitor/Views/Controls/TrafficAreaChart.xaml NetworkMonitor/Views/Controls/TrafficAreaChart.xaml.cs
git commit -m "Add a compact mode to the traffic area chart."
```

---

### Task 6: `MiniGraphViewModel` and the `MiniTrafficSection` control

The view model that reads the feed and the reusable section control used once for Internet and once for Local.

**Files:**
- Create: `NetworkMonitor/ViewModels/MiniGraphViewModel.cs`
- Create: `NetworkMonitor/Views/Controls/MiniTrafficSection.xaml`
- Create: `NetworkMonitor/Views/Controls/MiniTrafficSection.xaml.cs`
- Modify: `NetworkMonitor/App.xaml.cs` (register the view model as a singleton)

**Interfaces:**
- Consumes: `LiveTrafficFeed` (Task 4), `MiniGraphState` (Task 3), `MiniGraphFormatter` (Task 2), `TrafficAreaChart.Compact` (Task 5), `Settings.RateUnitMode`.
- Produces:
  - `MiniGraphViewModel` with `IReadOnlyList<ChartPoint>? InternetPoints`, `LocalPoints`, `string InternetRateText`, `LocalRateText`, `SpeedTestText`, `UnknownDevicesText`, `bool HasUnknownDevices`, `bool ShowInternet`, `ShowLocal`, `ShowSpeedTest`, `ShowUnknownDevices`, `bool ShowFooter`, `bool ShowEmptyHint`, and `void Attach()` / `void Detach()`.
  - `MiniTrafficSection` with `string Label`, `string RateText`, `IReadOnlyList<ChartPoint>? Points`.

**Attach/Detach is the cost control:** while the window is hidden the view model unsubscribes from `Updated`, so a hidden widget costs only the ring-buffer writes the feed was doing anyway.

- [ ] **Step 1: Write the view model**

Create `NetworkMonitor/ViewModels/MiniGraphViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using NetworkMonitor.Models.Charting;
using NetworkMonitor.Models.Formatting;
using NetworkMonitor.Services.Data;
using NetworkMonitor.Services.Platform;
using NetworkMonitor.Services.Traffic;

namespace NetworkMonitor.ViewModels
{
    public sealed class MiniGraphViewModel : ObservableObject
    {
        private readonly LiveTrafficFeed _feed;
        private readonly MiniGraphState _state;
        private readonly Settings _settings;
        private readonly DispatcherQueue _dispatcherQueue;
        private bool _attached;

        public MiniGraphViewModel(LiveTrafficFeed feed, MiniGraphState state, Settings settings)
        {
            _feed = feed;
            _state = state;
            _settings = settings;
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            _state.Changed += OnStateChanged;
        }

        private IReadOnlyList<ChartPoint>? _internetPoints;

        public IReadOnlyList<ChartPoint>? InternetPoints
        {
            get => _internetPoints;
            private set => SetProperty(ref _internetPoints, value);
        }

        private IReadOnlyList<ChartPoint>? _localPoints;

        public IReadOnlyList<ChartPoint>? LocalPoints
        {
            get => _localPoints;
            private set => SetProperty(ref _localPoints, value);
        }

        private string _internetRateText = "—";

        public string InternetRateText
        {
            get => _internetRateText;
            private set => SetProperty(ref _internetRateText, value);
        }

        private string _localRateText = "—";

        public string LocalRateText
        {
            get => _localRateText;
            private set => SetProperty(ref _localRateText, value);
        }

        private string _speedTestText = "No speed test yet";

        public string SpeedTestText
        {
            get => _speedTestText;
            private set => SetProperty(ref _speedTestText, value);
        }

        private string _unknownDevicesText = "✓ no unknown devices";

        public string UnknownDevicesText
        {
            get => _unknownDevicesText;
            private set => SetProperty(ref _unknownDevicesText, value);
        }

        private bool _hasUnknownDevices;

        public bool HasUnknownDevices
        {
            get => _hasUnknownDevices;
            private set => SetProperty(ref _hasUnknownDevices, value);
        }

        public bool ShowInternet => _state.ShowInternet;

        public bool ShowLocal => _state.ShowLocal;

        public bool ShowSpeedTest => _state.ShowSpeedTest;

        public bool ShowUnknownDevices => _state.ShowUnknownDevices;

        public bool ShowFooter => _state.ShowSpeedTest || _state.ShowUnknownDevices;

        public bool ShowEmptyHint => !_state.HasAnySection;

        public void Attach()
        {

            if (!_attached)
            {
                _attached = true;
                _feed.Updated += OnFeedUpdated;
                Refresh();
            }

        }

        public void Detach()
        {

            if (_attached)
            {
                _attached = false;
                _feed.Updated -= OnFeedUpdated;
            }

        }

        public void Refresh()
        {
            RateUnitMode mode = _settings.RateUnitMode;

            if (_state.ShowInternet)
            {
                InternetPoints = _feed.WanSnapshot();
                InternetRateText = MiniGraphFormatter.Rate(_feed.WanDownloadBytesPerSecond, _feed.WanUploadBytesPerSecond, mode);
            }

            if (_state.ShowLocal)
            {
                LocalPoints = _feed.LanSnapshot();
                LocalRateText = MiniGraphFormatter.Rate(_feed.LanDownloadBytesPerSecond, _feed.LanUploadBytesPerSecond, mode);
            }

            SpeedTestText = MiniGraphFormatter.SpeedTest(_feed.LatestSpeedTest, mode);
            UnknownDevicesText = MiniGraphFormatter.UnknownDevices(_feed.UnapprovedDeviceCount);
            HasUnknownDevices = _feed.UnapprovedDeviceCount > 0;
        }

        private void OnFeedUpdated(object? sender, EventArgs args)
        {
            _dispatcherQueue.TryEnqueue(Refresh);
        }

        private void OnStateChanged(object? sender, EventArgs args)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                OnPropertyChanged(nameof(ShowInternet));
                OnPropertyChanged(nameof(ShowLocal));
                OnPropertyChanged(nameof(ShowSpeedTest));
                OnPropertyChanged(nameof(ShowUnknownDevices));
                OnPropertyChanged(nameof(ShowFooter));
                OnPropertyChanged(nameof(ShowEmptyHint));
                Refresh();
            });
        }
    }
}
```

- [ ] **Step 2: Write the section control markup**

Create `NetworkMonitor/Views/Controls/MiniTrafficSection.xaml`:

```xaml
<?xml version="1.0" encoding="utf-8"?>

<UserControl
    x:Class="NetworkMonitor.Views.Controls.MiniTrafficSection"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:chart="using:NetworkMonitor.Views.Controls">

    <UserControl.Resources>

        <ResourceDictionary>

            <ResourceDictionary.ThemeDictionaries>

                <ResourceDictionary
                    x:Key="Light">

                    <LinearGradientBrush
                        x:Key="MiniScrimBrush"
                        StartPoint="0,0"
                        EndPoint="0,1">

                        <GradientStop
                            Color="#F2FFFFFF"
                            Offset="0.0" />

                        <GradientStop
                            Color="#00FFFFFF"
                            Offset="1.0" />

                    </LinearGradientBrush>

                </ResourceDictionary>

                <ResourceDictionary
                    x:Key="Dark">

                    <LinearGradientBrush
                        x:Key="MiniScrimBrush"
                        StartPoint="0,0"
                        EndPoint="0,1">

                        <GradientStop
                            Color="#F2202020"
                            Offset="0.0" />

                        <GradientStop
                            Color="#00202020"
                            Offset="1.0" />

                    </LinearGradientBrush>

                </ResourceDictionary>

            </ResourceDictionary.ThemeDictionaries>

        </ResourceDictionary>

    </UserControl.Resources>

    <Grid
        Background="Transparent">

        <chart:TrafficAreaChart
            x:Name="SectionChart"
            Compact="True" />

        <Rectangle
            Height="26"
            VerticalAlignment="Top"
            Fill="{StaticResource MiniScrimBrush}"
            IsHitTestVisible="False" />

        <Grid
            VerticalAlignment="Top"
            Margin="8,3,8,0"
            ColumnDefinitions="*,Auto"
            IsHitTestVisible="False">

            <TextBlock
                x:Name="LabelText"
                Grid.Column="0"
                FontSize="10"
                FontWeight="SemiBold"
                CharacterSpacing="80"
                Opacity="0.7" />

            <TextBlock
                x:Name="RateLabel"
                Grid.Column="1"
                FontSize="11"
                FontWeight="SemiBold"
                Opacity="0.9" />

        </Grid>

    </Grid>

</UserControl>
```

- [ ] **Step 3: Write the section control code-behind**

Create `NetworkMonitor/Views/Controls/MiniTrafficSection.xaml.cs`:

```csharp
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NetworkMonitor.Models.Charting;

namespace NetworkMonitor.Views.Controls
{
    public sealed partial class MiniTrafficSection : UserControl
    {
        public static readonly DependencyProperty LabelProperty =
            DependencyProperty.Register(
                nameof(Label),
                typeof(string),
                typeof(MiniTrafficSection),
                new PropertyMetadata(string.Empty, OnLabelChanged));

        public static readonly DependencyProperty RateTextProperty =
            DependencyProperty.Register(
                nameof(RateText),
                typeof(string),
                typeof(MiniTrafficSection),
                new PropertyMetadata(string.Empty, OnRateTextChanged));

        public static readonly DependencyProperty PointsProperty =
            DependencyProperty.Register(
                nameof(Points),
                typeof(IReadOnlyList<ChartPoint>),
                typeof(MiniTrafficSection),
                new PropertyMetadata(null, OnPointsChanged));

        public MiniTrafficSection()
        {
            InitializeComponent();
        }

        public string Label
        {
            get => (string)GetValue(LabelProperty);
            set => SetValue(LabelProperty, value);
        }

        public string RateText
        {
            get => (string)GetValue(RateTextProperty);
            set => SetValue(RateTextProperty, value);
        }

        public IReadOnlyList<ChartPoint>? Points
        {
            get => (IReadOnlyList<ChartPoint>?)GetValue(PointsProperty);
            set => SetValue(PointsProperty, value);
        }

        private static void OnLabelChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        {
            MiniTrafficSection section = (MiniTrafficSection)sender;
            section.LabelText.Text = ((string?)args.NewValue ?? string.Empty).ToUpperInvariant();
        }

        private static void OnRateTextChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        {
            MiniTrafficSection section = (MiniTrafficSection)sender;
            section.RateLabel.Text = (string?)args.NewValue ?? string.Empty;
        }

        private static void OnPointsChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        {
            MiniTrafficSection section = (MiniTrafficSection)sender;
            section.SectionChart.ChartPoints = args.NewValue as IReadOnlyList<ChartPoint>;
            section.SectionChart.MarkLiveUpdate();
        }
    }
}
```

- [ ] **Step 4: Register the view model**

In `NetworkMonitor/App.xaml.cs`, beside the other singleton view models (after `services.AddSingleton<SpeedTestViewModel>();`, line 164):

```csharp
                        services.AddSingleton<MiniGraphViewModel>();
```

- [ ] **Step 5: Verify**

Run: `dotnet build NetworkMonitor.slnx -c Debug --nologo`
Expected: build succeeded. Nothing is shown yet — the window arrives in the next task.

- [ ] **Step 6: Commit**

```bash
git add NetworkMonitor/ViewModels/MiniGraphViewModel.cs NetworkMonitor/Views/Controls/MiniTrafficSection.xaml NetworkMonitor/Views/Controls/MiniTrafficSection.xaml.cs NetworkMonitor/App.xaml.cs
git commit -m "Add the mini graph view model and traffic section control."
```

---

### Task 7: `MiniGraphWindow` — the widget itself

The frameless always-on-top window, its placement handling, and its lifetime in `App`. This is the first task with something visible on screen.

**Files:**
- Create: `NetworkMonitor/MiniGraphWindow.xaml`
- Create: `NetworkMonitor/MiniGraphWindow.xaml.cs`
- Modify: `NetworkMonitor/App.xaml.cs` (create lazily, show/hide from `MiniGraphState.Changed`, close on exit)
- Modify: `NetworkMonitor/MainWindow.xaml.cs` (close the widget in `ShutdownGracefully`)

**Interfaces:**
- Consumes: `MiniGraphViewModel` (Task 6), `MiniGraphState` (Task 3), `MiniTrafficSection` (Task 6).
- Produces: `MiniGraphWindow` with `void ShowWidget()`, `void HideWidget()`, `void CloseWidget()`; `App.ApplyMiniGraphVisibility()` static.

**Deviation from the spec, and why.** The spec proposed dragging via `InputNonClientPointerSource.SetRegionRects(NonClientRegionKind.Caption, …)`. A caption region swallows XAML pointer input, which would kill the double-click-to-drill-in the spec also asks for on the same surface. This plan drags manually instead: pointer-pressed on the background captures the pointer, and movement past a 4 px threshold moves the `AppWindow`. Below the threshold nothing moves, so `DoubleTapped` still fires normally. No snap-assist, which the spec lists as out of scope anyway.

- [ ] **Step 1: Write the window markup**

Create `NetworkMonitor/MiniGraphWindow.xaml`:

```xaml
<?xml version="1.0" encoding="utf-8"?>

<Window
    x:Class="NetworkMonitor.MiniGraphWindow"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:controls="using:NetworkMonitor.Views.Controls">

    <Grid
        x:Name="RootLayer"
        Background="{ThemeResource SolidBackgroundFillColorBaseBrush}"
        PointerPressed="RootPointerPressed"
        PointerMoved="RootPointerMoved"
        PointerReleased="RootPointerReleased"
        PointerEntered="RootPointerEntered"
        PointerExited="RootPointerExited">

        <Grid
            x:Name="SectionsPanel"
            RowDefinitions="*,*,Auto">

            <controls:MiniTrafficSection
                x:Name="InternetSection"
                Grid.Row="0"
                Label="Internet"
                MinHeight="40"
                DoubleTapped="InternetSectionDoubleTapped"
                RateText="{x:Bind ViewModel.InternetRateText, Mode=OneWay}"
                Points="{x:Bind ViewModel.InternetPoints, Mode=OneWay}" />

            <controls:MiniTrafficSection
                x:Name="LocalSection"
                Grid.Row="1"
                Label="Local"
                MinHeight="40"
                DoubleTapped="LocalSectionDoubleTapped"
                RateText="{x:Bind ViewModel.LocalRateText, Mode=OneWay}"
                Points="{x:Bind ViewModel.LocalPoints, Mode=OneWay}" />

            <StackPanel
                x:Name="FooterPanel"
                Grid.Row="2"
                Padding="8,5,8,6"
                Spacing="2"
                Background="{ThemeResource LayerFillColorDefaultBrush}"
                BorderBrush="{ThemeResource DividerStrokeColorDefaultBrush}"
                BorderThickness="0,1,0,0">

                <TextBlock
                    x:Name="SpeedTestLine"
                    FontSize="11"
                    DoubleTapped="SpeedLineDoubleTapped"
                    Text="{x:Bind ViewModel.SpeedTestText, Mode=OneWay}" />

                <TextBlock
                    x:Name="UnknownDevicesLine"
                    FontSize="11"
                    DoubleTapped="DevicesLineDoubleTapped"
                    Text="{x:Bind ViewModel.UnknownDevicesText, Mode=OneWay}" />

            </StackPanel>

        </Grid>

        <TextBlock
            x:Name="EmptyHint"
            FontSize="12"
            Opacity="0.7"
            TextWrapping="Wrap"
            TextAlignment="Center"
            Margin="16"
            HorizontalAlignment="Center"
            VerticalAlignment="Center"
            Visibility="Collapsed"
            Text="Right-click to choose what to show" />

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

    </Grid>

</Window>
```

- [ ] **Step 2: Write the window code-behind**

Create `NetworkMonitor/MiniGraphWindow.xaml.cs`:

```csharp
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using NetworkMonitor.Services.Data;
using NetworkMonitor.Services.Platform;
using NetworkMonitor.ViewModels;
using Windows.Foundation;
using Windows.Graphics;

namespace NetworkMonitor
{
    public sealed partial class MiniGraphWindow : Window
    {
        private const int GwlExStyle = -20;
        private const long WsExToolWindow = 0x00000080;
        private const int MinimumWidth = 240;
        private const int MinimumHeight = 120;
        private const double DragThreshold = 4.0;

        private readonly MiniGraphState _state;
        private readonly Settings _settings;
        private readonly DispatcherTimer _savePlacementTimer;
        private readonly IntPtr _hwnd;
        private bool _placementRestored;
        private bool _pointerDown;
        private bool _dragging;
        private Point _dragOrigin;
        private PointInt32 _dragWindowOrigin;

        public MiniGraphWindow(MiniGraphViewModel viewModel, MiniGraphState state, Settings settings)
        {
            ViewModel = viewModel;
            _state = state;
            _settings = settings;
            InitializeComponent();

            _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

            _savePlacementTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(400)
            };
            _savePlacementTimer.Tick += OnSavePlacementTimerTick;

            ConfigureWindow();
            ApplyLayout();
            RestorePlacement();

            _state.Changed += OnStateChanged;
            AppWindow.Changed += OnAppWindowChanged;
        }

        public MiniGraphViewModel ViewModel
        {
            get;
        }

        public void ShowWidget()
        {
            ViewModel.Attach();
            AppWindow.Show();
        }

        public void HideWidget()
        {
            ViewModel.Detach();
            AppWindow.Hide();
        }

        public void CloseWidget()
        {
            _savePlacementTimer.Stop();
            _state.Changed -= OnStateChanged;
            ViewModel.Detach();
            Close();
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        private void ConfigureWindow()
        {
            AppWindow.IsShownInSwitchers = false;
            Title = "Umnatha mini graph";

            if (AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.SetBorderAndTitleBar(true, false);
                presenter.IsAlwaysOnTop = true;
                presenter.IsResizable = true;
                presenter.IsMaximizable = false;
                presenter.IsMinimizable = false;
            }

            long exStyle = GetWindowLongPtr(_hwnd, GwlExStyle).ToInt64();
            exStyle |= WsExToolWindow;

            SetWindowLongPtr(_hwnd, GwlExStyle, new IntPtr(exStyle));
        }

        private void RestorePlacement()
        {
            int width = Math.Max(MinimumWidth, _settings.MiniGraphWidth);
            int height = Math.Max(MinimumHeight, _settings.MiniGraphHeight);
            int positionX = _settings.MiniGraphX;
            int positionY = _settings.MiniGraphY;
            bool onScreen = false;

            if (positionX > int.MinValue && positionY > int.MinValue && _settings.MiniGraphX >= 0)
            {
                DisplayArea area = DisplayArea.GetFromPoint(new PointInt32(positionX, positionY), DisplayAreaFallback.None);
                onScreen = area is not null;
            }

            if (!onScreen)
            {
                DisplayArea primary = DisplayArea.Primary;
                RectInt32 workArea = primary.WorkArea;
                positionX = workArea.X + workArea.Width - width - 16;
                positionY = workArea.Y + workArea.Height - height - 16;
            }

            AppWindow.MoveAndResize(new RectInt32(positionX, positionY, width, height));
            _placementRestored = true;
        }

        private void ApplyLayout()
        {
            InternetSection.Visibility = _state.ShowInternet ? Visibility.Visible : Visibility.Collapsed;
            LocalSection.Visibility = _state.ShowLocal ? Visibility.Visible : Visibility.Collapsed;
            SpeedTestLine.Visibility = _state.ShowSpeedTest ? Visibility.Visible : Visibility.Collapsed;
            UnknownDevicesLine.Visibility = _state.ShowUnknownDevices ? Visibility.Visible : Visibility.Collapsed;
            FooterPanel.Visibility = _state.ShowSpeedTest || _state.ShowUnknownDevices ? Visibility.Visible : Visibility.Collapsed;
            EmptyHint.Visibility = _state.HasAnySection ? Visibility.Collapsed : Visibility.Visible;

            // The strips are fixed height and the charts share everything left over, so switching Local
            // off makes Internet twice as tall rather than shrinking the window.
            SectionsPanel.RowDefinitions[0].Height = _state.ShowInternet ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            SectionsPanel.RowDefinitions[1].Height = _state.ShowLocal ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        }

        private void OnStateChanged(object? sender, EventArgs args)
        {
            DispatcherQueue.TryEnqueue(ApplyLayout);
        }

        private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
        {

            if (_placementRestored && (args.DidPositionChange || args.DidSizeChange))
            {
                ClampMinimumSize();
                _savePlacementTimer.Stop();
                _savePlacementTimer.Start();
            }

        }

        private void ClampMinimumSize()
        {
            SizeInt32 size = AppWindow.Size;

            if (size.Width < MinimumWidth || size.Height < MinimumHeight)
            {
                int width = Math.Max(MinimumWidth, size.Width);
                int height = Math.Max(MinimumHeight, size.Height);

                AppWindow.Resize(new SizeInt32(width, height));
            }

        }

        private void OnSavePlacementTimerTick(object? sender, object args)
        {
            _savePlacementTimer.Stop();
            _state.SavePlacement(AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height);
        }

        private void RootPointerPressed(object sender, PointerRoutedEventArgs args)
        {

            if (args.GetCurrentPoint(RootLayer).Properties.IsLeftButtonPressed)
            {
                _pointerDown = true;
                _dragging = false;
                _dragOrigin = args.GetCurrentPoint(null).Position;
                _dragWindowOrigin = AppWindow.Position;
                RootLayer.CapturePointer(args.Pointer);
            }

        }

        private void RootPointerMoved(object sender, PointerRoutedEventArgs args)
        {

            if (_pointerDown)
            {
                Point current = args.GetCurrentPoint(null).Position;
                double deltaX = current.X - _dragOrigin.X;
                double deltaY = current.Y - _dragOrigin.Y;

                // Below the threshold nothing moves, so a double-click still reaches the section
                // underneath instead of being eaten by a one-pixel drag.
                if (_dragging || Math.Abs(deltaX) > DragThreshold || Math.Abs(deltaY) > DragThreshold)
                {
                    _dragging = true;

                    AppWindow.Move(new PointInt32(
                        _dragWindowOrigin.X + (int)Math.Round(deltaX),
                        _dragWindowOrigin.Y + (int)Math.Round(deltaY)));
                }

            }

        }

        private void RootPointerReleased(object sender, PointerRoutedEventArgs args)
        {
            _pointerDown = false;
            _dragging = false;
            RootLayer.ReleasePointerCapture(args.Pointer);
        }

        private void CloseGlyphClick(object sender, RoutedEventArgs args)
        {
            _state.IsVisible = false;
        }
    }
}
```

`RootPointerEntered`, `RootPointerExited` and the four `DoubleTapped` handlers are referenced by the markup but arrive in Tasks 10 and 11. Add empty-bodied stubs now so the project compiles:

```csharp
        private void RootPointerEntered(object sender, PointerRoutedEventArgs args)
        {
        }

        private void RootPointerExited(object sender, PointerRoutedEventArgs args)
        {
        }

        private void InternetSectionDoubleTapped(object sender, DoubleTappedRoutedEventArgs args)
        {
        }

        private void LocalSectionDoubleTapped(object sender, DoubleTappedRoutedEventArgs args)
        {
        }

        private void SpeedLineDoubleTapped(object sender, DoubleTappedRoutedEventArgs args)
        {
        }

        private void DevicesLineDoubleTapped(object sender, DoubleTappedRoutedEventArgs args)
        {
        }
```

- [ ] **Step 3: Own the window's lifetime in `App`**

In `NetworkMonitor/App.xaml.cs`, add a field beside the other statics (after line 38):

```csharp
        private static MiniGraphWindow? _miniGraphWindow;
```

Add these methods after `OnLaunched`:

```csharp
        internal static void ApplyMiniGraphVisibility()
        {
            MiniGraphState state = AppHost.Services.GetRequiredService<MiniGraphState>();

            try
            {

                if (state.IsVisible)
                {

                    if (_miniGraphWindow is null)
                    {
                        _miniGraphWindow = new MiniGraphWindow(
                            AppHost.Services.GetRequiredService<MiniGraphViewModel>(),
                            state,
                            AppHost.Services.GetRequiredService<Settings>());
                    }

                    _miniGraphWindow.ShowWidget();
                }
                else
                {
                    _miniGraphWindow?.HideWidget();
                }

            }
            catch (Exception exception)
            {
                AppLog.Error("App.ApplyMiniGraphVisibility", exception);
            }

        }

        internal static void CloseMiniGraph()
        {
            _miniGraphWindow?.CloseWidget();
            _miniGraphWindow = null;
        }
```

The window is created lazily on first show and **hidden rather than closed** thereafter, so toggling it is instant and its Win2D surfaces are not rebuilt each time.

In `OnLaunched`, after `window.RestoreWindowPlacement();` (line 274):

```csharp
                MiniGraphState miniGraphState = AppHost.Services.GetRequiredService<MiniGraphState>();
                miniGraphState.Changed += (stateSender, stateArgs) => ApplyMiniGraphVisibility();
                ApplyMiniGraphVisibility();
```

- [ ] **Step 4: Close it with the app**

In `NetworkMonitor/MainWindow.xaml.cs`, in `ShutdownGracefully` (line 262), before `StopHost();`:

```csharp
            App.CloseMiniGraph();
```

- [ ] **Step 5: Verify it appears**

Run: `dotnet build NetworkMonitor.slnx -c Debug --nologo`
Expected: build succeeded.

There is still no UI to turn it on, so verify by hand: close the app, edit `%LocalAppData%\NetworkMonitor\settings.json`, set `"ShowMiniGraph": true`, restart. Confirm the widget appears bottom-right, sits above other windows, shows two live charts and the two footer lines, has no taskbar button, does not appear in Alt-Tab, can be dragged and resized, and that its position and size survive a restart.

- [ ] **Step 6: Commit**

```bash
git add NetworkMonitor/MiniGraphWindow.xaml NetworkMonitor/MiniGraphWindow.xaml.cs NetworkMonitor/App.xaml.cs NetworkMonitor/MainWindow.xaml.cs
git commit -m "Add the floating mini graph window."
```

---

### Task 8: The three entry points

Tray menu, Traffic page toolbar and Settings switch — all writing `MiniGraphState.IsVisible`, all following each other because they read the same state.

**Files:**
- Modify: `NetworkMonitor.Services/Platform/TrayIconService.cs`
- Modify: `NetworkMonitor/MainWindow.xaml.cs:84` (pass the new callback)
- Modify: `NetworkMonitor/Views/TrafficHostPage.xaml` and `.xaml.cs`
- Modify: `NetworkMonitor/ViewModels/SettingsViewModel.cs`
- Modify: `NetworkMonitor/Views/SettingsPage.xaml`

**Interfaces:**
- Consumes: `MiniGraphState` (Task 3).
- Produces: `TrayIconService(IntPtr hwnd, Action onExit, Action onToggleMiniGraph, Func<bool> isMiniGraphVisible)`.

- [ ] **Step 1: Add the tray item**

In `NetworkMonitor.Services/Platform/TrayIconService.cs`, add two constants beside `MenuExit` (line 108):

```csharp
        private const uint MenuMiniGraph = 3;
        private const uint MfChecked = 0x0008;
```

Add two fields beside `_onExit` (line 111):

```csharp
        private readonly Action _onToggleMiniGraph;
        private readonly Func<bool> _isMiniGraphVisible;
```

Change the constructor signature and assign them:

```csharp
        public TrayIconService(IntPtr hwnd, Action onExit, Action onToggleMiniGraph, Func<bool> isMiniGraphVisible)
        {
            _hwnd = hwnd;
            _onExit = onExit;
            _onToggleMiniGraph = onToggleMiniGraph;
            _isMiniGraphVisible = isMiniGraphVisible;
            _subclassProc = SubclassProc;
```

In `ShowContextMenu`, replace the two `AppendMenu` calls (lines 217–218) with:

```csharp
            uint miniGraphFlags = _isMiniGraphVisible() ? MfString | MfChecked : MfString;

            AppendMenu(hMenu, miniGraphFlags, MenuMiniGraph, "Mini graph");
            AppendMenu(hMenu, MfString, MenuShow, "Show Umnatha Network Monitor");
            AppendMenu(hMenu, MfString, MenuExit, "Exit");
```

And add a branch to the command handling:

```csharp
            if (cmd == MenuMiniGraph)
            {
                _onToggleMiniGraph();
            }
            else if (cmd == MenuShow)
            {
                ShowFromTray(hWnd);
            }
            else if (cmd == MenuExit)
            {
                _onExit();
            }
```

- [ ] **Step 2: Wire the tray item in `MainWindow`**

`MainWindow` needs `MiniGraphState`. Add it to the constructor parameter list (line 43) and to a field:

```csharp
        private readonly MiniGraphState _miniGraphState;
```

```csharp
        public MainWindow(ScanWorker scanWorker, Settings settings, IDbContextFactory<AppDbContext> dbFactory, InAppNotificationService notificationService, SpeedTestWorker speedTestWorker, UpdateViewModel updateViewModel, MiniGraphState miniGraphState)
```

Assign `_miniGraphState = miniGraphState;` next to the other assignments, then change the tray construction (line 84) to:

```csharp
            _trayIcon = new TrayIconService(
                _hwnd,
                OnExitApp,
                () => _miniGraphState.IsVisible = !_miniGraphState.IsVisible,
                () => _miniGraphState.IsVisible);
```

`MainWindow` is registered as `AddTransient<MainWindow>()`, so DI supplies the new parameter with no further change.

- [ ] **Step 3: Add the Traffic page toggle**

In `NetworkMonitor/Views/TrafficHostPage.xaml`, wrap the `SelectorBar` so the toggle sits at the right of the same row. Replace the `SelectorBar` element (lines 12–31) with:

```xaml
        <Grid
            Grid.Row="0"
            Margin="24,12,24,0"
            ColumnDefinitions="*,Auto">

            <SelectorBar
                Grid.Column="0"
                x:Name="TabBar"
                FontSize="13"
                SelectionChanged="TabBarSelectionChanged">

                <SelectorBarItem
                    Tag="Internet"
                    Text="Internet" />

                <SelectorBarItem
                    Tag="Local"
                    Text="Local" />

                <SelectorBarItem
                    Tag="SpeedTest"
                    Text="Speed Test" />

            </SelectorBar>

            <ToggleButton
                Grid.Column="1"
                x:Name="MiniGraphToggle"
                VerticalAlignment="Center"
                Padding="10,4"
                FontSize="12"
                Content="Mini graph"
                Click="MiniGraphToggleClick" />

        </Grid>
```

In `NetworkMonitor/Views/TrafficHostPage.xaml.cs`, add the state, keep the button in sync, and unhook on navigation away:

```csharp
        private readonly MiniGraphState _miniGraphState = App.AppHost.Services.GetRequiredService<MiniGraphState>();
```

```csharp
        private void MiniGraphToggleClick(object sender, RoutedEventArgs args)
        {
            _miniGraphState.IsVisible = MiniGraphToggle.IsChecked == true;
        }

        private void OnMiniGraphStateChanged(object? sender, EventArgs args)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                MiniGraphToggle.IsChecked = _miniGraphState.IsVisible;
            });
        }
```

`TrafficHostPage` has no navigation overrides today, and `ContentFrame.Navigate` builds a fresh instance each time the user leaves and returns, so subscribe and unsubscribe on `Loaded` / `Unloaded` rather than adding overrides. Wire them in the constructor after the existing `TabBar.SelectedItem = TabBar.Items[0];`:

```csharp
            Loaded += OnPageLoaded;
            Unloaded += OnPageUnloaded;
```

```csharp
        private void OnPageLoaded(object sender, RoutedEventArgs args)
        {
            MiniGraphToggle.IsChecked = _miniGraphState.IsVisible;
            _miniGraphState.Changed += OnMiniGraphStateChanged;
        }

        private void OnPageUnloaded(object sender, RoutedEventArgs args)
        {
            _miniGraphState.Changed -= OnMiniGraphStateChanged;
        }
```

Add `using Microsoft.Extensions.DependencyInjection;` and `using NetworkMonitor.Services.Platform;` — the file currently imports only `Microsoft.UI.Xaml` and `Microsoft.UI.Xaml.Controls`.

- [ ] **Step 4: Add the Settings controls**

In `NetworkMonitor/ViewModels/SettingsViewModel.cs`, add properties following the existing hand-written `SetProperty` pattern. These write through `MiniGraphState` rather than `Settings` directly, so the tray and toolbar follow. Inject `MiniGraphState` into the constructor and store it as `_miniGraphState`, then add to the Properties section:

```csharp
        private bool _showMiniGraph;

        public bool ShowMiniGraph
        {
            get => _showMiniGraph;
            set
            {

                if (SetProperty(ref _showMiniGraph, value))
                {
                    _miniGraphState.IsVisible = value;
                }

            }
        }

        private bool _miniGraphShowInternet;

        public bool MiniGraphShowInternet
        {
            get => _miniGraphShowInternet;
            set
            {

                if (SetProperty(ref _miniGraphShowInternet, value))
                {
                    _miniGraphState.ShowInternet = value;
                }

            }
        }

        private bool _miniGraphShowLocal;

        public bool MiniGraphShowLocal
        {
            get => _miniGraphShowLocal;
            set
            {

                if (SetProperty(ref _miniGraphShowLocal, value))
                {
                    _miniGraphState.ShowLocal = value;
                }

            }
        }

        private bool _miniGraphShowSpeedTest;

        public bool MiniGraphShowSpeedTest
        {
            get => _miniGraphShowSpeedTest;
            set
            {

                if (SetProperty(ref _miniGraphShowSpeedTest, value))
                {
                    _miniGraphState.ShowSpeedTest = value;
                }

            }
        }

        private bool _miniGraphShowUnknownDevices;

        public bool MiniGraphShowUnknownDevices
        {
            get => _miniGraphShowUnknownDevices;
            set
            {

                if (SetProperty(ref _miniGraphShowUnknownDevices, value))
                {
                    _miniGraphState.ShowUnknownDevices = value;
                }

            }
        }

        private double _miniGraphOpacity;

        public double MiniGraphOpacity
        {
            get => _miniGraphOpacity;
            set
            {

                if (SetProperty(ref _miniGraphOpacity, value))
                {
                    _miniGraphState.Opacity = (int)value;
                }

            }
        }
```

Seed all six from `MiniGraphState` in the constructor beside the other seeds (line 50). **These six must be excluded from `OnSettingChanged`'s `isPersistable` test** — `MiniGraphState` already saves, and letting `PersistAll()` run would fire a "Settings saved" toast on every drag of the opacity slider:

```csharp
            bool isPersistable = args.PropertyName is not null
                && args.PropertyName != nameof(PurgeStatus)
                && args.PropertyName != nameof(TrafficPurgeStatus)
                && args.PropertyName != nameof(RunAtStartup)
                && args.PropertyName != nameof(SubnetBaseEditable)
                && args.PropertyName != nameof(ShowMiniGraph)
                && args.PropertyName != nameof(MiniGraphShowInternet)
                && args.PropertyName != nameof(MiniGraphShowLocal)
                && args.PropertyName != nameof(MiniGraphShowSpeedTest)
                && args.PropertyName != nameof(MiniGraphShowUnknownDevices)
                && args.PropertyName != nameof(MiniGraphOpacity);
```

In `NetworkMonitor/Views/SettingsPage.xaml`, add a new card to the Traffic panel, after the Sampling card's closing `</Border>` (line 216):

```xaml
                <Border
                    Style="{StaticResource SettingsCard}">

                    <StackPanel
                        Spacing="12">

                        <TextBlock
                            Style="{StaticResource SettingsCardHeader}"
                            Text="Floating mini graph" />

                        <StackPanel
                            Spacing="4">

                            <ToggleSwitch
                                Header="Show floating mini graph"
                                IsOn="{x:Bind ViewModel.ShowMiniGraph, Mode=TwoWay}" />

                            <TextBlock
                                Text="A small always-on-top widget showing live activity without the main window open. Hovering it makes it fully opaque so you can read it."
                                FontSize="12"
                                Opacity="0.65"
                                TextWrapping="Wrap" />

                        </StackPanel>

                        <CheckBox
                            Content="Internet chart"
                            IsChecked="{x:Bind ViewModel.MiniGraphShowInternet, Mode=TwoWay}" />

                        <CheckBox
                            Content="Local chart"
                            IsChecked="{x:Bind ViewModel.MiniGraphShowLocal, Mode=TwoWay}" />

                        <CheckBox
                            Content="Last speed test"
                            IsChecked="{x:Bind ViewModel.MiniGraphShowSpeedTest, Mode=TwoWay}" />

                        <CheckBox
                            Content="Unknown devices"
                            IsChecked="{x:Bind ViewModel.MiniGraphShowUnknownDevices, Mode=TwoWay}" />

                        <StackPanel
                            Spacing="4">

                            <TextBlock
                                Text="Resting opacity (%)" />

                            <Slider
                                Minimum="50"
                                Maximum="100"
                                StepFrequency="5"
                                TickFrequency="10"
                                TickPlacement="Outside"
                                Value="{x:Bind ViewModel.MiniGraphOpacity, Mode=TwoWay}" />

                        </StackPanel>

                    </StackPanel>

                </Border>
```

- [ ] **Step 5: Verify all three stay in sync**

Run: `dotnet build NetworkMonitor.slnx -c Debug --nologo`, then run the app (x64).

Confirm: the Settings switch shows and hides the widget; the Traffic toolbar toggle does the same and reflects the Settings switch; the tray right-click menu shows a checked "Mini graph" item above "Show Umnatha Network Monitor" that toggles it and whose tick follows the other two; unchecking all four sections shows "Right-click to choose what to show"; and switching Local off makes the Internet chart fill the freed space without the window changing size.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "Add the mini graph's tray, toolbar and settings entry points."
```

---

### Task 9: Right-click flyout and the close glyph

**Files:**
- Modify: `NetworkMonitor/MiniGraphWindow.xaml` (add the flyout)
- Modify: `NetworkMonitor/MiniGraphWindow.xaml.cs`

- [ ] **Step 1: Add the flyout markup**

Inside `RootLayer` in `NetworkMonitor/MiniGraphWindow.xaml`, before the `SectionsPanel`, attach a context flyout:

```xaml
        <FlyoutBase.AttachedFlyout>

            <MenuFlyout
                x:Name="WidgetMenu" />

        </FlyoutBase.AttachedFlyout>
```

Add `RightTapped="RootRightTapped"` to `RootLayer`'s event-handler attributes.

- [ ] **Step 2: Build the flyout in code**

The section items and the opacity radio items are checkable and must re-read state each time, so build the menu on open rather than in markup. Add to `MiniGraphWindow.xaml.cs`:

```csharp
        private void RootRightTapped(object sender, RightTappedRoutedEventArgs args)
        {
            WidgetMenu.Items.Clear();
            WidgetMenu.Items.Add(BuildSectionItem("Internet", _state.ShowInternet, value => _state.ShowInternet = value));
            WidgetMenu.Items.Add(BuildSectionItem("Local", _state.ShowLocal, value => _state.ShowLocal = value));
            WidgetMenu.Items.Add(BuildSectionItem("Speed test", _state.ShowSpeedTest, value => _state.ShowSpeedTest = value));
            WidgetMenu.Items.Add(BuildSectionItem("Unknown devices", _state.ShowUnknownDevices, value => _state.ShowUnknownDevices = value));
            WidgetMenu.Items.Add(BuildOpacitySubmenu());
            WidgetMenu.Items.Add(new MenuFlyoutSeparator());

            MenuFlyoutItem openItem = new MenuFlyoutItem
            {
                Text = "Open Network Monitor"
            };
            openItem.Click += (itemSender, itemArgs) => ShowMainWindow(null);
            WidgetMenu.Items.Add(openItem);

            MenuFlyoutItem closeItem = new MenuFlyoutItem
            {
                Text = "Close"
            };
            closeItem.Click += (itemSender, itemArgs) => _state.IsVisible = false;
            WidgetMenu.Items.Add(closeItem);

            WidgetMenu.ShowAt(RootLayer, args.GetPosition(RootLayer));
        }

        private ToggleMenuFlyoutItem BuildSectionItem(string text, bool isChecked, Action<bool> assign)
        {
            ToggleMenuFlyoutItem item = new ToggleMenuFlyoutItem
            {
                Text = text,
                IsChecked = isChecked
            };

            item.Click += (sender, args) => assign(item.IsChecked);

            return item;
        }

        private MenuFlyoutSubItem BuildOpacitySubmenu()
        {
            MenuFlyoutSubItem submenu = new MenuFlyoutSubItem
            {
                Text = "Opacity"
            };

            int[] levels = { 50, 60, 70, 80, 90, 100 };
            int current = _state.Opacity;

            foreach (int level in levels)
            {
                RadioMenuFlyoutItem item = new RadioMenuFlyoutItem
                {
                    Text = $"{level}%",
                    GroupName = "MiniGraphOpacity",
                    IsChecked = level == current
                };

                item.Click += (sender, args) => _state.Opacity = level;
                submenu.Items.Add(item);
            }

            return submenu;
        }
```

Add `using Microsoft.UI.Xaml.Controls;` to the file.

- [ ] **Step 3: Fade the close glyph in on hover**

The glyph sits at `Opacity="0"`. In `RootPointerEntered` set `CloseGlyph.Opacity = 1.0;` and in `RootPointerExited` set it back to `0.0`. Give it a transition in the constructor after `InitializeComponent()`:

```csharp
            CloseGlyph.OpacityTransition = new ScalarTransition
            {
                Duration = TimeSpan.FromMilliseconds(120)
            };
```

- [ ] **Step 4: Verify**

Run the app (x64). Confirm: right-clicking the widget opens a flyout with four checkable sections, an Opacity submenu with the current value selected, a separator, "Open Network Monitor" and "Close"; toggling a section from the flyout updates the Settings checkboxes too; "Close" clears the tray tick, the toolbar toggle and the Settings switch; and the ✕ glyph fades in on hover and closes the widget the same way.

`ShowMainWindow` arrives in Task 11 — for this task give it a body of `App.ShowMainWindow();` only if Task 11 is already done, otherwise stub it as an empty method and complete it there.

- [ ] **Step 5: Commit**

```bash
git add NetworkMonitor/MiniGraphWindow.xaml NetworkMonitor/MiniGraphWindow.xaml.cs
git commit -m "Add the mini graph's right-click menu and close glyph."
```

---

### Task 10: Opacity and hover to full opacity

**Files:**
- Modify: `NetworkMonitor/MiniGraphWindow.xaml.cs`

**The risk to check first.** WinUI 3 desktop windows are created with `WS_EX_NOREDIRECTIONBITMAP` and composited through DirectComposition, and Win2D's `CanvasControl` renders onto its own composition surface. Root-element `Opacity` is expected to carry through, but **verify it before building the rest of this task**. If the chart stays opaque while the text fades, fall back to `WS_EX_LAYERED` plus `SetLayeredWindowAttributes(hwnd, 0, alpha, LWA_ALPHA)` on the top-level window, which fades the composed result regardless of what drew it, and drive that alpha from the same timers.

**Timing, from the spec.** `MiniGraphOpacity` is the *resting* opacity. The rise waits 150 ms of dwell before starting and the fall waits 300 ms after the pointer leaves; each transition animates over ~120 ms. The dwell is what stops a pointer clipping a corner from making the widget flash — which would undercut the whole reason the opacity is low. Both timers cancel if the pointer reverses before they elapse. The animation is presentation only and never writes `MiniGraphOpacity`, so the resting value survives a hover and the setting is not churned to disk.

- [ ] **Step 1: Verify root opacity reaches the Win2D chart**

Temporarily add `RootLayer.Opacity = 0.5;` at the end of the constructor, run the app (x64) with the widget over a text document, and confirm the **chart** fades along with the text. Remove the line. If the chart did not fade, implement the layered-window fallback described above in place of `RootLayer.Opacity` throughout this task.

- [ ] **Step 2: Add the fields and timers**

In `MiniGraphWindow.xaml.cs`, add constants and fields:

```csharp
        private static readonly TimeSpan HoverRiseDelay = TimeSpan.FromMilliseconds(150);
        private static readonly TimeSpan HoverFallDelay = TimeSpan.FromMilliseconds(300);
        private static readonly TimeSpan OpacityFadeDuration = TimeSpan.FromMilliseconds(120);
```

```csharp
        private readonly DispatcherTimer _hoverRiseTimer;
        private readonly DispatcherTimer _hoverFallTimer;
        private bool _pointerInside;
```

In the constructor after `InitializeComponent()`:

```csharp
            RootLayer.OpacityTransition = new ScalarTransition
            {
                Duration = OpacityFadeDuration
            };

            _hoverRiseTimer = new DispatcherTimer
            {
                Interval = HoverRiseDelay
            };
            _hoverRiseTimer.Tick += OnHoverRiseTick;

            _hoverFallTimer = new DispatcherTimer
            {
                Interval = HoverFallDelay
            };
            _hoverFallTimer.Tick += OnHoverFallTick;
```

and after `ApplyLayout()` add `ApplyRestingOpacity();`.

- [ ] **Step 3: Add the handlers**

```csharp
        private void ApplyRestingOpacity()
        {

            if (!_pointerInside)
            {
                RootLayer.Opacity = _state.Opacity / 100.0;
            }

        }

        private void RootPointerEntered(object sender, PointerRoutedEventArgs args)
        {
            _pointerInside = true;
            CloseGlyph.Opacity = 1.0;
            _hoverFallTimer.Stop();

            if (RootLayer.Opacity < 1.0)
            {
                _hoverRiseTimer.Stop();
                _hoverRiseTimer.Start();
            }

        }

        private void RootPointerExited(object sender, PointerRoutedEventArgs args)
        {
            _pointerInside = false;
            CloseGlyph.Opacity = 0.0;
            _hoverRiseTimer.Stop();
            _hoverFallTimer.Stop();
            _hoverFallTimer.Start();
        }

        private void OnHoverRiseTick(object? sender, object args)
        {
            _hoverRiseTimer.Stop();

            if (_pointerInside)
            {
                RootLayer.Opacity = 1.0;
            }

        }

        private void OnHoverFallTick(object? sender, object args)
        {
            _hoverFallTimer.Stop();
            ApplyRestingOpacity();
        }
```

Call `ApplyRestingOpacity()` from `OnStateChanged` alongside `ApplyLayout` so the Settings slider and the flyout submenu take effect immediately.

Stop both timers in `CloseWidget` and `HideWidget`.

- [ ] **Step 4: Verify**

Run the app (x64), set opacity to 50% over a text document. Confirm the whole widget fades — **including the Win2D chart** — and still responds to drag, right-click and double-click. Hover it and confirm it rises to fully opaque after a beat and settles back on exit. Sweep the pointer quickly across a corner and confirm it does **not** flash. Set opacity to 100 and confirm hovering changes nothing. Check both light and dark themes and 4K at 200% scaling — DPI has bitten this project before.

- [ ] **Step 5: Commit**

```bash
git add NetworkMonitor/MiniGraphWindow.xaml.cs
git commit -m "Add mini graph opacity with hover to full opacity."
```

---

### Task 11: Double-click to drill in

**Files:**
- Modify: `NetworkMonitor/App.xaml.cs` (a `ShowMainWindow` helper)
- Modify: `NetworkMonitor/MainWindow.xaml.cs` (a method to select a Traffic tab)
- Modify: `NetworkMonitor/Views/TrafficHostPage.xaml.cs` (select a tab by tag)
- Modify: `NetworkMonitor/MiniGraphWindow.xaml.cs` (fill in the four handlers)

**Interfaces:**
- Produces: `App.ShowMainWindow()`; `MainWindow.NavigateToTraffic(string tabTag)` where `tabTag` is `"Internet"`, `"Local"` or `"SpeedTest"`; `MainWindow.NavigateToUnapprovedDevices()`; `TrafficHostPage.SelectTab(string tabTag)`.

- [ ] **Step 1: Restore and focus the main window**

In `NetworkMonitor/App.xaml.cs`:

```csharp
        internal static void ShowMainWindow()
        {

            if (_mainWindowHwnd != IntPtr.Zero)
            {
                ShowWindow(_mainWindowHwnd, SwRestore);
                SetForegroundWindow(_mainWindowHwnd);
            }

        }
```

- [ ] **Step 2: Add the navigation entry points**

In `TrafficHostPage.xaml.cs`:

```csharp
        internal void SelectTab(string tabTag)
        {

            foreach (object item in TabBar.Items)
            {

                if (item is SelectorBarItem barItem && barItem.Tag?.ToString() == tabTag)
                {
                    TabBar.SelectedItem = barItem;

                    break;
                }

            }

        }
```

In `MainWindow.xaml.cs`, modelled on the existing `NavigateToHistory`:

```csharp
        public void NavigateToTraffic(string tabTag)
        {

            foreach (object item in NavView.MenuItems)
            {

                if (item is NavigationViewItem navigationItem && navigationItem.Tag?.ToString() == "traffic")
                {
                    NavView.SelectedItem = navigationItem;

                    break;
                }

            }

            if (ContentFrame.Content is not TrafficHostPage)
            {
                ContentFrame.Navigate(typeof(TrafficHostPage));
            }

            TrafficHostPage? host = ContentFrame.Content as TrafficHostPage;
            host?.SelectTab(tabTag);
        }

        public void NavigateToUnapprovedDevices()
        {

            foreach (object item in NavView.MenuItems)
            {

                if (item is NavigationViewItem navigationItem && navigationItem.Tag?.ToString() == "devices")
                {
                    NavView.SelectedItem = navigationItem;

                    break;
                }

            }

            if (ContentFrame.Content is not DevicesHostPage)
            {
                ContentFrame.Navigate(typeof(DevicesHostPage));
            }

            DevicesHostPage? host = ContentFrame.Content as DevicesHostPage;
            host?.SelectTab("Unapproved");
        }
```

`DevicesHostPage` needs the same helper. Its tabs are tagged `Devices`, `Approved`, `Unapproved` and `History`, so `"Unapproved"` above is correct. Add to `DevicesHostPage.xaml.cs`, mirroring the loop its existing `ShowDeviceHistory` already uses:

```csharp
        internal void SelectTab(string tabTag)
        {

            foreach (object item in TabBar.Items)
            {

                if (item is SelectorBarItem barItem && barItem.Tag?.ToString() == tabTag)
                {
                    TabBar.SelectedItem = barItem;

                    break;
                }

            }

        }
```

- [ ] **Step 3: Fill in the four handlers**

Replace the stubs in `MiniGraphWindow.xaml.cs`:

```csharp
        private void InternetSectionDoubleTapped(object sender, DoubleTappedRoutedEventArgs args)
        {
            ShowMainWindow("Internet");
        }

        private void LocalSectionDoubleTapped(object sender, DoubleTappedRoutedEventArgs args)
        {
            ShowMainWindow("Local");
        }

        private void SpeedLineDoubleTapped(object sender, DoubleTappedRoutedEventArgs args)
        {
            ShowMainWindow("SpeedTest");
        }

        private void DevicesLineDoubleTapped(object sender, DoubleTappedRoutedEventArgs args)
        {
            App.ShowMainWindow();
            MainWindow.Current?.NavigateToUnapprovedDevices();
        }

        private void ShowMainWindow(string? trafficTabTag)
        {
            App.ShowMainWindow();

            if (trafficTabTag is not null)
            {
                MainWindow.Current?.NavigateToTraffic(trafficTabTag);
            }

        }
```

- [ ] **Step 4: Verify**

Run the app (x64). Hide the main window to the tray, then double-click each part of the widget: the Internet chart opens Traffic → Internet, the Local chart opens Traffic → Local, the speed line opens Traffic → Speed Test, the devices line opens Devices → Unapproved. Confirm a single click-and-drag still moves the window and does not trigger navigation, and that "Open Network Monitor" on the flyout also restores the window.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "Add double-click drill-in from the mini graph."
```

---

### Task 12: End-to-end verification and documentation

**Files:**
- Modify: `CLAUDE.md` (Key Files table)
- Modify: `Documents/To Do.txt`
- Modify: `NetworkMonitor.slnx` (register this plan document)

- [ ] **Step 1: Run the full manual checklist from the spec**

- Run a large download and confirm the mini chart tracks the Internet tab's chart.
- Copy to the NAS and confirm the Local section moves while Internet stays flat.
- Toggle from all three entry points and confirm they stay in sync.
- Drag, resize, restart the app, confirm placement and size return.
- Switch Local off and confirm the Internet chart fills the freed space with the window keeping its size.
- Hide the app to the tray and confirm the widget keeps updating.
- Drop opacity to 50% over a text document and confirm the whole widget fades, chart included, and still responds to drag, right-click and double-click.
- Hover at 50% and confirm the rise, the settle, and no flash on a fast corner sweep.
- Check light and dark themes, and 4K at 200% scaling.
- Set the traffic interval to 60 seconds and confirm the mini chart draws a spread trace rather than a one-second spike followed by 59 zeros.
- Empty database: confirm the speed strip reads "No speed test yet" and the devices strip reads "✓ no unknown devices".
- Unplug or reconfigure a second monitor with the widget on it, restart, and confirm it lands bottom-right of the primary work area.

- [ ] **Step 2: Run the suite one last time**

Run: `dotnet test NetworkMonitor.Tests/NetworkMonitor.Tests.csproj -v q --nologo`
Expected: `Failed: 0, Passed: 294`.

- [ ] **Step 3: Update the Key Files table**

Add to the table in `CLAUDE.md`:

```markdown
| `NetworkMonitor.Core/Traffic/LiveRateBuffer.cs` | Fixed ring of one-second buckets behind the mini graph; zero-fills idle gaps, spreads a flush across its interval |
| `NetworkMonitor.Services/Traffic/LiveTrafficFeed.cs` | Always-on singleton feeding the mini graph from `Flushed` / `SpeedTestCompleted` / `ScanCompleted`; two DB reads at startup, none after |
| `NetworkMonitor/MiniGraphWindow.xaml` | Frameless always-on-top widget: Internet + Local charts, speed and unknown-device strips, hover-to-opaque |
```

- [ ] **Step 4: Close the To Do entries**

In `Documents/To Do.txt`, change the roadmap line `Floating mini graph` to `Done - Floating mini graph` and mark the two decided lines under the "Floating mini graph:" heading as done.

- [ ] **Step 5: Check the solution file is still in sync**

This plan was already registered in `NetworkMonitor.slnx` under `<Folder Name="/Documents/Superpowers/Plans/">` when it was written. Confirm it is still there, and that nothing else added under `Documents/` during implementation is missing from the slnx.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "Document the floating mini graph and close its to-do entries."
```

---

## Notes for the implementer

**No database delete is required on upgrade.** This feature adds no tables and no columns; the nine new settings are additive JSON fields that default sensibly when absent from an existing `settings.json`.

**Deviations from the spec, and why:**

1. **`AddInterval` alongside `Add`.** The spec's `Add(timestampUtc, download, upload)` charges a whole flush to one second. That is the bug commit 28be399 fixed on the Traffic page, and it would reappear in the widget at any traffic interval above one second. `AddInterval` reuses the already-tested `FlushSpread.Distribute`.
2. **Manual dragging instead of `SetRegionRects`.** A caption region swallows XAML pointer input, which would kill the double-click-to-drill-in the spec asks for on the same surface. Manual drag with a 4 px threshold keeps both. Snap-assist is lost; the spec lists edge snapping as out of scope anyway.
3. **A gradient scrim via theme dictionaries** rather than a brush computed from the window background, because XAML gradient stops take colours, not brushes.

**Click-through is deliberately not implemented.** See the Out of Scope section of the spec for the reasoning — a window that passes mouse input through receives none itself, which would kill dragging, the ✕ glyph, the right-click flyout and double-click-to-drill.
