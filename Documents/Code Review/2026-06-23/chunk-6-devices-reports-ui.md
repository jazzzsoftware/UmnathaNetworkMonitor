# Chunk 6 — Devices & Reports UI (reviewed 2026-06-23)

Largest chunk (ViewModels, device/traffic/reports/settings pages, CSV importer, file dialogs, the Win2D area chart). The UI logic is generally careful (the area-chart animation/freeze handling and the hand-written CSV parser are solid). The important issues are an **event-subscription leak** across the transient device VMs, the **MAC-normalization gap on import** (the concrete cause of C4-5), and **UI-thread export** (the concrete cause of C5-2).

Resolves cross-refs: **C4-5** (→ C6-2) and **C5-2** (→ C6-3).

---

## Findings

### C6-1 [RISK] Transient device VMs subscribe to `ScanWorker.ScanCompleted` and never unsubscribe
`AllDevicesViewModel.cs:24`, `UnapprovedDevicesViewModel.cs:24`, `DeviceHistoryViewModel.cs:26` each do `_scanWorker.ScanCompleted += OnScanCompleted` in their constructor. These VMs are registered **`AddTransient`** and resolved fresh in each page's constructor, while `ScanWorker` is a singleton. Every navigation to a devices tab creates a new VM that subscribes — and nothing ever unsubscribes. Result: the singleton accumulates handlers for every VM ever created (memory leak), and **each dead VM still runs `OnScanCompleted` → `LoadAsync` (a DB round-trip) on every scan**. After heavy navigation, one scan triggers many redundant DB loads.
**Fix:** unsubscribe (implement page `OnNavigatedFrom` → VM `Dispose`/detach), or make the VMs singletons, or use a weak-event pattern.
Status: **FIXED 2026-06-25 (leaks batch)** — each VM (`AllDevicesViewModel`, `UnapprovedDevicesViewModel`, `DeviceHistoryViewModel`) gained a `Detach()` that does `_scanWorker.ScanCompleted -= OnScanCompleted`. All four hosting pages (AllDevices, **Approved** [also uses AllDevicesViewModel], Unapproved, History) call `ViewModel.Detach()` from a new `Unloaded` handler. **Used `Unloaded`, not `OnNavigatedFrom`** — `DevicesHostPage` hosts the tabs in separate Frames and only toggles Visibility, so `OnNavigatedFrom` never fires on tab switches; `Unloaded` fires when the host page is discarded. **Used `Detach()`, not `IDisposable`** — these transient VMs are resolved from the root provider, which would *track* any `IDisposable` for disposal-at-exit and keep them alive (a second leak); a plain method lets them GC normally.

### C6-2 [RISK — resolves C4-5] CSV import stores non-canonical MAC addresses
`DeviceCsvImporter.BuildDevice` (`:139`) sets `MacAddress = mac.Trim()` with **no** normalization, and `AllDevicesViewModel.ImportApprovedDevicesAsync` (`:180`) inserts that candidate as-is. So importing `aa-bb-cc-…` (or lowercase) stores a MAC that doesn't match the scanner's canonical `AA:BB:CC:…` form. Consequences (per C4-5): the next scan fails to match it and creates a **duplicate** device row; the merge's `Replace/ToUpper` work-around (C4-2) is the only thing papering over it. Note the importer's own dedup *does* use `NormalizeMac` for the lookup dictionary — but then still stores the un-normalized value.
**Fix:** normalize MAC on import (same `Replace("-",":").ToUpperInvariant()` the scanner uses) before insert.
Status: **FIXED 2026-06-25 (batch 2)** — `DeviceCsvImporter.BuildDevice` now stores `MacNormalizer.Normalize(mac)`. New shared `MacNormalizer` is the single write-time rule (also used by scanner + import VM).

