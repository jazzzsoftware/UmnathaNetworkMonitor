# Chunk 4 — Local traffic UI

Reviewed 2026-07-27. Fix phase completed 2026-07-27 — see `progress.md`.

11 findings: **3 BUG · 2 RISK · 6 PERF/CLEANUP**. The in-place row reconcile, the pause/live badge state machine and the lens switching all hold up. The problems cluster around the **live refresh path**: on the default range it doesn't do what it was built to do, and where it does run it can't add rows.

---

## C4-1 [PERF] On the default 5-minute range the incremental path never runs — status: fixed

`NetworkMonitor/ViewModels/LocalViewModel.cs:198-216`, `NetworkMonitor.Core/Traffic/TrafficWindow.cs:5-11`, `NetworkMonitor/ViewModels/InternetViewModel.cs:590-601`

`ApplyLiveFlushAsync` takes the cheap in-memory path only when the aligned cutoff hasn't moved:

```csharp
long cutoffEpoch = TrafficWindow.AlignedCutoffEpoch(nowEpoch, _windowBucketSeconds, _windowChartPoints.Count);

if (cutoffEpoch != _windowCutoffEpoch) { await LoadAsync(); }   // full reload
else                                   { ApplyFlushToWindow(deltas); }
```

For the 5-minute range `BucketSizeFor` returns `TrafficIntervalSeconds` — **1 second** by default — and `AlignedCutoffEpoch` is `(now/1 + 1) * 1 - 300`, which changes **every second**. So every flush takes the full-reload branch: a new `DbContext`, a full `Devices` read, two SQL aggregations over `LocalTrafficEntries`, a complete regroup and two `ObservableCollection` replacements — once a second, for as long as the tab is open.

The entire `_windowFlows` / `ApplyFlushToWindow` mechanism only ever runs on the 1-hour and 6-hour ranges, which is the opposite of the intent.

**Proposed fix:** when the cutoff has advanced by exactly one bucket, shift the window in memory (drop the first chart point, append an empty one, drop the flows that fell out) instead of reloading; reserve the full reload for larger jumps and explicit refreshes.

**Applied to both tabs.** The shift needs per-bucket attribution to know what to evict, so `LoadFlowBucketsAsync` (Local) and `LoadAppBucketsAsync` (Internet) now group by bucket index as well, and `ShiftWindow` subtracts the evicted bucket from the running totals. `InternetViewModel` was outside the agreed scope of this review and was fixed as a follow-up at the user's request — along with C4-3 and the C4-10 connection cleanup, which were identical there.

---

## C4-2 [BUG] A live refresh can never add a row — status: fixed

`NetworkMonitor/ViewModels/LocalViewModel.cs:304,409-465`

`RebuildGroups` calls `ApplyGroups(groups, reorder: false)`, and in that mode the reconcile is asymmetric:

```csharp
if (existingByIdentity.TryGetValue(identity, out LocalTrafficGroupRow? current)) { current.UpdateFrom(incomingRow); … }
else if (reorder) { _groups.Insert(index, incomingRow); }      // ← never runs when reorder == false
```

Rows that disappear are removed (that loop is unconditional), rows that persist are updated — but a **new** app or device is silently dropped. On the 1-hour range that's up to a minute of invisibility; on the 6-hour range up to five minutes (per C4-1 those are exactly the ranges where this path runs).

Its bytes are meanwhile counted in the chart *and* in the status line, which is computed from the full incoming set (`:288-302`) — so the header reads "7 apps · 4.2 GB total" above six rows.

`reorder: false` exists to stop the grid re-sorting under the user's drill-down (commit `43c3232`), which is right — but "don't move existing rows" shouldn't mean "don't show new ones".

**Proposed fix:** in the `reorder == false` branch, append unseen rows at the end (before the background row) rather than skipping them; existing row positions stay put.

---

## C4-3 [RISK] `LoadAsync` has no re-entrancy guard — status: fixed

`NetworkMonitor/ViewModels/LocalViewModel.cs:144-191`

