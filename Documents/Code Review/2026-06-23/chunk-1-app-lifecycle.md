# Chunk 1 — App lifecycle & infra (reviewed 2026-06-23)

Files reviewed:
- `NetworkMonitor/App.xaml.cs`
- `NetworkMonitor/MainWindow.xaml.cs`
- `NetworkMonitor/SplashWindow.xaml.cs`
- `NetworkMonitor/Services/TrayIconService.cs`
- `NetworkMonitor/Services/WindowsStartupService.cs`

Overall: solid. Single-instance guard, elevation relaunch, tray, and startup-minimized logic are correct. The notable gaps are around **graceful shutdown** and **launch-time error handling**, not core logic. No Critical/data-loss bugs found.

---

## Findings

### C1-1 [RISK] Unguarded DB init in `async void OnLaunched` → crash on failure
`App.xaml.cs:137-151`. `EnsureCreatedAsync`, the `Ensure*TableAsync` calls, and `BackfillTrafficRollupsAsync` run inside `async void OnLaunched` with no surrounding try/catch, and there is **no `Application.UnhandledException` handler** registered anywhere. Any failure (DB locked, disk full, schema mismatch) escapes as an unobserved exception → process crash; a shown splash is left orphaned on screen. (The `Migrate*` helpers self-guard with try/catch, but the `Ensure*`/`Backfill` calls do not, and the retention `try` only covers lines 153-167.)
**Fix:** wrap the launch sequence in try/catch (close splash + show a fatal-error dialog, or fail fast cleanly), and/or register `UnhandledException`.
Status: **FIXED 2026-06-25 (batch 1)** — `OnLaunched` body wrapped in try/catch (closes splash + `MessageBox` on failure); registered `Application.UnhandledException` → marks handled + surfaces a `MessageBox`.

### C1-2 [RISK] Host never stopped/disposed on exit → ETW session not shut down deterministically
`App.xaml.cs:184` calls `AppHost.StartAsync()` but nothing ever calls `AppHost.StopAsync()` or disposes the host. On tray → Exit (`MainWindow.OnExitApp` → `Close` → `OnAppWindowClosing`, `MainWindow.xaml.cs:158-173`) only `CheckpointDatabase()` and `_trayIcon.Dispose()` run. Hosted services therefore never receive shutdown:
- `TrafficCollector.ExecuteAsync` registered `ct.Register(() => _session.Stop())` and its `Dispose()` disposes the ETW session — **neither runs on a normal exit** because the stopping token is only cancelled by host shutdown. The real-time kernel session `NetworkMonitorTraffic` is left relying on process-exit fallback to be torn down.
- Other BackgroundServices (ScanWorker, TrafficTracker, DigestWorker, DatabaseBackupWorker) also skip graceful stop.
**Fix:** in the exit path, `await AppHost.StopAsync()` and dispose the host before the process ends, so background services (and the ETW session) shut down deterministically. Order it before/around the existing WAL checkpoint.
Status: **FIXED 2026-06-25 (batch 1)** — `MainWindow.OnAppWindowClosing` (exit path) now calls `App.AppHost.StopAsync(5s)` + `Dispose()` via a new `StopHost()` helper, before `CheckpointDatabase()`.

### C1-3 [RISK, low] `schtasks` invoked with redirected streams that are never drained + `WaitForExit`
`WindowsStartupService.cs:36-58`. `RedirectStandardOutput`/`RedirectStandardError = true` but the streams are never read, and `WaitForExit()` is called with no timeout. This is the classic redirect deadlock: if the child fills a pipe buffer it blocks on write while the parent blocks on `WaitForExit`. `schtasks` output is small so a hang is unlikely in practice, but it's latent.
**Fix:** either stop redirecting (keep `CreateNoWindow`), or drain the streams (async `ReadToEndAsync`, or `OutputDataReceived`) before/while waiting.
Status: **FIXED 2026-06-25 (batch 3)** — `RunSchTasks` replaced by async `RunSchTasksAsync` which starts `ReadToEndAsync` on both stdout+stderr, then `await WaitForExitAsync()` + `Task.WhenAll`. Both pipes drained → no redirect deadlock.

### C1-4 [RISK, low] Splash can be orphaned if `root.Loaded` never fires
`App.xaml.cs:189-197`. The splash is closed only from the main window's `root.Loaded` handler, and only when `window.Content is FrameworkElement`. If content isn't a `FrameworkElement` or `Loaded` doesn't fire, the always-on-top splash stays forever.
**Fix:** add a fallback (close the splash unconditionally after `window.Activate()`, or on a short timeout).
Status: **FIXED 2026-06-26** — `OnLaunched` now wraps splash teardown in an idempotent local `CloseSplashOnce()` (guarded by a `splashClosed` flag). The `root.Loaded` handler calls it (when content is a `FrameworkElement`), and a non-repeating `DispatcherQueueTimer` (5s, UI thread) calls it as a fallback — so the splash closes even if content isn't a `FrameworkElement` or `Loaded` never fires. Idempotent, so whichever fires first wins and the other is a no-op.

