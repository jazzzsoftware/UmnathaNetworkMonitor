# Local Traffic Attribution Implementation Plan

> **SUPERSEDED 2026-07-16** by `2026-07-16-local-traffic-app-centric.md` — the Local tab was pivoted from device-centric to **app-centric** (apps over the LAN, device as drill-down) with Internet made WAN-only. This device-centric plan is kept for history only. See `Documents/superpowers/specs/2026-07-16-local-traffic-app-centric-design.md`.

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Revised 2026-07-15** to fold in these decisions (superseding the original 2026-07-06 draft):

1. The former "Traffic" tab is now **Internet** (rename already shipped). The new tab is **Local**, placed **between Internet and Speed Test**.
2. **System is removed everywhere in the Internet view** — the app grid, the chart, the "All Apps" total, *and* the digest's top-apps. `ProcessName = 'System'` is hard-excluded (no toggle).
3. **Local is added to all reports** — the daily digest gets a Local section with **both a chart and a table**.
4. The **Local page follows the Internet page design with full parity** — area chart, range buttons (5m/1h/6h/24h/7d), Live/Paused/History badge, pause-on-scroll, click-a-bar History, click-row-to-filter — and **matches Internet's columns**.

**Goal:** A "Local" tab that isolates LAN-local traffic by remote endpoint (resolved to a known device name where possible) with the same chart+grid experience as Internet — so a NAS backup shows as "→ Synology NAS" instead of being lost under System.

**Architecture:** The ETW collector already aggregates bytes by PID; leave that path untouched and add a *second, parallel* in-memory dictionary keyed by the remote IPv4 address, populated only when a cheap classifier says the address is LAN-local. A dedicated per-minute `LocalTrafficRollup` table stores the aggregates. The UI resolves each stored IP to a device name at display time against the current known-device list.

**Tech Stack:** .NET 10, WinUI 3, EF Core 10 + SQLite (EnsureCreated, no migrations), CommunityToolkit.Mvvm, Microsoft.Diagnostics.Tracing (TraceEvent), xUnit v3.

---

## Naming rule (LOCKED)

**Anything named `Traffic*` is a COMMON element shared by both tabs.** Everything tab-specific is prefixed `Internet*` or `Local*` (`Lan*` for the classifier).

| Bucket | Names |
|---|---|
| **Common (`Traffic*`)** | `TrafficCollector`, `TrafficTracker`, `TrafficFlushedEventArgs`, `TrafficWindow`, `TrafficRateFormatter`, `TrafficAreaChart`, `TrafficHostPage`, the capture tables `TrafficRollups`/`TrafficEntries` (+ entities `TrafficRollup`/`TrafficEntry` — they hold **all-process** traffic incl. local bytes and feed the digest), and `Settings.TrafficIntervalSeconds` / `Settings.TrafficPurgeDays`. Also common: `ByteSizeFormatter` (Services/Common). |
| **Internet-specific (`Internet*`)** | `InternetPage`, `InternetViewModel`, `InternetLoadResult`, `InternetTrafficAppRow` *(rename of `TrafficAppRow`)*, `InternetTrafficAppSummary` *(rename of `TrafficAppSummary`)*, `DigestSummary.InternetTopApps` *(rename of `TopApps`)*, `DigestChartRenderer.RenderInternetTrafficChart` / `RenderInternetTrafficSplitChart` *(renames)*, `Settings.InternetTimeRangeHours` *(rename of `TrafficTimeRangeHours`)*. |
| **Local-specific (`Local*` / `Lan*`)** | `LocalPage`, `LocalViewModel`, `LocalLoadResult`, `LocalTrafficDeviceRow`, `LocalTrafficDeviceSummary`, `DigestSummary.TopLocalDevices`, `LocalTrafficRollup`/`LocalTrafficRollups`, `LanClassifier`, `LocalTrafficAggregator`, `LocalTrafficNameResolver`, `LocalTrafficMinute`, `LocalTrafficDelta`, `DigestChartRenderer.RenderLocalTrafficSplitChart`, `Settings.LocalTimeRangeHours` *(new)*. |