### C6-3 [PERF — resolves C5-2] Report export builds the document on the UI thread, before the save dialog
`ReportsPage.ExportDigestPdfClick/ExportHistoryPdfClick/ExportAllReportsCsvClick` call `ViewModel.BuildPdf(report)` / `BuildAllReportsCsv()` as an **argument** to `SaveBytesAsync`, so it's evaluated **synchronously on the UI thread and before** the save dialog is shown. `BuildPdf` renders Win2D charts (288 DPI) + runs QuestPDF — CPU-heavy work that freezes the UI, and is wasted entirely if the user cancels the dialog.
**Fix:** show the save dialog first; then build the bytes inside `Task.Run` after a path is chosen.
Status: **FIXED 2026-06-25 (batch 3)** — `SaveBytesAsync` now takes a `Func<byte[]>`, shows the save dialog first, and builds the bytes via `await Task.Run(buildData)` only after a path is chosen. PDF handlers guard `report is not null` so an empty export never prompts. Resolves C5-2.

### C6-4 [RISK, low] `async void` scan handlers can throw unobserved
`OnScanCompleted` (all three device VMs) is `async void` and calls `LoadAsync` (DB). A transient DB error throws into nothing (unobserved → possible crash). Amplified by C6-1 (many handlers). Wrap the body in try/catch.
Status: **FIXED 2026-06-25 (quick wins)** — all three `OnScanCompleted` handlers (`AllDevicesViewModel`, `UnapprovedDevicesViewModel`, `DeviceHistoryViewModel`) wrap their body in try/catch (Exception).

### C6-5 [UX/consistency] Settings persistence is split between "instant" and "Save button"
`SettingsViewModel` — `ChartSmoothScrolling` (`:138-142`) and `RunAtStartup` (`:155-166`) persist immediately inside their setters, while every other setting only persists when the **Save** command runs (`:220-240`). Mixed model is confusing: a user editing fields and *not* clicking Save still has two of them silently persisted.
Status: **FIXED 2026-06-25 (decided cleanups)** — user chose **all instant**. Constructor wires `PropertyChanged += OnSettingChanged`, which calls `PersistAll()` on any property change except the status props + `RunAtStartup` (handled separately; no recursion since `SaveStatus` is excluded). `Save()` command now just calls `PersistAll()` (button still works, redundant). `ChartSmoothScrolling` reverted to a plain setter (the handler persists it).

### C6-6 [PERF] Live traffic reloads the DB every flush; grid collections fully rebuilt
- `TrafficPage.OnTrafficFlushed` runs `ViewModel.LoadAsync()` (two SQL aggregates) on **every 1s flush** while live+≤6h, and `TrafficAreaChart` invalidates every `CompositionTarget.Rendering` frame. Bounded by window, but continuous DB + GPU work while the page is open. **FIXED 2026-06-26 (part 1, incremental live):** `TrafficTracker.Flushed` now carries the just-flushed entries (`TrafficFlushedEventArgs`); `TrafficViewModel.ApplyLiveFlushAsync` accumulates them into the newest chart bucket + per-app totals **in memory** between bucket boundaries (within a window nothing expires, so addition is exact) and only re-runs the full SQL `LoadAsync` at a bucket boundary (cutoff advances) or when unseeded. Per-tick SQL eliminated for the 1h/6h views (re-aggregates every 1min/6min instead of every second); 5m view stays SQL-backed each tick by design (tiny window). The chart still receives a fresh full `ChartPoints` collection per tick (its `MigrateDisplayed` animation depends on that). `TrafficWindow.AlignedCutoffEpoch` (unit-tested) is the boundary check.
- Device VMs' `ApplyFilter` replaces the whole `ObservableCollection` on every scan/filter/sort (`Devices = new ObservableCollection<…>`), forcing a full DataGrid rebuild and losing row selection.
Status: **FIXED 2026-06-26 (both parts; user chose incremental for both).** Part 1 = TrafficPage live reload (above); part 2 = device grids (below). `Device` now implements `INotifyPropertyChanged` (via `ObservableObject`, hand-written `SetProperty`; computed `DisplayName`/`LastSeenLabel`/`TypeIcon` re-raised) + a `CopyValuesFrom`. New `Services/CollectionReconciler` (`MergeUnordered` for `_allDevices`, `SyncOrdered` for the bound `ObservableCollection`) reconciles by `Id` in place — matched rows updated, new added, gone removed, order fixed via `Move`. `AllDevicesViewModel`/`UnapprovedDevicesViewModel` merge fresh DB loads into `_allDevices` (stable instances) then sync `Devices`; `DeviceHistoryViewModel` syncs `Events` (membership-only, events immutable). No per-scan rebuild/flicker/scroll-reset; instance identity preserved (keeps MarkKnown/Delete correct). Bonus: INPC on `Device` cleared the device `WMC1506` OneWay-binding warnings. 6 new reconciler unit tests. Part 1 (TrafficPage 1s reload → in-memory incremental) tracked next.

