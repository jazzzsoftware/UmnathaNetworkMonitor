# Code Review — 2026-07-27 (ledger)

**Read this file first on every resume.** Procedure: `../code-review-procedure.md`.

---

## ✅ REVIEW COMPLETE — 2026-07-28

Every finding is `fixed`, `won't-fix` or explicitly accepted; all four chunks are co-reviewed. Build **0 errors / 0 warnings** (x64), **248 tests green**. Branch: **`review/2026-07-27-fixes`**.

**46 findings (44 reviewer + 2 user) — 44 fixed, 2 won't-fix, 0 deferred.**

Closed since the 2026-07-27 pause:

1. **C1-15 — done** via route (b): the download/verify orchestration moved into Core behind delegates, leaving Services a thin adapter. The layering rule holds (tests still reference Models + Core only). See the 2026-07-28 fix-phase entry.
2. **Co-review of chunks 2, 3 and 4 — done.** No user findings in chunks 2 or 3; the behaviour decisions in both were put to the user and confirmed. Chunk 4 produced **U4-1** (two findings recorded as fixed while only part of each was applied), now fixed.
3. **`summary.md` + `smoke-checklist.md`** — written for this review, alongside the 2026-06-23 pair.
4. **`Documents/To Do.txt`** — folded into this branch.

Standing decisions:

- **C1-6** stays `won't-fix` until the installer is code-signed — an Authenticode check on an unsigned build would reject every update rather than protect one. Worth a reminder when signing is next considered.
- **C4-8** stays `won't-fix` on corrected reasoning (the proposed fix would have frozen the chart).
- **C4-11 item 1** (`MinimumSpinnerMs = 500`) accepted as deliberate anti-flicker.
- **C3-2 sub-point 1** (constructor `Refresh()` on the splash path) kept on purpose — deferring it would degrade classification for the first packets after startup.

Still open, and **not** part of the review: whether to bump the version and write release notes for these fixes. 0.0.9 is shipped and several of these are user-visible changes to the update path itself; nothing in this branch touches `<Version>`.

### Manual verification still outstanding

Nothing below is covered by unit tests; all of it is DB- or timing-bound and needs the real app running.

- Shifted live window on **both** tabs across all three live ranges (5 min / 1 h / 6 h): chart scrolls smoothly, totals still match a manual refresh after several minutes, rows age out of the window rather than accumulating.
- A new app/device appearing mid-window without collapsing an open drill-down.
- Update banner across check → download → **cancel** → download → install, and the app returning **visible** after the silent install.
- First `TrafficTracker` flush after upgrading clears the existing multi-day backlog of raw rows — one large delete, guarded by its own 2-minute watchdog, retried next cycle if it times out.
- **DB delete is NOT required** for this branch: no schema changed.

---

## Scope

The work done **since the 2026-06-23 review**, narrowed by the user to the two largest new subsystems:

- **Auto-update** — `NetworkMonitor.Core/Update`, `NetworkMonitor.Services/Update`, `NetworkMonitor.Models/Update`, `UpdateViewModel`, the MainWindow banner, the SettingsPage update card, and `Installer/NetworkMonitor.iss`.
- **Local traffic tab** — LAN capture in `TrafficCollector`/`TrafficTracker`, the classifier/grouper in `NetworkMonitor.Core/Traffic`, the `LocalTraffic*` models, and `LocalViewModel` / `LocalPage`.

**Explicitly out of scope** (user decision): the Models/Core/Services project split, mDNS enrichment, the speed-test rewrite, the SI-units change, digest changes, and everything already covered by the 2026-06-23 review.

Baseline: the public history was squashed at `f5ba5bd Initial release.` (2026-07-05); 92 commits follow it. The two subsystems above land in `4fb2f25`…`e165c70` (update) and `a979f5c`…`7e7b370` plus the `LocalPage`/`LocalViewModel` commits (Local traffic).

## Review dimensions