Genuinely shared private helpers inside `DigestChartRenderer` (`DrawGroupedBars`, the private `FormatBytes(double)`) stay unprefixed.

---

## Global Constraints

Copied from the resolved decision spec (`Documents/superpowers/specs/2026-07-05-local-traffic-attribution-design.md`, section 8) and `CLAUDE.md`:

- **v1 is IPv4-only.** Do not wire the `...IPV6` ETW variants. In-memory key is a packed `uint`; the DB `RemoteIp` column stores the **canonical string** so IPv6 fits later with no schema change.
- **v1 is endpoint-only.** No app-level (handle-enumeration) attribution.
- **Do not re-key the existing `pid` dictionary.** Add a separate LAN-only dictionary; internet packets are classified out with a handful of integer compares and never touch it.
- **One new table only** — `LocalTrafficRollup (MinuteEpoch, RemoteIp, BytesUploaded, BytesDownloaded)`. No changes to `TrafficEntry` / `TrafficRollup` schema.
- **Name resolution at display time**, against the current `Devices` table (`IpAddress` → `DisplayName`); unmatched → bare IP. No staleness threshold.
- **Retention** follows the existing `Settings.TrafficPurgeDays` policy — no new setting.
- **DB impact:** the new table requires a **one-time local DB delete** on upgrade (EnsureCreated, no migrations). State this in the completion summary.
- **Coding conventions (CLAUDE.md):** no `var`; no single-character names; always curly braces; `string.Empty` not `""`; single exit point (one `return` at the end, value assigned to a local first); blank lines around every block and at method boundaries; class member order Fields → Constructor → Properties → Public → Override → Private; backing field directly above its hand-written `SetProperty` property (no `[ObservableProperty]`); property `{`/`get;`/`set;` each on their own line; no underscores except leading `_` on private fields.
- **XAML conventions:** `InternetPage.xaml` / `DevicesPage.xaml` are the canonical references — blank line after `<?xml?>`, one attribute per line indented 4 spaces, simple assignments → event handlers/Command → value bindings, blank line around every element.
- **slnx:** every new file must be added to `NetworkMonitor.slnx`.

---

## File Structure

**New files:**

| File | Responsibility |
|---|---|
| `NetworkMonitor/Services/Traffic/LanClassifier.cs` | Pure classifier: pack `IPAddress`→IPv4 `uint`, decide LAN-local, format back to dotted string. Active-subnet ranges from `NetworkInterface`, refreshed on network change. |
| `NetworkMonitor/Models/LocalTrafficRollup.cs` | EF entity for the per-minute LAN aggregate. |
| `NetworkMonitor/Models/LocalTrafficDeviceRow.cs` | Immutable display row for the Local grid (endpoint IP, device name, up, down, total + formatted text). |
| `NetworkMonitor/Services/Traffic/LocalTrafficDelta.cs` | Record carried on the flush event: one endpoint's bytes for the just-completed interval. |
| `NetworkMonitor/Services/Traffic/LocalTrafficAggregator.cs` | Pure: rollup rows + device-name map → sorted `LocalTrafficDeviceRow`s (defines `LocalTrafficMinute`). |
| `NetworkMonitor/Services/Traffic/LocalTrafficNameResolver.cs` | Pure: resolve an IP against a name map, falling back to the bare IP. |
| `NetworkMonitor/ViewModels/LocalViewModel.cs` | Full-parity VM mirroring `InternetViewModel`, keyed by endpoint (defines `LocalLoadResult`). |
| `NetworkMonitor/Views/LocalPage.xaml` (+ `.xaml.cs`) | Chart + range buttons + badge + endpoint grid; mirrors `InternetPage`. |
| `NetworkMonitor/Models/LocalTrafficDeviceSummary.cs` | Digest per-endpoint summary row (parallel to `InternetTrafficAppSummary`). |
| `NetworkMonitor.Tests/LanClassifierTests.cs` | Classification + packing. |
| `NetworkMonitor.Tests/LocalTrafficNameResolverTests.cs` | Name resolution. |
| `NetworkMonitor.Tests/LocalTrafficAggregatorTests.cs` | Aggregation (totals + sort). |

