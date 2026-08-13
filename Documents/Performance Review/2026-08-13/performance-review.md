# Performance Review — 2026-08-13

A review of the codebase for slow or inefficient operations, covering five areas: the always-on-top widget and its charts, the traffic-capture (ETW) pipeline, data access (EF Core / SQLite), the network-scanning pipeline, and the device/history view models.

Line numbers reference the files as they were at the time of review. Findings are ranked by severity and hot-path frequency. **No High-severity issue is a correctness bug** — the app works; these are efficiency opportunities.

> **Schema note:** unlike the 2026-07-01 review, this one requires **no schema change and no migration**. Every finding below is runtime, rendering or serialisation work. The index set added in July still covers every query shape found this round — see *Areas checked and found efficient*.

## What changed since 2026-07-01

The previous review's 24 findings were re-checked against the current tree. All the implemented ones are still in place: the rollup upserts still share one transaction (`TrafficTracker.WriteFlushAsync:168-220`), the device load is still bounded (`DeviceTracker:26-30`), the digest preview still renders at display DPI rather than 288 (`DigestReportView:131-146`), reverse-DNS is still semaphore-bound at 20 (`NetworkScanner:13,25`), the PID info cache still prunes (`TrafficTracker:288-316`), the reconciler still rebuilds once on a large reorder (`CollectionReconciler:115-125`), and hidden tabs no longer reload on scan (`DeviceHistoryViewModel:204-210`).

What has changed is the **workload**. The mini graph widget, the Local tab and the live rate badges all landed after that review, and the widget in particular introduced a chart that is five times denser than any chart the July review looked at and that runs all day over the top of whatever the user is doing. That is where most of this review's weight sits.

---

## Top fixes (highest value first)

1. **Cut the widget's per-frame geometry.** ~4,800 cubic-bezier segments rebuilt per frame, continuously, for detail finer than one pixel. (Finding H1)
2. **Stop replacing the Internet app grid's whole collection every 5 seconds.** (Finding H2)
3. **Stop materialising one duplicate `Device` per history event.** (Finding H3)
4. **Cache the `JsonSerializerOptions` in `Settings.Save`** — a fresh reflection cache per save, and saves fire on a 400 ms debounce while the widget is dragged. (Finding M1)
5. **Unhook `CompositionTarget.Rendering` when a chart is not live**, rather than short-circuiting inside the handler. (Finding M2)

---

## High severity

### H1 — The widget's charts rebuild ~4,800 bezier segments per frame, at display refresh rate, all day

- **`Views/Controls/TrafficAreaChart.xaml.cs:469-477`** (`OnRendering` → `Invalidate`), **`:375-418`** (`DrawArea`), **`:567-571`** (both series drawn per frame)
- The widget holds **300 one-second points** (`LiveTrafficFeed.WindowSeconds = 300`, `LiveRateBuffer.Snapshot:114-142`) against the full-page charts' 60. Per frame, per visible section, `DrawArea` runs twice (download + upload), and **each call builds the bezier chain twice** — once into a `CanvasPathBuilder` for the filled area (`:385-401`) and again into a second builder for the stroked line (`:403-417`), from the same points.
- With both sections shown that is **8 `CanvasPathBuilder` + 8 `CanvasGeometry` + ~4,800 `AddCubicBezier` interop calls per frame**, created and torn down every frame. At 60 Hz that is ~288,000 native calls per second; on a 144 Hz display, ~690,000. Unlike the full-page charts this runs whenever the widget is visible — which is its whole purpose, so in practice all day.
- **Most of that detail is invisible.** The horizontal strip's Internet cell is **170 DIP wide** (`Core/Widget/HorizontalStripMetrics.cs:14`). Drawing 300 points into 170 px is 1.8 points per pixel; the vertical widget at its 240 DIP minimum is not much better.
- The July review (H3) deliberately left the geometry rebuild alone, reasoning that over a 5-minute window the per-frame scroll is sub-pixel so the rebuild is not a smoothness necessity. That reasoning now argues the other way: **if the frame-to-frame movement is sub-pixel, rebuilding the geometry every frame produces no visible change at all.** What was a fair trade for a 60-point chart on a page the user navigates away from is a different trade for a 300-point chart × 2 that sits above every other window.
- **Fix**, in increasing order of effort — any one is a large cut, all three together roughly an order of magnitude:
  - **(a)** Decimate to the drawable resolution: reduce to ~1 point per horizontal pixel (max-per-column, so spikes survive) before building geometry. On the strip that is 300 → ~170, on a small vertical widget ~240.
  - **(b)** Build the bezier chain once per series and reuse it for both the fill and the stroke — `CanvasGeometry` can be filled and drawn from the same path; only the closing lines back to the baseline differ.
  - **(c)** Throttle the invalidate: skip the frame when the x-shift since the last draw is under ~0.5 px *and* the ease has converged (`_displayed* ≈ _download/_upload`, `_displayMax ≈ _targetMax`). The perceived smoothness comes from the post-flush ease, not from the scroll.
