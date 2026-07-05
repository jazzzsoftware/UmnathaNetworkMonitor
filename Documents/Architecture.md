# Network Monitor — Architecture

## Stack

| Layer | Technology |
|---|---|
| UI framework | WinUI 3 (Windows App SDK), unpackaged |
| Runtime | .NET 10 |
| MVVM | CommunityToolkit.Mvvm (`ObservableObject`, `[RelayCommand]`) |
| Data grid | CommunityToolkit.WinUI.UI.Controls.DataGrid 7.x |
| ORM | EF Core 10 + SQLite (`EnsureCreated`, no migrations) |
| DI / hosting | Microsoft.Extensions.Hosting, BackgroundService |
| Per-process traffic | ETW kernel network provider (Microsoft.Diagnostics.Tracing / TraceEvent) |
| Chart rendering (reports) | Win2D (Microsoft.Graphics.Canvas) |
| PDF export | QuestPDF (Community licence) |
| Notifications | Windows toast notifications + in-app toast banner |

## Process model

The app **requires administrator rights** — the ETW kernel network session used for per-process traffic capture is admin-only. `App` checks for elevation at startup and, if not elevated, relaunches itself with the `runas` verb (forwarding its command-line arguments) and exits.

A **single-instance** guard uses a named mutex plus a named `EventWaitHandle`. A second launch signals the event and exits; the running instance listens for that signal and restores/foregrounds its window.

```
App() ctor
 ├─ IsElevated()? ── no ──► RelaunchElevated() (runas, forwards args) ──► Exit
 └─ yes
     ├─ acquire single-instance mutex ── already held ──► signal activation event ──► Exit
     ├─ start activation listener thread
     └─ build IHost (DI registration), then OnLaunched()
```

## Project layout

