# Performance Review — 2026-07-01

A review of the codebase for slow or inefficient operations, covering four areas: data access (EF Core / SQLite), the traffic-capture (ETW) pipeline, the network-scanning pipeline, and the UI / rendering / digest-export layer.

Line numbers reference the files as they were at the time of review. Findings are ranked by severity and hot-path frequency. **No High-severity issue is a correctness bug** — the app works; these are efficiency opportunities.

> **Schema note:** the project uses EF `EnsureCreated` with **no migrations**, so any index addition below requires deleting/recreating the SQLite database. Each such finding is marked **(DB delete required)**.

---

## Top fixes (highest value first)

1. **Wrap the traffic rollup upserts in one transaction** — an fsync per process every 5 s today. (Finding H1)
2. **Bound / index the `DeviceTracker` device load** — loads the entire, never-purged `Devices` table every scan. (Finding H2)
3. **Cap on-screen digest chart DPI** — currently renders/decodes ~9× the needed pixels. (Finding H4)
4. **Stop reloading hidden tabs and the full event table on every scan.** (Finding H5)
5. **Reduce per-frame work in the live traffic chart** (geometry, arrays, text formatting at ~60 fps). (Findings H3, M2)

---

## High severity

### H1 — Rollup upserts run one SQLite commit (fsync) per process, every flush
- **`Services/Traffic/TrafficTracker.cs:143-151`** (`UpsertRollupsAsync`)
- The `INSERT … ON CONFLICT` loop calls `await command.ExecuteNonQueryAsync()` once per active process with **no explicit transaction**, so each row is its own implicit commit. In WAL + `synchronous=FULL`, that is one fsync per row.
- **Frequency:** every `TrafficIntervalSeconds` (default 5 s), N = number of active networked processes (often 20-50). This is the hottest write path in the app and competes with the scanner for the DB.
- **Fix:** wrap the loop in a single `DbTransaction` (`BeginTransactionAsync` … `CommitAsync`); ideally fold the raw `TrafficEntries` insert (line 92) into the same transaction. Expect ~10-50× fewer fsyncs per flush.

### H2 — `DeviceTracker.MergeAsync` loads the entire (unbounded) Devices table every scan
- **`Services/Scanning/DeviceTracker.cs:20`** (`db.Devices.ToListAsync`), nested match at **`:88`**
- Every merge pulls **all** `Devices` rows into memory *with change-tracking*, sorts them (`OrderByDescending(...).ThenBy(...)`), and the new-randomized-MAC hostname path does an in-memory `Where(...).FirstOrDefault()` **inside** the per-scanned-device loop → O(scanned × devices).
- **Why it grows:** the `Devices` table is **never purged** anywhere (purge loops only touch events, sessions, traffic, speed tests, digests). With MAC-randomizing phones/guests it grows without bound to thousands of rows.
- **Frequency:** every scan (default 5 min).
- **Fix:** load only candidates that can match — `Where(d => scannedMacs.Contains(d.MacAddress) || d.IsOnline)` (indexed on `MacAddress`); pre-build the approved-hostname lookup once instead of re-scanning per device. Consider an index on `IsOnline`.

### H3 — Live traffic chart rebuilds native Win2D geometry + arrays every frame (~60 fps)
- **`Views/Controls/TrafficAreaChart.xaml.cs:234-267`** (`DrawArea`), **`:350-351`** (`BuildPoints`), invalidated from `OnRendering` at **`:297-305`**
- While the Traffic tab is live, `CompositionTarget.Rendering` invalidates the canvas ~60 fps. Each draw allocates two fresh `Vector2[]` (via `BuildPoints`) and, per `DrawArea`, 2 `CanvasPathBuilder` + 2 `CanvasGeometry` — ~240 native Direct2D objects + ~120 managed arrays created and torn down **per second**, continuously, on the render thread.
- **Fix:** reuse persistent `Vector2[]` buffers (reallocate only when `_count` changes); once the ease animation has converged (`_displayed* ≈ _download/_upload`, `_displayMax ≈ _targetMax`) stop invalidating and scroll via a transform rather than rebuilding beziers; at minimum cache geometries and rebuild only when point values change.
- **Outcome — buffer reuse done; native geometry rebuild left as-is. Reasons:** (1) the live chart was tested and runs smooth with no jank; (2) the managed GC pressure (the `Vector2[]` allocations) is already eliminated; (3) the chart is small — tens of points — so rebuilding its Direct2D geometry is cheap on any modern GPU; (4) over a 5-min+ window the per-frame horizontal scroll is **sub-pixel**, so the 60 fps rebuild isn't a smoothness necessity (the perceived smoothness comes from the post-flush ease animation, not the redraw); (5) a transform-based scroll rework is high-risk/low-reward because the growing live edge isn't a pure translation and it needs iterative live visual testing. Revisit only if profiling shows the chart dropping frames on a low-end GPU.

