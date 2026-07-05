# Code Review — Progress Ledger

Scope: full structured review of the NetworkMonitor codebase (existing code in place, not a single diff).
Depth: Full — correctness/concurrency/resource/error-handling bugs **plus** reuse/simplification/efficiency cleanups **plus** CLAUDE.md convention adherence.
Started: 2026-06-23
Method: reviewed subsystem-by-subsystem in place. This ledger is the source of truth across sessions — read it first on resume. Per-chunk findings live in `Documents/Review/chunk-N-<name>.md` and are summarised here.

## Review dimensions (applied to every chunk)

1. **Correctness** — logic bugs, edge cases, off-by-one, null/empty handling.
2. **Concurrency & async** — `async void`, dispatcher marshalling, the interlocked counters, background-loop cancellation.
3. **Resource lifetime** — `IDisposable`/native handles/DB connections/ETW session, `using` coverage.
4. **Error handling** — the empty `catch {}` blocks: deliberate vs swallowing real failures.
5. **Conventions** — CLAUDE.md rules (single exit, blank-line blocks, no `var`, braces, member order).

Each finding is tagged **[BUG]** / **[RISK]** / **[CLEANUP]** with `file:line`, a short rationale, and a proposed fix. Status per finding: open · fixed · deferred · won't-fix.

## Chunks

| # | Chunk | Files | State | Findings | Actioned |
|---|-------|-------|-------|----------|----------|
| 1 | App lifecycle & infra | `App.xaml.cs`, `MainWindow.xaml.cs`, `SplashWindow`, `TrayIconService`, `WindowsStartupService` | ✅ reviewed | 9 (2 risk, 2 low-risk, 5 cleanup) | 8 (C1-1, C1-2, C1-3, C1-5, C1-6, C1-7, C1-8, C1-9, U1-4) |
| 2 | Traffic capture | `TrafficCollector`, `TrafficTracker`, `TrafficWindow`, `TrafficRateFormatter`, `AppTrafficTotal` | ✅ reviewed | 8 (4 risk, 1 perf, 3 cleanup) | 4 (C2-2, C2-4, C2-5, C2-7) |
| 3 | Data layer | `AppDbContext`, `Settings`, `OuiDatabase`, `SortPreference` | ✅ co-reviewed | 8 (3 risk, 5 cleanup) + U1-3 resolved + U3-1, U3-2 | 7 (C3-1, C3-2, C3-3, C3-4, C3-6/U3-2, C3-7/U3-1, C3-8) |
| 4 | Scanning | `NetworkScanner`, `DeviceTracker`, `ScanWorker`, `DeviceNotification` | ✅ co-reviewed | 7 (3 risk, 1 perf, 2 cleanup, C3-5 resolved); no user findings | 5 (C4-2, C4-5, C4-3, C4-4, C4-6) |
| 5 | Daily digest | `DigestWorker`, `DigestSchedule`, `DigestGenerator`, `DigestSummaryBuilder`, `DigestChartRenderer`, `DigestPdfExporter`, `DigestCsvExporter` | ✅ co-reviewed | 5 (1 perf, 2 low-risk, 2 cleanup) + U5-1 | 5 (C5-1, C5-2 via C6-3, C5-3, C5-4, C5-5) |
| 6 | Devices & Reports UI | `DevicesHostPage` + device pages/VMs, `ReportsPage`/VM, `DigestReportView`, CSV import/export | ✅ co-reviewed | 9 (3 risk, 2 perf, 4 cleanup); resolves C4-5, C5-2 + U6-1 | 7 (C6-1, C6-2, C6-3, C6-4, C6-5, C6-7, C6-9) |
| 7 | Backup | `DatabaseBackupWorker` | ✅ co-reviewed | 4 (1 risk + 2 low, 1 cleanup) + U7-1 | 4 (C7-1, C7-2/U7-1, C7-3, C7-4) |

State legend: ⬜ pending · ⏳ in progress · ✅ reviewed

## Review complete — 2026-06-23

All 7 chunks reviewed (50 findings + 4 user findings; 0 fixes applied — record-only per workflow). No Critical/data-loss bugs. Awaiting user co-review, then a batch fix phase.

**Cross-cutting themes (each spans multiple chunks):**
1. **Background-service resilience / shutdown.** Default `BackgroundServiceExceptionBehavior=StopHost` means an unhandled exception in a worker loop tears down ALL background services. TrafficCollector/TrafficTracker loops are unguarded (C2-2, C2-4); the host is never `StopAsync`'d on exit so the ETW session leaks (C1-2, C2-1); and the new backup worker can tight-loop on failure (C7-1). ScanWorker/DigestWorker are the resilient role models.
2. **MAC canonicalization.** Inconsistent normalization: scanner stores canonical, importer doesn't (C6-2), merge papers over it with index-defeating SQL (C4-2), and the unique index can be bypassed → duplicate devices (C4-5/C6-2). One write-time normalization rule fixes the cluster.
3. **UI-thread blocking.** PDF/CSV export builds on the UI thread before the dialog (C6-3/C5-2); `schtasks` runs on the UI thread (C1-3/C6-7).
4. **Launch/error robustness.** Unguarded DB init in async-void OnLaunched, no UnhandledException handler (C1-1); non-atomic settings/sort saves (C3-4).
5. **Retention consistency.** Traffic purge is split between a hard-coded 7-day startup delete and the `TrafficPurgeDays` setting; rollups ignore the setting (C4-4).
6. **Cleanup/consistency.** SI-vs-binary units (C2-7), duplicated app-data path (C3-7) and save dialogs/device-page code (C6-8), several minor IDisposable leaks (C2-5, C5-1, C6-9, C1-8), `EnsureCreated`-vs-hand-DDL drift (C3-1).

