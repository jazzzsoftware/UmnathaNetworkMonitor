# Chunk 3 — Traffic data pipeline & concurrency

Range `c07260c..b215581`. Ledger: `progress.md`.

**11 findings — 2 BUG · 1 RISK · 8 CLEANUP/PERF. All `open`.**

## What was verified as correct

The bucketing maths is the part most likely to be subtly wrong, and it is right. Each item below was established by tracing concrete timelines, not by reading intent.

**The interval boundary tiles exactly — no double-count, no destroyed data.** Trace with a 5s interval, flushes at t = 10.4s and t = 15.4s:

- *Flush 1*: `intervalStart = 5.4`, `end = 10.4`. `Advance(10)` runs with `_lastEpoch == -1` (`LiveRateBuffer.cs:125`), so it only seeds `_lastEpoch = 10` on a fresh zeroed array. `FlushSpread` sees overlaps `{5:0.6, 6..9:1.0, 10:0.4}` — bucket 10 gets 0.4s worth.
- *Flush 2*: `intervalStart = 10.4` (the previous end), `end = 15.4`. `Advance(15)` (`LiveRateBuffer.cs:56`) zeroes **11..15 only** — `first = _lastEpoch + 1 = 11` (`:134`) — so bucket 10 is untouched. Then `firstEpoch = 10` and the overlaps are `{10:0.6, 11..14:1.0, 15:0.4}`.
- Bucket 10 ends with 0.4s from flush 1 + 0.6s from flush 2 = **exactly one second of traffic**.