### H4 — Digest charts render at 288 DPI even for the on-screen preview
- **`Services/Digest/DigestChartRenderer.cs:17`** (`RenderDpi = 288f`), used at **`:108`**; preview path **`Views/Controls/DigestReportView.xaml.cs:111-137`**
- `CanvasRenderTarget(840, 360, 288)` produces a 2520×1080 px bitmap (2520×540 for speed charts). Four such PNGs per report. The **same** 288-DPI renderer feeds the on-screen preview; `BitmapImage.SetSourceAsync` then decodes those multi-megapixel PNGs **on the UI thread**, only to display them at ~840×360 logical px — ~9× the pixels decoded/downscaled for nothing. Runs on every summary change, theme switch, and report selection.
- **Fix:** pass DPI into the renderer; use ~96 DPI (or actual display DPI) for the preview and keep 288 DPI only for `DigestPdfExporter`. Optionally set `BitmapImage.DecodePixelWidth`.

### H5 — Device/history view-models reload their whole list on every scan, including hidden tabs
- **`ViewModels/AllDevicesViewModel.cs:318-338`** (load `:99-112`), **`UnapprovedDevicesViewModel.cs:211-223`** (`:77-90`), **`ViewModels/DeviceHistoryViewModel.cs:177-189`** (load `:86-99`)
- `DevicesHostPage` keeps the All / Approved / Unapproved / History frames alive after first visit and each VM stays subscribed to `ScanCompleted`, so one scan triggers 3-4 full table queries + LINQ filter + sort + reconcile **even for hidden tabs**. Worst is `DeviceHistoryViewModel.LoadAsync`, which pulls **all** `DeviceEvents` with `.Include(Device)` (up to `HistoryPurgeDays` = 30 days), tracked, and re-sorts on every scan whether or not the tab is shown.
- **Frequency:** every scan (5 min) + every manual scan.
- **Fix:** mark hidden tabs "dirty" on scan-complete and reload lazily on tab activation (host already reloads on tab select); at minimum skip the History reload when its page isn't active. See M6 (`AsNoTracking`) and M1 (index) which compound this.

---

## Medium severity

### M1 — No index on `DeviceEvent.Timestamp` **(DB delete required)**
- **`Data/AppDbContext.cs:27-31`** (only the FK index exists)
- Range/order-by on `Timestamp` runs on **every scan completion** (`DeviceHistoryViewModel.cs:94/98`), plus digest generation (`DigestGenerator.cs:17-19`), the `DigestWorker` existence check, and the daily purge. `DeviceEvents` accumulates (age-purged only), so these are full-table sorts.
- **Fix:** `modelBuilder.Entity<DeviceEvent>().HasIndex(e => e.Timestamp);`

### M2 — Live chart formats strings + allocates `CanvasTextFormat` + does a resource lookup every frame
- **`Views/Controls/TrafficAreaChart.xaml.cs:381-406`** (`DrawAxisLabels` / `DrawStackedLabel`)
- Per draw (~60 fps): `Application.Current.Resources[...]` lookup + 4 `Color` structs, a `new CanvasTextFormat` created/disposed, and 4 interpolated label strings — yet these change only when `_targetMax` changes (rarely).
- **Fix:** cache the `CanvasTextFormat` (create in `ChartCanvasCreateResources`, dispose in `OnUnloaded`) and the colors; recompute label strings only when `_targetMax`/`_bucketSeconds` change.

### M3 — `Process.GetProcessById` + `ProcessName` re-resolved for every PID every flush
- **`Services/Traffic/TrafficTracker.cs:69-70`**
- Only the process *path* is cached (`_pathCache`); the name is re-resolved via a full `Process` object every 5 s for every active PID, even though PID→name is static for a live process.
- **Fix:** extend the cache (keyed by PID + StartTime) to hold the name too; resolve once per process lifetime.