**Suggested top fix priorities:** C7-1, C1-1, C2-2/C2-4 (resilience) · C1-2 (graceful shutdown) · C6-1 (VM subscription leak) · C6-2/C4-5 (canonical MAC) · C4-3 (concurrent scans) · C4-4 (retention) · C6-3/C5-2 (UI-thread export).

## Log

Chunk 1 (App lifecycle & infra): reviewed — see `chunk-1-app-lifecycle.md`. 9 findings, none Critical. Headline risks: C1-1 unguarded DB init in async-void OnLaunched (no UnhandledException handler) → crash on DB failure; C1-2 AppHost never StopAsync'd/disposed on exit → ETW kernel session + background services not shut down gracefully. Low-risk: C1-3 schtasks redirect-without-drain deadlock, C1-4 splash orphan if Loaded never fires. Cleanups: C1-5 indent, C1-6 empty OnShowWindow callback, C1-7 Current hides Window.Current (CS0108), C1-8 HICON not destroyed, C1-9 relaunch args not re-quoted. User findings U1-1..U1-4 (format, apply code rules, Migrate* necessity → revisit Ch3, list registry entries in architecture.md). Actions: pending user triage.

Chunk 2 (Traffic capture): reviewed — see `chunk-2-traffic-capture.md`. 8 findings. Concurrency model (interlocked counters) is correct. Key risk theme: default BackgroundServiceExceptionBehavior=StopHost means an unhandled exception in a traffic loop tears down ALL background services. C2-1 leftover ETW session name collision on restart (ties C1-2); C2-2 unguarded ETW start/Process faults service→StopHost; C2-3 PID array capped at 65536 → high-PID traffic silently dropped; C2-4 TrafficTracker flush loop has no try/catch (DigestWorker does) → DB error→StopHost; C2-5 Process.GetProcessById never disposed (leak/flush); C2-6 no pid→name/path caching (perf); C2-7 SI vs binary unit inconsistency; C2-8 AppTrafficTotal DTO in Services not Models. Actions: pending user triage.

Chunk 3 (Data layer): reviewed — see `chunk-3-data-layer.md`. 8 findings + U1-3 resolution. C3-1 EnsureCreated vs hand-written DDL drift hazard (TrafficRollups not an entity); C3-2 RESOLVES U1-3 — the two Migrate* methods are safe to delete under "fresh DB on schema change"; optionally make TrafficRollups an entity and drop all manual DDL; C3-3 ALTER-every-startup exception control flow; C3-4 Settings/SortPreference Save non-atomic + unguarded (corruption + UI-thread throw); C3-5 RISK verify Ch4 — OuiDatabase.Lookup doesn't dash→colon normalize, so vendor lookup fails if MACs stored with dashes (arp -a uses dashes); C3-6 Settings member order + double blank line; C3-7 app-data path duplicated 4x; C3-8 SortPreference.Load bare catch. **Co-reviewed 2026-06-25:** C3-2 DECIDED — full removal of all Migrate*/Ensure*TableAsync + make TrafficRollups an entity (closes C3-1, C3-3). User findings U3-1 (rename app-data folder NetworkMonitor→UmnathaNetworkMonitor everywhere; fold into C3-7) and U3-2 (Settings.cs properties above methods; dup of C3-6). C3-5 closed by C4-1. Remaining open: C3-4, C3-6/U3-2, C3-7/U3-1, C3-8.

Chunk 4 (Scanning): reviewed — see `chunk-4-scanning.md`. 7 findings; ScanWorker is the resilience role model (try/catch loops). C4-1 RESOLVES C3-5 (MAC normalization consistent → vendor lookup works); C4-2 merge N+1 query with Replace/ToUpper defeats MAC unique index (perf); C4-3 no mutual exclusion between manual ScanNowAsync and periodic scan → concurrent DB writes; C4-4 traffic retention inconsistent (App.OnLaunched hardcodes 7d for TrafficEntries+Rollups while ScanWorker uses TrafficPurgeDays; rollups ignore the setting); C4-5 defensive MAC re-normalization implies non-canonical rows possible (cross-ref CSV importer Ch6); C4-6 SemaphoreSlim not disposed; C4-7 ping not cancellable mid-flight. **Co-reviewed 2026-06-25: no user findings, all findings stand as written.** Note C4-2/C4-4/C4-5 interlock — canonical-MAC-on-write (C4-5) is the keystone enabling the C4-2 index fix. Suggested fix order C4-3 → C4-4 → C4-2 → C4-5, then C4-6/C4-7.

Chunk 5 (Daily digest): reviewed — see `chunk-5-daily-digest.md`. 5 findings; most polished subsystem (SDD-reviewed, pure+unit-tested builder/schedule). C5-1 CanvasPathBuilder not disposed in DrawDonut (minor COM leak); C5-2 PDF chart render synchronous in BuildPdf → verify Reports export offloads off UI thread (Ch6); C5-3 two CSV exporters use inconsistent escaping; C5-4 CSV formula-injection not mitigated (low); C5-5 DigestWorker reads DateTime.Now twice. No core correctness bugs. **Co-reviewed 2026-06-25:** C5-2 confirmed as C6-3 (UI-thread export). User finding U5-1 [cross-cutting] — refactor the whole `Services` folder into sub-folders by concern (scope is codebase-wide, not digest-only; mind namespaces/DI/.csproj/.slnx in fix phase). Remaining open: C5-1, C5-3, C5-4, C5-5, U5-1.