```
NetworkMonitor/
├── App.xaml.cs               Elevation + single-instance, IHost build, DI, DB init, startup window handling
├── MainWindow.xaml.cs        NavigationView shell, tray icon, toast/digest dispatch, window-placement persistence
├── SplashWindow.xaml.cs      Startup splash (suppressed when launched minimized)
│
├── Data/
│   ├── AppDbContext.cs       EF Core context; DbPath → LocalApplicationData; schema via EnsureCreated
│   ├── OuiDatabase.cs        Loads oui.txt → MAC prefix → vendor name
│   ├── Settings.cs           Scan, traffic, digest, notification and window settings; persisted to settings.json
│   └── SortPreference.cs     Per-page sort state persisted to LocalApplicationData
│
├── Models/
│   ├── Device.cs             Persisted device record (MAC, IP, hostname, IsApproved, etc.)
│   ├── DeviceEvent.cs        Single appeared/disappeared event linked to a Device
│   ├── DeviceEventType.cs    Enum: Appeared | Disappeared
│   ├── DeviceType.cs         Enum: Unknown | Router | Switch | WiFi | PC | Server | Mobile | Camera | ...
│   ├── ScanSession.cs        Metadata for each completed scan run
│   ├── TrafficEntry.cs       Per-flush per-process byte counters (raw, 7-day retention)
│   ├── TrafficAppRow.cs      Aggregated per-process row for the Traffic page grid
│   ├── AppTrafficTotal.cs    Per-process totals over a period (digest input)
│   ├── ChartPoint.cs         Point for the Traffic area chart
│   ├── DigestReport.cs       Persisted digest row (period, headline, serialised summary JSON)
│   └── DigestSummary.cs      Full digest payload (devices + traffic) serialised into DigestReport
│
├── Services/   (grouped by concern; each sub-folder is its own namespace, e.g. NetworkMonitor.Services.Traffic)
│   ├── Scanning/
│   │   ├── NetworkScanner.cs        Ping sweep + ARP parse + DNS resolve → ScannedDevice list
│   │   ├── DeviceTracker.cs         Merges scan results into the database
│   │   ├── ScanWorker.cs            PeriodicTimer scan loop; daily history auto-purge
│   │   ├── DeviceNotification.cs    DTO carrying notification data between services and UI
│   │   └── MacNormalizer.cs         Single write-time MAC canonicalisation rule
│   ├── Traffic/
│   │   ├── TrafficCollector.cs      ETW kernel TCP/UDP session → per-PID byte counters (BackgroundService)
│   │   ├── TrafficTracker.cs        Periodic flush of counters → process name/path → TrafficEntries + TrafficRollups
│   │   ├── TrafficFlushedEventArgs.cs  Carries the just-flushed entries to the Traffic page
│   │   ├── TrafficWindow.cs         Time-window helpers for the Traffic page
│   │   └── TrafficRateFormatter.cs  Byte/rate formatting helpers
│   ├── Digest/
│   │   ├── DigestGenerator.cs       Builds + persists a DigestReport for a period; raises ReportGenerated
│   │   ├── DigestSummaryBuilder.cs  Pure builder: events + devices + traffic → DigestSummary
│   │   ├── DigestSchedule.cs        Pure schedule maths: next run + missed-window catch-up
│   │   ├── DigestWorker.cs          Daily digest loop, catch-up, report purge (BackgroundService)
│   │   ├── DigestChartRenderer.cs   Win2D bar + donut charts → PNG (rendered at 288 DPI for crisp output)
│   │   ├── DigestPdfExporter.cs     QuestPDF document (charts + tables) → PDF bytes
│   │   └── DigestCsvExporter.cs     One/all digest reports → CSV
│   ├── SpeedTest/
│   │   ├── SpeedTestService.cs      Cloudflare download/upload/latency measurement (self-bounded, 120s)
│   │   ├── SpeedTestWorker.cs       Hourly speed-test loop; RunNowAsync for on-demand (BackgroundService)
│   │   ├── SpeedTestMath.cs         Pure throughput/jitter maths (unit-tested)
│   │   ├── SpeedTestMessage.cs      Status-message helper
│   │   └── SpeedTestCompletedEventArgs.cs  Carries the latest result to the UI
│   ├── Csv/
│   │   ├── DeviceCsvExporter.cs     Export device list to CSV
│   │   ├── DeviceCsvImporter.cs     Import device list from CSV
│   │   └── CsvField.cs              Shared CSV escaping + formula-injection guard
│   ├── Backup/
│   │   └── DatabaseBackupWorker.cs  Daily timestamped DB backup + approved-devices CSV (BackgroundService)
│   ├── Platform/
│   │   ├── AppLog.cs                Opt-in diagnostic file logger (app/scan events + exceptions, no PII)
│   │   ├── InAppNotificationService.cs  Raises in-app toast-banner messages
│   │   ├── TrayIconService.cs       Win32 system tray icon + context menu (Show / Exit)
│   │   ├── WindowsStartupService.cs Enable/disable "start with Windows" via schtasks onlogon task
│   │   └── OpenFileDialog.cs / Win32FileSaveDialog.cs  Win32 file pickers (open + IFileDialog save)
│   └── Common/
│       ├── CollectionReconciler.cs  In-place ObservableCollection/list reconcile by key (no full rebuild)
│       └── Watchdog.cs              Runs an async operation under a timeout; abandons + cancels a hung await
│
├── ViewModels/
│   ├── AllDevicesViewModel.cs      Devices grid (last 24h), scan command, mark-known logic
│   ├── UnapprovedDevicesViewModel.cs  Unknown-device grid + approve actions
│   ├── DeviceHistoryViewModel.cs   Per-device event history + search
│   ├── TrafficViewModel.cs         Live per-process traffic + area chart state
│   ├── ReportsViewModel.cs         Digest list, latest/selected summaries, generate/delete/export
│   └── SettingsViewModel.cs        Settings load/save, manual purge, startup toggle
│
└── Views/
    ├── DevicesHostPage.xaml(.cs)     Host with a SelectorBar: Devices | Approved | Unapproved | History
    ├── AllDevicesPage.xaml(.cs)      Live device grid (last 24 hours)
    ├── ApprovedDevicesPage.xaml(.cs) Known/approved devices with edit/delete
    ├── UnapprovedDevicesPage.xaml(.cs) Unapproved devices with approve action
    ├── DeviceHistoryPage.xaml(.cs)   Per-device appeared/disappeared event log
    ├── TrafficPage.xaml(.cs)         Live per-process traffic grid + area chart
    ├── ReportsPage.xaml(.cs)         Daily digest viewer + history + PDF/CSV export
    ├── SettingsPage.xaml(.cs)        Settings form with sticky Save footer
    └── Controls/
        ├── TrafficAreaChart.xaml(.cs)  Live stacked area chart with smooth scrolling
        └── DigestReportView.xaml(.cs)  Reusable digest renderer (charts + tables) for the Reports page
```