### M4 — Exception-as-control-flow + uncached path for inaccessible processes, every flush
- **`Services/Traffic/TrafficTracker.cs:163-187`**
- `process.StartTime` throws for protected/system processes; the caught case skips caching, so `GetProcessPath` (OpenProcess/QueryFullProcessImageName) **and the thrown exception** repeat every flush for those PIDs indefinitely.
- **Fix:** cache a best-effort/negative result (PID-only fallback with a short TTL) so neither the exception nor the Win32 query recurs each flush.

### M5 — `RepaintRows` resets the entire DataGrid `ItemsSource` on any single approval change
- **`Views/AllDevicesPage.xaml.cs:126-130`** (from `OnDeviceApprovalChanged` `:89-97`)
- `ItemsSource = null; ItemsSource = ViewModel.Devices;` forces the grid to tear down and re-realize **every** row (re-running `DataGridLoadingRow`), defeating the incremental `CollectionReconciler`. During a CSV import / bulk approve of N devices, N full-grid resets get enqueued.
- **Fix:** repaint only the affected row, or drive the row background from a binding/converter on `Device.IsApproved` so no code-behind reset is needed.

### M6 — Missing `AsNoTracking()` on read-only list loads
- **`ViewModels/DeviceHistoryViewModel.cs:88-99`** (worst — full event list + `Include`), `AllDevicesViewModel.cs:103-127`, `UnapprovedDevicesViewModel.cs:81-83`, `SpeedTestViewModel.cs:97-100`, `ReportsViewModel.cs:167-174`, `Services/Backup/DatabaseBackupWorker.cs:100-102`, `DigestGenerator.cs:17-21` (the two read queries).
- These attach entities to the change tracker for data that is only displayed/exported — needless per-row tracking + memory.
- **Fix:** append `.AsNoTracking()`.

### M7 — `ReportsViewModel` loads all reports (incl. large `SummaryJson`), tracked, unindexed order
- **`ViewModels/ReportsViewModel.cs:167-174`** (`OrderByDescending(r => r.PeriodEnd).ToListAsync`)
- Selects every column including the potentially large serialized summary for all reports, tracked, with no index on `PeriodEnd`; only the newest is needed for `LatestReport`.
- **Fix:** `.AsNoTracking()`; add `HasIndex(r => r.PeriodEnd)` **(DB delete required)**; if the list UI needs only headline/period, project those and lazy-load `SummaryJson` on selection.

### M8 — Search filter re-runs full filter + sort + `ToList` on every keystroke, with string allocations
- **`ViewModels/AllDevicesViewModel.cs:257-316`** (also `UnapprovedDevicesViewModel.cs:164-209`, `DeviceHistoryViewModel.cs:115-149`)
- The `SearchText` setter filters on every character, lowercasing up to 5 fields per device (`ToLowerInvariant` allocations), re-sorting, and `ToList`-ing. `IpSortKey` (`:309-316`) allocates a padded string per element per sort — note `DigestSummaryBuilder.IpSortKey:100-116` already does this allocation-free as a single `long`.
- **Fix:** debounce `SearchText` (~200 ms); precompute a lowercased search blob per `Device`; adopt the numeric-`long` IP sort key.

### M9 — Unbounded reverse-DNS concurrency in the scan
- **`Services/Scanning/NetworkScanner.cs:25-35`** (resolve at `:30`, `ResolveHostnameAsync` `:74-89`)
- After the ping sweep, every responding IP fires `Dns.GetHostEntryAsync` concurrently (not gated by the ping semaphore). A 2 s per-lookup cap prevents a full stall, but 50-150 simultaneous PTR queries spike threads/handles, and every host with no PTR waits the full 2 s in parallel. Scales with live-device count.
- **Fix:** gate DNS with a bounded `SemaphoreSlim` (e.g. 20). Keep the 2 s timeout.

### M10 — `CollectionReconciler.SyncOrdered` reorder path is O(n²)
- **`Services/Common/CollectionReconciler.cs:102-130`**
- For each out-of-place index it linearly scans forward then `collection.Move(...)`, each Move raising a `CollectionChanged` the grid handles — ~O(n²) scans + O(n) Move events on a full reversal (e.g. toggling sort direction). Tolerable at ≤254 rows today.
- **Fix:** detect large-reorder/reversal and rebuild once (single reset) instead of many incremental Moves.