Nothing prevents overlapping loads, and per C4-1 the live path calls `LoadAsync()` **every second** on the default range. If `BuildDataAsync` takes longer than the flush interval — likely once `LocalTrafficEntries` is large, and it will be (chunk 2, C2-5) — two or more runs overlap and complete in arbitrary order. The loser's results are applied last: `ChartPoints`, `SeedWindowState` (`_windowCutoffEpoch`, `_windowBucketSeconds`, `_windowFlows`) and `Groups` are all overwritten from a stale snapshot, and the incremental window is then seeded from data that doesn't match the displayed cutoff.

`IsLoading` is set and cleared independently by each caller too, so an overlapping pair can leave the spinner up (or take it down early).

**Proposed fix:** a `SemaphoreSlim(1,1)` around the body with live reloads skipping rather than queueing (`Wait(0)`), or a monotonically-increasing request id whose result is discarded if superseded.

---

## C4-4 [RISK] Every `LocalPage` instance is rooted for the life of the window — status: fixed

`NetworkMonitor/Views/LocalPage.xaml.cs:53-56,94-102`

```csharp
if (MainWindow.Current is not null)
{
    MainWindow.Current.Closed += OnMainWindowClosed;   // never detached
}
```

`OnPageUnloaded` detaches the `TrafficTracker.Flushed` handler but not this one. `Frame` doesn't cache pages (`NavigationCacheMode` is `Disabled` by default), so a fresh `LocalPage` is constructed every time the user navigates to the Traffic host — and each one stays reachable from `MainWindow.Closed`, holding its brushes, its DataGrid and its whole visual tree. Navigate Traffic → Devices → Traffic a few dozen times and that's a few dozen live pages.

`InternetPage.xaml.cs:82-93` has the same shape.

**Proposed fix:** detach in `OnPageUnloaded` alongside the tracker handler — or drop the `Closed` subscription entirely, since `Unloaded` already fires when the window closes.

---

## C4-5 [BUG] The chart's time-axis labels go stale — status: fixed

`NetworkMonitor/Views/LocalPage.xaml.cs:82,294,443-454`

`UpdateTimeLabels` reads `DateTime.Now` and is called from exactly two places: `OnNavigatedTo` and the range-change handler. The live window scrolls continuously, but the four axis labels never move. Leave the 5-minute view open for an hour and the axis still describes the window as it was an hour ago — the chart and its own axis disagree, which is worse than having no labels.

**Proposed fix:** refresh the labels from the flush handler, next to `AreaChart.MarkLiveUpdate()`.

---

## C4-6 [BUG] Live rate chips freeze on a fully idle interval — status: fixed

`NetworkMonitor.Services/Traffic/TrafficTracker.cs:120-141`, `NetworkMonitor/ViewModels/LocalViewModel.cs:334-391`

`Flushed` is raised only inside `if (entries.Count > 0 || localEntries.Count > 0)`. The rate windows are aged **only** when a flush arrives (`UpdateRateWindows` enqueues a zero for every key absent from the flush), so an interval with no WAN *and* no LAN bytes ages nothing: `_rateWindows` keeps its last five non-zero samples and the chips keep advertising a rate that has stopped. It clears only when the user leaves live mode (`SetRatesActive(false)`).

In practice some WAN traffic usually exists, which is why this survives — but "the app is quiet" is exactly when the stale chip is most visible.

**Proposed fix:** raise `Flushed` unconditionally with empty lists (it costs nothing and both view models already handle empty deltas correctly).

---

## C4-7 [PERF] The device-name map is re-read from the database on every tick — status: fixed

`NetworkMonitor/ViewModels/LocalViewModel.cs:491,536-552`

`BuildNameMapAsync` runs `db.Devices.AsNoTracking().ToListAsync()` inside every `BuildDataAsync` — once a second on the default range (C4-1) — to build a dictionary that changes only when a scan completes, i.e. every 5 minutes by default.

**Proposed fix:** cache the map in the view model and invalidate it on `ScanWorker.ScanCompleted` (the view model can take the worker, as other view models already do).

---

## C4-8 [PERF] The chart collection is replaced wholesale on every tick — status: won't-fix (finding corrected)