Chunk 6 (Devices & Reports UI): reviewed — see `chunk-6-devices-reports-ui.md`. 9 findings (largest chunk). C6-1 transient device VMs subscribe to ScanWorker.ScanCompleted and never unsubscribe → handler leak + redundant DB loads per scan; C6-2 RESOLVES C4-5 — CSV import stores non-canonical MACs (no normalize) → duplicate devices; C6-3 RESOLVES C5-2 — report export builds PDF/CSV on UI thread and before the save dialog; C6-4 async-void scan handlers unobserved; C6-5 settings persistence split (instant vs Save button); C6-6 live traffic reloads DB every 1s + collections fully rebuilt; C6-7 RunAtStartup runs schtasks on UI thread; C6-8 two save-dialog impls + duplicated device-page code; C6-9 chart gradient brushes not disposed. **Co-reviewed 2026-06-25:** user finding U6-1 [cross-cutting] — full convention scan of all C# files for member order / backing-field-above-property / SetProperty layout (AllDevicesViewModel flagged); **merges with U1-2** as one consolidated convention pass in the fix phase. All 9 findings stand. Remaining open: C6-1..C6-9, U6-1.

Chunk 7 (Backup): reviewed — see `chunk-7-backup.md`. 4 findings; self-review of DatabaseBackupWorker. C7-1 RISK (real bug introduced this session) — tight infinite retry loop if backups keep failing (delay stays Zero because no file is ever written; generic Exception swallowed and loops immediately) → add a retry floor; C7-2 no retention/unbounded growth (by design — decision pending); C7-3 db+csv not atomic; C7-4 cadence keyed off file write-time only. Actions: pending user triage. ALL 7 CHUNKS COMPLETE — see "Review complete" section above. **Co-reviewed 2026-06-25:** user finding U7-1 RESOLVES C7-2 — backup retention = keep last 3 days (prune older .db + matching .csv by shared timestamp; mind C7-3/C7-4 pairing). C7-1 remains the top fix-phase priority.

## Co-review complete — 2026-06-25