---

## Low severity

- **L1 — `TrafficViewModel` rebuilds whole `ObservableCollection`s per flush** (`ViewModels/TrafficViewModel.cs:113-114, 233-234`): forces a full chart rebind every ~5 s; reconcile in place if optimized.
- **L2 — `_pathCache` grows unbounded** (`Services/Traffic/TrafficTracker.cs:19, 184`): dead PIDs never evicted — slow memory creep. Evict entries whose PID no longer resolves.
- **L3 — Redundant `MacNormalizer.Normalize` allocations in merge** (`Services/Scanning/DeviceTracker.cs:27, 37, 42, 61`): DB MACs are already normalized; the line-42 re-normalization is pure waste. Normalize scanned MACs once into a set.
- **L4 — New `Ping` per host** (`Services/Scanning/NetworkScanner.cs:54`): 254 allocations/scan; `Ping` isn't thread-safe for concurrent sends, so only pool one-per-semaphore-slot if profiling flags it.
- **L5 — `OuiDatabase.Lookup` substring+lowercase allocation per call** (`Data/OuiDatabase.cs:41`): tens of calls/scan — negligible (the dictionary itself is loaded once, O(1) lookups).
- **L6 — `Settings.Save` writes `settings.json` on every individual setting change** (`ViewModels/SettingsViewModel.cs:221-257`, `Data/Settings.cs:155-163`): not per-keystroke (inputs commit on blur/Enter), but several toggles = several writes. Consider a debounce timer like `SaveWindowPlacement` already uses.
- **L7 — `AppLog.Write` opens/closes the file per entry under a global lock** (`Services/Platform/AppLog.cs:49-73`): fine at current volume (errors + occasional info, off by default); buffer via a `StreamWriter`/channel if logging ever gets hot.
- **L8 — Missing indexes on small-but-filtered columns** (`Device.IsOnline`/`IsApproved`, `DigestReport.IsScheduled`/`PeriodEnd`): low impact while those tables stay small — add only if they grow **(DB delete required)**.
- **L9 — `SpeedTestViewModel.LoadAsync` over-fetches the full purge window then filters client-side** (`ViewModels/SpeedTestViewModel.cs:97-107`): the full set feeds the history grid, so minor; add `.AsNoTracking()`.

---

## Areas checked and found efficient (no action)

- **ETW hot path** (`Services/Traffic/TrafficCollector.cs:87-98`, `AddBytes`): the highest-frequency code (thousands of events/s) does only a `ConcurrentDictionary.GetOrAdd` with a **static** factory (no closure alloc) + lock-free `Interlocked.Add`. No strings, locks, or per-event allocation after a PID's first event. Handler lambdas created once. Excellent.
- **Ping sweep** (`NetworkScanner.cs:13-19`): correctly bounded by `MaxParallelPings` via `SemaphoreSlim`, 500 ms timeout.
- **ARP parsing** (`NetworkScanner.cs:105, 125`): `[GeneratedRegex]` compiled once; `arp -a` run once/scan, read async.
- **Merge writes** (`DeviceTracker.cs:125`) and **traffic entry inserts** (`TrafficTracker.cs:92`): batched via `AddRange` + a single `SaveChangesAsync` — no per-row saves, no N+1.
- **Aggregation pushed to SQL** (`TrafficViewModel.LoadAppRowsAsync`/`LoadChartBucketsAsync`, `DigestGenerator.LoadTrafficTotalsAsync`): `SUM`/`GROUP BY` against indexed columns, not summed in C#.
- **Purge paths** (`ScanWorker.cs:107-142`, `SettingsViewModel.cs:259-284`, `DigestWorker.cs:144-156`): set-based `ExecuteDeleteAsync`/`ExecuteSqlRaw`; existence checks use `AnyAsync`.
- **OUI load** (`OuiDatabase.cs:13-29`): `oui.txt` parsed once into a dictionary.
- **PDF/CSV export** (`ReportsPage.xaml.cs:148`, `DigestPdfExporter`, `ReportsViewModel.BuildAllReportsCsv`) and **on-screen chart render** (`DigestReportView.xaml.cs:111-121`): offloaded to `Task.Run` — off the UI thread (the DPI in H4 is the only preview concern).
- **`Device.CopyValuesFrom`** (`Models/Device.cs:221-234`): `SetProperty` raises `PropertyChanged` only for genuinely changed fields — no spurious per-scan churn.
- **`DigestGenerator` / `DigestSummaryBuilder`**: run on the `DigestWorker` background thread, single raw SQL aggregate, allocation-free numeric IP sort keys.