## Shell & navigation

`MainWindow` hosts a `NavigationView` with four destinations:

| Nav item | Page | Notes |
|---|---|---|
| Traffic | `TrafficPage` | Default page on launch |
| Devices | `DevicesHostPage` | Inner `SelectorBar`: Devices / Approved / Unapproved / History |
| Reports | `ReportsPage` | Daily digest viewer |
| Settings | `SettingsPage` | |

`DevicesHostPage` lazy-navigates each inner frame on first selection. `MainWindow.NavigateToHistory(mac)` deep-links from any device into its history tab.

## Scanning pipeline

```
ScanWorker (PeriodicTimer)
    └─► NetworkScanner.ScanAsync()
            ├─ Ping sweep (parallel, MaxParallelPings concurrency)
            ├─ Parse `arp -a` output → MAC addresses for responding IPs
            ├─ DNS reverse lookup → hostnames
            └─ OuiDatabase lookup → vendor names
    └─► DeviceTracker.MergeAsync()
            ├─ Match each result to existing DB record by MAC address
            ├─ Create new Device (IsApproved=false) for first-seen MACs
            ├─ Mark offline devices that did not respond
            ├─ Write DeviceEvent rows for appearances and disappearances
            └─ Return notifications for changed devices
    └─► ScanWorker raises ScanCompleted + DeviceStatusChanged events
            ├─ MainWindow updates Last Device Scan / Next Device Scan labels
            ├─ MainWindow dispatches Windows toasts; InAppNotificationService shows the in-app banner
            └─ Device view models reload
```