1. Correctness — logic bugs, edge cases, off-by-one, null/empty handling.
2. Concurrency & async — `async void`, dispatcher marshalling, interlocked counters, background-loop cancellation.
3. Resource lifetime — `IDisposable`, native handles, DB connections, ETW session, `using` coverage.
4. Error handling — empty `catch {}`: deliberate vs swallowing.
5. Conventions — CLAUDE.md rules (single exit, blank-line blocks, no `var`, member order, backing-field-above-property, braces, naming, XAML attribute order).

Plus reuse / simplification / efficiency.

## Chunks

| # | Chunk | State | Findings | Actioned |
|---|-------|-------|----------|----------|
| 1 | Auto-update | complete · co-reviewed | 15 + U1-1 (1 BUG · 4 RISK · 11 CLEANUP) | 15 fixed · 1 won't-fix |
| 2 | Local traffic capture & storage | complete · co-reviewed | 11 (2 BUG · 3 RISK · 6 CLEANUP) | 11 fixed |
| 3 | Local traffic classification & grouping | complete · co-reviewed | 7 (1 RISK · 6 CLEANUP) | 7 fixed |
| 4 | Local traffic UI | complete · co-reviewed | 11 + U4-1 (3 BUG · 2 RISK · 6 PERF/CLEANUP) | 11 fixed · 1 won't-fix |

**46 findings total (44 reviewer + 2 user) — 44 fixed, 2 won't-fix, 0 deferred.** IDs: `C<chunk>-<n>` reviewer, `U<chunk>-<n>` user.

User findings: **U1-1** — underscores in three test method names, against the CLAUDE.md no-underscores rule; fixed, and a sweep confirmed the remaining underscores in the test project are all inside string literals (GitHub JSON keys, mDNS device names).

Not fixed, and why:

- **C1-6** — `won't-fix` by user decision: the build isn't code-signed, so an Authenticode check would reject every update instead of protecting one. Revisit when signing lands.
- **C1-15** — deferred: testing `UpdateService` needs either a Services reference from the test project or moving download orchestration into Core; both change the layering CLAUDE.md fixes, which is a bigger decision than this review. The Core half was covered instead (9 new tests).
- **C4-8** — `won't-fix`, **finding corrected on verification**: `TrafficAreaChart.ChartPoints` is a DependencyProperty that only redraws on identity change and never listens to `INotifyCollectionChanged`, so the proposed in-place mutation would have silently frozen the chart. The replacement is load-bearing; C4-1 reduces how often it happens instead.

Process note: the user chose to **skip the per-chunk co-review pause** — all four chunks were reviewed first, and the fix phase applies everything at the end.

## Cross-cutting themes

1. **The LAN half and the WAN half disagree with each other.** Unknown-PID bytes are kept on one side and dropped on the other (C2-1); an exited process degrades gracefully on one side and loses its data on the other (C2-2). Every divergence found was accidental, not designed.
2. **Shutdown paths that bypass the host.** C1-1 walks around the `StopHost` route added for 2026-06-23's C1-2 — losing the pending flush, the WAL checkpoint, the tray icon and the window placement.
3. **The live-refresh path doesn't do what it was designed to do.** On the default range every tick is a full reload (C4-1), and where the incremental path *does* run it can't add rows (C4-2), has no re-entrancy guard (C4-3) and re-reads the device table each time (C4-7).
4. **Write volume outruns what anything reads.** Per-second per-flow rows retained for 7 days to serve a 5-minute window (C2-5), with unbounded in-memory counters behind them (C2-3, C4-9).
5. **Mask-agnostic address tests.** `.255` is assumed to be broadcast everywhere (C3-1), silently discarding valid hosts on wider subnets and valid public addresses.

## Suggested fix batches