**Modified files:**

| File | Change |
|---|---|
| `NetworkMonitor/Services/Traffic/TrafficCollector.cs` | Inject `LanClassifier`; capture remote address; second LAN dictionary; `DrainAndResetLocal()`. |
| `NetworkMonitor/Services/Traffic/TrafficFlushedEventArgs.cs` | Add `LocalDeltas` payload. |
| `NetworkMonitor/Services/Traffic/TrafficTracker.cs` | Drain LAN snapshot; upsert `LocalTrafficRollup`; raise deltas on flush. |
| `NetworkMonitor/Data/AppDbContext.cs` | `DbSet<LocalTrafficRollup>` + unique index. |
| `NetworkMonitor/Services/Scanning/ScanWorker.cs` | Purge old `LocalTrafficRollups` alongside the existing traffic purge. |
| `NetworkMonitor/ViewModels/InternetViewModel.cs` | Exclude `ProcessName = 'System'` from both SQL queries + the live-flush path. |
| `NetworkMonitor/Models/TrafficAppRow.cs` → `InternetTrafficAppRow.cs` | Rename (class + file); update `InternetViewModel` + `InternetPage.xaml`. |
| `NetworkMonitor/Data/Settings.cs` | Rename `TrafficTimeRangeHours` → `InternetTimeRangeHours`; add `LocalTimeRangeHours`. |
| `NetworkMonitor/Views/SettingsPage.xaml` | Genericise the Traffic-section descriptions (apply to both tabs). |
| `NetworkMonitor/Views/TrafficHostPage.xaml` (+ `.xaml.cs`) | Add "Local" `SelectorBarItem` + `LocalFrame` between Internet and Speed Test + navigation. |
| `NetworkMonitor/App.xaml.cs` | Register `LanClassifier` and `LocalViewModel`. |
| `NetworkMonitor/Models/DigestSummary.cs` | Rename `TopApps`→`InternetTopApps`; add `TopLocalDevices`. |
| `NetworkMonitor/Models/TrafficAppSummary.cs` → `InternetTrafficAppSummary.cs` | Rename. |
| `NetworkMonitor/Services/Digest/DigestSummaryBuilder.cs` | Build `TopLocalDevices` from `LocalTrafficRollups`; exclude System from `InternetTopApps`. |
| `NetworkMonitor/Services/Digest/DigestChartRenderer.cs` | Rename `RenderTrafficChart`/`RenderTrafficSplitChart`→`RenderInternetTraffic*`; add `RenderLocalTrafficSplitChart`. |
| `NetworkMonitor/Services/Digest/DigestPdfExporter.cs` | Add Local section (chart + table); update Internet method calls. |
| `NetworkMonitor/Services/Digest/DigestCsvExporter.cs` | Add Local devices table (Raw + Friendly columns). |
| `NetworkMonitor/Views/Controls/DigestReportView.xaml` (+ `.cs`) | Add Local chart image + top-devices table. |
| `NetworkMonitor.slnx` | Add every new file + this plan. |

---

## Task 1 — LanClassifier (pure) + IPv4 packing

Unchanged from the original draft (naming already `Lan*`). Create `LanClassifier` with:
- `bool TryClassifyLocal(IPAddress, out uint packed)` — `true` only when IPv4 **and** LAN-local.
- `static bool TryPackIpv4(IPAddress, out uint)` / `static string Format(uint)`.
- `void Refresh()` — rebuild active-subnet ranges; ctor subscribes to `NetworkChange.NetworkAddressChanged`.
- Fixed ranges: `10/8`, `172.16/12`, `192.168/16`, `169.254/16`, unioned with each up interface's `ip & mask`…`start | ~mask`. Store as `private volatile (uint Start,uint End)[] _ranges` for lock-free swap.