The intervals tile because each flush's start *is* the previous flush's end, and `Advance` never zeroes below `_lastEpoch + 1`. (A double-count here was alleged in the coordinator's first pass and **withdrawn** — see the ledger Log.)

- **`ChartPoint` argument order is right.** `LiveRateBuffer.cs:107` passes `(timestamp, upload, download)`; `ChartPoint.cs:3` is `record ChartPoint(DateTime BucketStart, long BytesUploaded, long BytesDownloaded)`. No silent up/down swap.
- **`FlushSpread.Distribute` is sound.** The `bucketStartsUtc.Count > 0` guard (`:24`) protects the `allocated[allocated.Length - 1]` fallback at `:54`. The remainder redistribution at `:76` preserves the total exactly, and `FlushSpreadTests.TotalIsAlwaysPreserved` sweeps 27 sub-second offsets to prove it. `largestIndex` keeps the *first* maximum (strict `>` at `:67`), so on a whole-second grid a full-overlap interior bucket takes the ≤5-byte remainder — immaterial.
- **The widget's filters actually match the tabs.** Verified, not assumed: `InternetViewModel.cs:32,46,59,71` all carry `AND ProcessName <> 'System'`, and `:331,432` repeat it on the live path — matching `LiveTrafficFeed.cs:172`. `LocalViewModel.cs:427,515` keep only `FlowCategory.Data` — matching `LiveTrafficFeed.cs:193`. The explanatory comments are accurate.
- **Startup ordering is safe.** `App.xaml.cs:219` runs `EnsureCreatedAsync` + the WAL pragma, and only then `await AppHost.StartAsync()` at `:237`. `SeedAsync` cannot hit a missing database.
- **Concurrency hygiene is deliberate.** Every handler is wrapped so a widget fault cannot kill the flush loop (`LiveTrafficFeed.cs:162,226,256`); `Updated?.Invoke` is raised **outside** `_gate` (`:214`); the view model immediately marshals to the dispatcher (`MiniGraphViewModel.cs:101`) so no UI work runs on the flush thread. `MiniGraphViewModel` is only ever constructed on the UI thread, so `DispatcherQueue.GetForCurrentThread()` cannot return null.
- **Wall-clock intervals beat assumed cadence.** `TrafficTracker.ExecuteAsync` is `Delay(interval) → flush → purge`, so the true gap drifts above `TrafficIntervalSeconds`. Measuring from the previous flush rather than trusting the setting is the right call.
- **`Snapshot` is not a per-frame allocation.** `MiniGraphViewModel.Refresh` (`:115,120`) runs only from `Updated` (per flush / speed test / scan) and `MiniGraphState.Changed`. The per-frame path is `TrafficAreaChart.OnRendering → Invalidate` (`:441`), which redraws arrays it already holds. 300 `ChartPoint`s every ~5s is nothing. **(But see C3-2 for what the per-frame path *does* cost.)**

---

## C3-1 `[BUG]` — a backward clock step corrupts the live buffers for up to 5 minutes, silently

`NetworkMonitor.Core/Traffic/LiveRateBuffer.cs:132` · `NetworkMonitor.Services/Traffic/LiveTrafficFeed.cs:202-206`

`Advance` acts only when `epoch > _lastEpoch`; a smaller epoch is silently ignored. Trace a 20-second backward step (NTP correction after RTC drift, a VM resume, a user setting the clock) with `_lastEpoch = 1000`:

1. The next flush reads `now = 985` while `_lastFlushUtc = 1000`, so `intervalEndUtc <= intervalStartUtc` and `AddInterval` falls through to `Add(985, …)` (`:50`). Bucket 985 already holds real pre-jump traffic, and `Accumulate` **adds on top of it** (`:159`) because `IsHeld(985)` is true.
2. Every subsequent flush until wall-clock passes 1000 calls `Advance(≤1000)`, which does nothing — so **no bucket is ever zeroed** and 15–20 seconds of the trace accumulate old + new traffic indefinitely.
3. Meanwhile `Snapshot` computes `endEpoch = max(nowEpoch, _lastEpoch) = 1000` (`:92`), so the chart's right edge sits 15s in the future showing stale pre-jump buckets that will never be refreshed.

**Why it matters.** Silently inflated rates and a frozen right edge on an always-visible widget, with nothing in the log. This is the class of defect that gets reported as "the graph is wrong sometimes" and takes a week to find.

Large backward steps are **safe** — `IsHeld` drops them, as `LiveRateBufferTests.SamplesOlderThanTheWindowAreDropped` shows. It is precisely the sub-window step that corrupts.

**Fix.** Treat a backward step as a discontinuity. In `Advance`, add an `else if (epoch < _lastEpoch)` branch that calls `Clear()` and re-seeds; in `OnFlushed`, if `nowUtc < _lastFlushUtc`, reset `_lastFlushUtc = nowUtc` and skip the interval rather than feeding an inverted one to `AddInterval`.

**Status:** `open`

---

## C3-2 `[PERF]` — the widget ignores `Settings.ChartSmoothScrolling`, so its 60Hz redraw cannot be turned off

`NetworkMonitor/Views/Controls/MiniTrafficSection.xaml:71-73`

`InternetPage.xaml.cs:68` and `LocalPage.xaml.cs:73` both apply `AreaChart.SmoothScrolling = _settings.ChartSmoothScrolling`. `MiniTrafficSection` never sets it, so `TrafficAreaChart`'s DependencyProperty default of `true` (`TrafficAreaChart.xaml.cs:84`) wins.

Consequence: `OnRendering` invalidates **every frame** (`:444`), and each frame `ChartCanvasDraw` builds **four** `CanvasGeometry` paths of ~300 cubic Béziers each (two series × two sections, `DrawArea` lines 370-402) plus a 300-iteration `EaseValues` pass per section — at display refresh rate, permanently, on a widget the user leaves open all day.

Two compounding factors:

- This is **5× the geometry** the full-page charts carry (300 one-second points vs 60 five-second ones).
- The mini sections are typically 200–600px wide, so most of those 300 segments are **sub-pixel**.

The setting exists precisely to let a user stop this, and the one chart that runs 24/7 is the one that ignores it. Noticeable on battery.

**Fix.** Bind `SmoothScrolling` through `MiniTrafficSection` to `Settings.ChartSmoothScrolling` the way the two pages do. Separately, consider decimating the snapshot to roughly one point per 2px (max-of-window) before building the geometry.

**Status:** `open`

---

## C3-3 `[CLEANUP]` — `_lastFlushUtc` is read and written outside `_gate`

`NetworkMonitor.Services/Traffic/LiveTrafficFeed.cs:203-206`

No real overlap could be constructed: `Flushed` is raised from the sequential `ExecuteAsync` loop, and an abandoned flush (`Watchdog.RunAsync` timeout, `Watchdog.cs:22`) has its token cancelled, so `WriteFlushAsync` throws before line 159 is reached — the only exception being a fully empty flush, which has no awaits and cannot time out.

So it is single-threaded **in practice**, but nothing in the type enforces it, and the next person to add a second `Flushed` raiser gets a silently wrong interval.

**Fix.** Move both lines inside the existing `lock (_gate)`.

**Status:** `open`

---

## C3-4 `[RISK]` — a gap longer than the window compresses all its bytes into the visible 5 minutes

`NetworkMonitor.Core/Traffic/LiveRateBuffer.cs:59-64` · `FlushSpread.cs:63`

`AddInterval` clamps `firstEpoch` to `oldestHeld`, correctly capping the loop at 300 buckets. But `FlushSpread` then normalises by `totalOverlap`, which is now ~300s instead of the interval's true length — so e.g. 3 hours of bytes are spread over 5 minutes at **36× the real rate**, and `RoundAxisMax` rescales the whole chart to it.

The trigger is narrow (the setting caps `TrafficIntervalSeconds` at 60 — `SettingsPage.xaml:106` — and a sleeping machine accrues no bytes), which is why this is RISK rather than BUG.

**Fix.** Scale `totalBytes` by `retainedSeconds / intervalSeconds` before distributing, discarding the share that belongs to seconds no longer held.

**Status:** `open`

---

## C3-5 `[CLEANUP]` — the live edge reads systematically low

`NetworkMonitor.Core/Traffic/LiveRateBuffer.cs:85-113` · `NetworkMonitor/Views/Controls/TrafficAreaChart.xaml.cs:352-354`

The newest bucket only ever holds the fraction of a second the flush actually covered (0.4s in the trace above), and `BuildPoints` extends that partial value flat to `now` as the lead point. During a sustained transfer the rightmost slice of the trace therefore sits at ~50% of the true rate on average until the next flush tops the bucket up.

Small — under 2% of the chart width — but it is a permanent dip at the exact point the eye goes to.

**Fix.** Have `Snapshot` end at the last *complete* second, so the lead point extends a fully-covered bucket.

**Status:** `open`

---

## C3-6 `[CLEANUP]` — `FlushSpread` silently discards a negative total, while `LiveRateBuffer` accepts one

`NetworkMonitor.Core/Traffic/FlushSpread.cs:24` · `LiveRateBuffer.cs:159`

`Distribute` requires `totalBytes > 0`, so a negative input returns all zeros — quietly violating the "sums to exactly `totalBytes`" contract in its own header comment. `LiveRateBuffer.Accumulate` happily adds a negative. Two entry points, two behaviours, neither tested. Not reachable today (ETW counters are accumulated unsigned), but a counter reset in the collector would take different routes depending on which path ran.

**Fix.** Either assert, or document the intent and make both agree.

**Status:** `open`

---

## C3-7 `[CLEANUP]` — the eased trace never reaches the labelled peak

`NetworkMonitor/Views/Controls/TrafficAreaChart.xaml.cs:30, 766, 842-846`

`EaseTimeConstantSeconds = 2.5` against one-second buckets means newly arrived buckets converge to ~63% of their value over 2.5s, while `PeakText` is computed from the raw `maxValue`. The header can read "Peak 560 Mb/s" while the drawn curve visibly falls short of the top gridline. Cosmetic, but it is a number-versus-picture mismatch on the same control.

**Status:** `open`

---

## C3-8 `[CLEANUP]` — torn read of the unapproved-device count

`NetworkMonitor/ViewModels/MiniGraphViewModel.cs:125-126`

`_feed.UnapprovedDeviceCount` is called twice, taking `_gate` separately each time, so the text and the warning flag can disagree if a scan lands between them.

**Fix.** Read once into a local.

**Status:** `open`

---

## C3-9 `[CLEANUP]` — `RefreshUnapprovedCountAsync` is unbounded and unstoppable

`NetworkMonitor.Services/Traffic/LiveTrafficFeed.cs:250, 259`

Line 250 fires and forgets; line 259 passes `CancellationToken.None`. A scan completing during shutdown leaves a DB read racing the context factory's disposal — caught and logged at `:270`, but it is noise in the log for a condition that is expected.

**Fix.** Pass the hosted-service token via a linked CTS.

**Status:** `open`

---

## C3-10 `[PERF]` — `SeedAsync` runs its two queries on the UI thread

`NetworkMonitor/App.xaml.cs:237` · `NetworkMonitor.Services/Traffic/LiveTrafficFeed.cs:77`

`StartAsync` is awaited from `OnLaunched` (UI thread), and `LiveTrafficFeed.StartAsync` awaits `SeedAsync` with no `ConfigureAwait(false)`. Microsoft.Data.Sqlite is synchronous underneath, so both queries execute on the UI thread and block every later hosted service plus `MainWindow` creation.

Both tables are small so this is milliseconds today — but `CountUnapprovedAsync`'s predicate (`:125`) is a `Devices` table scan that grows with the device list.

**Fix.** Wrap in `Task.Run`, or don't await the seed in `StartAsync`.

**Status:** `open`

---

## C3-11 `[CLEANUP]` — `LiveRateBuffer` has no internal synchronisation and doesn't say so

`NetworkMonitor.Core/Traffic/LiveRateBuffer.cs` (whole type)

All three mutators plus `Snapshot` touch `_lastEpoch` and the arrays with no lock; correctness depends entirely on every caller holding `LiveTrafficFeed._gate`. It is a **public** type in Core with nothing recording that contract.

**Fix.** Either lock internally, or add a one-line remark to the class comment.

**Status:** `open`

---

## Files reviewed

- `NetworkMonitor.Services/Traffic/LiveTrafficFeed.cs`, `TrafficTracker.cs`
- `NetworkMonitor.Core/Traffic/LiveRateBuffer.cs`, `FlushSpread.cs`, `LocalFlowClassifier.cs`
- `NetworkMonitor.Core/Charting/AxisScale.cs`
- `NetworkMonitor.Models/Charting/ChartPoint.cs`, `Formatting/MiniGraphFormatter.cs`
- `NetworkMonitor/ViewModels/MiniGraphViewModel.cs`, `InternetViewModel.cs`, `LocalViewModel.cs`
- `NetworkMonitor/Views/Controls/MiniTrafficSection.xaml`, `MiniTrafficSection.xaml.cs`, `TrafficAreaChart.xaml.cs`
- `NetworkMonitor/Views/InternetPage.xaml.cs`, `LocalPage.xaml.cs`
- `NetworkMonitor/App.xaml.cs` (startup ordering)
- `NetworkMonitor.Tests/LiveRateBufferTests.cs`, `FlushSpreadTests.cs`, `AxisScaleTests.cs`

## User findings

_(to be filled in at co-review — assign `U3-n` IDs)_