1. **Data-loss and correctness** — C2-1, C2-2, C3-1, C1-2, C4-2, C4-5, C4-6.
2. **Lifetime and resilience** — C1-1, C1-3, C1-4, C4-4, C4-3, C2-3.
3. **Write volume and live-path performance** — C2-5, C4-1, C4-7, C4-8, C4-9, C2-4, C2-6.
4. **Update-feature polish** — C1-5, C1-7, C1-8, C1-9, C1-10, C1-11, C1-14.
5. **Structure, conventions and docs last** — C1-12, C1-13, C1-15, C2-7…C2-11, C3-2…C3-7, C4-10, C4-11.

## Log

- **2026-07-27** — Review opened. Scope agreed with the user: auto-update + Local traffic tab only. Chunk 1 reviewed (15 findings). User elected to gather all findings and apply the fixes at the end, so chunks 2–4 followed without a co-review pause: chunk 2 (11), chunk 3 (7), chunk 4 (11). **44 findings, no Critical / data-loss-at-rest issues**; the two closest are C2-2 (WAN bytes of exited processes discarded) and C2-1 (unknown-PID WAN bytes discarded).

## Fix phase — all four chunks (2026-07-27)

Applied in one pass at the user's request. Build: `dotnet build NetworkMonitor.slnx -p:Platform=x64` → **0 errors, 0 warnings**. Tests: **227 passed / 0 failed** (up from 218 — 9 added). **No DB delete is required**: no schema changed, and the new raw-entry retention only deletes rows the UI no longer reads.

**Capture completeness (C2-1, C2-2, C3-1)**

- `TrafficCollector.AddBytes` now maps an unattributed pid (`-1`) to System on **both** halves instead of only the LAN one, so unknown-pid WAN bytes are counted rather than dropped. `SystemPid` moved to a shared `NetworkMonitor.Core.Traffic.WellKnownPids`.
- `TrafficTracker` gained one `ResolveProcess(pid)` used by both halves: on `ArgumentException` (process exited mid-interval) it falls back to the cached name for that pid, then to System — the counter is never discarded. The old `ResolveLocalProcess` is gone; `ResolveProcessInfo` narrowed its bare `catch (Exception)` to `Win32Exception`/`InvalidOperationException`/`NotSupportedException` and now has a single exit.
- `LanClassifier.IsBroadcastOrMulticast` no longer treats every `.255` as broadcast. It checks the limited broadcast address and the computed `end` of the adapter subnet that actually owns the address; where no adapter owns it, the `/24` assumption applies **inside private space only**, so public hosts ending in `.255` and hosts on `/23`-and-wider LANs are kept.

**Storage and write volume (C2-3, C2-4, C2-5, C2-6, C2-7, C2-8)**

- One flush is now one transaction: entries and rollups for both halves commit together (`WriteFlushAsync`), replacing four separate commits, and the two 60-line upsert blocks collapsed into `ExecuteUpsertAsync` + shared SQL constants.
- Raw `TrafficEntries` / `LocalTrafficEntries` are purged to **1 hour** on the flush loop (own watchdog, 5-minute cadence). Rollups keep the full `TrafficPurgeDays` history and still feed every range beyond 5 minutes plus the digest.
- Counter dictionaries drop a flow after 60 consecutive idle drains, re-draining the removed array so bytes racing the removal aren't stranded. `PruneInfoCache` now considers both snapshots and triggers on cache size alone.

**Live refresh (C4-1, C4-2, C4-3, C4-5, C4-6, C4-7, C4-9)**

- The window now **shifts** instead of reloading: `LoadFlowBucketsAsync` returns per-bucket flow dictionaries, `ShiftWindow` evicts the oldest bucket (subtracting it from the aggregate) and appends a fresh one. The 5-minute range no longer runs a full DB round-trip every second, and flows now age out of the window instead of accumulating.
- `ApplyGroups(reorder: false)` inserts unseen rows ahead of the trailing discovery row, so a newly-active app or device appears immediately without re-sorting under an open drill-down.
- `LoadAsync` is serialised behind a `SemaphoreSlim(1,1)`, so a live tick and an explicit reload can no longer finish out of order and re-seed the window from a stale cutoff.
- `Flushed` is raised even for an empty flush, so live rate chips age to zero instead of freezing at the last non-zero value; time-axis labels refresh on each live tick; the device-name map is cached for 60 seconds.
- `LocalPage` and `InternetPage` detach `MainWindow.Closed` on unload — previously every page instance ever navigated to stayed rooted for the life of the window.