- `SmoothScrolling` already lets the user turn the whole thing off, but that is a workaround for the cost, not a fix for it — the default path is the one that needs to be cheap.

### H2 — The Internet app grid's entire collection is replaced on every flush (5 s)

- **`ViewModels/InternetViewModel.cs:401`** (`Apps = new ObservableCollection<...>(displayRows)`) in `RebuildAppRows` **`:375-403`**, called from `ApplyLiveFlushAsync` **`:258`**; same pattern on load at **`:187`**
- Assigning a **new collection instance** is heavier than a `Reset`: the `DataGrid` drops its `ItemsSource` binding target and re-realizes every row, re-running row templates and converters, every five seconds for as long as the Internet tab is open. `InternetPage.SyncGridSelection` (**`Views/InternetPage.xaml.cs:264-281`**) then re-applies the selection immediately afterwards, so a selection round-trip rides along with it.
- **This is exactly what the Local tab already avoids.** `LocalViewModel.ApplyGroups` (**`:580-642`**) reconciles in place — remove missing, `UpdateFrom` matched, insert new — and never touches `Groups` as a reference.
- **The root cause is the row type.** `LocalTrafficGroupRow` is an `ObservableObject` with `UpdateFrom`; `InternetTrafficAppRow` (**`Models/Traffic/InternetTrafficAppRow.cs`**) is an immutable `record` with no change notification, so replacing the collection is the only way its values can reach the grid.
- **Fix:** give `InternetTrafficAppRow` the shape `LocalTrafficGroupRow` already has — `ObservableObject`, `SetProperty`-backed byte/rate properties, an `UpdateFrom` — then reconcile `Apps` in place (`CollectionReconciler.SyncOrdered` keyed on `ProcessName`, with the All-Apps row pinned first). The reconciler's large-reorder guard (`:115-125`) already handles the case where the byte ordering churns.

### H3 — Device history materialises one duplicate `Device` object per event row