### C6-7 [CLEANUP] `RunAtStartup` toggle runs `schtasks` synchronously on the UI thread
`SettingsViewModel.RunAtStartup` setter calls `_startupService.Enable()/Disable()`, which spawn `schtasks` and `WaitForExit` (see C1-3) — blocking the UI thread during the toggle.
Status: **FIXED 2026-06-25 (batch 3)** — `WindowsStartupService` methods are now async (`EnableAsync`/`DisableAsync`/`IsEnabledAsync`); the setter offloads via `_ = ApplyStartupAsync(value)`, and the ctor inits `RunAtStartup` via `InitializeRunAtStartupAsync()` (off-thread query, result marshalled back through the dispatcher). No UI-thread `schtasks` blocking.

### C6-8 [CLEANUP] Two save-dialog implementations + duplicated device-page code
- `Win32FileSaveDialog` (IFileDialog COM, used by Reports) and `SaveFileDialog` (`GetSaveFileNameW`, used by Approved export) are two implementations of the same thing; `OpenFileDialog` is a third comdlg32 wrapper. Consolidate.
- `AllDevicesPage`, `ApprovedDevicesPage`, `UnapprovedDevicesPage` duplicate near-identical sort-indicator / copy / history / approve-edit `ContentDialog` code (the approve/edit dialog is copy-pasted ~3×). Extract a shared base/page or helper.
Status: **FIXED 2026-06-26 (both parts).** Part 1 (save dialogs): standardised on the modern IFileDialog — `Win32FileSaveDialog.PickSavePath` gained an optional `title` param (→ `IFileDialog.SetTitle`); `ApprovedDevicesPage` export calls it; legacy `SaveFileDialog.cs` (`GetSaveFileNameW`) deleted; `OpenFileDialog` kept. Part 2 (device-page dedup): new `Views/DeviceGridSupport.cs` with two static helpers — `DeviceGridSort` (`RegisterDeviceColumns`, `ApplyIndicator`, `HandleSorting`) and `DeviceDialogs` (`CopyTagToClipboard`, `NavigateToHistory`, `ShowEditDeviceAsync`, `ShowDeleteConfirmAsync`). `AllDevicesPage`/`ApprovedDevicesPage`/`UnapprovedDevicesPage` route sort-indicator/sorting/copy/history and the approve-edit + delete `ContentDialog`s through them (the ~50-line approve/edit dialog that was copy-pasted 3× now lives once); `DeviceHistoryPage` also uses the generic sort + copy helpers (`HandleSorting` with a null page-key skips the SortPreference save). Static-helper approach chosen to match the codebase convention (MacNormalizer/CsvField/CollectionReconciler) and to work across the differing VM types.

### C6-9 [CLEANUP] `CanvasLinearGradientBrush` fills not disposed on chart unload
`TrafficAreaChart` — `_receivedFill`/`_sentFill` (created in `ChartCanvasCreateResources`) are `IDisposable` but never disposed in `OnUnloaded`. Minor COM leak per page navigation. (The `Rendering` hook and `ChartCanvas.RemoveFromVisualTree()` cleanup is correctly done — good.)
Status: **FIXED 2026-06-25 (leaks batch)** — `OnUnloaded` now disposes and nulls both `_receivedFill` and `_sentFill` (null-guarded; recreated by `ChartCanvasCreateResources` on reload).