**Update feature (C1-1 … C1-14)**

- `IInstallerLauncher.LaunchAndExit` takes a `beforeExit` callback, wired to the new `MainWindow.ShutdownForUpdate`, which runs the same graceful path as a tray Exit (save placement → `StopHost` → WAL checkpoint → tray icon) before `Environment.Exit`. `OnAppWindowClosing` shares it via `ShutdownGracefully`, guarded so it can't run twice.
- `SemanticVersion` accepts four-component versions, and `CheckAsync` reports an unreadable **installed** version as a failure instead of silently answering "you're on the latest version".
- `UpdateService.LastResult` is replayed to `UpdateViewModel` on construction, so a check completing before the window exists is no longer lost for 24 hours.
- The download is cancellable (view-model CTS + a Cancel button while busy; cancelled on exit) and cancellation is reported as "available" rather than an error.
- Release assets are paired by name (`<installer>.exe.sha256`, falling back to the sole checksum), progress reports only on whole-percent changes, the `Updates` folder is swept at startup, the update `HttpClient` carries a default User-Agent, a manual check applies its own returned result, the Settings card shows inline status instead of a duplicate InfoBar, and `Command` now precedes value bindings in the touched XAML.
- The installer relaunches **visible** after a silent update (user decision on C1-13).

**Classification and grouping (C3-2 … C3-7)**

- `LanClassifier` is sealed, `IDisposable`, and debounces `NetworkAddressChanged` bursts through a 2-second timer instead of rebuilding per event.
- A group's service tag is taken after the size sort, so a device is labelled with the service that actually moved the bytes; the discovery name lookup is a single `TryGetValue`; the rate-chip threshold is expressed as `ShowRateAboveBitsPerSecond = 500_000` with its reasoning.
- `Documents/Overview.md` now states what the Local totals exclude (broadcast/multicast, discovery from unknown peers, VPN-reached private ranges counted as Local) and how the raw/rollup retention split works.

**Tests added (9):** four-component version parsing; three release asset-pairing cases; public `.255` addresses treated as ordinary hosts; group service tag taken from the dominant child.

**Co-review (U1-1):** three `LocalTrafficGrouperTests` methods renamed off the `Scenario_Expectation` style to satisfy the codebase-wide no-underscores rule.

### Follow-ups — both closed 2026-07-27

Raised as out-of-scope at the end of the fix phase; the user asked for them straight away, so they were applied in the same pass.

- **`InternetViewModel` live window (F-1) — done.** The C4-1 shift now applies to the Internet tab as well: `LoadAppBucketsAsync` returns per-bucket app totals, `AggregateAppRows` builds the display rows from them, `ShiftWindow` evicts the oldest bucket from the running totals, and `ApplyFlushToWindow` feeds the newest one. The 5-minute range no longer runs a full reload every second. The C4-3 re-entrancy guard (`SemaphoreSlim(1,1)`) and the unbalanced-`OpenConnectionAsync` cleanup (C4-10) were carried across at the same time, since both defects were identical in this file. Two new nested types: `InternetAppTotals`, plus `AppBuckets` on `InternetLoadResult`.
- **`ScanWorker.PurgeOldHistoryAsync` raw deletes (F-2) — done.** The `TrafficEntries` / `LocalTrafficEntries` deletes are removed; with the hourly purge in `TrafficTracker` they could only ever match nothing. The daily purge now owns the rollups and speed-test results only, with a comment pointing at the new owner.