TDD: `LanClassifierTests` — RFC1918/link-local classify local; public addresses (8.8.8.8, 1.1.1.1, 172.32.0.1, 11.0.0.1, 192.169.0.1) not local; IPv6 rejected; pack/format round-trip `192.168.1.50`↔`0xC0A80132`. (Full test + impl code as in the original draft — reuse verbatim.)

**Commit:** `Add LanClassifier for IPv4 LAN-local classification.`

---

## Task 2 — LocalTrafficRollup entity + DbContext mapping

- Create `Models/LocalTrafficRollup.cs`: `int Id; long MinuteEpoch; string RemoteIp = string.Empty; long BytesUploaded; long BytesDownloaded;`.
- `AppDbContext`: `public DbSet<LocalTrafficRollup> LocalTrafficRollups => Set<LocalTrafficRollup>();` and a unique index on `{ MinuteEpoch, RemoteIp }` in `OnModelCreating`.
- Build to verify mapping.

**DB note:** new table ⇒ one-time DB delete on upgrade.

**Commit:** `Add LocalTrafficRollup entity and DbContext mapping.`

---

## Task 3 — TrafficCollector second LAN dictionary

- `TrafficCollector(LanClassifier lanClassifier) : BackgroundService`; add `ConcurrentDictionary<uint,long[]> _localCounters`.
- `Dictionary<uint,(long Upload,long Download)> DrainAndResetLocal()` mirroring `DrainAndReset()`.
- Handlers pass the remote address (`daddr` on send, `saddr` on recv). `AddBytes(int pid, IPAddress remote, int bytes, bool upload)` keeps PID accumulation and additionally accumulates into `_localCounters` only when `lanClassifier.TryClassifyLocal(remote, out uint packed)`.
- Confirm TraceEvent property names `saddr`/`daddr` at first build.

**Commit:** `Add LAN-local byte accumulation to TrafficCollector.`

---

## Task 4 — Flush args + TrafficTracker upsert

- `LocalTrafficDelta(string RemoteIp, long BytesUploaded, long BytesDownloaded)` record.
- `TrafficFlushedEventArgs(IReadOnlyList<TrafficEntry> entries, IReadOnlyList<LocalTrafficDelta> localDeltas)` — add `LocalDeltas`.
- In `TrafficTracker.FlushAsync`: drain `DrainAndResetLocal()`, build the delta list via `LanClassifier.Format`, upsert into `LocalTrafficRollups` with `ON CONFLICT(MinuteEpoch,RemoteIp) DO UPDATE SET Bytes… = Bytes… + excluded.…`, and always raise `Flushed` with both `entries` and `localDeltas`. Guard becomes `entries.Count > 0 || localDeltas.Count > 0`.
- Add `UpsertLocalRollupsAsync` mirroring `UpsertRollupsAsync`. (Full code as in the original draft.)

**Commit:** `Persist LAN rollups and carry LAN deltas on traffic flush.`

---

## Task 5 — Purge wiring

In `ScanWorker.PurgeOldHistoryAsync`, inside the `TrafficPurgeDays` block, add `DELETE FROM LocalTrafficRollups WHERE MinuteEpoch < {rollupCutoffEpoch}` next to the existing `TrafficRollups` delete.

**Commit:** `Purge LocalTrafficRollups under the traffic retention policy.`

---

## Task 6 — LocalTrafficNameResolver (pure)

`static string Resolve(string remoteIp, IReadOnlyDictionary<string,string> namesByIp)` → mapped name, else `remoteIp` (also falls back when the name is empty/whitespace).

TDD: known IP → name; unknown → bare IP; empty name → bare IP. (Full code as in the original draft.)

**Commit:** `Add LocalTrafficNameResolver for display-time IP naming.`

---

## Task 7 — LocalTrafficDeviceRow model + LocalTrafficAggregator (pure)