---

## Notes (not findings)
- **Strength:** `TrafficPage` unsubscribes `_trafficTracker.Flushed` in `OnNavigatedFrom`, and `TrafficAreaChart` unhooks `CompositionTarget.Rendering` on unload — the correct pattern the device VMs (C6-1) are missing.
- **Strength:** `DeviceCsvImporter.ParseRows` is a proper quoted-CSV parser (handles embedded quotes/commas/newlines).
- **Strength:** `TrafficViewModel` is a singleton and does parameterized raw SQL on a background thread (`Task.Run`) with a minimum-spinner UX.
- `TrafficViewModel.FormatBytes` uses binary 1024 (consistent with digest); rate labels use SI via `TrafficRateFormatter` (the C2-7 inconsistency).
- Win32 dialog COM/HGlobal handles are released in `finally` blocks — looks correct.

## Triage / actions
No fixes applied (record-only). Priority when fixing: C6-1 (leak + redundant loads), C6-2 (canonical MAC on import — pairs with C4-5), C6-3 (UI-thread export — pairs with C5-2). Then C6-4. C6-5..C6-9 are UX/perf/cleanup.

---

## Files reviewed
- `NetworkMonitor/ViewModels/AllDevicesViewModel.cs`
- `NetworkMonitor/ViewModels/UnapprovedDevicesViewModel.cs`
- `NetworkMonitor/ViewModels/DeviceHistoryViewModel.cs`
- `NetworkMonitor/ViewModels/TrafficViewModel.cs`
- `NetworkMonitor/ViewModels/SettingsViewModel.cs`
- `NetworkMonitor/ViewModels/ReportsViewModel.cs` (read in Chunk 5 context)
- `NetworkMonitor/Views/DevicesHostPage.xaml.cs`
- `NetworkMonitor/Views/AllDevicesPage.xaml.cs`
- `NetworkMonitor/Views/ApprovedDevicesPage.xaml.cs`
- `NetworkMonitor/Views/UnapprovedDevicesPage.xaml.cs`
- `NetworkMonitor/Views/DeviceHistoryPage.xaml.cs` (handlers reviewed)
- `NetworkMonitor/Views/TrafficPage.xaml.cs`
- `NetworkMonitor/Views/ReportsPage.xaml.cs`
- `NetworkMonitor/Views/SettingsPage.xaml.cs`
- `NetworkMonitor/Views/Controls/TrafficAreaChart.xaml.cs`
- `NetworkMonitor/Views/Controls/DigestReportView.xaml.cs` (read in Chunk 5 context)
- `NetworkMonitor/Services/DeviceCsvImporter.cs`
- `NetworkMonitor/Services/DeviceCsvExporter.cs` (read in Chunk 0 context)
- `NetworkMonitor/Services/OpenFileDialog.cs`
- `NetworkMonitor/Services/SaveFileDialog.cs`
- `NetworkMonitor/Services/Win32FileSaveDialog.cs`

## User findings (reconciled)

### U6-1 [ACTION — cross-cutting] Full convention scan: member order (Fields/Properties) across all files
`AllDevicesViewModel.cs` has fields and properties mixed up (backing fields not directly above their property; order not Fields → Constructor → Properties → Public methods → Override → Private). Same problem likely in other files. **Do a full scan of every C# file** for CLAUDE.md code rules — especially member ordering, the backing-field-above-its-property rule, and the hand-written `SetProperty` property layout.
**Scope:** codebase-wide; **subsumes/merges with U1-2** (the CLAUDE.md convention scan deferred from Chunk 1). Treat as one consolidated convention pass in the fix phase.
Status: **FIXED 2026-06-26 (convention scan, with U1-2)** — 5-way parallel audit of all C# files; ~38 violations fixed across 10 files (member order incl. AllDevicesViewModel, blank-lines, single-exit, single-char vars, property braces, underscore constants). See the "Convention scan" fix-phase entry in progress.md.