### Manual verification still outstanding

Not covered by unit tests (DB- and timing-bound):

- The shifted live window on **both tabs** and all three live ranges (5 min / 1 h / 6 h): chart scrolls smoothly, totals match a manual refresh after several minutes of running, and rows age out of the window rather than accumulating.
- New apps/devices appear in the grid mid-window without collapsing an open drill-down.
- The update banner across check → download → cancel → download → install, and that the app returns **visible** after the silent install.
- After an upgrade, the first `TrafficTracker` flush clears the existing multi-day backlog of raw rows (a large one-off delete, guarded by its own 2-minute watchdog and retried next cycle if it times out).

---

## Fix phase — C1-15 and co-review completion (2026-07-28)

Build: `dotnet build NetworkMonitor.slnx -p:Platform=x64` → **0 errors, 0 warnings**. Tests: **248 passed / 0 failed** (up from 227 — 21 added). **No DB delete is required**: no schema, entity or column changed.

**C1-15 — `UpdateService` orchestration moved to Core (route b).** Written test-first: the 16 new tests were run and watched fail against `NotImplementedException` stubs before any implementation existed.

- `NetworkMonitor.Core/Update/UpdateChecker.cs` — the decision ladder (unreadable tag → unreadable installed version → not newer → newer-but-no-usable-download → available) plus the exception → `Failed(...)` mapping, behind a `Func<string, CancellationToken, Task<string>>` text fetcher. Returns `UpdateCheckOutcome(Result, Cancelled)` so a check abandoned by host shutdown can still be dropped rather than shown as a failure.
- `NetworkMonitor.Core/Update/UpdateDownloader.cs` — checksum fetch, download, SHA256, verify, the partial-file delete on any failure, the whole-percent progress throttle and the folder sweep. Transport arrives as a stream-provider delegate returning `UpdateDownloadStream` (content + optional `Content-Length` + the response object it owns and disposes).
- `NetworkMonitor.Services/Update/UpdateService.cs` is now a thin adapter: HTTP transport (the GitHub `Accept` header, `ResponseHeadersRead`), the `AppPaths` location, `LastResult`/`CheckCompleted`, and the installer launch. No orchestration left in it. `AppInfo.GetVersion()` is passed in rather than read inside the ladder, which is what makes the version cases testable.
- 16 tests: 7 on the checker (including cancellation reported separately from failure, and a fetch exception logging its source), 9 on the downloader — the four gaps C1-15 named (checksum-mismatch delete, `CleanFolder` sweep, missing `Content-Length` fallback to the release size, exception → `Failed`) plus partial-delete on a mid-transfer failure, the stale-file sweep before a download, and whole-percent progress dedup driven by a 1-byte-chunk stream.

**U4-1 — C4-10 and C4-11 completed.** Details under each finding in `chunk-4-local-ui.md`. In short: the six query shapes are now fixed strings chosen by an `if` (`const` where possible, `static readonly` for the four that embed Core's discovery predicate), the optional bucket-range predicates became nullable parameters, a shared `AddParameter` helper replaced ~10 copies of the create-name-value-add block per file, and the rolling rate window moved to `NetworkMonitor.Core/Traffic/RateWindow.cs` with a running total and 5 tests. `InternetViewModel` received the same treatment, as the same code was duplicated there.

**Documentation.** C3-2's entry now records that the constructor `Refresh()` is kept deliberately; C4-10 and C4-11 record what was actually applied on each date; the `## User findings` sections of chunks 2–4 record the co-review outcome.

### Manual verification added by this phase

- The Local and Internet charts still draw correctly after the SQL rewrite, on **both** the rollup path (1 h and longer) and the raw-entry path (5 min), and **clicking a chart bucket to drill down** still filters to that bucket — that is the path the bucket-range predicates changed from appended SQL to nullable parameters.
- Live rate chips still appear and age correctly with the shared `RateWindow` on both tabs.