---

## Suggested sequencing

1. **Cheap + high value, no schema change:** H1 (rollup transaction), H4 (preview DPI), M6 (`AsNoTracking`), M9 (bound DNS).
2. **Structural but contained:** H2 (bound device load), H5 (lazy hidden-tab reload), H3/M2 (per-frame chart work), M3/M4 (PID caching), M5 (row repaint).
3. **Schema changes (batch into one DB recreate):** M1 (`DeviceEvent.Timestamp`), M7 (`DigestReport.PeriodEnd`), L8 — all require deleting/recreating the DB (`EnsureCreated`, no migrations), so do them together.

---

## Progress tracker

Work order (my preferred sequencing). Tick **Done** as each is completed.

### Phase 1 — cheap, high value, no schema change

| # | ID | Fix | Severity | DB delete | Done |
|---|----|-----|----------|-----------|------|
| 1 | H1 | Wrap rollup upserts in one transaction | High | No | ✅ |
| 2 | H4 | Cap on-screen digest chart DPI (display scale), keep 288 for PDF | High | No | ✅ |
| 3 | M6 | Add `AsNoTracking()` to read-only list loads | Med | No | ✅ |
| 4 | M9 | Bound reverse-DNS concurrency with a semaphore | Med | No | ✅ |

### Phase 2 — structural but contained

| # | ID | Fix | Severity | DB delete | Done |
|---|----|-----|----------|-----------|------|
| 5 | H2 | Bound the `DeviceTracker` device load (query candidates only) | High | No | ✅ |
| 6 | H5 | Lazy reload of hidden tabs / full event table | High | No | ✅ |
| 7 | H3 | Reuse chart `Vector2[]` buffers (geometry rebuild deliberately left as-is) | High | No | ✅ |
| 8 | M2 | Cache `CanvasTextFormat` + axis label strings in the chart | Med | No | ✅ |
| 9 | M3 | Cache PID → ProcessName | Med | No | ✅ |
| 10 | M4 | Cache path for inaccessible PIDs (no per-flush path lookup) | Med | No | ✅ |
| 11 | M5 | Coalesce repeated `ItemsSource` resets on approval change | Med | No | ✅ |
| 12 | M8 | Debounce search + numeric `long` IP sort key | Med | No | ✅ |
| 13 | M10 | Reconciler: rebuild once on large reorder instead of O(n²) Moves | Med | No | ✅ |

> **H3 decision:** the per-frame `Vector2[]` allocations are eliminated (buffers reused). The remaining native-geometry rebuild is **deliberately left as-is** — the live chart tested smooth with no jank, the managed GC pressure is gone, the chart is small (tens of points), and over a 5-min+ window the per-frame scroll is sub-pixel so the rebuild isn't a smoothness necessity (the perceived smoothness comes from the post-flush ease animation). A transform-based scroll rework would be high-risk/low-reward (the growing live edge isn't a pure translation). Revisit only if profiling shows the chart dropping frames on a low-end GPU.
> **Live check completed ✅ (2026-07-01):** the chart (H3/M2), grid row-repaint (M5), and hidden-tab lazy reload (H5) were verified with a live UI run — all passed.

### Phase 3 — schema changes (batch into one DB recreate)

| # | ID | Fix | Severity | DB delete | Done |
|---|----|-----|----------|-----------|------|
| 14 | M1 | Index `DeviceEvent.Timestamp` | Med | **Yes** | ✅ |
| 15 | M7 | `AsNoTracking` (Phase 1) + index `DigestReport.PeriodEnd` | Med | **Yes** | ✅ |
| 16 | L8 | Indexes on `Device.IsOnline`/`IsApproved`, `DigestReport.IsScheduled` | Low | **Yes** | ✅ |

> **Phase 3 done (2026-07-01):** five indexes added in `AppDbContext.OnModelCreating` — `DeviceEvent.Timestamp`, `Device.IsOnline`, `Device.IsApproved`, `DigestReport.PeriodEnd`, `DigestReport.IsScheduled`. Requires deleting the SQLite DB (done) so `EnsureCreated` rebuilds the schema with them.

