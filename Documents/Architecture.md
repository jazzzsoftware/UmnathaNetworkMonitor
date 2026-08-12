# Network Monitor — Architecture

## Stack

| Layer | Technology |
|---|---|
| UI framework | WinUI 3 (Windows App SDK), unpackaged |
| Runtime | .NET 10 |
| MVVM | CommunityToolkit.Mvvm (`ObservableObject`, `[RelayCommand]`) |
| Data grid | CommunityToolkit.WinUI.UI.Controls.DataGrid 7.x |
| ORM | EF Core 10 + SQLite (migrations applied at startup by `DatabaseInitializer`; see [Data model](#data-model)) |
| DI / hosting | Microsoft.Extensions.Hosting, BackgroundService |
| Per-process traffic | ETW kernel network provider (Microsoft.Diagnostics.Tracing / TraceEvent) |
| Chart rendering (reports + widget) | Win2D (Microsoft.Graphics.Canvas) |
| PDF export | QuestPDF (Community licence) |
| Notifications | Windows toast notifications + in-app toast banner |
| Updates | GitHub Releases API + Inno Setup silent install |

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
NetworkMonitor.Models/   (class library, net10.0 — referenced by the app AND the test project;
│                         each sub-folder is its own namespace, e.g. NetworkMonitor.Models.Traffic)
├── Charting/            ChartPoint, ChartSeries, ChartValue — chart primitives
├── Devices/             Device, DeviceEvent, DeviceEventType, DeviceType
├── Digest/              DigestReport, DigestSummary + per-section summary rows (New/Unapproved device, hourly)
├── Formatting/          TrafficRateFormatter, ByteSizeFormatter, RateUnitMode, MiniGraphFormatter
├── Scanning/            ScanSession — metadata for each completed scan run
├── SpeedTest/           SpeedTestResult, SpeedTestRowSummary
├── Traffic/             Traffic/LocalTraffic entities + rollups, app/device row models, per-app summaries
├── Update/              AvailableUpdate, UpdateAvailability, UpdateCheckResult
└── Widget/              MiniGraphOrientation (Vertical | Horizontal)

NetworkMonitor.Core/     (class library, net10.0 — pure, UI-free service logic; referenced by the app AND the tests;
│                         each sub-folder is its own namespace, e.g. NetworkMonitor.Core.Traffic)
├── Charting/            AxisScale — the 1/2/5/10 × 10ⁿ "nice maximum" axis ladder
│                        Oklch, OklchColour — sRGB ↔ OKLab ↔ OKLCH conversion, gamut reduction, WCAG contrast
│                        PaletteVariant — base hex + surface → the hex actually drawn
│                        ChartRole, ChartSurface, ChartPalette, ChartSchemePreset, ChartSchemeCatalog
├── Common/              Watchdog (timeout wrapper), CollectionReconciler (in-place list reconcile)
├── Csv/                 CsvField, DeviceCsvExporter, DeviceCsvImporter, SpeedTestCsvExporter
├── Data/                OuiDatabase — loads oui.txt → MAC prefix → vendor name
├── Digest/              DigestSummaryBuilder, DigestSchedule, DigestCsvExporter
├── Scanning/            MacNormalizer, MdnsInfo, MdnsEnrichment, MdnsResponseParser
├── SpeedTest/           SpeedTestMath, SpeedTestMessage
├── Traffic/             LanClassifier, LocalFlowClassifier, LocalTrafficGrouper, TrafficWindow,
│                        LocalTrafficNameResolver, LiveRateBuffer, FlushSpread, RateWindow,
│                        WellKnownPids, flow/minute records, LocalLens
├── Update/              UpdateChecker, ReleaseInfoParser, SemanticVersion, UpdateDecision,
│                        UpdateDownloader, UpdateDownloadStream, ChecksumVerifier
└── Widget/              HorizontalStripMetrics — the strip's derived width, font scale, height clamp

NetworkMonitor.Services/ (class library, net10.0-windows — background/platform services; UseWinUI for the Win2D
│                         digest renderer; each sub-folder is its own namespace, e.g. NetworkMonitor.Services.Traffic)
├── Charting/
│   └── ChartPaletteService.cs  Singleton: resolves the chosen scheme + surface → a cached Color per role;
│                               raises PaletteChanged. The single source of chart colour at runtime.
├── Data/
│   ├── AppDbContext.cs       EF Core context; DbPath → LocalApplicationData; migrations via DatabaseInitializer
│   ├── Settings.cs           Scan, traffic, digest, notification, update, widget, chart-scheme and window settings → settings.json
│   ├── SortPreference.cs     Per-page sort state persisted to LocalApplicationData
│   ├── DatabaseCheckpoint.cs WAL checkpoint (TRUNCATE) on clean exit
│   └── AppPaths.cs / AtomicFile.cs  App-data folder resolution + atomic file writes
├── Scanning/
│   ├── NetworkScanner.cs        Ping sweep + ARP parse + DNS resolve → ScannedDevice list
│   ├── MdnsProbe.cs             Per-scan DNS-SD query/listen pass feeding MdnsEnrichment
│   ├── DeviceTracker.cs         Merges scan results into the database
│   ├── ScanWorker.cs            PeriodicTimer scan loop; daily history/traffic auto-purge
│   └── DeviceNotification.cs    DTO carrying notification data between services and UI
├── Traffic/
│   ├── TrafficCollector.cs      ETW kernel TCP/UDP session → per-PID byte counters (BackgroundService)
│   ├── TrafficTracker.cs        Periodic flush of counters → process name/path → TrafficEntries + TrafficRollups
│   ├── LiveTrafficFeed.cs       Always-on IHostedService feeding the mini graph (see Floating mini graph)
│   └── TrafficFlushedEventArgs.cs  Carries the just-flushed entries to the Traffic page
├── Update/
│   ├── UpdateService.cs         Check / download / verify / launch orchestration + 20 s check deadline
│   ├── UpdateCheckWorker.cs     Startup check after 10 s, then every 24 h (BackgroundService)
│   └── InstallerLauncher.cs     Runs the verified installer silently (IInstallerLauncher)
├── Digest/
│   ├── DigestGenerator.cs       Builds + persists a DigestReport for a period; raises ReportGenerated
│   ├── DigestWorker.cs          Daily digest loop, catch-up, report purge (BackgroundService)
│   ├── DigestChartRenderer.cs   Win2D bar + donut charts → PNG (rendered at 288 DPI for crisp output)
│   └── DigestPdfExporter.cs     QuestPDF document (charts + tables) → PDF bytes
├── SpeedTest/
│   ├── SpeedTestService.cs      Cloudflare parallel-stream download/upload + latency (self-bounded, 120s)
│   ├── SpeedTestWorker.cs       Hourly speed-test loop; RunNowAsync for on-demand (BackgroundService)
│   └── SpeedTestCompletedEventArgs.cs  Carries the latest result to the UI
├── Csv/
│   └── DeviceEventCsvExporter.cs  Export device event history to CSV
├── Backup/
│   └── DatabaseBackupWorker.cs  Daily timestamped DB backup + approved-devices CSV (BackgroundService)
└── Platform/
    ├── AppLog.cs                Opt-in diagnostic file logger (app/scan events + exceptions, no PII)
    ├── AppInfo.cs               Installed version, resolved once for the About box and update check
    ├── InAppNotificationService.cs  Raises in-app toast-banner messages
    ├── TrayIconService.cs       Win32 system tray icon + context menu (Mini graph / Show / Exit)
    ├── MiniGraphState.cs        Shared widget state — visibility, sections, opacity, orientation, placement
    ├── TaskbarTopmostGuard.cs   Re-asserts HWND_TOPMOST when the taskbar takes foreground
    ├── WindowsStartupService.cs Enable/disable "start with Windows" via schtasks onlogon task
    ├── ShellLauncher.cs         Opens folders and URLs through the shell
    └── OpenFileDialog.cs / Win32FileSaveDialog.cs  Win32 file pickers (open + IFileDialog save)

NetworkMonitor/           (the WinUI app — pure UI shell)
├── App.xaml.cs               Elevation + single-instance, IHost build, DI, DB init, startup window handling,
│                             mini graph window lifetime (ShowMiniGraph / CloseMiniGraph)
├── MainWindow.xaml.cs        NavigationView shell, tray icon, toast/digest dispatch, update banner,
│                             window-placement persistence, ShutdownForUpdate
├── MiniGraphWindow.xaml(.cs) Always-on-top widget window, both orientations (see Floating mini graph)
├── SplashWindow.xaml.cs      Startup splash (suppressed when launched minimized)
│
├── ViewModels/
│   ├── AllDevicesViewModel.cs      Devices grid (last 24h), Online-only filter, scan command, mark-known logic
│   ├── UnapprovedDevicesViewModel.cs  Unknown-device grid + approve actions
│   ├── DeviceHistoryViewModel.cs   Per-device event history + search
│   ├── InternetViewModel.cs        Live per-process WAN traffic + area chart state + rate badges
│   ├── LocalViewModel.cs           LAN app/device lenses, in-place row reconcile, rate badges
│   ├── SpeedTestViewModel.cs       Speed-test history, tiles, charts, run-now, CSV export
│   ├── MiniGraphViewModel.cs       Widget state: live WAN/LAN series, last speed test, unknown count
│   ├── UpdateViewModel.cs          Update banner state, download progress, Update now / Later
│   ├── ReportsViewModel.cs         Digest list, latest/selected summaries, generate/delete/export
│   └── SettingsViewModel.cs        Settings load/save, manual purge, startup and widget toggles
│
└── Views/
    ├── TrafficHostPage.xaml(.cs)    Host with a SelectorBar: Internet | Local | Speed Test, plus the
    │                                Mini graph toolbar toggle
    ├── DevicesHostPage.xaml(.cs)     Host with a SelectorBar: Devices | Approved | Unapproved | History
    ├── AllDevicesPage.xaml(.cs)      Live device grid (last 24 hours)
    ├── ApprovedDevicesPage.xaml(.cs) Known/approved devices with edit/delete
    ├── UnapprovedDevicesPage.xaml(.cs) Unapproved devices with approve action
    ├── DeviceHistoryPage.xaml(.cs)   Per-device appeared/disappeared event log
    ├── InternetPage.xaml(.cs)        Live per-process internet traffic grid + area chart
    ├── LocalPage.xaml(.cs)           LAN traffic grid: lens toggle, service/discovery/rate chips, drill-down
    ├── SpeedTestPage.xaml(.cs)       Speed-test tiles, throughput/latency charts and history grid
    ├── ReportsPage.xaml(.cs)         Daily digest viewer + history + PDF/CSV export
    ├── SettingsPage.xaml(.cs)        Settings form (Traffic / Devices / Theme / Other tabs) with sticky Save footer
    └── Controls/
        ├── TrafficAreaChart.xaml(.cs)  Live stacked area chart with smooth scrolling + a compact mode
        ├── MiniTrafficSection.xaml(.cs) One labelled chart cell of the mini graph
        ├── SpeedTrendChart.xaml(.cs)   Speed-test throughput/latency trend chart
        └── DigestReportView.xaml(.cs)  Reusable digest renderer (charts + tables) for the Reports page

NetworkMonitor.Tests/     (xunit — ProjectReference to Models + Core only; no source links,
                           so anything needing a test belongs in Core or Models, never Services)

Tools/                    (standalone tooling — things you run, not things that ship; registered
│                          in the slnx as folders of files, NOT as solution projects, so the
│                          solution build stays clean)
├── Installer/
│   ├── build-installer.ps1   Publishes self-contained x64, compiles the Inno Setup installer,
│   │                         writes the companion .sha256 the in-app updater verifies against
│   ├── NetworkMonitor.iss    Inno Setup script; paths are relative to this file
│   └── Output/               Build artifacts (gitignored)
└── RetentionProbe/
    ├── Program.cs            Diagnostic for the raw-entry purge: census, purge timing against the
    │                         120s watchdog, page/freelist counts, WAL collapse, rollup coverage
    └── README.md             Usage, how to read the output, and the recorded baseline
```

In Solution Explorer the five projects are grouped under the `/App/` and `/Tests/` solution
folders. Those are virtual groupings only — nothing moves on disk.

## Shell & navigation

`MainWindow` hosts a `NavigationView` with three menu destinations plus the built-in settings item (`IsSettingsVisible="True"`):

| Nav item | Page | Notes |
|---|---|---|
| Traffic | `TrafficHostPage` | Inner `SelectorBar`: Internet / Local / Speed Test, plus a **Mini graph** toolbar toggle. Default page on launch |
| Devices | `DevicesHostPage` | Inner `SelectorBar`: Devices / Approved / Unapproved / History |
| Reports | `ReportsPage` | Daily digest viewer |
| ⚙ (footer) | `SettingsPage` | The `NavigationView`'s own settings item |

`DevicesHostPage` lazy-navigates each inner frame on first selection; `TrafficHostPage` navigates the Internet frame up front (it is the landing page) and the Local and Speed Test frames on first selection. `MainWindow.NavigateToHistory(mac)` deep-links from any device into its history tab, and `MainWindow.NavigateTo(...)` is what the mini graph's double-click drill-in targets.

An `InfoBar` docked above the content frame carries the **update banner** (`UpdateViewModel`) — availability message, download progress with Cancel, then **Update now** / **Later**.

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

Per-process network usage is captured from the kernel (ETW), not by polling adapters. One collector feeds two views: **Internet** (WAN, per process) and **Local** (LAN, per process × peer device).

```
TrafficCollector (BackgroundService)
    └─ ETW kernel session (NetworkTCPIP): TcpIpSend/Recv + UdpIpSend/Recv
    └─ per event: resolve the REMOTE endpoint + service port, then classify by remote IP:
         • self / loopback (this PC's own IPs, 127/8)  → dropped
         • LAN peer (private ranges)                    → _localCounters, keyed by
                                                          LocalFlowKey(pid, remoteIp, protocol, remotePort)
         • WAN / internet                               → _counters, keyed by pid
    └─ counters are lock-light long[] cells bumped with Interlocked

TrafficTracker (BackgroundService, every TrafficIntervalSeconds)
    └─ DrainAndReset() + DrainAndResetLocal() snapshots
    └─ resolve PID → process name + full image path (QueryFullProcessImageName)
    └─ write raw rows to TrafficEntries (WAN) and LocalTrafficEntries (LAN)
    └─ upsert per-minute aggregates into TrafficRollups + LocalTrafficRollups (ON CONFLICT … DO UPDATE)
    └─ raise Flushed(entries, localDeltas) → InternetViewModel + LocalViewModel refresh live
```

**Endpoint attribution (a real ETW gotcha).** Which field holds the *remote* side differs between TCP and UDP:

- **TCP** events are *connection-oriented* — `saddr/sport` is always the **local** endpoint and `daddr/dport` the **remote**, for *both* directions. So `TcpIpSend` **and** `TcpIpRecv` read `daddr/dport`.
- **UDP** events are *packet-oriented* — `saddr` is the sender. So `UdpIpSend` reads `daddr` (we send) and `UdpIpRecv` reads `saddr` (remote sends).

Reading `saddr` on TCP recv (i.e. our own IP) makes every download look like self-traffic, which the self/loopback filter then silently drops — the bug that once hid **all** TCP downloads, including SMB/NAS reads (fixed by "attribute recv to the remote endpoint").

**SMB / file shares are attributed to System (PID 4).** A copy to/from a NAS is done by the kernel SMB redirector, so the socket is owned by `System`, not the app that started it (e.g. Macrium). This is a Windows fact shared by Resource Monitor and every host tool; the Local page surfaces it honestly (below).

- **TrafficEntries / LocalTrafficEntries** hold raw per-flush rows. They serve the live 5-minute window and nothing else, so `TrafficTracker` purges them on a **1-hour** retention (`RawEntryRetention`), rate-limited to once every 5 minutes on the flush loop. Keeping per-second rows for `TrafficPurgeDays` wrote days of data to answer a five-minute question; `ScanWorker`'s purge deliberately no longer touches these tables.
- **TrafficRollups / LocalTrafficRollups** hold per-minute aggregates and are the long-lived source for the grids and digest, purged per `TrafficPurgeDays` (default 7). `LocalTrafficRollups` additionally carry `Protocol` + `RemotePort` (unique key `(MinuteEpoch, ProcessName, RemoteIp, Protocol, RemotePort)`).

### Internet page — live vs paused

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

**Single-instance requirement.** The pause lives on the `InternetPage` view instance, so exactly one live instance may be subscribed to `TrafficTracker.Flushed` — otherwise an unpaused orphan keeps rebuilding the shared list and defeats the pause. Two safeguards enforce this:

- `MainWindow.NavViewLoaded` selects the first nav item (which already navigates to `TrafficHostPage`) and only calls `ContentFrame.Navigate` if the frame is still empty — preventing a duplicate host/page at startup.
- `InternetPage` subscribes to `Flushed` on `Loaded` and unsubscribes on `Unloaded` (not `OnNavigatedTo`/`OnNavigatedFrom`, which do **not** fire for a page hosted in an inner `Frame` when its outer host is swapped). Any orphaned page detaches the moment it leaves the visual tree, which also covers the repeated Traffic⇄Devices navigation leak.

Because an inner tab switch (Internet ⇄ Local ⇄ Speed Test) only toggles `Frame.Visibility` after the first navigation, `TrafficHostPage` explicitly calls `ResetToLive()` on the Internet and Local pages when leaving their tabs, so each returns Live.

### Chart axis scaling

`TrafficAreaChart` draws a gridline at the axis maximum and another at half of it, then rounds the
observed peak up via `AxisScale.NiceMax` (Core, unit-tested). The ladder is **1 / 2 / 5 / 10 × 10ⁿ**,
chosen so every step halves cleanly for that mid gridline.

It replaced a rule that rounded up to the next multiple of ten *in whatever unit the peak fell into*
(`Ceiling(bitsPerSecond / divisor / 10) * 10`). That collapsed every peak between 1 and 10 Gb/s onto
a single 10 Gb/s axis — a 2 Gb/s LAN transfer was drawn in the bottom fifth of the chart — and had
the same effect just above every other unit boundary. It only looked reasonable mid-decade, where
45 Mb/s rounded to 50 Mb/s.

Note the axis is only ever as good as the peak fed to it: an inflated peak still wastes chart
height, so a trace that sits low despite this scaling points at the peak calculation, not the axis.
See **Live bucket attribution** below for the defect that used to do exactly that.

### Live bucket attribution

A flush drains everything `TrafficCollector` accumulated since the *previous* drain, so its bytes
belong to that whole interval — not to the instant the drain happened. The live window used to add
each flush to whichever bucket was newest at the time, which meant a drain that slipped past a
one-second boundary left the previous bucket empty and made the next one read roughly double.

`FlushSpread.Distribute` (Core, unit-tested) allocates a flush across the buckets its interval
overlaps, in proportion to that overlap, with the remainder given to the largest share so the total
is preserved to the byte. Both `LocalViewModel` and `InternetViewModel` track `_lastFlushUtc` and
pass the real interval into `ApplyFlushToWindow`.

Measured on a gigabit LAN against Windows' own NIC byte counters (2026-07-28): the sustained rate
and the totals were always correct — the rate chip read 559 Mb/s against the adapter's 560 Mb/s
framed, and cumulative bytes came to 88% of framed, which is what Ethernet/IP/TCP overhead predicts.
Only the per-second buckets were wrong, swinging 260–839 Mb/s around a wire that never left ~560,
and producing "peaks" above the physical line rate. Spreading flattened the trace to a plateau and
brought the peak back under line rate.

The lesson worth keeping: **1-second buckets resolve finer than the capture pipeline's timing
accuracy.** Anything at that granularity has to spread bytes over the interval they were collected
in, not stamp them at the drain. Ranges of 1 h and above bucket by the minute, so they were never
affected.

### Local page — app/device lenses & noise folding

The Local page shows LAN traffic for *this* PC (the only scope any host tool can attribute per app). Two pieces shape it:

- **`LocalFlowClassifier`** maps each `(protocol, remotePort)` to a category and an optional service tag:
  - **Discovery** — UDP service ports for mDNS (5353), SSDP (1900), LLMNR (5355), WS-Discovery (3702), NetBIOS (137/138), DHCP (67/68), NAT-PMP/PCP (5350/5351). This is the "every device, tiny bytes" chatter that browsers (Cast/DIAL) and AV suites (LAN scans) generate. The port set lives **once** here and is reused as `DiscoverySqlPredicate` by the Local chart query and the digest, so the three can't drift.
  - **Data** — everything else, with a tag where known: **SMB** (445/139), NFS, AFP, HTTP/HTTPS, SSH, RDP.
- **`LocalTrafficGrouper`** turns the classified per-minute flows into a generic two-level row model (`LocalTrafficGroupRow` → `LocalTrafficLeafRow`) for either lens:
  - **By app** (default) — top-level = process, expand → peer devices.
  - **By device** — top-level = LAN device (friendly name + IP), expand → the apps that talked to it. This is the lens that makes a **NAS backup obvious**: the NAS rises to the top on a big **upload**, tagged **SMB**, under **System**.
  - All **Discovery** flows fold into one collapsed **"N devices — discovery only"** group so real transfers stay up front; grid, chart and digest all exclude discovery from their totals.
- **Live ordering** — `ApplyGroups(groups, reorder)` re-sorts on each live tick only while
  `SelectedGroupKey` is null. Freezing the order unconditionally (the original C4-2 behaviour) kept
  the grid steady under an open drill-down, but it also meant a row that started talking later stayed
  wherever it was first inserted: a NAS pulling gigabytes sat below an idle phone. Sorting while the
  user is scanning and holding the order once they drill in keeps both properties.

**Non-device noise is filtered out.** Two things otherwise pollute the LAN peer list with addresses nothing lives at: (1) **broadcast/multicast** — `LanClassifier.IsBroadcastOrMulticast` drops directed/limited broadcast (last octet `.255`) and `224–239.x` multicast at capture, alongside self/loopback; (2) **subnet sweeps** — a scanner (AV, another host) probing all 256 addresses of a /24 would otherwise create 256 phantom peers, so the grouper folds a **discovery** flow into the background group **only when its `RemoteIp` is a known device** (present in `namesByIp`, i.e. one the scanner actually found). Real **data** flows are never filtered — a transfer to an as-yet-unscanned device still shows.

Rows are stable observable objects reconciled **in place** (`ApplyGroups`) rather than replaced each flush, so an expanded drill-down keeps its selection and expansion while the numbers tick live. `System` is kept on Local (it's where SMB lives) but excluded from Internet.

### Live rate badge (both pages)

Both grids show a green **`● 118 Mb/s · 15 MB/s`** pill on any top-level row that is actively transferring. How it's computed:

1. On **every** live flush, the per-interval bytes for each group/app are pushed into a small per-key rolling window (`_rateWindows`, last **5** samples), plus an `__all` total. Feeding happens *before* the flush's incremental-vs-reload branch, so it's independent of which path the sliding window takes (getting this wrong is why the badge first appeared only intermittently).
2. **rate = average(window) ÷ `TrafficIntervalSeconds`** → bytes/sec, formatted by the shared **`TrafficRateFormatter.Composite`** into a bits/s and/or bytes/s figure (e.g. `141 Mb/s · 18 MB/s`), auto-scaling the unit. Which units appear is governed by the **Speed units** setting (`Settings.RateUnitMode` → static `TrafficRateFormatter.Mode`: Both / Mb/s only / MB/s only), honoured by the badges, both chart axes, the Speed Test page (tiles, chart, grid columns), the digest report view/PDF/chart and the speed-test toast — CSV exports always keep both units. Formatting is **decimal** (÷1,000,000) to match the byte columns (`ByteSizeFormatter`) — the **whole app uses base-1000 (SI) units**, the ISP/speedtest.net convention people compare against. (Binary ÷1,024 units — strictly KiB/MiB — were tried and dropped: network speeds are universally quoted decimal.)
3. The pill is **live-only** and shows only **above 0.5 Mb/s** (a 62.5 KB/s threshold), so idle discovery chatter and paused/long-range views stay clean. Leaving live clears the windows via `SetRatesActive(false)`.

Local bakes the rate onto its in-place observable rows (`RateBytesPerSec` → `HasRate`/`RateText`); Internet, which rebuilds its row *records* each flush, bakes it into each new `InternetTrafficAppRow` after the flush branch. Same threshold, units and smoothing on both.

## Speed test

`SpeedTestService` measures against Cloudflare's public endpoints (`speed.cloudflare.com/__down` / `__up`). A single TCP stream is throughput-limited by the bandwidth-delay product and can't fill a fast link, so — like speedtest.net and Cloudflare's own web test — it runs **6 parallel streams** and reports the **aggregate throughput over a steady-state window** (a 2 s warm-up is discarded, then ~6 s measured). That lands within a few percent of those tools, where a single 50 MB transfer read far low.

Three details make it work:

- **Parallelism needs separate TCP connections.** The throughput requests force **HTTP/1.1** (the handler allows 32 connections/server) so the six streams open six real connections. Over **HTTP/2** they'd multiplex onto *one* connection and share one congestion window — no faster than a single stream.
- **Latency stays on HTTP/2.** The latency/warm-up probes use the client's default HTTP/2; Cloudflare's `__down` is slow over HTTP/1.1 (~450 ms vs ~24 ms), so forcing 1.1 there inflates it. Server processing (`Server-Timing: cfRequestDuration`) is subtracted, and the reported latency is the **min** of 10 samples (jitter = their spread).
- **Download chunk cap.** `/__down?bytes=N` returns **403 for N ≥ 100 MB**, so each stream requests 99,999,999 bytes and loops to refill the window; upload streams a continuous body per connection (`CountingUploadContent`).

Throughput is reported in **decimal** Mb/s / MB/s (÷1,000,000) — the ISP/speedtest.net convention, and the same base-1000 units used everywhere else in the app. An accurate run transfers **~750 MB** (~18 GB/day at the hourly cadence); Settings warns about this so metered users can disable it.

## Floating mini graph

An optional always-on-top widget showing live Internet and Local throughput, the last speed test and the unknown-device count, without the main window open. It is one `Window` (`MiniGraphWindow`) in two layouts, not two widgets.

```
LiveTrafficFeed (IHostedService, always on)
    ├─ startup: TWO database reads (last 5 min of rollups, latest speed test) — none after
    ├─ TrafficTracker.Flushed      → LiveRateBuffer (WAN) + LiveRateBuffer (LAN)
    ├─ SpeedTestWorker.SpeedTestCompleted → LatestSpeedTest
    ├─ ScanWorker.ScanCompleted    → UnapprovedDeviceCount
    └─ raises Updated → MiniGraphViewModel → MiniTrafficSection charts

MiniGraphState (singleton)   IsVisible · sections · Opacity · Orientation · placement
    ├─ tray menu "Mini graph"    ─┐
    ├─ Traffic toolbar toggle     ├─ all three write the same state; the window,
    ├─ Settings → Floating mini graph ─┘  the toolbar and Settings all follow it
    └─ persisted through Settings → settings.json
```

**Why the feed runs from startup.** The widget must open with five minutes already drawn rather than an empty chart, so `LiveTrafficFeed` is registered whether or not the widget is ever shown. The cost is bounded and known: roughly 15 KB held permanently in two `LiveRateBuffer` rings and exactly two DB reads at startup. Every handler is wrapped, because a fault here must never propagate into the flush loop or the scan loop the rest of the app depends on.

**`LiveRateBuffer`** (Core, unit-tested) is a fixed ring of one-second buckets. It zero-fills idle gaps — a widget that has been idle shows a flat line, not a stale one — and spreads each flush across the interval it was collected in, the same `FlushSpread` reasoning the main charts use.

### Window mechanics

- **Opacity is a layered window, not XAML opacity.** `WS_EX_LAYERED` + `SetLayeredWindowAttributes` is what makes the resting opacity mean anything: fading the XAML root only blends the content toward the window's own opaque surface, so 50% looked like a dimmed widget rather than a see-through one. With the style set, DWM composites the whole window — charts included — against whatever is behind it. Hover rises to full opacity (150 ms rise / 300 ms fall delay, stepped by hand at 16 ms because a layered window's alpha has no animation behind it).
- **The frame always stays.** It supplies the resize edges — dragging the top or bottom edge is how the strip's height is set. Only its *paint* is optional: `DWMWA_BORDER_COLOR = DWMWA_COLOR_NONE` removes it while leaving hit-testing intact. That, and the rounded-corner preference, need Windows 11 22000+; older builds fail the call and keep the default border.
- **Sizes are DIPs, positions are physical pixels.** The size is stored in DIPs so the widget keeps its apparent size across displays of different scaling; the position stays in physical pixels because that is the coordinate space `DisplayArea` and `AppWindow` both work in. Restoring a position measures the target display's DPI directly, because the window cannot be asked for its own DPI before it is first shown.
- **Placement is debounced** (400 ms) and flushed explicitly before a hide, an orientation switch or an exit — otherwise a drag followed immediately by any of those stops the timer before it fires and loses the new position.
- **`MonitorFromPoint(..., NEAREST)`, not `DisplayArea.GetFromPoint(..., None)`.** A widget dragged a few pixels past a screen edge saves a position inside no display at all; `None` returns null for it, which sent the restore down the never-placed path and dumped the widget in the work area's bottom-right corner. Nearest resolves a display anyway and the existing clamp pulls it back on-screen while keeping where the user put it.
- **Alt+F4 is handled.** The widget carries a resize border, so Alt+F4 destroys it behind the app's back; `OnWindowClosed` clears the shared state, or the tray item, the toolbar toggle and Settings all keep reporting a dead window as visible.

### Orientation

`MiniGraphState.Orientation` (`Vertical | Horizontal`) relayouts the **same** window in place — `ApplyLayout` swaps `RowDefinitions` for `ColumnDefinitions` and reassigns `Grid.Row`/`Grid.Column` on the existing children. There is no second window class and no duplicate `MiniTrafficSection` instances. Each orientation stores its **own** placement (`MiniGraphX/Y/Width/Height` vs `MiniGraphStripX/Y/StripHeight`), so switching back and forth returns each layout to where it was left.

`HorizontalStripMetrics` (Core, unit-tested) owns everything derived about the strip:

- **Width is not draggable** — it is the sum of the cells currently switched on (Internet 170, Local 170, speed 196, unknown devices 146, close 22, plus padding/gaps). Nominal constants rather than runtime text measurement: the strings are fixed-format, and measuring would make the width untestable and give the window two competing sources of truth for its own size.
- **Height clamps to 40–120** and drives the font scale (1.0–2.0). Horizontal takes its scale from height alone — a width term would inflate the text as sections were switched on.
- **Below 34 px the peak label is dropped** rather than allowed to collide with the section label.

**Taskbar placement carries no taskbar logic.** The strip is simply a topmost window the user can drag onto the taskbar; there is no docking, snapping, auto-hide tracking or Explorer coupling (a real in-taskbar band would need a deprecated deskband or `SetParent` into `Shell_TrayWnd`). Two consequences are handled explicitly: the horizontal strip clamps to the **display**, not the work area — the work area excludes the taskbar by definition, so clamping there would push a taskbar-docked strip back up every time — and `TaskbarTopmostGuard` re-asserts `HWND_TOPMOST` (with `SWP_NOACTIVATE`) on foreground changes, because the taskbar shares the topmost band and activating it would otherwise bury the strip until the widget was toggled off and on.

**Interaction.** Double-clicking a section drills into the matching page (`MainWindow.NavigateTo`, restoring the window at its previous maximized state); the right-click menu carries Open, section toggles, border, an opacity radio submenu and the orientation submenu; a ✕ glyph closes the widget. While hidden, `AppWindow.Hide` leaves the XAML tree loaded, so `IsLive` is what stops the charts rendering frames nobody can see, and a relayout requested while hidden is deferred until just before it is shown.

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
          → generates one report per missed daily window (covers downtime), each
            gated on the same HasDataAsync check — a window the app slept through
            has nothing to report, so it is skipped rather than filed empty

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

Digest charts are rasterised by `DigestChartRenderer` at a logical **840 × 360** (speed charts 840 × 180). The **on-screen preview** renders at the display scale (`96 × XamlRoot.RasterizationScale`), while the **PDF export** keeps **288 DPI** so charts stay sharp when printed or zoomed (the DPI is a parameter on the render methods; the PDF path uses the default).

Scaling the preview DPI by the display scale alone is **not** enough to stay crisp. WinUI maps one bitmap pixel to one DIP and ignores PNG DPI metadata, so a higher-DPI render makes the bitmap *larger in DIPs* rather than denser. The preview images are `Stretch="Uniform"` with no width constraint, so they fill the page width — which means the bitmap is scaled to an area whose device-pixel count is `displayedWidth × rasterizationScale`, and a bitmap rendered only at `96 × rasterizationScale` never has enough pixels for it.

`DigestReportView.PreviewDpi()` therefore folds the displayed width into the DPI:

```
dpi = 96 × (displayedWidth / DigestChartRenderer.ChartWidth) × rasterizationScale
```

The chart geometry still draws at the authored `ChartWidth` logical units — only the pixel count changes — so the bitmap ends up with exactly as many pixels as the on-screen area has device pixels, a 1:1 mapping at any window width or scale factor. `ChartWidth` is public for this reason. The result is clamped to 96–384 DPI so a very wide window cannot push five Win2D renders into absurd bitmap sizes.

Two re-render triggers keep that mapping true: `Loaded` (because `XamlRoot` is null — and the scale therefore 1.0 — when the `Summary` binding fires before the control is attached, and `ActualWidth` is still 0 before layout), and a 250 ms-debounced `SizeChanged` so resizing the window re-sharpens once when the drag settles rather than on every frame. A 4 DPI threshold suppresses re-renders for drift too small to see.

The **PDF export never had this problem**: QuestPDF places the PNG into a fixed physical area on the page, where extra pixels always become density.

`GenerateNowAsync` produces an immediate (unscheduled, `IsScheduled = false`) report covering the last 24 hours without disturbing the scheduled cadence. Because catch-up anchors on the last **scheduled** report only, a manual report never advances the cursor.

**First launch after a DB delete:** the startup catch-up runs against an empty database (before the first scan or traffic flush), so `HasDataAsync` returns false and **no report is generated on that first launch**. Once the app has collected devices/events/traffic, the next launch's startup catch-up finds data and generates the report — which is why a digest appears on the *second* launch, not the first. Use **Generate now** (or wait for the scheduled hour) to produce one immediately.

## Data model

```
Device
  Id, MacAddress (unique index), IpAddress, Hostname
  FriendlyName, MdnsName, Vendor, Model, Type, Notes
  IsApproved (index), IsHost, IsOnline (index), FirstSeen, LastSeen
  DisplayName => FriendlyName ?? MdnsName ?? Hostname ?? IpAddress

DeviceEvent
  Id, DeviceId (FK → Device, cascade delete)
  EventType (Appeared | Disappeared), Timestamp (index)

ScanSession
  Id, StartedAt, CompletedAt
  DevicesFound, NewDevices, DevicesGone

TrafficEntry            (raw, 1-hour retention)
  Id, Timestamp, ProcessName, ProcessPath
  BytesUploaded, BytesDownloaded
  index (Timestamp, ProcessName)

TrafficRollups          (per-minute WAN aggregate; long-lived)
  Id, MinuteEpoch, ProcessName, ProcessPath
  BytesUploaded, BytesDownloaded
  unique index (MinuteEpoch, ProcessName)

LocalTrafficEntry       (raw LAN, 1-hour retention)
  Id, Timestamp (index), ProcessName, ProcessPath
  RemoteIp, Protocol, RemotePort
  BytesUploaded, BytesDownloaded

LocalTrafficRollups     (per-minute LAN aggregate; long-lived)
  Id, MinuteEpoch, ProcessName, ProcessPath
  RemoteIp, Protocol, RemotePort
  BytesUploaded, BytesDownloaded
  unique index (MinuteEpoch, ProcessName, RemoteIp, Protocol, RemotePort)

SpeedTestResult
  Id, Timestamp (index), Server
  DownloadMbps, UploadMbps, LatencyMs, JitterMs
  Success, Error

DigestReport
  Id, PeriodStart, PeriodEnd (index), GeneratedAt
  Headline, SummaryJson, IsScheduled (index)
```

Every table is an EF entity — there is no hand-written DDL, no `CREATE TABLE IF NOT EXISTS` guards and no in-place `ALTER`/rename. WAL mode is enabled on startup. The only raw SQL is the retention `DELETE`s and the per-minute rollup upsert (`INSERT … ON CONFLICT`).

> **Every schema change ships an EF Core migration, in the same commit as the change.** The app is publicly released, so a user's `networkmonitor.db` holds the only copy of their device and traffic history — "delete the database and let it rebuild" is not an acceptable answer to a schema change. `App.OnLaunched` calls `DatabaseInitializer.InitializeAsync`, which baselines and then migrates: a database with application tables but no `__EFMigrationsHistory` (anything created by the pre-migration `EnsureCreated` path) has `InitialCreate` written into the history table as **already applied** rather than replayed onto it, so `MigrateAsync` then applies only what came after. `EnsureCreated` is gone from the app — it is a no-op against an existing file and would silently skip every later migration. Its only remaining uses are in `Tools/MigrationVerify`, which builds v0.0.8-era databases on purpose to prove the baseline path. Migrations live in `NetworkMonitor.Services/Data/Migrations/`. See the *Database* section of [`CLAUDE.md`](../CLAUDE.md).

## Data retention

| Data | Default retention | Configurable | Mechanism |
|---|---|---|---|
| Device history (`DeviceEvents`, `ScanSessions`) | 30 days | ✅ `Settings.HistoryPurgeDays` (Settings → Devices) | `ScanWorker` 24h purge loop (0 = disabled) |
| Traffic — raw rows (`TrafficEntries`, `LocalTrafficEntries`) | 1 hour | ❌ `TrafficTracker.RawEntryRetention` const | `TrafficTracker` flush loop, rate-limited to once per 5 min |
| Traffic — per-minute rollups (`TrafficRollups`, `LocalTrafficRollups`) | 7 days | ✅ `Settings.TrafficPurgeDays` (Settings → Traffic) | `ScanWorker` purge loop |
| Speed test results (`SpeedTestResults`) | 7 days | ✅ `Settings.TrafficPurgeDays` (folded into traffic purge) | `ScanWorker` purge loop |
| Daily digests (`DigestReports`) | 30 days | ✅ `Settings.DigestPurgeDays` (Settings → Other) | `DigestWorker` purge |
| Database backups (`.db` + approved-devices `.csv`) | 3 days | ❌ `DatabaseBackupWorker.RetentionDays` const | pruned after each successful backup (retention is by **age**, not count — see note below) |
| Diagnostic logs (`Log-*.txt`) | 7 days | ❌ `AppLog.RetentionDays` const | pruned on startup when logging is enabled |

## Diagnostic logging

Logging is **off by default** and toggled by `Settings.EnableLogging` (Settings → Other → "Enable diagnostic logging", with an "Open logs folder" link). When enabled, `AppLog` writes a daily file `Log-yyyyMMdd.txt` to `%LOCALAPPDATA%\UmnathaNetworkMonitor\Logs\`:

- **Info** entries — app start (with version) and stop, scan start / scan completed (device counts only), speed-test completed (throughput/latency figures only), and **watchdog timeout notices** when a worker abandons a stuck cycle.
- **Error** entries — the global `App.UnhandledException` handler plus the previously-silent `catch` blocks in the background services (`ScanWorker`, `TrafficTracker`, `TrafficCollector`, `SpeedTestWorker`, `DigestWorker`, `DatabaseBackupWorker`), `AtomicFile`, and `MainWindow` shutdown.

No device or network identifiers (MAC, IP, hostname) are ever written — only counts and operation names — so logs are safe for an end user to share. Normal shutdown cancellation is not recorded as an error.

**Expected conditions are Info, not Error.** An update check made with no connection is the worked example: `UpdateChecker` wraps only the transport call, so anything the fetch delegate throws is a connectivity problem and is recorded as a single Info line rather than an `HttpRequestException` with a stack trace. Errors are reserved for faults the user can neither cause nor fix — a response that arrives but cannot be parsed still logs as one. The same split explains why `UpdateChecker` treats cancellation as cancellation *only when the caller's token was actually cancelled*: `HttpClient` reports its own timeout as `TaskCanceledException`, and misreading that as a user cancellation makes `UpdateService` suppress the result, leaving the UI showing nothing at all.

The check also carries its own **20-second deadline** (`UpdateService.CheckTimeout`), separate from the shared client's 10-minute timeout — that budget exists for downloads, and applying it to a one-request JSON check meant an unreachable server that hangs rather than refusing could leave the banner pending for minutes. The deadline is applied with a linked token and then rethrown as `TimeoutException`, deliberately *not* as a cancellation, for the reason above. `AppLog` is a dependency-free static logger (no Serilog/Sentry); the App SDK / Sentry route was rejected because the app runs elevated.

## Settings persistence

`Settings` is loaded from `settings.json` at startup (falling back to `appsettings.json` defaults, with subnet auto-detection, if the file does not exist) and registered as a singleton. Settings changes persist **immediately**: `SettingsViewModel` writes the singleton back to `settings.json` on any property change (atomic temp-file + rename via `AtomicFile`). The Settings page Save button re-saves but is redundant. Scan changes take effect on the next scan cycle — no restart required. Window placement (`WindowX/Y/Width/Height`, `WindowMaximized`) is also persisted in `settings.json` and restored on launch, as is the mini graph's per-orientation placement, section selection, opacity, border and orientation, and the per-page state that is set from the page rather than from Settings (`InternetTimeRangeHours`, `LocalTimeRangeHours`, `LocalLens`, `DevicesOnlineOnly`).

The chart scheme is the exception to "writes on any property change". `ChartSchemeId` and the five `ChartCustom*` colours are written by `ChartPaletteService`, not by `PersistAll`, and their view-model properties are excluded from `SettingsViewModel.OnSettingChanged` — that handler is an opt-out list, so anything persisting through its own service has to be named there or it double-writes and raises a second "Settings saved" toast. Picking a scheme saves immediately; a custom colour does not, because the `ColorPicker` is bound `TwoWay` and fires on every drag tick. Those are marked dirty and flushed at a boundary — the picker's flyout closing, or the settings page unloading, which is what catches a window closed with a picker still open.

## Registry usage

The app writes a single registry location (current-user only; no machine-wide keys):

| Key | Values | Purpose |
|---|---|---|
| `HKCU\SOFTWARE\Classes\AppUserModelId\{Aumid}` (where `Aumid` = `NetworkMonitor.App`) | `DisplayName` = `Umnatha Network Monitor`; `IconUri` = `<install>\Assets\app.ico` | Registers the AppUserModelID so Windows toast notifications show the correct app name and icon. Written on each launch in `App.OnLaunched`. |

No other registry keys are read or written. "Start with Windows" uses a Scheduled Task (`schtasks`), **not** a `Run` registry key.

## Background services

Registered as hosted services on the IHost:

- **ScanWorker** — scan loop (immediate, then every `IntervalMinutes`) and a 24-hour purge loop that deletes `DeviceEvents`/`ScanSessions` older than `HistoryPurgeDays` (0 = disabled). `ScanNowAsync` triggers an out-of-schedule scan. When `Settings.AutoDetectSubnet` is on (default), every scan first re-detects the subnet (`Settings.TryDetectSubnetBase`, persisted on change) so a laptop that moves networks keeps scanning the right range, and `NetworkChange.NetworkAddressChanged` triggers a debounced (5 s) immediate scan on top of the schedule. A subnet change raises `NetworkChanged`, which `MainWindow` surfaces as an in-app banner plus a Windows toast (when toasts are enabled). Detection failure (no network) never overwrites the stored subnet.
- **TrafficCollector** — owns the ETW kernel network session.
- **TrafficTracker** — flushes counters every `TrafficIntervalSeconds`, and purges the raw entry tables on their 1-hour retention (rate-limited to once every 5 min). Rollup retention (`TrafficPurgeDays`) is handled by `ScanWorker`'s purge loop, not here.
- **LiveTrafficFeed** — `IHostedService`, always on: keeps the mini graph's two one-second ring buffers, the latest speed test and the unknown-device count fed from the other workers' events (see [Floating mini graph](#floating-mini-graph)).
- **SpeedTestWorker** — hourly Cloudflare speed test (on the hour), stored in `SpeedTestResults`; `RunNowAsync` triggers one on demand.
- **DigestWorker** — daily digest generation, missed-window catch-up, and report purge (`DigestPurgeDays`).
- **DatabaseBackupWorker** — every 24 hours, snapshots the database and exports the approved-devices list (see Database backup).
- **UpdateCheckWorker** — GitHub release check 10 s after startup then every 24 h, while `AutoCheckForUpdates` is on (see [Automatic updates](#automatic-updates)).

Each worker is an **independent** `BackgroundService` on its own loop and shares no lock with the others, so a stall in one can never freeze another (e.g. speed tests keep running while a scan is stuck).

### Hang protection

Every **iterative** worker wraps its per-cycle work in `Common/Watchdog.RunAsync(operation, timeout, ct)`. The watchdog races the operation against `Task.Delay(timeout)`; if the timeout wins it cancels the linked token, abandons (and observes) the stuck task, and throws `TimeoutException`. Crucially it recovers **even when an `await` never observes the token** — the original failure mode, where an in-flight `Ping` whose completion callback never fired (after an overnight network/adapter reset) wedged the scan gate indefinitely and silently killed all scanning until an app restart. On timeout each worker logs an `[INFO]` line and continues to its next cycle, so a hang self-heals and is visible in the diagnostic log. Where one worker guards two operations under a shared handler — `TrafficTracker` does — the log line must name the stage that actually timed out and quote *its* timeout, not the first one in the method.

| Worker | Bounded operation | Timeout |
|---|---|---|
| `ScanWorker` | ping/ARP/DNS scan + merge | `ScanTimeout` = 2 min |
| `SpeedTestWorker` | HTTP speed test (service also self-bounds at 120 s) | `RunTimeout` = 3 min |
| `TrafficTracker` | per-second DB flush (`ct` now threaded through all EF/SQLite calls) | `FlushTimeout` = 30 s |
| `TrafficTracker` | raw-entry purge, rate-limited to once every 5 min | `PurgeTimeout` = 2 min |
| `DigestWorker` | catch-up + report purge (startup and each loop) | `CycleTimeout` = 5 min |
| `DatabaseBackupWorker` | SQLite backup + CSV export | `BackupTimeout` = 5 min |

`DatabaseBackupWorker`'s SQLite `BackupDatabase` call is synchronous and not cancellable, so it is offloaded to a threadpool thread (`Task.Run`) — that lets the watchdog abandon the *await* and recover the loop even if the underlying copy blocks. `TrafficCollector` is excluded by design: it is a continuous ETW event pump (not an iteration loop), stopped cleanly via `ct.Register(_session.Stop)`.

## System tray & window lifecycle

`MainWindow` creates a `TrayIconService` (Win32 `Shell_NotifyIcon` + window subclass). Behaviour:

- **Close** is intercepted (`AppWindow.Closing`): instead of exiting, the window is hidden (`SW_HIDE`) — the app keeps running in the tray.
- **Tray double-click / "Show"** restores and foregrounds the window.
- **Tray "Mini graph"** toggles `MiniGraphState.IsVisible` and shows a check mark when the widget is up — the same state the Traffic toolbar toggle and Settings write, so all three stay in sync.
- **Tray "Exit"** sets `_exitRequested`, checkpoints the database, disposes the tray icon and closes for real. `MainWindow.ShutdownForUpdate` deliberately routes through this same path.

### Start with Windows / start minimized

`WindowsStartupService` registers a Scheduled Task (`schtasks … /sc onlogon /rl highest`) whose command launches the exe with a `--minimized` argument. At launch `App.ShouldStartMinimized()` checks the command line for that flag:

- **Flag present** (logon task) → the splash window is suppressed and, after the main window initialises, it is hidden to the tray (`SW_HIDE`) — no taskbar button, tray icon only.
- **Flag absent** (manual double-click or VS debug) → normal startup with splash and a visible window.

The `--minimized` flag is also forwarded through the elevated self-relaunch so the behaviour survives the admin elevation step.

## Automatic updates

```
UpdateCheckWorker (BackgroundService, only while Settings.AutoCheckForUpdates)
    └─ 10 s after startup, then every 24 h ─► UpdateService.CheckAsync (20 s deadline)
            └─ GET api.github.com/repos/jazzzsoftware/UmnathaNetworkMonitor/releases/latest
                 ├─ ReleaseInfoParser  → tag_name + one .exe asset + one .sha256 asset
                 ├─ SemanticVersion / UpdateDecision → compare against AppInfo's installed version
                 └─ UpdateCheckResult → UpdateViewModel → InfoBar banner in MainWindow

"Update now"
    └─ UpdateDownloader → UpdateDownloadStream (progress, cancellable)
    └─ ChecksumVerifier  → SHA-256 must match the .sha256 asset, or the file is discarded
    └─ MainWindow.ShutdownForUpdate() — the SAME graceful path as a tray Exit
    └─ InstallerLauncher: installer.exe /SILENT /SUPPRESSMSGBOXES /NORESTART
```

Points that are easy to get wrong and were:

- **The installer launch must not bypass the host shutdown.** An earlier version exited around `StopHost`, losing the pending traffic flush, the WAL checkpoint, the tray icon and the window placement. `ShutdownForUpdate` runs the same route as a tray Exit before the installer starts.
- **An unreadable installed version must not read as "up to date".** `AppInfo` failing to resolve a version once meant the app reported itself current forever; the decision now distinguishes "no update" from "cannot tell".
- **A check that completes before the window exists must not be lost.** `UpdateService.LastResult` holds the outcome so a result arriving during startup is still shown, rather than being dropped for 24 hours.
- **The download is cancellable** and partial files are cleaned up (`CleanUpDownloads`).
- **No Authenticode check.** Deliberate (`won't-fix`, 2026-07-27 review C1-6): the build is not code-signed, so a publisher check would reject every update rather than protect one. Until signing lands, the trust anchor is the GitHub release itself plus the SHA-256 match. Revisit when the installer is signed.
- **Timeouts and logging** — see [Diagnostic logging](#diagnostic-logging) for why the 20-second check deadline is separate from the shared client's 10-minute download timeout, and why an unreachable server logs Info rather than Error.

The release shape this expects (tag `vX.Y.Z`, one `.exe` asset, one `.exe.sha256` asset) is documented for maintainers in [`CONTRIBUTING.md`](../CONTRIBUTING.md).

## Database backup

SQLite WAL mode is used by default. On a clean exit (tray → Exit), `PRAGMA wal_checkpoint(TRUNCATE)` folds all WAL data into `networkmonitor.db` before the window closes. After a clean exit the single `.db` file is sufficient for a complete backup.

`DatabaseBackupWorker` (hosted service) additionally creates an automatic backup every 24 hours into `%LOCALAPPDATA%\UmnathaNetworkMonitor\Backups\`:

- `networkmonitor_yyyy-MM-dd_HH-mm-ss.db` — a consistent snapshot produced via the SQLite online backup API (`SqliteConnection.BackupDatabase`), which is safe to take while the app is running under WAL.
- `approved-devices_yyyy-MM-dd_HH-mm-ss.csv` — the approved (known) devices exported through `DeviceCsvExporter`, sharing the same timestamp as the `.db` snapshot.

To avoid redundant copies when the app restarts often, a backup runs at startup only if the newest existing `.db` backup is older than 24 hours; otherwise it waits out the remainder of the interval (cadence keyed off the timestamp embedded in the backup filename). Backups older than **3 days** are pruned after each successful backup; on a failed CSV export the just-written `.db` is removed so a `.db`/`.csv` pair is all-or-nothing. There is no in-app restore — restoring is a manual file operation.

> **Note — you may see 4 backups, not 3.** Retention is by **age** (delete anything older than 3 days), not by **count**. With the ~24-hour cadence, a 3-day window holds today's backup plus the three previous days, so **up to 4 `.db`/`.csv` pairs** can be present at once (a file lands right on the 3-day boundary before the next prune removes it). This is intended behaviour of an age-based policy — it is not a bug. If exactly 3 files were ever required, retention would need to switch from age-based (`RetentionDays`) to count-based ("keep newest 3").

## Unapproved device row highlighting

Device grids attach a `LoadingRow` handler that sets an amber background on rows where `Device.IsApproved == false`; approved rows use the default theme background. Because the CommunityToolkit `DataGrid` only paints a row's background when the row is realised (mutating `DataGridRow.Background` afterwards does not repaint), approval changes that must update the highlight force the rows to regenerate by resetting `DeviceGrid.ItemsSource` (`AllDevicesPage.RepaintRows`) — done after approving on the All grid and after the tab-switch reload (`ReloadAndRepaintAsync`). Routine scan refreshes keep the in-place incremental reconcile (no flicker); they don't normally change approval. The Approved devices grid does not use this handler since all rows there are approved by definition.

## Build output / deployment

Both `SelfContained=true` and `WindowsAppSDKSelfContained=true` are set, so the output folder is **fully self-contained** — the .NET runtime, the Windows App SDK / WinUI 3 runtime, and all native dependencies are copied in. Nothing needs to be pre-installed to run it; copy the `win-x64` output folder to another machine (matching architecture) and launch `NetworkMonitor.exe`. Note the database and `settings.json` live in `%LOCALAPPDATA%\UmnathaNetworkMonitor\`, **not** in the app folder, so a portable copy does not carry the dev machine's data across.

For a full file-by-file catalogue of the bin folder (~414 files + ~103 folders) and what each one is, see **[Project Bin folder description](Project%20Bin%20folder%20description.md)**.