### C1-5 [CLEANUP] Stray over-indentation
`App.xaml.cs:20` — the `Aumid` const is indented 12 spaces vs the 8-space field block around it.
Status: **FIXED 2026-06-25 (quick wins)** — re-indented to 8 spaces.

### C1-6 [CLEANUP] Empty no-op tray "show" callback
`MainWindow.xaml.cs:175-177` — `OnShowWindow()` is empty and is passed as the tray's `onShow` action, but `TrayIconService` already restores+foregrounds the window itself (`TrayIconService.cs:154-156, 179-181`). The callback does nothing. Remove it or give it a purpose.
Status: **FIXED 2026-06-25 (quick wins)** — removed `OnShowWindow()` and dropped the `onShow` parameter from `TrayIconService` (ctor + both call sites); the tray already restores/foregrounds.

### C1-7 [CLEANUP] `MainWindow.Current` hides `Window.Current` (CS0108)
`MainWindow.xaml.cs:30` — produces the persistent CS0108 build warning. Add the `new` keyword (or rename) to make the intent explicit and silence the warning.
Status: **FIXED 2026-06-25 (quick wins)** — `public static new MainWindow? Current`. CS0108 warning gone (build verified).

### C1-8 [CLEANUP] Tray icon `HICON` never `DestroyIcon`'d
`TrayIconService.cs:117` — the icon handle from `LoadImage` is never destroyed. One handle for app lifetime, freed at process exit; minor GDI hygiene only.
Status: **FIXED 2026-06-25 (quick wins)** — added `DestroyIcon` P/Invoke; track `_hIcon` + `_ownsIcon` (true only when `LoadImage` succeeded, not the shared `LoadIcon` system fallback); `Dispose` destroys it when owned.

### C1-9 [CLEANUP, optional] Forwarded relaunch args not re-quoted
`App.xaml.cs:230-233` — `RelaunchElevated` space-joins args without quoting. Correct for `--minimized`; would break for any future arg containing spaces.
Status: **FIXED 2026-06-25 (quick wins)** — forwarded args now go through `QuoteArgument` (wraps any arg containing a space in double quotes) before `string.Join`.

---

## Notes (not findings)
- `async void` event-handler overrides (`OnLaunched`, the various `…Click`/event handlers) are the correct WinUI idiom — not flagged.
- The start-minimized path activates then `SW_HIDE`s the window (`App.xaml.cs:199-205`); a brief flash at logon is possible by design — acceptable tradeoff vs. content not initialising.
- Single-instance mutex held for process lifetime (never disposed) is the standard pattern — fine.

## Triage / actions
No fixes applied during review (user preference — findings recorded only, fixes batched at end). Recommended fix-now when we get there: C1-1, C1-2 (robustness), then the cheap cleanups C1-5/C1-6/C1-7. C1-3/C1-4/C1-8/C1-9 are low-risk.

---

## Files reviewed
- `NetworkMonitor/App.xaml.cs`
- `NetworkMonitor/MainWindow.xaml.cs`
- `NetworkMonitor/SplashWindow.xaml.cs`
- `NetworkMonitor/Services/TrayIconService.cs`
- `NetworkMonitor/Services/WindowsStartupService.cs`

## User findings (raw)
Format code files
Apply code rules to code files
is this necessary "await db.Migratexxxxxx();". For now assume an db changes will need a new db.
list all registry entries in architecture.md.

## User findings (reconciled)

### U1-1 [ACTION] Format code files
Run a formatting pass over the chunk-1 source files.
Status: open (batch — fix phase)

### U1-2 [ACTION] Apply code rules to code files
Bring the chunk-1 source files into CLAUDE.md-convention compliance (single exit, blank-line blocks, no `var`, member order, braces, etc.). Overlaps with C1-5 (indent).
Status: **FIXED 2026-06-26 (convention scan, merged with U6-1)** — codebase-wide parallel audit + fixes; App.xaml.cs (blank lines) and MainWindow.xaml.cs (member order) corrected among the 10 touched files. See progress.md "Convention scan" entry.

### U1-3 [QUESTION → CLEANUP] Are the `Migrate*` calls necessary?
`App.xaml.cs:146,148` call `MigrateBandwidthToTrafficAsync()` / `MigrateAddProcessPathAsync()`. They preserve existing user data across the bandwidth→traffic rename and the added `ProcessPath` column. User's stance: a future DB change just means a fresh DB, so these in-place migrations may be removable. Deferred to **Chunk 3 (Data layer)**.
Status: open (revisit in Chunk 3)

### U1-4 [ACTION/DOC] List all registry entries in `architecture.md`
Document every registry location the app reads/writes. Known: `HKCU\SOFTWARE\Classes\AppUserModelId\{Aumid}` → `DisplayName`, `IconUri` (`App.xaml.cs:177-182`). Will sweep for others first.
Status: **FIXED 2026-06-25 (quick wins)** — swept the codebase (grep `Registry.`/`CreateSubKey`/`SetValue`): the AppUserModelId key is the **only** registry write; everything else (settings, sort, startup task) is files/`schtasks`. Added a "Registry usage" section to `architecture.md`. Also refreshed now-stale doc statements there (migrations removed, `UmnathaNetworkMonitor` folder, 3-day backup retention, instant settings save, retention via ScanWorker).