All 7 chunks co-reviewed with the user (one chunk at a time, each committed). Co-review user findings: U2-2 (TrafficRateFormatter→switch), U3-1 (rename app-data folder NetworkMonitor→UmnathaNetworkMonitor everywhere, fold into C3-7), U3-2 (Settings.cs properties above methods, dup C3-6), U5-1 (refactor Services folder into sub-folders — cross-cutting), U6-1 (full convention scan of all C# files for member order / backing-field-above-property / SetProperty layout — merges with U1-2), U7-1 (backup retention = 3 days, resolves C7-2). Decisions: C3-2 DECIDED (remove all Migrate*/Ensure*TableAsync, make TrafficRollups an entity — closes C3-1, C3-3); C7-2 RESOLVED (3-day retention). Still 0 fixes applied. **NEXT: batch FIX phase.** Suggested fix-phase batching:
- **Resilience/bugs:** C7-1 (retry floor), C1-1 (guarded DB init + UnhandledException), C2-2/C2-4 (guard traffic loops), C1-2 (graceful host shutdown).
- **MAC canonicalization cluster:** C6-2 + C4-5 + C4-2 (normalize-on-write everywhere → index-friendly merge).
- **UI-thread offload:** C6-3/C5-2 (export off UI thread, after dialog), C6-7/C1-3 (schtasks off UI thread).
- **Data integrity:** C3-4 (atomic settings/sort saves), C4-4 (single-source retention).
- **Leaks:** C6-1 (VM subscription leak), C5-1/C6-9/C2-5/C4-6 (IDisposable).
- **Structural/convention (do late, high churn):** U5-1 (Services sub-folders), U6-1+U1-2 (convention scan), C3-7+U3-1 (central path helper + rename), backup retention U7-1.
- **Decisions to apply:** C3-2 (drop migrations + TrafficRollups entity), U1-4 (registry entries in architecture.md).

## Fix phase — Batch 1 (Resilience/bugs) — 2026-06-25

DONE & verified (build clean, 63/63 tests pass). 5 findings fixed:
- **C7-1** `DatabaseBackupWorker` — added `RetryFloor` (5 min); `CreateBackupAsync` returns `bool`; loop waits the floor whenever no backup was created (exception or missing source DB) → no tight-spin.
- **C2-4** `TrafficTracker.ExecuteAsync` — flush loop body wrapped in try/catch (OCE + Exception), mirroring ScanWorker/DigestWorker.
- **C2-2** `TrafficCollector.ExecuteAsync` — now async; ETW setup + `Process()` wrapped in try/catch → ETW failure no longer faults the host.
- **C1-1** `App.OnLaunched` — whole launch sequence wrapped in try/catch (closes splash + `MessageBox` on failure); registered `Application.UnhandledException` (marks handled + `MessageBox`). Added `MessageBox` P/Invoke + `ShowFatalError`/`OnUnhandledException`.
- **C1-2** `MainWindow.OnAppWindowClosing` — exit path calls `App.AppHost.StopAsync(5s)` + `Dispose()` via new `StopHost()` before `CheckpointDatabase()`.

Still open in those chunks: C2-1 (ETW session-name collision — relates, separate batch), C2-3/C2-5/C2-6/C2-7/C2-8, C1-3..C1-9, C7-2(decided→fix)/C7-3/C7-4. NEXT: Batch 2 (MAC canonicalization: C6-2 + C4-5 + C4-2).

## Fix phase — Batch 2 (MAC canonicalization) — 2026-06-25

DONE & verified (build clean, 63/63 tests pass). 3 findings fixed:
- **New `Services/MacNormalizer.cs`** — single write-time normalization rule: `mac.Trim().Replace("-", ":").ToUpperInvariant()`. Scanner (`NormaliseMac`), import VM (`AllDevicesViewModel.NormalizeMac`), and CSV importer all route through it. (Also linked into the test project csproj.)
- **C6-2** `DeviceCsvImporter.BuildDevice` — stores `MacNormalizer.Normalize(mac)` instead of `mac.Trim()` → imported MACs canonical, no duplicate devices.
- **C4-2** `DeviceTracker.MergeAsync` — rewritten: loads all devices once into a `Dictionary<string, Device>` keyed by canonical MAC (IsKnown/Id tie-break preserved), matches in-memory; removed the per-device `Replace/ToUpper` SQL that defeated the unique index.
- **C4-5** — resolved as a cluster: normalize-on-write everywhere via `MacNormalizer`; merge's remaining normalize-on-read is cheap in-memory only, kept for robustness to legacy rows.

Note: existing dev DB may hold legacy non-canonical MAC strings; the new merge still matches them (no dupes), but a fresh DB is recommended for a clean canonical state.

## Fix phase — C4-3 (concurrent-scan gate) — 2026-06-25

DONE & verified (build clean, 63/63 tests pass). `ScanWorker` now has `_scanGate = new SemaphoreSlim(1,1)`; `RunScanAsync` wraps its body in `WaitAsync(ct)` / `Release()` (finally), so manual `ScanNowAsync` and the periodic loop serialize (queue, not skip → the "Scan Network" button never silently no-ops). Gate disposed in a new `Dispose()` override. Kills the concurrent-DbContext-write / doubled-ScanSession risk.

## Fix phase — Batch 3 (UI-thread offload) — 2026-06-25

DONE & verified (build clean, 63/63 tests pass). 4 findings fixed:
- **C6-3 (resolves C5-2)** `ReportsPage` — `SaveBytesAsync` now takes a `Func<byte[]>`, shows the save dialog FIRST, then builds the PDF/CSV via `await Task.Run(buildData)` only after a path is chosen. PDF handlers guard `report is not null` so an empty export never prompts. Win2D + QuestPDF no longer run on the UI thread, and nothing is built if the user cancels.
- **C6-7** `SettingsViewModel` — `WindowsStartupService` made async (`EnableAsync`/`DisableAsync`/`IsEnabledAsync`); the `RunAtStartup` setter offloads via `_ = ApplyStartupAsync(value)`; ctor inits the toggle via `InitializeRunAtStartupAsync()` (off-thread query marshalled back through a captured `DispatcherQueue`). No UI-thread `schtasks` blocking.
- **C1-3** `WindowsStartupService` — `RunSchTasks` → async `RunSchTasksAsync`: starts `ReadToEndAsync` on stdout+stderr, then `await WaitForExitAsync()` + `Task.WhenAll`. Both pipes drained → redirect deadlock eliminated.

DB delete: NOT necessary (UI-thread/process changes only).

## Fix phase — Data integrity (C3-4, C4-4) — 2026-06-25

DONE & verified (build clean, 63/63 tests pass). 2 findings fixed:
- **C3-4** — new `Data/AtomicFile.WriteAllText(path, contents)`: writes `<path>.tmp` then `File.Move(temp, path, overwrite:true)`, wrapped in try/catch, creates the dir. `Settings.Save()` and `SortPreference.Save()` route through it → atomic (no torn writes) and non-throwing (no IO exception surfacing from UI property setters / sort handlers).
- **C4-4** — `ScanWorker.PurgeOldHistoryAsync` now purges **both** `TrafficEntries` and `TrafficRollups` using `settings.TrafficPurgeDays` (rollups via the matching `MinuteEpoch` cutoff). Removed the hard-coded 7-day retention block from `App.OnLaunched`. Single source of truth; runs at startup (purge loop fires immediately on host start) and every 24h. Rollups now respect the user setting instead of being stuck at a fixed 7 days.

DB delete: NOT necessary (no schema change; atomic-write + retention-location only).

## Fix phase — C6-1 (VM subscription leak) — 2026-06-25

DONE & verified (build clean, 63/63 tests pass). The three device VMs (`AllDevicesViewModel`, `UnapprovedDevicesViewModel`, `DeviceHistoryViewModel`) each got a `Detach()` that unsubscribes `_scanWorker.ScanCompleted -= OnScanCompleted`. All four hosting pages (AllDevices, Approved [also uses AllDevicesViewModel], Unapproved, History) call `ViewModel.Detach()` from a new `Unloaded` handler. Two deliberate design calls: (1) **`Unloaded` not `OnNavigatedFrom`** — `DevicesHostPage` keeps each tab in its own Frame and only toggles Visibility, so navigation events don't fire on tab switches; `Unloaded` fires when the host page is discarded. (2) **`Detach()` not `IDisposable`** — transient VMs resolved from the root provider would be tracked-for-disposal and kept alive (a second leak), so a plain method is correct here; lets them GC. DB delete: NOT necessary.

## Fix phase — IDisposable leaks (C5-1, C6-9, C2-5, C4-6) — 2026-06-25

DONE & verified (build clean, 63/63 tests pass). 4 small disposal fixes:
- **C5-1** `DigestChartRenderer.DrawDonut` — `pathBuilder` now a `using` declaration (disposed per slice).
- **C6-9** `TrafficAreaChart.OnUnloaded` — disposes + nulls `_receivedFill`/`_sentFill` (null-guarded; recreated by `ChartCanvasCreateResources` on reload).
- **C2-5** `TrafficTracker.FlushAsync` — `using Process process = Process.GetProcessById(kvp.Key);` then read `process.ProcessName` (disposed each flush). C2-6 pid→name caching still open (perf).
- **C4-6** `NetworkScanner.ScanAsync` — `using SemaphoreSlim semaphore = new(...)` (safe: all ping tasks awaited via `Task.WhenAll` before return).

DB delete: NOT necessary.

## Fix phase — Backup batch (U7-1, C7-3, C7-4) — 2026-06-25

DONE & verified (build clean, 63/63 tests pass). `DatabaseBackupWorker` rewritten:
- **U7-1 (resolves C7-2)** — `RetentionDays = 3`; after each successful backup, `PruneOldBackups` deletes `networkmonitor_*.db` + `approved-devices_*.csv` older than 3 days (by parsed filename timestamp; prune wrapped in try/catch so it can't fault the backup).
- **C7-3** — CSV export wrapped in try/catch inside `CreateBackupAsync`; on failure deletes the just-written `.db` (`TryDelete`) and rethrows → `backupCreated = false` → retry-floor (5 min) retries. DB+CSV pair is now all-or-nothing.
- **C7-4** — cadence + retention key off the timestamp **embedded in the filename** (`ParseBackupTimestampUtc`: `yyyy-MM-dd_HH-mm-ss` → UTC via `AssumeLocal`), not `File.GetLastWriteTimeUtc` → immune to file touches/copies. `BackupDatabaseFile` refactored to take the full backup path.

DB delete: NOT necessary (backup logic only; no app DB schema/data change).

## Fix phase — C3-7 + U3-1 (central path helper + folder rename) — 2026-06-25

DONE & verified (build clean, 63/63 tests pass). New `Data/AppPaths.AppDataFolder` => `%LOCALAPPDATA%\UmnathaNetworkMonitor` is the single source of the app-data folder. `AppDbContext.DbPath`, `Settings.SettingsFilePath`, `SortPreference.FilePath` all combine onto it; `DatabaseBackupWorker.GetBackupDirectory` already derived from `DbPath`. C3-7 (de-dup) + U3-1 (rename) done in one edit. **DB delete: REQUIRED** — old `%LOCALAPPDATA%\NetworkMonitor` is orphaned; the new `UmnathaNetworkMonitor\` (+ `Backups\`) is created on next app launch. NEXT: C3-2 (below), then U5-1 / convention scan / small cleanups.

## Fix phase — C3-2 (drop migrations + TrafficRollups entity) — 2026-06-25

DONE & verified (build clean, 63/63 tests pass). Closes C3-1, C3-2, C3-3.
- New `Models/TrafficRollup` entity + `DbSet<TrafficRollup> TrafficRollups`; unique index `(MinuteEpoch, ProcessName)` in `OnModelCreating`.
- Deleted all manual DDL/migration methods from `AppDbContext` (`EnsureDeviceEventsTableAsync`, `MigrateBandwidthToTrafficAsync`, `EnsureTrafficEntriesTableAsync`, `MigrateAddProcessPathAsync`, `EnsureTrafficRollupsTableAsync`, `EnsureDigestReportsTableAsync`, `BackfillTrafficRollupsAsync`) + the now-unused `using System.Data.Common`.
- `App.OnLaunched`: removed the 7 Ensure*/Migrate*/Backfill calls; kept `EnsureCreatedAsync` + WAL pragma. `EnsureCreated` is now the sole schema source; zero raw DDL remains (only the retention `DELETE`s, which are DML).
- EF's `TrafficRollups` table/columns match the old hand-DDL, and the unique index keeps `TrafficTracker.UpsertRollupsAsync`'s raw `INSERT … ON CONFLICT(MinuteEpoch, ProcessName)` working.

**DB delete: REQUIRED** — schema is now built fresh by `EnsureCreated`; delete the DB so the new path (incl. EF-built `TrafficRollups`) is exercised. **Recommend a manual run to verify**: traffic capture writes entries+rollups, the traffic chart populates, and a digest generates — the tests don't cover EF schema creation or the raw ON CONFLICT against the EF-built table. NEXT: small cleanups, then U5-1 (Services sub-folders), then convention scan (last).

## Fix phase — Small cleanups (round 1) — 2026-06-25

DONE & verified (build clean, 63/63 tests pass). 3 safe in-file fixes:
- **C3-6 / U3-2** `Settings.cs` — moved `Save()` + `DetectSubnetBase()` below all instance properties (order: Properties → Public methods); removed the stray double blank line.
- **C3-8** `SortPreference.Load` — bare `catch {}` → `catch (JsonException) {}` + `catch (IOException) {}`.
- **C5-5** `DigestWorker` — sample `DateTime now = DateTime.Now` once for both `NextRunLocal` and the delay.

DB delete: NOT necessary.

## Fix phase — Decided cleanups (C2-7, C6-5) — 2026-06-25

DONE & verified (build clean, 63/63 tests pass; rate-formatter tests updated).
- **C2-7** — user chose **binary (1024)**. `TrafficRateFormatter` thresholds/divisors 1000→1024; now consistent with the size formatters. Test InlineData updated to binary-clean values.
- **C6-5** — user chose **all instant**. `SettingsViewModel` wires `PropertyChanged += OnSettingChanged` → `PersistAll()` on any change except status props + `RunAtStartup` (no recursion; `SaveStatus` excluded). `Save()` command now calls `PersistAll()`; `ChartSmoothScrolling` reverted to plain setter.

DB delete: NOT necessary. Worth a manual eyeball: Settings page changes persist immediately; traffic chart rate labels now binary.

## Fix phase — CSV pair (C5-3, C5-4) — 2026-06-25

DONE & verified (build clean, 63/63 tests pass; exporter + round-trip tests unaffected).
- **C5-3** — new shared `Services/CsvField.Escape` (conditional quoting). Both `DeviceCsvExporter` and `DigestCsvExporter` route **every** field through it; the divergent `DeviceCsvExporter.Escape` (conditional) and `DigestCsvExporter.Quote` (always-quote, partial coverage) removed. `CsvField.cs` linked into the test csproj.
- **C5-4** — `CsvField.Escape` prefixes any value starting with `= + - @` with a leading `'` before quoting, blocking CSV formula injection in Excel/Sheets. Both exporters covered.

DB delete: NOT necessary. **Deferred to U5-1 reorg:** C2-8 (move `AppTrafficTotal` to Models).

## Fix phase — Quick wins (C6-4, C1-5..C1-9, U1-4) — 2026-06-25

DONE & verified (build clean, 0 errors, CS0108 gone; 63/63 tests pass). 7 findings:
- **C6-4** — all three device-VM `OnScanCompleted` handlers wrapped in try/catch.
- **C1-5** — `Aumid` const re-indented (12→8 spaces).
- **C1-6** — removed empty `MainWindow.OnShowWindow()` + dropped the `onShow` param from `TrayIconService` (ctor + 2 call sites); tray already restores/foregrounds.
- **C1-7** — `public static new MainWindow? Current` → CS0108 warning gone.
- **C1-8** — `TrayIconService` destroys the loaded HICON in `Dispose` (`DestroyIcon`; `_ownsIcon` guards against destroying the shared `LoadIcon` system fallback).
- **C1-9** — `RelaunchElevated` forwarded args go through `QuoteArgument` (quotes args containing spaces).
- **U1-4** — swept registry usage (only the AppUserModelId key); added a "Registry usage" section to `architecture.md` and refreshed now-stale doc statements (migrations removed, `UmnathaNetworkMonitor` folder, 3-day backup retention, instant settings save, retention via ScanWorker).

DB delete: NOT necessary. Remaining: C2-1, C2-3, C2-6, C6-6, C6-8 (slightly more involved), C1-4 (splash fallback), then U5-1 (+C2-8) and the convention scan (U6-1+U1-2) last.

## Fix phase — Traffic capture residuals (C2-1, C2-3, U2-2) — 2026-06-26

DONE & verified (build clean, 0 errors; 63/63 tests pass). 3 findings fixed:
- **C2-1** `TrafficCollector` — new `StopOrphanedSession()` runs before constructing the session: scans `TraceEventSession.GetActiveSessionNames()` and, if `NetworkMonitorTraffic` is found, attaches (`TraceEventSessionOptions.Attach`) + `Stop()`s the leftover. Sits inside the existing C2-2 try/catch so cleanup faults can't tear down the host. Closes the crash-then-restart session-name collision (pairs with C1-2 graceful shutdown).
- **C2-3** `TrafficCollector` — `_counters` changed from fixed `long[65536][]` to `ConcurrentDictionary<int, long[]>`. `AddBytes` drops the `pid < Length` cap and uses `GetOrAdd(pid, static key => new long[2])` + `Interlocked.Add`; `DrainAndReset` iterates the map. High-PID traffic no longer silently dropped; lock-free single-writer/single-reader pattern preserved. (`Architecture.md` updated: array → dictionary.)
- **U2-2** `TrafficRateFormatter` — `BitsPerSecond`/`BytesPerSecond` if/else chains → `switch` expressions (relational patterns) assigned to `result`, single-exit preserved.

DB delete: NOT necessary (in-memory counter + ETW lifecycle only; no schema/data change).

**C2-6 deferred — needs a decision.** Caching pid→(name, path) is a perf win, but Windows recycles PIDs, so a lifetime cache can mis-attribute traffic to a stale process after a PID is reused. A correct cache needs invalidation (e.g. key on pid + process start time, or evict on flush miss). Left open pending a call on the staleness/perf tradeoff.

Remaining: C2-6 (decision), C6-6, C6-8, C1-4, then U5-1 (+C2-8) and the convention scan (U6-1+U1-2) last.

## Fix phase — C6-6 part 2 (incremental device grids) — 2026-06-26

DONE & verified (build clean, 0 errors, device WMC1506 warnings cleared; 69/69 tests pass — 63 + 6 new). User chose **incremental for both** halves of C6-6; this is part 2 (device grids). Part 1 (TrafficPage live 1s reload → in-memory incremental) is next, separate change.
- **`Models/Device`** now implements `INotifyPropertyChanged` (inherits `ObservableObject`; all properties hand-written `SetProperty` per conventions; computed `DisplayName`/`LastSeenLabel`/`TypeIcon` re-raised from their dependencies) + a `CopyValuesFrom(Device)`.
- **New `Services/CollectionReconciler`** — `MergeUnordered(IList,…)` (update matched in place / add new / remove gone, order irrelevant) for the canonical `_allDevices` list, and `SyncOrdered(ObservableCollection,…)` (same, plus `Move` to match target order) for the bound collection. Both key by `Id`, preserve instance identity.
- **`AllDevicesViewModel` / `UnapprovedDevicesViewModel`** — `LoadAsync` merges fresh DB results into `_allDevices` (stable instances) on the dispatcher, then `ApplyFilter` syncs `Devices` in place instead of `Devices = new ObservableCollection(...)`. Stable instances keep `MarkKnownAsync`/`DeleteAsync` correct (the displayed instance *is* the `_allDevices` instance).
- **`DeviceHistoryViewModel`** — `ApplyFilter` syncs `Events` in place (membership-only; `DeviceEvent` rows immutable so `applyValues` is a no-op).
- **Tests** — `CollectionReconcilerTests` (6): instance retention, in-place value apply, add/remove, reorder, for both helpers. `CollectionReconciler.cs` linked into the test csproj; added `CommunityToolkit.Mvvm` package ref there (linked `Device.cs` now needs `ObservableObject`).

Why instance identity matters: a true incremental sync must update the *same* `Device` instance in place (needs INPC so cells refresh) rather than swap it — otherwise selection/scroll reset and `MarkKnown`/`Delete` (which mutate the displayed instance then re-filter) would clobber state. Merge-at-load keeps `_allDevices` == displayed instances end-to-end.

DB delete: NOT necessary (no schema change; EF maps the same scalar columns; INPC is ignored by snapshot change-tracking).

## Fix phase — C6-6 part 1 (incremental live traffic) — 2026-06-26

DONE & verified (build clean, 0 errors; 69/69 tests pass). Removes the per-second SQL on the Traffic page while live.
- **`TrafficTracker.Flushed` contract change** — `EventHandler` → `EventHandler<TrafficFlushedEventArgs>` (new `Services/TrafficFlushedEventArgs` carrying the `IReadOnlyList<TrafficEntry>` just written). Only subscriber is `TrafficPage`.
- **`TrafficViewModel.ApplyLiveFlushAsync(entries)`** — boundary-aware hybrid: compute `AlignedCutoffEpoch(now, …)`; if the window slid (cutoff changed) or state is unseeded → full `LoadAsync` (SQL truth); else `ApplyFlushToWindow` accumulates the flushed bytes into the newest chart bucket (`ChartPoint with { … }`) and the per-app totals **in memory**, rebuilds the `Apps`/`ChartPoints` collections + status, no SQL. **Key correctness fact:** between bucket boundaries the window cutoff is fixed, so nothing expires from the per-app totals — pure addition is exact; expiry only happens at a boundary, where we re-seed from SQL. No per-bucket-per-app state needed.
- **Window state** seeded by `LoadAsync` via `SeedWindowState` (`TrafficLoadResult` extended with `CutoffEpoch`/`BucketSeconds`): `_windowCutoffEpoch`, `_windowBucketSeconds`, `_windowChartPoints`, `_windowAppTotals`.
- **`TrafficPage.OnTrafficFlushed`** now takes `TrafficFlushedEventArgs` and calls `ApplyLiveFlushAsync(args.Entries)` instead of `LoadAsync()`. Same live guard (≤6h, not paused, no bucket selected).
- Effect: 1h view → SQL every ~1min, 6h view → every ~6min (was every ~1s); 5m view stays SQL each tick (5s buckets ⇒ every tick is a boundary; tiny window, harmless). The chart still gets a fresh full `ChartPoints` per tick so `MigrateDisplayed` animates as before.

No double-counting: the flush writes entries to the DB *before* firing `Flushed`, so a boundary re-seed (SQL) already includes them; the in-memory delta path is discarded at each re-seed.

DB delete: NOT necessary. **Manual verification recommended** (the live aggregation path isn't unit-tested — DB-bound): on the Traffic page in live mode, confirm the chart + Apps grid update smoothly each second on the 1h/6h ranges, totals match a manual refresh, and switching ranges/selecting an app/selecting a bucket still re-seeds correctly. NEXT (C6-6 fully done): C6-8, C1-4, then U5-1 (+C2-8) and the convention scan (U6-1+U1-2) last; C2-6 still pending your decision.

## Fix phase — C1-4 (splash orphan fallback) — 2026-06-26

DONE & verified (build clean, 0 errors). `App.OnLaunched` splash teardown is now an idempotent local `CloseSplashOnce()` (guarded by `splashClosed`). It's invoked from the `root.Loaded` handler (when `window.Content is FrameworkElement`) AND from a non-repeating 5s `DispatcherQueueTimer` (UI thread) as a fallback — so the always-on-top splash can no longer orphan if content isn't a `FrameworkElement` or `Loaded` never fires. Whichever fires first closes it; the other is a no-op.

DB delete: NOT necessary (UI lifecycle only; no test coverage — App launch path isn't unit-tested). NEXT: C6-8, then U5-1 (+C2-8) and convention scan last; C2-6 still pending your decision.

## Fix phase — C6-8 part 1 (save-dialog consolidation) — 2026-06-26

DONE & verified (build clean, 0 errors; 69/69 tests pass). Collapsed the two save-dialog implementations into one (the modern IFileDialog).
- `Win32FileSaveDialog.PickSavePath` gained an optional `title` parameter → `IFileDialog.SetTitle` when provided (ReportsPage callers unaffected — param is optional/last).
- `ApprovedDevicesPage` export switched from `SaveFileDialog.Show(...)` to `Win32FileSaveDialog.PickSavePath(hwnd, "approved-devices.csv", "CSV File", ".csv", "Export Approved Devices")`.
- Deleted `Services/SaveFileDialog.cs` (legacy `GetSaveFileNameW`). `OpenFileDialog` (only open-picker) kept. `Architecture.md` file-tree updated. SDK-glob project ⇒ no csproj/slnx edit needed for the deletion.

DB delete: NOT necessary (file-picker only). **C6-8 part 2 (device-page ContentDialog/sort/copy/history dedup across the 3 pages) intentionally left open** — high-churn UI refactor; do it together with U5-1 (Services sub-folders) / U6-1 (convention scan). NEXT remaining: C6-8 part 2, U5-1 (+C2-8), convention scan (U6-1+U1-2); C2-6 still pending your decision.

## Fix phase — U5-1 + C2-8 (Services reorg into sub-folders) — 2026-06-26

DONE & verified (main build clean, 0 errors; test build clean, 69/69 pass). User chose **matching folder namespaces**.
- **28 files moved** (git-tracked renames, history preserved) into concern sub-folders, each its own namespace: `Services/Scanning` (NetworkScanner, DeviceTracker, ScanWorker, DeviceNotification, MacNormalizer), `Services/Traffic` (TrafficCollector, TrafficTracker, TrafficWindow, TrafficRateFormatter, TrafficFlushedEventArgs), `Services/Digest` (7 digest files), `Services/Csv` (DeviceCsvExporter/Importer, CsvField), `Services/Backup` (DatabaseBackupWorker), `Services/Platform` (InAppNotificationService, TrayIconService, WindowsStartupService, OpenFileDialog, Win32FileSaveDialog), `Services/Common` (CollectionReconciler).
- **C2-8** folded in: `AppTrafficTotal` → `Models/AppTrafficTotal.cs` (namespace `NetworkMonitor.Models`).
- **Explicit per-file usings** (user preference over project-level `<Using>` globals). Removed the now-dangling `using NetworkMonitor.Services;` from 23 code files (root namespace is empty) and added the specific sub-namespace usings each file needs (`using NetworkMonitor.Services.Traffic;` etc.) — compiler-driven, so dependencies are self-evident per file. No `<Using Include>` globals in either csproj.
- **Test csproj** link paths updated to the new sub-folder locations; `AppTrafficTotal` link → Models.
- slnx unchanged (SDK glob — no per-file references); `Architecture.md` Services tree rewritten to show the sub-folders.

DB delete: NOT necessary (namespaces/file-locations only; no schema/behaviour change). Closes U5-1 and C2-8. NEXT remaining: C6-8 part 2 (device-page ContentDialog dedup), convention scan (U6-1+U1-2); C2-6 still pending your decision.

## Fix phase — Convention scan (U6-1 + U1-2) — 2026-06-26

DONE & verified (main build clean 0 errors; 69/69 tests pass). Ran a 5-way parallel read-only audit of every C# source file against the CLAUDE.md conventions, then fixed all ~38 confirmed violations across 10 files. The other ~40 source files were already compliant.
- **Blank-line (rule 7)** — `SortPreference.Load` (if/try), `App.OnLaunched` ctor + `ListenForActivation`, `ScanWorker` (both loop methods, 6 spots), `NetworkScanner` (finally block), `TrafficTracker.GetProcessPath` (if/try), `TrafficViewModel` (both `await using` reader blocks).
- **Member order (rule 9)** — `AllDevicesViewModel` (`ScanNowAsync`→private section, `ShowApprovedOnly`→Properties, `Detach`→public), `UnapprovedDevicesViewModel`/`DeviceHistoryViewModel` (`ScanNowAsync`/`Detach` repositioned), `MainWindow` (`Current` property→after ctor, `NavigateToHistory`→public section), `TrafficPage` (`ViewModel`→after ctor), `TrayIconService` (`Dispose`→before private methods).
- **Single-exit (rule 6)** — `TrafficTracker.FlushAsync` refactored from two early-return guards to nested `if` blocks (no early returns).
- **Single-char vars (rule 2)** — `Device? d`→`tracked` (AllDevices ×3, Unapproved ×2); `Match m`→`match` in `NetworkScanner`.
- **Property braces (rule 11)** — `AllDevicesViewModel.ShowApprovedOnly` expanded to multi-line `{ get; set; }`.
- **Underscores (rule 12)** — `TrayIconService` 16 SCREAMING_CASE Win32 constants → PascalCase (`IMAGE_ICON`→`ImageIcon`, etc.) + all references; matches the codebase's other interop constants (`OfnFileMustExist`).

Method/audit: parallel general-purpose subagents (1 per folder group) reported file:line + rule; fixes applied by hand and re-verified. DB delete: NOT necessary (style only). **Closes U6-1 + U1-2.** Remaining review items: C6-8 part 2 (device-page ContentDialog dedup), C2-6 (pid-cache decision).

## Fix phase — C6-8 part 2 (device-page dedup) — 2026-06-26

DONE & verified (main build clean 0 errors; 69/69 tests pass). New `Views/DeviceGridSupport.cs` with two static helpers (consistent with the codebase's static-helper style; works across the differing VM types):
- **`DeviceGridSort`** — `RegisterDeviceColumns` (the 6-column map), `ApplyIndicator` (sort-direction glyphs), `HandleSorting` (column-click → sort + optional `SortPreference` save; null page-key skips the save).
- **`DeviceDialogs`** — `CopyTagToClipboard`, `NavigateToHistory`, `ShowEditDeviceAsync` (the name/type/notes panel + dialog that was copy-pasted 3×; applies fields to the Device on confirm, returns bool), `ShowDeleteConfirmAsync`.
- `AllDevicesPage` / `ApprovedDevicesPage` / `UnapprovedDevicesPage` now delegate sort-indicator / sorting / copy / history / approve-edit / delete to the helpers (each page keeps only its unique bits: highlight row, import/export, ShowApprovedOnly, notifications). `DeviceHistoryPage` uses the generic sort + copy helpers too. Net: ~200 lines of duplicated code removed; dropped now-unused `Windows.ApplicationModel.DataTransfer` usings.

DB delete: NOT necessary (UI refactor only). **Closes C6-8.** Only remaining review item: **C2-6** (pid→name/path cache — pending the PID-reuse staleness decision).

## Fix phase — C2-6 (pid→path cache, start-time keyed) — 2026-06-26

DONE & verified (main build clean 0 errors; 69/69 tests pass). User chose "do it properly".
- `TrafficTracker._pathCache` = `Dictionary<int, (DateTime StartTime, string? Path)>`; new instance method `ResolveProcessPath(int pid, Process process)` replaces the per-flush `GetProcessPath(kvp.Key)` call.
- Reads `process.StartTime`; reuses the cached path **only when the cached StartTime equals the current one** — so a recycled PID (different process ⇒ different start time) misses the cache and is re-resolved, preventing traffic mis-attribution (the staleness risk that made a naive cache unsafe).
- Cache hit ⇒ skips the `OpenProcess` + `QueryFullProcessImageName` syscalls (the cost C2-6 flagged); miss ⇒ resolves via `GetProcessPath` and stores. If `StartTime` is inaccessible (access-denied/exited process) it resolves fresh and doesn't cache (safe fallback). `ProcessName` still read from the already-obtained `Process` (free). Single-threaded flush ⇒ no locking.

DB delete: NOT necessary. **This was the last open review finding — the full code review is now complete (all findings actioned across chunks 1–7 + user findings).** Outstanding non-code item: a manual run-through of the C6-6-part-1 live-traffic incremental path (DB-bound, not unit-tested).