- **`ViewModels/DeviceHistoryViewModel.cs:97`** — `db.DeviceEvents.AsNoTracking().Include(deviceEvent => deviceEvent.Device)`
- `AsNoTracking()` has **no identity resolution**. With an `Include`, EF materialises a **separate `Device` instance for every `DeviceEvent` row**, rather than one shared instance per device. A month of events for a household that flaps a few phones on and off is easily thousands of rows, so a load allocates thousands of `Device` objects where ~50 distinct ones exist — and each carries every string column (hostname, vendor, notes, MAC, IP).
- **Frequency:** the initial tab load, every scan while the History tab is active (`:204-214`), and any settings change to `HistoryPurgeDays`.
- **Fix:** either `AsNoTrackingWithIdentityResolution()` (one line, keeps the object graph the grid and the sort comparators already use), or — better — project to a flat row type carrying only the seven columns the grid and `ApplySorting` actually read (`Timestamp`, `EventType`, and the device's `Type`/`DisplayName`/`IpAddress`/`MacAddress`/`Vendor`). The projection also removes the duplicate-string cost, not just the duplicate-object cost.

---

## Medium severity

### M1 — `Settings.Save` builds a fresh `JsonSerializerOptions` on every call

- **`Services/Data/Settings.cs:352-365`**
- Every `JsonSerializerOptions` instance owns its own converter and type-metadata cache. Constructing a new one per save means the serializer redoes the reflection warm-up for the whole `Settings` type on **every single save** instead of reusing the cache it built last time, and each throwaway cache is garbage.
- **Frequency:** 13 call sites, including the widget's **400 ms placement debounce** (`MiniGraphState.SavePlacement`/`SaveStripPlacement:82-100` ← `MiniGraphWindow.OnSavePlacementTimerTick:1011`). A three-second drag writes ~7 times; every section toggle, opacity step, orientation flip, chart-range change and palette change writes once (`MiniGraphState.Apply:113-121`, `ChartPaletteService:88,139`, `InternetViewModel:106`, `LocalViewModel:137,155`).
- **Fix:** one `private static readonly JsonSerializerOptions` beside `_saveLock`. Note the settings *load* path (`App.xaml.cs:99`) uses the default options and so is already cached — this is the write side only.

### M2 — `CompositionTarget.Rendering` stays hooked while a chart is not live

- **`Views/Controls/TrafficAreaChart.xaml.cs:420-436`** (hook on `Loaded`), **`:469-477`** (`OnRendering` short-circuits on `_isLive`), **`:438-451`** (unhook only on `Unloaded`)
- Hiding the widget leaves its XAML tree loaded by design (`MiniGraphWindow:186-191`), and the Internet/Local pages stay loaded in History mode. In all those states the handler is still called on every compositor frame to evaluate one boolean. That is trivial CPU but it is not free: a live `Rendering` subscription keeps the UI thread waking at refresh rate, which is exactly the wake-up a hidden widget or a backgrounded app should not be paying for on battery.
- **Fix:** add/remove the subscription in `OnIsLiveChanged` (and keep the `Unloaded` removal as the backstop), so a hidden widget or a paused chart genuinely idles.

### M3 — History sort boxes every key and compares through `Comparer<object>`

- **`ViewModels/DeviceHistoryViewModel.cs:160-176`** (`ApplySorting`)
- The key selector is typed `Func<DeviceEvent, object?>`, so sorting by `Timestamp` boxes a `DateTime` per event and sorting by `EventType`/`Type` boxes an `int` per event; `OrderBy` then compares those boxes through `Comparer<object>.Default`, which is an interface dispatch and a type check per comparison rather than an inlined `DateTime` compare.
- **Frequency:** every `ApplyFilter` — i.e. every scan while the tab is active, every column-header click, and every debounced keystroke — over the full 30-day event list.
- **Fix:** switch on `_sortProperty` to select a typed `IOrderedEnumerable` (`OrderBy(e => e.Timestamp)` etc.) rather than a single `object?`-returning lambda. Compounds with H3: fewer rows and flatter rows make the sort cheaper again.

### M4 — History search lowercases up to four fields per event on every filter pass

- **`ViewModels/DeviceHistoryViewModel.cs:141-153`**
- `ToLowerInvariant()` allocates a new string per field per event, discarded immediately. Over a large event list that is tens of thousands of throwaway strings per search pass. The 200 ms debounce means this runs once per typing pause rather than per character, so it is not per-keystroke — but it is per pause, and it is the same allocation pattern M8 of the July review removed from the device lists without reaching this one.
- **Fix:** `Contains(query, StringComparison.OrdinalIgnoreCase)` — no allocation at all, and it removes the need to lowercase `SearchText` too. (A precomputed search blob is the alternative, but with a projected row type from H3 the ordinal-comparison route is simpler.)

### M5 — Live snapshots and chart arrays are reallocated wholesale every flush

- **`Core/Traffic/LiveRateBuffer.cs:114-142`** (`Snapshot` allocates a fresh `List<ChartPoint>` of 300 **records**), called twice per flush from **`LiveTrafficFeed:102-124`**; **`TrafficAreaChart.ApplyPoints:772-813`** allocates five fresh `double[300]` per section per flush
- Per flush with the widget open: ~600 `ChartPoint` records + 2 lists + 10 `double[300]` (~24 KB), all promptly garbage. At the 5 s default that is steady low-rate churn rather than a spike — Gen 0 work, not a leak — but it is pure repetition of identical-shaped buffers.
- **Fix:** have `Snapshot` fill a caller-supplied buffer (or expose the ring as spans), and reuse the `double[]` in `ApplyPoints` when `count` is unchanged, exactly as `BuildPoints:325-373` already does for its `Vector2[]`.

### M6 — `LocalViewModel.ApplyGroups` allocates identity strings per row and scans O(n²) to place them

- **`ViewModels/LocalViewModel.cs:580-642`**, `GroupIdentity` **`:668-673`**
- `GroupIdentity` builds an interpolated string per row, and it is called three times per row per flush (once building `incomingIdentities`, once per existing row for the map, once per incoming row for the lookup) plus once per row in the removal scan. The reorder path then calls `_groups.IndexOf(current)` (**`:617`**) inside the placement loop, which is a linear scan per row.
- At today's row counts (tens of apps/devices) this is small — flagged for the shape, not the current cost.
- **Fix:** key on a `readonly record struct (GroupKind Kind, string Key)` instead of a formatted string, and track the current index alongside the map entry rather than re-scanning with `IndexOf`.

---

## Low severity

- **L1 — `Host.CreateDefaultBuilder()`** (`App.xaml.cs:88`): pulls in console/debug/event-source logging providers and `reloadOnChange: true` file watchers on `appsettings.json` for a desktop app that logs through `AppLog` and reads that file exactly once, at `:110`. Costs a little startup time behind the splash and holds two `FileSystemWatcher`s for the session. `new HostBuilder()` with just the config source actually used would trim both.
- **L2 — `ChartPoint` is a `record` (reference type)** (`Models/Charting/ChartPoint.cs`): every window rebuild, snapshot and shift allocates 300 of them. A `readonly record struct` would remove the allocation and the pointer chase in the chart's tight loops — but it is a public model type used across four projects, so this is a "when something else touches it" change, not a standalone one.
- **L3 — `LocalViewModel.BuildNameMapAsync` loads the whole `Devices` table** (`:765-792`) every 60 s while the Local tab is open, to build an IP→name map. Bounded by the cache lifetime and by table size, and `AsNoTracking` is applied — but a projection to `(IpAddress, FriendlyName, Hostname, MacAddress)` would avoid materialising every column of every device just to read a display name.
- **L4 — `ApplyPoints` recomputes `TrafficRateFormatter.BucketSeconds(points)` twice** per update (`TrafficAreaChart:786` and again at `:907` via `UpdatePeakLabels`), each walking the point list. Pass the value already computed.
- **L5 — `AtomicFile.WriteAllText` per save**: temp-write plus move, twice the file operations of a plain write. Correct and deliberate (a torn `settings.json` is unrecoverable), and the right trade — noted only so it is not mistaken for accidental cost when M1 is fixed.
- **L6 — Carried over, still open and still correctly deprioritised:** L4 (pool `Ping` per semaphore slot), L6 (debounce `Settings.Save` — M1 makes each save cheap enough that the debounce stays unnecessary), L7 (buffer `AppLog` writes — the per-write flush is deliberate so a crash log survives).

---

## Areas checked and found efficient (no action)

- **ETW hot path** (`Traffic/TrafficCollector.cs:177-206`, `AddBytes`): still the best code in the app. Thousands of events per second doing a LAN/WAN classification, a `ConcurrentDictionary.GetOrAdd` with a **static** factory (no closure allocation) and a lock-free `Interlocked.Add`. No strings, no locks, no per-event allocation after a key's first event. The idle-counter eviction (`:135-159`) removes dead PIDs and flows without a scan, and re-drains the array after removal so no bytes are stranded.
- **Flush write path** (`TrafficTracker.WriteFlushAsync:168-220`): raw entries batched through one `SaveChangesAsync`, both rollup upserts sharing that same transaction, and `ExecuteUpsertAsync:245-279` creating the command and its parameters **once** and rebinding values per row — a prepared-statement loop, not a command per row. The July H1 fix has held up.
- **Raw-entry retention** (`TrafficTracker:48-52, 222-243`): one-hour retention on the per-flush tables with a five-minute set-based `ExecuteDeleteAsync`, because only the 5-minute live view reads them. This keeps the hottest-growing tables small, which is why none of the chart queries needed a new index.
- **Index coverage**: every range query found this round leads with an indexed column — `TrafficRollups(MinuteEpoch, ProcessName)` and `LocalTrafficRollups(MinuteEpoch, …)` serve both the chart `GROUP BY`s and the purge `DELETE`s; `TrafficEntries(Timestamp, ProcessName)` and `LocalTrafficEntries(Timestamp)` serve the sub-minute path; `DeviceEvent.Timestamp`, `Device.IsOnline`/`IsApproved`, `DigestReport.PeriodEnd`/`IsScheduled` and `SpeedTestResult.Timestamp` all still present (`Data/AppDbContext.cs:29-69`). The only unindexed purge predicate is `ScanSession.CompletedAt`, on a table with one row per scan — not worth an index.
- **Aggregation pushed to SQL**: `InternetViewModel.LoadAppBucketsAsync`/`LoadChartBucketsAsync` and the four `LocalViewModel` query shapes all `SUM`/`GROUP BY` in SQLite against indexed columns, with the bucket index computed in the query rather than by walking rows in C#.
- **Incremental live window**: both traffic view models shift the window and apply deltas in memory (`ShiftWindow` / `ApplyFlushToWindow`) rather than re-querying per flush, falling back to a reload only when the window jumps (`InternetViewModel:236-255`, `LocalViewModel:303-323`). On the 5-minute range this is the difference between one DB round-trip per second and none.
- **Startup** (`App.xaml.cs:233-258`): DB migration and the OUI parse are both on `Task.Run` behind the splash, and `LiveTrafficFeed.StartAsync:76-87` explicitly moves its two seed queries off the UI thread with a comment explaining why. The splash has a 5 s fallback timer so a slow first run cannot leave it stuck.
- **Scan pipeline** (`NetworkScanner:21-71`): ping sweep bounded by `MaxParallelPings`, reverse DNS bounded at 20 with a 2 s cap, `arp -a` run once with a compiled `[GeneratedRegex]`, and the mDNS probe overlapped with the ping sweep rather than run after it.
- **Device merge** (`DeviceTracker:26-30`): candidate-bounded query, tracked deliberately (these rows are written), one `SaveChangesAsync` for the whole merge. The in-memory hostname scan at `:100-105` is inside the per-device loop but only on the new-randomised-MAC branch, which fires rarely.
- **Widget state writes**: `MiniGraphWindow` debounces placement at 400 ms and flushes on hide/teardown/orientation change, so a drag is one write rather than one per mouse move. The `Bindings.StopTracking()` in `Teardown:294` prevents the singleton view model rooting destroyed windows — a leak that would have compounded across show/hide cycles.
- **Digest and backup**: chart rendering and PDF export off the UI thread, preview at display DPI with a 4-DPI change threshold (`DigestReportView:213-216`), backup via `SqliteConnection.BackupDatabase` on `Task.Run` with a 5-minute watchdog.

---

## Suggested sequencing

1. **Cheap, self-contained, no behaviour change:** M1 (`JsonSerializerOptions`), M2 (unhook `Rendering`), M4 (ordinal `Contains`), M3 (typed sort keys), L4.
2. **The headline:** H1 in the order (b) → (a) → (c) — reuse the path first because it halves the work with no visual change at all, then decimate, then throttle. Each step wants a live look at the widget in both orientations before the next.
3. **Structural but contained:** H3 (projection or identity resolution), H2 (observable row type + in-place reconcile — the larger of the two, and the one that needs the Internet grid re-tested for selection, sorting and the drill-in).
4. **Opportunistic:** M5, M6, L1, L3.

---

## Progress tracker

Work order (my preferred sequencing). Tick **Done** as each is completed.

### Phase 1 — cheap, high value

| # | ID | Fix | Severity | Migration | Done |
|---|----|-----|----------|-----------|------|
| 1 | M1 | Cache `JsonSerializerOptions` in `Settings.Save` | Med | No | ✅ |
| 2 | M2 | Unhook `CompositionTarget.Rendering` when not live | Med | No | ✅ |
| 3 | M4 | Ordinal `Contains` in the history search | Med | No | ✅ |
| 4 | M3 | Typed sort keys in `DeviceHistoryViewModel` | Med | No | ✅ |
| 5 | L4 | Compute `BucketSeconds` once per update | Low | No | ✅ |

> **Phase 1 done (2026-08-13).** Builds clean, 494/494 tests pass.
> - **M1:** `SaveOptions` is now a `static readonly` field beside `_saveLock`.
> - **M2:** the hook is driven by `UpdateRenderingHook()`, which subscribes only while `_isLoaded && _isLive && _smoothScrolling`. `_frozen` is deliberately left as an in-handler check — it flips on every hover, so folding it in would churn the subscription. A chart turning smooth scrolling off now costs nothing rather than a per-frame no-op.
> - **M4:** also fixed a latent inconsistency — `IpAddress` was matched case-sensitively against an already-lowercased query, which only went unnoticed because an IP has no letters.
> - **M3:** `SortBy<TKey>` keeps each key at its own type; the ascending/descending choice stays in one place.
> - **L4:** `UpdatePeakLabels` now takes `bucketSeconds` rather than re-deriving it from the points.
> - **Live check passed (2026-08-13):** M2 was tested in the running app — the widget hidden and re-shown, both full-page charts paused and resumed, and `ChartSmoothScrolling` toggled off and back on with the widget visible. Every chart resumed drawing each time.

### Phase 2 — the widget chart

| # | ID | Fix | Severity | Migration | Done |
|---|----|-----|----------|-----------|------|
| 6 | H1b | Build each series' bezier path once, fill and stroke from it | High | No | ✕ |
| 7 | H1a | Decimate points to ~1 per horizontal pixel (max-per-column) | High | No | ✕ |
| 8 | H1c | Skip the frame on sub-pixel shift once the ease has converged | High | No | ✕ |

### Phase 3 — structural

| # | ID | Fix | Severity | Migration | Done |
|---|----|-----|----------|-----------|------|
| 9 | H3 | Project history events / identity-resolve the `Include` | High | No | ✕ |
| 10 | H2 | Observable `InternetTrafficAppRow` + in-place reconcile | High | No | ✕ |

### Phase 4 — opportunistic

| # | ID | Fix | Severity | Migration | Done |
|---|----|-----|----------|-----------|------|
| 11 | M5 | Reuse snapshot and chart buffers across flushes | Med | No | ✕ |
| 12 | M6 | Struct group identity + no `IndexOf` in the placement loop | Med | No | ✕ |
| 13 | L1 | Trim `CreateDefaultBuilder` to the config actually used | Low | No | ✕ |
| 14 | L3 | Project the name map instead of loading whole devices | Low | No | ✕ |
| 15 | L2 | `ChartPoint` as a `readonly record struct` | Low | No | ✕ |

---

## Live testing required

The rendering and grid changes cannot be signed off from a build and a test run alone:

- **H1 (widget charts).** Both orientations, both sections, at the minimum size and dragged large; confirm the trace shape, the peak label, the gridline values and the time row are unchanged, that spikes still show at the decimated resolution, and that scrolling still reads as smooth with `ChartSmoothScrolling` on and off.
- **H2 (Internet grid).** Watch a live 5-minute range for several minutes: row values must update in place with no flicker, the selection must survive a flush, column sorting and the app drill-in must still work, and the All Apps row must stay pinned first.
- **H3 (history).** Load a populated 30-day history, sort by every column, search, and deep-link from a device into History.
- **M2.** ✅ Done 2026-08-13. Hide and re-show the widget, pause/resume both full-page charts, and toggle `ChartSmoothScrolling` off and on with the widget visible, confirming they resume drawing each time.

## Database impact

**None.** No schema change, no new `DbSet`, no new or altered column or index — so **no EF Core migration is required for this review**. The findings are rendering, serialisation, query-projection and collection-reconciliation changes. H3's projection changes what is *selected*, not what is *stored*.