`NetworkMonitor/ViewModels/LocalViewModel.cs:161,273`

`ChartPoints = new ObservableCollection<ChartPoint>(…)` raises a reset for a 300-point series every second, even on the incremental path where exactly **one** point changed. 2026-06-23's C6-6 moved the device grids to in-place reconcile for precisely this reason; the chart still resets.

**Proposed fix:** on the incremental path mutate the last element of the existing collection; keep the wholesale replacement for genuine reloads.

**Corrected on verification — the proposed fix would have broken the chart.** `TrafficAreaChart.ChartPoints` is a `DependencyProperty` of `IReadOnlyList<ChartPoint>` whose only redraw trigger is `OnChartPointsChanged` (`TrafficAreaChart.xaml.cs:61-66,128-132`); the control never subscribes to `INotifyCollectionChanged`. Mutating the existing collection in place would leave the canvas showing stale data with no error. The replacement is load-bearing, so it stays. The cost it represents is addressed instead by C4-1, which cuts how often a rebuild happens at all.

---

## C4-9 [PERF] `_windowFlows` keeps every flow in the window, keyed by ephemeral port — status: fixed

`NetworkMonitor/ViewModels/LocalViewModel.cs:27,237-306`

The key is `(ProcessName, RemoteIp, Protocol, RemotePort)` — the same ephemeral-port problem as C2-3, so on the 6-hour range this dictionary can hold every distinct inbound connection of the last six hours. `RebuildGroups` then materialises the **entire** dictionary into a fresh `List<LocalFlowMinute>` and re-runs `LocalTrafficGrouper.Build` over all of it, once per flush.

Nothing ever evicts flows that have aged out of the window either — entries only disappear on the next full reload.

**Proposed fix:** age flows out with the window, and/or key the incremental dictionary on the fields the grouper actually distinguishes.

---

## C4-10 [CLEANUP] SQL is assembled by string interpolation and the connection is left open — status: fixed

`NetworkMonitor/ViewModels/LocalViewModel.cs:580,586-593,680,686-696`

`sourceTable`, `whereClause`, `epochExpr` and `selectionColumn` are interpolated into the command text. Every one is an internal constant today, so there is no injection *now*; the risk is that the pattern normalises interpolation in a query that also takes user-derived values. Both methods also call `OpenConnectionAsync` without a matching close (same as C2-8) — harmless because the context is disposed, but unbalanced.

**Proposed fix:** hoist the two SQL shapes into `const string` templates chosen by an `if`, so nothing user-reachable can ever reach the interpolation; drop the explicit opens.

---

## C4-11 [CLEANUP] Minor items — status: fixed

- `MinimumSpinnerMs = 500` (`LocalViewModel.cs:18,170-179`) delays *every* `showLoading` reload to at least half a second, including the one triggered by clicking a grid row. Deliberate anti-flicker, but it makes selection feel slower than doing nothing.
- `UpdateRateWindows` calls `window.Sum()` / `window.Average()` (LINQ over a `Queue<long>`) per group per tick — trivial, but a running total would be simpler than both.
- `RebuildGroups` duplicates the status-text construction from `BuildDataAsync` (`:288-302` vs `:512-529`) with one difference: the live copy always says "total" and never the "at &lt;time&gt;" scope. Extract one helper.

---

## Files reviewed

- `NetworkMonitor/ViewModels/LocalViewModel.cs`
- `NetworkMonitor/ViewModels/LocalLoadResult.cs`
- `NetworkMonitor/Views/LocalPage.xaml.cs`
- `NetworkMonitor/Views/LocalPage.xaml`
- `NetworkMonitor/Views/TrafficHostPage.xaml` / `.xaml.cs` (Local tab hosting)
- `NetworkMonitor/Views/InternetPage.xaml.cs` (only the shared `Flushed` / `Closed` wiring — C4-4)
- `NetworkMonitor/ViewModels/InternetViewModel.cs` (`BucketSizeFor` only — shared with Local)

## User findings

_(to be filled in during co-review — each becomes `U4-<n>`)_