Every ping and the `arp -a` process are **cancellable** — the ping uses the `SendPingAsync(host, TimeSpan, …, CancellationToken)` overload with a hard backstop deadline (`PingTimeoutMs` + 2 s buffer), and the ARP read has its own 10 s timeout that kills a stuck process. This matters because the whole scan runs under a **watchdog** (see [Hang protection](#hang-protection)): a scan (periodic *or* manual `ScanNowAsync`) is serialised through one `SemaphoreSlim` gate and bounded by `ScanWorker.ScanTimeout` (2 min). Both pieces are needed together — the watchdog can only unblock the gate if the awaits inside the scan actually observe cancellation. A `ct.ThrowIfCancellationRequested()` after `ScanAsync` prevents a timed-out scan from merging a bogus empty result (which would wrongly mark every device offline).

## Traffic pipeline

Per-process network usage is captured from the kernel, not by polling adapters.

```
TrafficCollector (BackgroundService)
    └─ ETW kernel session (NetworkTCPIP): TcpIpSend/Recv + UdpIpSend/Recv
    └─ accumulates bytes per PID into a lock-light ConcurrentDictionary<int, long[]> counter map

TrafficTracker (BackgroundService, every TrafficIntervalSeconds)
    └─ DrainAndReset() snapshot of the counters
    └─ resolve PID → process name + full image path (QueryFullProcessImageName)
    └─ write raw rows to TrafficEntries
    └─ upsert per-minute aggregates into TrafficRollups (ON CONFLICT … DO UPDATE)
    └─ raise Flushed → TrafficViewModel refreshes the grid + area chart
```

- **TrafficEntries** holds raw per-flush rows and is purged per `TrafficPurgeDays` (default 7).
- **TrafficRollups** holds per-minute, per-process aggregates and is the long-lived source for digest traffic totals.

### Traffic page — live vs paused

Each flush re-sorts the app list and replaces the grid's `ItemsSource`, which resets the CommunityToolkit `DataGrid` scroll to the top. So the user cannot normally scroll to read/reach the bottom of the list while traffic is live. The page solves this with a single **pause** state driven by one field, `_pauseReason` (`None | Badge | Scroll | Bucket`):

- **Live** (`None`) — chart scrolls, list auto-updates and re-sorts.
- **Paused** (any non-`None`) — both chart and list freeze. `OnTrafficFlushed` drops the flush entirely when `_pauseReason != None`, so nothing rebuilds the list. Background **data collection continues** (`TrafficTracker` keeps writing `TrafficEntries`/`TrafficRollups`); only the on-screen refresh is suspended, and a resume reloads the full history including everything captured while paused.

**Indicator:** a single **badge**, horizontally centred on the chart card, shows the state for every trigger — green **Live**, amber **Paused**, or amber **History** for a pinned bucket (with a ✕ when paused). Tapping the badge resumes. There is no separate list pill; an earlier per-list pill was removed as redundant once the badge already reflected the paused state.

| Trigger | Chart | List | Result mode |
|---|---|---|---|
| **Scroll the list down** (natural pause) | freezes | holds position, scroll freely to the bottom | badge → amber Paused |
| **Tap the badge** while Live | freezes | freezes | Paused (Badge) |
| **Click a chart bar** | freezes at that time | shows totals at that time (dashed marker) | Paused (Bucket) |
| **Tap the badge** | resumes | reloads at top | Live |
| **Click an app row** while paused | re-scopes chart to that app, stays frozen | stays put (chart-only reload) | unchanged |
| **Range button** | reloads for new range | reloads at top | Live |
| **Nav away** — switch to the Speed Test tab, or to another NavigationView section | resumes | reloads at top | Live |
| **Scroll to top** while paused | *no effect* | *no effect* | stays Paused (resume is explicit only) |

Auto-resume on scroll-to-top is deliberately **not** implemented: a programmatic list rebuild snaps the scrollbar to `0`, which is indistinguishable from a user scrolling up, so treating `0` as "resume" caused the pause to oscillate. Resume is therefore explicit (badge/range/nav) only.

**Single-instance requirement.** The pause lives on the `TrafficPage` view instance, so exactly one live instance may be subscribed to `TrafficTracker.Flushed` — otherwise an unpaused orphan keeps rebuilding the shared list and defeats the pause. Two safeguards enforce this:

- `MainWindow.NavViewLoaded` selects the first nav item (which already navigates to `TrafficHostPage`) and only calls `ContentFrame.Navigate` if the frame is still empty — preventing a duplicate host/page at startup.
- `TrafficPage` subscribes to `Flushed` on `Loaded` and unsubscribes on `Unloaded` (not `OnNavigatedTo`/`OnNavigatedFrom`, which do **not** fire for a page hosted in an inner `Frame` when its outer host is swapped). Any orphaned page detaches the moment it leaves the visual tree, which also covers the repeated Traffic⇄Devices navigation leak.

Because the inner tab switch (Traffic ⇄ Speed Test) only toggles `Frame.Visibility` and does not navigate, `TrafficHostPage` explicitly calls `TrafficPage.ResetToLive()` when leaving the Traffic tab so it returns Live.

## Daily digest pipeline

```
DigestWorker (BackgroundService)
    ├─ on start: CatchUpAsync() + PurgeOldReportsAsync()
    └─ loop: wait until DigestSchedule.NextRunLocal(DigestGenerationHour), then catch-up + purge

CatchUpAsync(isStartup)
    ├─ startup, and no report exists yet (e.g. fresh/deleted DB):
    │     generate ONE report for [most recent 06:00 boundary → now] — but ONLY if
    │     HasDataAsync finds traffic/events/devices in that window; otherwise skip
    │     (no empty "nothing happened" digest on a brand-new database)
    └─ otherwise (scheduled loop, or startup when reports already exist):
          DigestSchedule.MissedWindows(lastScheduledEnd, now, hour, retention)
          → generates one report per missed daily window (covers downtime)

DigestGenerator.GenerateAsync(startUtc, endUtc, isScheduled)
    ├─ load DeviceEvents in period + all Devices
    ├─ load per-process traffic totals from TrafficRollups (AppTrafficTotal)
    ├─ DigestSummaryBuilder.Build(...) → DigestSummary (pure, unit-tested)
    ├─ persist DigestReport (summary serialised to SummaryJson)
    └─ raise ReportGenerated → MainWindow shows a "Daily digest ready" toast (if DigestNotify)
```

Reports are read back on the Reports page via `ReportsViewModel`, which deserialises `SummaryJson` into a `DigestSummary` and feeds `DigestReportView`. Export paths:

- **PDF** — `DigestPdfExporter` (QuestPDF) embeds Win2D-rendered chart PNGs and device/traffic tables.
- **CSV** — `DigestCsvExporter` exports the selected report or all reports.

Digest charts are rasterised by `DigestChartRenderer`. The **on-screen preview** renders at the display scale (`96 × XamlRoot.RasterizationScale`) so it stays crisp without decoding needlessly large bitmaps, while the **PDF export** keeps **288 DPI** so charts stay sharp when printed or zoomed (the DPI is a parameter on the render methods; the PDF path uses the default).

`GenerateNowAsync` produces an immediate (unscheduled, `IsScheduled = false`) report covering the last 24 hours without disturbing the scheduled cadence. Because catch-up anchors on the last **scheduled** report only, a manual report never advances the cursor.

**First launch after a DB delete:** the startup catch-up runs against an empty database (before the first scan or traffic flush), so `HasDataAsync` returns false and **no report is generated on that first launch**. Once the app has collected devices/events/traffic, the next launch's startup catch-up finds data and generates the report — which is why a digest appears on the *second* launch, not the first. Use **Generate now** (or wait for the scheduled hour) to produce one immediately.

## Data model

```
Device
  Id, MacAddress (unique index), IpAddress, Hostname
  FriendlyName, Vendor, Type, Notes
  IsApproved, IsOnline, FirstSeen, LastSeen

DeviceEvent
  Id, DeviceId (FK → Device, cascade delete)
  EventType (Appeared | Disappeared), Timestamp

ScanSession
  Id, StartedAt, CompletedAt
  DevicesFound, NewDevices, DevicesGone

TrafficEntry            (raw, 7-day retention)
  Id, Timestamp, ProcessName, ProcessPath
  BytesUploaded, BytesDownloaded
  index (Timestamp, ProcessName)

TrafficRollups          (per-minute aggregate; long-lived)
  Id, MinuteEpoch, ProcessName, ProcessPath
  BytesUploaded, BytesDownloaded
  unique index (MinuteEpoch, ProcessName)

DigestReport
  Id, PeriodStart, PeriodEnd, GeneratedAt
  Headline, SummaryJson, IsScheduled
```

The database uses `EnsureCreated` (no EF migrations) as the **sole** schema source: every table — including `TrafficRollups` — is an EF entity, so `EnsureCreated` builds the full schema on a fresh database. There is no hand-written DDL, no `CREATE TABLE IF NOT EXISTS` guards, and no in-place `ALTER`/rename migrations. A breaking schema change means deleting the database and letting `EnsureCreated` rebuild it. WAL mode is enabled on startup. The only raw SQL remaining is the retention `DELETE`s and the per-minute rollup upsert (`INSERT … ON CONFLICT`).

## Data retention

| Data | Default retention | Configurable | Mechanism |
|---|---|---|---|
| Device history (`DeviceEvents`, `ScanSessions`) | 30 days | ✅ `Settings.HistoryPurgeDays` (Settings → Device) | `ScanWorker` 24h purge loop (0 = disabled) |
| Traffic — raw rows (`TrafficEntries`) | 7 days | ✅ `Settings.TrafficPurgeDays` (Settings → Traffic) | `ScanWorker` purge loop |
| Traffic — per-minute rollups (`TrafficRollups`) | 7 days | ✅ `Settings.TrafficPurgeDays` | `ScanWorker` purge loop |
| Speed test results (`SpeedTestResults`) | 7 days | ✅ `Settings.TrafficPurgeDays` (folded into traffic purge) | `ScanWorker` purge loop |
| Daily digests (`DigestReports`) | 30 days | ✅ `Settings.DigestPurgeDays` (Settings → Other) | `DigestWorker` purge |
| Database backups (`.db` + approved-devices `.csv`) | 3 days | ❌ `DatabaseBackupWorker.RetentionDays` const | pruned after each successful backup |
| Diagnostic logs (`Log-*.txt`) | 7 days | ❌ `AppLog.RetentionDays` const | pruned on startup when logging is enabled |

## Diagnostic logging

Logging is **off by default** and toggled by `Settings.EnableLogging` (Settings → Other → "Enable diagnostic logging", with an "Open logs folder" link). When enabled, `AppLog` writes a daily file `Log-yyyyMMdd.txt` to `%LOCALAPPDATA%\UmnathaNetworkMonitor\Logs\`:

- **Info** entries — app start (with version) and stop, scan start / scan completed (device counts only), speed-test completed (throughput/latency figures only), and **watchdog timeout notices** when a worker abandons a stuck cycle.
- **Error** entries — the global `App.UnhandledException` handler plus the previously-silent `catch` blocks in the background services (`ScanWorker`, `TrafficTracker`, `TrafficCollector`, `SpeedTestWorker`, `DigestWorker`, `DatabaseBackupWorker`), `AtomicFile`, and `MainWindow` shutdown.

No device or network identifiers (MAC, IP, hostname) are ever written — only counts and operation names — so logs are safe for an end user to share. Normal shutdown cancellation is not recorded as an error. `AppLog` is a dependency-free static logger (no Serilog/Sentry); the App SDK / Sentry route was rejected because the app runs elevated.

## Settings persistence

`Settings` is loaded from `settings.json` at startup (falling back to `appsettings.json` defaults, with subnet auto-detection, if the file does not exist) and registered as a singleton. Settings changes persist **immediately**: `SettingsViewModel` writes the singleton back to `settings.json` on any property change (atomic temp-file + rename via `AtomicFile`). The Settings page Save button re-saves but is redundant. Scan changes take effect on the next scan cycle — no restart required. Window placement (`WindowX/Y/Width/Height`, `WindowMaximized`) is also persisted in `settings.json` and restored on launch.

## Registry usage

The app writes a single registry location (current-user only; no machine-wide keys):

| Key | Values | Purpose |
|---|---|---|
| `HKCU\SOFTWARE\Classes\AppUserModelId\{Aumid}` (where `Aumid` = `NetworkMonitor.App`) | `DisplayName` = `Umnatha Network Monitor`; `IconUri` = `<install>\Assets\app.ico` | Registers the AppUserModelID so Windows toast notifications show the correct app name and icon. Written on each launch in `App.OnLaunched`. |

No other registry keys are read or written. "Start with Windows" uses a Scheduled Task (`schtasks`), **not** a `Run` registry key.

## Background services

Registered as hosted services on the IHost:

- **ScanWorker** — scan loop (immediate, then every `IntervalMinutes`) and a 24-hour purge loop that deletes `DeviceEvents`/`ScanSessions` older than `HistoryPurgeDays` (0 = disabled). `ScanNowAsync` triggers an out-of-schedule scan.
- **TrafficCollector** — owns the ETW kernel network session.
- **TrafficTracker** — flushes counters every `TrafficIntervalSeconds`. Traffic retention (both `TrafficEntries` and `TrafficRollups`, per `TrafficPurgeDays`) is handled by `ScanWorker`'s purge loop, not here.
- **SpeedTestWorker** — hourly Cloudflare speed test (on the hour), stored in `SpeedTestResults`; `RunNowAsync` triggers one on demand.
- **DigestWorker** — daily digest generation, missed-window catch-up, and report purge (`DigestPurgeDays`).
- **DatabaseBackupWorker** — every 24 hours, snapshots the database and exports the approved-devices list (see Database backup).

Each worker is an **independent** `BackgroundService` on its own loop and shares no lock with the others, so a stall in one can never freeze another (e.g. speed tests keep running while a scan is stuck).

### Hang protection

Every **iterative** worker wraps its per-cycle work in `Common/Watchdog.RunAsync(operation, timeout, ct)`. The watchdog races the operation against `Task.Delay(timeout)`; if the timeout wins it cancels the linked token, abandons (and observes) the stuck task, and throws `TimeoutException`. Crucially it recovers **even when an `await` never observes the token** — the original failure mode, where an in-flight `Ping` whose completion callback never fired (after an overnight network/adapter reset) wedged the scan gate indefinitely and silently killed all scanning until an app restart. On timeout each worker logs an `[INFO]` line and continues to its next cycle, so a hang self-heals and is visible in the diagnostic log.

| Worker | Bounded operation | Timeout |
|---|---|---|
| `ScanWorker` | ping/ARP/DNS scan + merge | `ScanTimeout` = 2 min |
| `SpeedTestWorker` | HTTP speed test (service also self-bounds at 120 s) | `RunTimeout` = 3 min |
| `TrafficTracker` | per-second DB flush (`ct` now threaded through all EF/SQLite calls) | `FlushTimeout` = 30 s |
| `DigestWorker` | catch-up + report purge (startup and each loop) | `CycleTimeout` = 5 min |
| `DatabaseBackupWorker` | SQLite backup + CSV export | `BackupTimeout` = 5 min |

`DatabaseBackupWorker`'s SQLite `BackupDatabase` call is synchronous and not cancellable, so it is offloaded to a threadpool thread (`Task.Run`) — that lets the watchdog abandon the *await* and recover the loop even if the underlying copy blocks. `TrafficCollector` is excluded by design: it is a continuous ETW event pump (not an iteration loop), stopped cleanly via `ct.Register(_session.Stop)`.

## System tray & window lifecycle

`MainWindow` creates a `TrayIconService` (Win32 `Shell_NotifyIcon` + window subclass). Behaviour:

- **Close** is intercepted (`AppWindow.Closing`): instead of exiting, the window is hidden (`SW_HIDE`) — the app keeps running in the tray.
- **Tray double-click / "Show"** restores and foregrounds the window.
- **Tray "Exit"** sets `_exitRequested`, checkpoints the database, disposes the tray icon and closes for real.

### Start with Windows / start minimized

`WindowsStartupService` registers a Scheduled Task (`schtasks … /sc onlogon /rl highest`) whose command launches the exe with a `--minimized` argument. At launch `App.ShouldStartMinimized()` checks the command line for that flag:

- **Flag present** (logon task) → the splash window is suppressed and, after the main window initialises, it is hidden to the tray (`SW_HIDE`) — no taskbar button, tray icon only.
- **Flag absent** (manual double-click or VS debug) → normal startup with splash and a visible window.

The `--minimized` flag is also forwarded through the elevated self-relaunch so the behaviour survives the admin elevation step.

## Database backup

SQLite WAL mode is used by default. On a clean exit (tray → Exit), `PRAGMA wal_checkpoint(TRUNCATE)` folds all WAL data into `networkmonitor.db` before the window closes. After a clean exit the single `.db` file is sufficient for a complete backup.

`DatabaseBackupWorker` (hosted service) additionally creates an automatic backup every 24 hours into `%LOCALAPPDATA%\UmnathaNetworkMonitor\Backups\`:

- `networkmonitor_yyyy-MM-dd_HH-mm-ss.db` — a consistent snapshot produced via the SQLite online backup API (`SqliteConnection.BackupDatabase`), which is safe to take while the app is running under WAL.
- `approved-devices_yyyy-MM-dd_HH-mm-ss.csv` — the approved (known) devices exported through `DeviceCsvExporter`, sharing the same timestamp as the `.db` snapshot.

To avoid redundant copies when the app restarts often, a backup runs at startup only if the newest existing `.db` backup is older than 24 hours; otherwise it waits out the remainder of the interval (cadence keyed off the timestamp embedded in the backup filename). Backups older than **3 days** are pruned after each successful backup; on a failed CSV export the just-written `.db` is removed so a `.db`/`.csv` pair is all-or-nothing. There is no in-app restore — restoring is a manual file operation.

## Unapproved device row highlighting

Device grids attach a `LoadingRow` handler that sets an amber background on rows where `Device.IsApproved == false`; approved rows use the default theme background. Because the CommunityToolkit `DataGrid` only paints a row's background when the row is realised (mutating `DataGridRow.Background` afterwards does not repaint), approval changes that must update the highlight force the rows to regenerate by resetting `DeviceGrid.ItemsSource` (`AllDevicesPage.RepaintRows`) — done after approving on the All grid and after the tab-switch reload (`ReloadAndRepaintAsync`). Routine scan refreshes keep the in-place incremental reconcile (no flicker); they don't normally change approval. The Approved devices grid does not use this handler since all rows there are approved by definition.

## Build output / deployment

Both `SelfContained=true` and `WindowsAppSDKSelfContained=true` are set, so the output folder is **fully self-contained** — the .NET runtime, the Windows App SDK / WinUI 3 runtime, and all native dependencies are copied in. Nothing needs to be pre-installed to run it; copy the `win-x64` output folder to another machine (matching architecture) and launch `NetworkMonitor.exe`. Note the database and `settings.json` live in `%LOCALAPPDATA%\UmnathaNetworkMonitor\`, **not** in the app folder, so a portable copy does not carry the dev machine's data across.

For a full file-by-file catalogue of the bin folder (~414 files + ~103 folders) and what each one is, see **[Project Bin folder description](Project%20Bin%20folder%20description.md)**.