- `Models/LocalTrafficDeviceRow.cs` — record `(string RemoteIp, string DisplayName, long BytesUploaded, long BytesDownloaded)` with computed `long TotalBytes => BytesUploaded + BytesDownloaded;` and formatted read-only props for the grid:
  - `DownloadText => ByteSizeFormatter.Format(BytesDownloaded)`
  - `UploadText => ByteSizeFormatter.Format(BytesUploaded)`
  - `TotalText => ByteSizeFormatter.Format(TotalBytes)`

  (Columns match Internet exactly: **Device · Download · Upload · Total** — no Current/Peak.)
- `Services/Traffic/LocalTrafficAggregator.cs` — defines `LocalTrafficMinute(long MinuteEpoch, string RemoteIp, long BytesUploaded, long BytesDownloaded)` and `static IReadOnlyList<LocalTrafficDeviceRow> Build(IReadOnlyList<LocalTrafficMinute> minutes, IReadOnlyDictionary<string,string> namesByIp)` — groups per IP into totals, resolves the name via `LocalTrafficNameResolver`, sorts by `TotalBytes` desc.

TDD: sums bytes per endpoint + resolves name; sorts by total desc. (No Peak — columns match Internet.)

**Commit:** `Add LocalTrafficDeviceRow and LocalTrafficAggregator with tests.`

---

## Task 8 — Exclude System from the Internet view

**Files:** `InternetViewModel.cs`.

- In `LoadAppRowsAsync` and `LoadChartBucketsAsync`, add `AND ProcessName <> 'System'` to the `whereClause` (so the grid list, the chart buckets, **and** the "All Apps" total — which sums the grid rows — all drop System).
- In the live path (`ApplyFlushToWindow` / `SeedWindowState`), skip entries where `entry.ProcessName == "System"`.
- Verify the chart total and the grid total agree (both now exclude System).

**Test:** add an `InternetViewModel`-level check is impractical (WinUI VM not linked into tests); rely on the SQL predicate + a manual check. Document the exclusion in the completion summary.

**Commit:** `Exclude System from the Internet traffic view.`

---

## Task 9 — Settings: split the time-range + genericise descriptions

**Files:** `Data/Settings.cs`, `ViewModels/InternetViewModel.cs`, `Views/SettingsPage.xaml`, (later) `LocalViewModel.cs`.

- `Settings`: rename `TrafficTimeRangeHours` → `InternetTimeRangeHours`; add `LocalTimeRangeHours` (same default `5.0/60.0`).
- `InternetViewModel`: point its `TimeRangeHours` backing persistence at `_settings.InternetTimeRangeHours`.
- `SettingsPage.xaml` (Traffic section) — reword descriptions so they cover **both** Internet and Local:
  - "Chart smooth scrolling" description: "the Traffic chart" → "the Internet and Local traffic charts".
  - Keep the section header "Traffic" (it's the common container); confirm "Scan Interval (seconds)" and "Purge traffic older than (days)" read generically (they apply to both tabs' capture/retention).
- `TrafficIntervalSeconds` / `TrafficPurgeDays` stay (common).

**DB note:** renaming a settings JSON key just re-seeds that value to default on first load — no DB impact.

**Commit:** `Split traffic time-range setting per tab and genericise Settings descriptions.`

---

## Task 10 — Rename TrafficAppRow → InternetTrafficAppRow

**Files:** `Models/TrafficAppRow.cs`→`InternetTrafficAppRow.cs`, `InternetViewModel.cs`, `InternetPage.xaml`.

- `git mv` the model file; rename the class; update the `x:DataType="models:TrafficAppRow"` bindings in `InternetPage.xaml` and all usages in `InternetViewModel`.
- Build + full test suite.

**Commit:** `Rename TrafficAppRow to InternetTrafficAppRow.`

---

## Task 11 — LocalViewModel (full parity)

**Files:** create `ViewModels/LocalViewModel.cs` (defines `LocalLoadResult`); modify `App.xaml.cs` (register `LanClassifier` + `LocalViewModel`).

