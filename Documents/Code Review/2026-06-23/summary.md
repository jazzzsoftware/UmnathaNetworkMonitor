# Code Review — Summary (2026-06-23)

Full structured review of the NetworkMonitor codebase. Started 2026-06-23; review + co-review + all fixes completed and pushed 2026-06-26. Procedure: `../code-review-procedure.md`. Ledger (full detail): `progress.md`. Per-chunk findings: `chunk-N-<name>.md`.

## Outcome

**54 findings** (50 reviewer + 4 headline user findings, plus co-review user findings U1-x…U7-x) across 7 risk-ordered chunks. **Every finding is resolved** — fixed, or decided/deferred and then fixed. No Critical / data-loss bugs were found. The test suite grew **63 → 69** (new `CollectionReconciler` tests); every fix batch built clean (x64) with all tests green.

## Findings by chunk

| # | Chunk | Findings | Headline issues |
|---|-------|----------|-----------------|
| 1 | App lifecycle & infra | 9 | unguarded DB init in async-void `OnLaunched` (C1-1); host never `StopAsync`'d → ETW session leak (C1-2); splash orphan (C1-4) |
| 2 | Traffic capture | 8 | unguarded ETW/flush loops → `StopHost` (C2-2/C2-4); PID > 65535 dropped (C2-3); pid path re-resolved every flush (C2-6) |
| 3 | Data layer | 8 | `EnsureCreated` vs hand-DDL drift (C3-1/C3-2); non-atomic settings/sort saves (C3-4); duplicated app-data path (C3-7) |
| 4 | Scanning | 7 | concurrent manual+periodic scans (C4-3); split traffic retention (C4-4); index-defeating MAC merge (C4-2) |
| 5 | Daily digest | 5 | UI-thread PDF render (C5-2); inconsistent CSV escaping (C5-3); formula-injection (C5-4) |
| 6 | Devices & Reports UI | 9 | VM `ScanCompleted` subscription leak (C6-1); non-canonical MAC on import (C6-2); UI-thread export (C6-3); per-scan grid rebuild + 1s live reload (C6-6) |
| 7 | Backup | 4 | tight retry loop on backup failure (C7-1); unbounded growth (C7-2); non-atomic db+csv pair (C7-3) |

## Cross-cutting themes (and how they were resolved)

1. **Background-service resilience.** Default `BackgroundServiceExceptionBehavior=StopHost` means one faulting worker tears down all of them. Guarded every loop (C2-2/C2-4/C7-1), added graceful host shutdown on exit (C1-2), and a launch-time try/catch + `UnhandledException` handler (C1-1).
2. **MAC canonicalization.** One write-time rule via new `MacNormalizer` (scanner + importer + import VM); merge rewritten to match in-memory by canonical key, restoring the unique index (C6-2/C4-5/C4-2).
3. **UI-thread offload.** Report export builds after the save dialog via `Task.Run`; `schtasks` made async (C6-3/C5-2, C6-7/C1-3).
4. **Retention consistency.** Single source of truth in `ScanWorker` using `TrafficPurgeDays` for entries + rollups; 3-day backup retention (C4-4, U7-1).
5. **Resource lifetime.** Disposed Win2D brushes, `Process`, `SemaphoreSlim`, gradient fills, `CanvasPathBuilder`, tray `HICON` (C5-1/C6-9/C2-5/C4-6/C1-8).

## Notable decisions

- **C3-2** — removed all manual DDL/`Migrate*`; `TrafficRollups` is now an EF entity, `EnsureCreated` is the sole schema source.
- **C2-7** binary (1024) rate units; **C6-5** all settings persist instantly; **C7-2** 3-day backup retention.
- **C6-6** — incremental for both halves: in-place `ObservableCollection` reconcile for the device grids (`Device` made INPC-observable), and in-memory incremental for live traffic (`TrafficTracker.Flushed` carries the flushed entries).
- **U5-1** — `Services/` split into 7 concern sub-folders with matching namespaces; **explicit per-file usings** (not project-level `<Using>` globals).
- **C2-6** — pid→path cache keyed on process **start time** so recycled PIDs can't mis-attribute traffic.

## New shared components created during the fix phase

`MacNormalizer`, `CsvField` (escaping + formula-injection guard), `AtomicFile`, `AppPaths`, `CollectionReconciler`, `TrafficFlushedEventArgs`, `Views/DeviceGridSupport.cs` (`DeviceGridSort` + `DeviceDialogs`), `Models/TrafficRollup`.

## Structural / convention work (done last)

- `Services/` reorganized into `Scanning / Traffic / Digest / Csv / Backup / Platform / Common` sub-folders (U5-1); `AppTrafficTotal` → `Models` (C2-8).
- Codebase-wide convention scan (U6-1 + U1-2): 5-way parallel audit, ~38 fixes across 10 files (member order, single-exit, blank-line blocks, single-char vars, property braces, underscore constants).
- Device-page duplication removed via `DeviceGridSupport` helpers (C6-8).

## Outstanding (non-code)

- **Manual run-through of the C6-6-part-1 live-traffic incremental path** — DB/timing-bound, not unit-tested. Confirm in the real app: chart + Apps grid update smoothly each second on the 1h/6h live ranges, totals match a manual refresh, and range/app/bucket selection re-seeds correctly.