### Phase 4 — low priority / opportunistic

| # | ID | Fix | Severity | DB delete | Done |
|---|----|-----|----------|-----------|------|
| 17 | L1 | `TrafficViewModel`: reconcile collections in place | Low | No | ✕ |
| 18 | L2 | Evict dead PIDs from the PID info cache | Low | No | ✅ |
| 19 | L3 | Remove redundant `MacNormalizer` allocations in merge | Low | No | ✅ |
| 20 | L4 | Pool `Ping` per semaphore slot | Low | No | ✕ |
| 21 | L5 | `OuiDatabase.Lookup` zero-alloc span lookup | Low | No | ✅ |
| 22 | L6 | Debounce `Settings.Save` | Low | No | ✕ |
| 23 | L7 | Buffer `AppLog` writes | Low | No | ✕ |
| 24 | L9 | `AsNoTracking` on `SpeedTestViewModel.LoadAsync` | Low | No | ✅ |

> **Phase 4 (2026-07-01):**
> - **Done:** L2 (`TrafficTracker` prunes PID cache entries for inactive PIDs once it exceeds 512), L3 (`DeviceTracker` trusts the already-normalized stored `MacAddress` instead of re-normalizing), L5 (`OuiDatabase.Lookup` uses a `ReadOnlySpan<char>` alternate lookup — no substring/`ToLower` allocation). L9 was already covered by Phase 1 (M6).
> - **Skipped (✕), with rationale:** L1 — the chart's `ChartPoints` dependency-property handler only fires on a *reference* change, so reconciling in place would stop live updates. L4 — `Ping` isn't thread-safe for concurrent sends and the gain is negligible (agent's own advice). L6 — a save debounce risks losing the last setting if the app is closed mid-window, for near-zero benefit on infrequent user changes. L7 — the per-write flush is intentional so a crash-diagnostic log isn't lost in a buffer; buffering would defeat its purpose.

---

## Review status: COMPLETE (2026-07-01)

All 24 findings addressed — **19 implemented**, **1 already covered** (L9 via M6), **4 consciously skipped** (L1/L4/L6/L7, rationale above). Phases 1–4 build clean, 97/97 tests pass, and the rendering/UI-lifecycle changes were verified with a live run — all passed.

**H3 left as-is (buffer-reuse win done, native geometry rebuild not reworked) because:** the live chart tested smooth with no jank; the managed allocations are already eliminated; the chart is small (tens of points) so the rebuild is cheap; and over a 5-min+ window the per-frame scroll is **sub-pixel**, so the rebuild isn't a smoothness necessity — the smoothness comes from the post-flush ease animation. A transform-based scroll rework would be high-risk/low-reward (the growing live edge isn't a pure translation). Revisit only if profiling shows dropped frames on a low-end GPU.

### Live testing — completed ✅ (all passed, 2026-07-01)

Phases 1–4 build clean with 97/97 tests passing. The rendering / UI-lifecycle changes below were also verified by running the app — **all passed**:

- **Traffic chart (H3/M2).** Confirm the live area chart still renders, scrolls, animates and shows correct axis labels (cached `CanvasTextFormat` + reused point buffers). Toggle themes; select a bucket / pause (History) and resume (Live).
- **Devices grid row highlight (M5).** Approve a single device and a bulk CSV import — confirm amber unapproved-row highlighting repaints correctly and there's no flicker/stale rows.
- **Hidden-tab lazy reload (H5).** On the Devices host, switch between Devices / Approved / Unapproved / History across a scan cycle; confirm each tab shows fresh data when selected and hidden tabs aren't doing work. Deep-link from a device into History still works.
- **Search boxes (M8).** Type in the Devices / Unapproved / History search — confirm the 200 ms debounce feels right and IP-column sorting is still correct.
- **Digest report (H4).** Open Reports — charts should be crisp at the display scale; export to PDF still renders at 288 DPI.
- **Speed test / scan / traffic** end-to-end after the DB was recreated (Phase 3 indexes) — confirm a clean first run builds the schema and everything populates.

### One-time action already done

- **DB deleted** for the Phase 3 index additions (`EnsureCreated` rebuilds the schema with the new indexes on next launch).