**Approach: mirror `InternetViewModel` exactly**, substituting the endpoint dimension for the app dimension. The two VMs are structurally identical; the differences are:

| InternetViewModel | LocalViewModel |
|---|---|
| `Apps : ObservableCollection<InternetTrafficAppRow>` | `Devices : ObservableCollection<LocalTrafficDeviceRow>` |
| `SelectedApp` (ProcessName key) | `SelectedEndpoint` (RemoteIp key) |
| `_settings.InternetTimeRangeHours` | `_settings.LocalTimeRangeHours` |
| reads `TrafficRollups`/`TrafficEntries`, groups by `ProcessName` | reads `LocalTrafficRollups`, groups by `RemoteIp` |
| filters `ProcessName <> 'System'` | (no System filter — LAN table has none) |
| name = process path/name | name = `LocalTrafficNameResolver.Resolve(ip, namesByIp)` against `Devices` |
| `ApplyLiveFlushAsync(IReadOnlyList<TrafficEntry>)` | `ApplyLiveFlushAsync(IReadOnlyList<LocalTrafficDelta>)` |
| `InternetLoadResult` | `LocalLoadResult` |

- Bring across the **whole** bucketed-chart machinery: `TimeRangeHours`, `ChartPoints`, `SelectedBucketStart`, `IsLoading`, `StatusText`, `LoadAsync`, `ApplyLiveFlushAsync`, `SeedWindowState`, `ApplyFlushToWindow`, `RebuildRows`, `BuildDataAsync`, the two SQL loaders, `BucketSizeFor` (reuse the shared logic — call `InternetViewModel.BucketSizeFor` or lift it to a common helper), and `TrafficWindow.AlignedCutoffEpoch`.
- The chart's per-bucket up/down comes from `LocalTrafficRollups` summed over all endpoints (or the `SelectedEndpoint` when one is pinned) — same shape as Internet's per-app chart.
- Name map: materialise `Devices` first (because `Device.DisplayName` is `[NotMapped]`), then build `Dictionary<string,string>` IP→DisplayName in memory.
- `StatusText` via `ByteSizeFormatter.Format`.
- Register `services.AddSingleton<LanClassifier>();` (before `TrafficCollector`) and `services.AddSingleton<LocalViewModel>();`.

**Commit:** `Add LocalViewModel with full Internet-parity chart/range/pause.`

---

## Task 12 — LocalPage view + host tab

**Files:** create `Views/LocalPage.xaml` (+ `.xaml.cs`); modify `TrafficHostPage.xaml` (+ `.xaml.cs`).

- **`LocalPage.xaml`: copy `InternetPage.xaml` verbatim**, then swap:
  - `x:Class` → `NetworkMonitor.Views.LocalPage`.
  - grid `x:DataType` → `models:LocalTrafficDeviceRow`; the four columns → **Device** (`DisplayName` bold over `RemoteIp` sub-line), **Download** (`DownloadText`), **Upload** (`UploadText`), **Total** (`TotalText`). Drop the app-specific "Open" button.
  - all `ViewModel.Apps` → `ViewModel.Devices`.
- **`LocalPage.xaml.cs`: copy `InternetPage.xaml.cs`**, retype `ViewModel` to `LocalViewModel`, resolve `LocalViewModel` from DI, keep the identical `Flushed` subscribe-on-`Loaded`/unsubscribe-on-`Unloaded` lifecycle, pause/range/badge handlers, and `OnTrafficFlushed` calling `ViewModel.ApplyLiveFlushAsync(args.LocalDeltas)`. Log tag `"LocalPage.OnTrafficFlushed"`.
- **`TrafficHostPage.xaml`**: insert a `SelectorBarItem Tag="Local" Text="Local"` **between Internet and Speed Test**, and a `LocalFrame` between `InternetFrame` and `SpeedTestFrame`.
- **`TrafficHostPage.xaml.cs`**: lazy-navigate `LocalFrame` to `typeof(LocalPage)` on first "Local" selection; toggle `LocalFrame.Visibility`; call `localPage.ResetToLive()` when leaving the Local tab (mirroring the Internet handling).

**Commit:** `Add Local tab with full-parity page between Internet and Speed Test.`

---

## Task 13 — Reports: Local section (chart + table)

**Files:** `Models/DigestSummary.cs`, `Models/TrafficAppSummary.cs`→`InternetTrafficAppSummary.cs`, new `Models/LocalTrafficDeviceSummary.cs`, `DigestSummaryBuilder.cs`, `DigestChartRenderer.cs`, `DigestPdfExporter.cs`, `DigestCsvExporter.cs`, `DigestReportView.xaml(.cs)`.

- **Model renames:** `TrafficAppSummary`→`InternetTrafficAppSummary`; `DigestSummary.TopApps`→`InternetTopApps`. New `LocalTrafficDeviceSummary(string DeviceName, string RemoteIp, long BytesDownloaded, long BytesUploaded)` + `DigestSummary.TopLocalDevices`.
- **`DigestSummaryBuilder`:** build `TopLocalDevices` by aggregating `LocalTrafficRollups` over the period grouped by `RemoteIp`, resolving names against the period's device list; exclude `System` when building `InternetTopApps`.
- **`DigestChartRenderer`:** rename the two internet methods to `RenderInternetTrafficChart` / `RenderInternetTrafficSplitChart`; add `RenderLocalTrafficSplitChart(summary, lightBackground)` (Download-vs-Upload over the top local endpoints, reusing the shared `DrawGroupedBars`).
- **`DigestReportView.xaml`:** after the Internet card, add a **Local** card: heading "Local — Download vs Upload", the local split chart image, "Top devices by local traffic", and a table (**Device · Download · Upload · Total**) bound to `TopLocalDevices` (`x:DataType="models:LocalTrafficDeviceSummary"`).
- **`DigestPdfExporter`:** add the parallel Local section (chart image + table) after the Internet section; byte cells via `ByteSizeFormatter.Format`.
- **`DigestCsvExporter`:** add a "Top devices by local traffic" table with **Raw + Friendly** paired columns (Device, Endpoint, Download (Raw)/(Friendly), Upload (Raw)/(Friendly)) — same style as the Internet top-apps table.
- Update `DigestCsvExporterTests` / any digest tests for the renamed field + new section.

**Commit:** `Add Local network traffic section to all digest reports.`

---

## Final: full test + build gate

- [ ] `dotnet test NetworkMonitor.Tests` — all existing + `LanClassifierTests`, `LocalTrafficNameResolverTests`, `LocalTrafficAggregatorTests` PASS.
- [ ] `dotnet build NetworkMonitor.slnx -c Release -p:Platform=x64` — Build succeeded.
- [ ] **Manual end-to-end (spec §10):** delete the DB once; run; open **Traffic → Local** (between Internet and Speed Test); run a NAS/SMB copy; within one interval a row for the NAS endpoint appears with a live chart, growing Download/Upload/Total, resolved to the device name if known; confirm **System no longer appears on Internet**; confirm the digest report shows the Local section (chart + table) and CSV has Raw/Friendly columns.
- [ ] Completion summary must state: **a one-time local DB delete is required on upgrade** (new `LocalTrafficRollups` table via EnsureCreated).

---

## Notes / carried assumptions

- `Device.DisplayName` is `[NotMapped]` → materialise `Devices` before building the IP→name map (both in `LocalViewModel` and `DigestSummaryBuilder`).
- TraceEvent property names `args.daddr` / `args.saddr` are the one external assumption to confirm at first build of Task 3.
- IPv6 explicitly out of scope; `RemoteIp` stored as string keeps the door open with no schema change.
- The capture tables `TrafficRollups`/`TrafficEntries` intentionally keep the `Traffic*` name — they hold all-process traffic **including local bytes** and are read by both the Internet tab and the digest, so they are common, not Internet-owned.
