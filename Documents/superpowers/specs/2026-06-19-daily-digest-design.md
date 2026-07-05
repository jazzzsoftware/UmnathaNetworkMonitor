# Daily Digest — Design Spec

**Date:** 2026-06-19
**Status:** Approved

---

## Overview

Add a **Daily Digest** feature: a summary of the past 24 hours of network activity, generated automatically each day at a configurable time (default 06:00 local), persisted to a list of reports, viewable in the app, and exportable per report to **PDF** and **CSV**.

Each report is a **stored snapshot** of computed summary data — not a live query — so reports remain accurate and viewable even after the underlying raw data is purged (traffic is purged after 7 days, device history after 30).

---

## Report contents and order

The same section order is used everywhere (UI detail view, PDF, CSV):

1. **Headline banner** — a one-line at-a-glance summary/alert (e.g. "⚠️ 2 new unknown devices · 3.4 GB traffic").
2. **Traffic — top 10 apps** — chart + table.
3. **New devices** — devices first seen in the window, unknown ones highlighted — chart + table.
4. **Device activity** — appeared/disappeared over the window, plus online/offline at report time — chart + table.
5. **Unknown devices present** — devices still unapproved at report time — chart + table.

Each data section (2–5) is rendered as **a chart (image) above its table**. The headline banner has no chart.

---

## Architecture

| File | Change |
|---|---|
| `NetworkMonitor/Models/DigestReport.cs` | New — EF entity (persisted report) |
| `NetworkMonitor/Models/DigestSummary.cs` | New — serializable snapshot (stored as JSON on the report) |
| `NetworkMonitor/Data/AppDbContext.cs` | Add `DbSet<DigestReport>` + `EnsureDigestReportsTableAsync()` |
| `NetworkMonitor/Services/DigestGenerator.cs` | New — computes a `DigestSummary` for a window and saves a `DigestReport` |
| `NetworkMonitor/Services/DigestWorker.cs` | New — `BackgroundService`: startup catch-up + daily 06:00 scheduling + retention purge |
| `NetworkMonitor/Services/DigestChartRenderer.cs` | New — Win2D charts → PNG bytes |
| `NetworkMonitor/Services/DigestPdfExporter.cs` | New — QuestPDF document export |
| `NetworkMonitor/Services/DigestCsvExporter.cs` | New — CSV export of all stats |
| `NetworkMonitor/ViewModels/ReportsViewModel.cs` | New — report list, selection, commands |
| `NetworkMonitor/Views/ReportsPage.xaml(.cs)` | New — master–detail Reports page |
| `NetworkMonitor/MainWindow.xaml(.cs)` | Add "Reports" NavigationView item + route; digest-ready toast |
| `NetworkMonitor/Data/Settings.cs` | Add `DigestPurgeDays`, `DigestGenerationHour`, `DigestNotify` |
| `NetworkMonitor/ViewModels/SettingsViewModel.cs` + `Views/SettingsPage.xaml` | New "Reports" settings group |
| `NetworkMonitor/App.xaml.cs` | Register new services/ViewModel; add table ensure on startup |
| `NetworkMonitor/NetworkMonitor.csproj` | Add **QuestPDF** package reference |
| `NetworkMonitor.Tests/*` | Link + test `DigestGenerator` / `DigestSummary` |

---

## Data model

### `DigestReport` (EF entity / SQLite table)

Created via `EnsureDigestReportsTableAsync()`, matching the existing EnsureCreated + manual-table pattern (no migrations).

| Column | Type | Purpose |
|---|---|---|
| `Id` | int PK | |
| `PeriodStart` | DateTime (UTC) | start of the 24h window |
| `PeriodEnd` | DateTime (UTC) | end of the 24h window |
| `GeneratedAt` | DateTime (UTC) | when produced |
| `Headline` | string | section-1 banner text |
| `SummaryJson` | string | serialized `DigestSummary` |
| `IsScheduled` | bool | `true` for daily/catch-up runs, `false` for manual "Generate now" |

### `DigestSummary` (serialized snapshot)

Holds everything needed to render charts + tables without touching raw (purgeable) data:

- **Traffic:** `TotalBytesSent`, `TotalBytesReceived`, `TopApps` (top 10: process name, bytes sent, bytes received).
- **NewDevices:** list (name, MAC, IP, vendor, type, `IsKnown`, `FirstSeen`).
- **Activity:** `AppearedCount`, `DisappearedCount`, `OnlineCount`, `OfflineCount`, and `HourlyActivity` (24 buckets of appeared/disappeared for the chart).
- **UnknownDevices:** list at report time (name, MAC, IP, vendor, type).
- **Headline:** the banner string (also stored on the entity column for cheap list display).

Serialized with `System.Text.Json`.

---

## Generation & scheduling

### Window definition

A daily report covers the **24 hours ending at the generation time** (default 06:00 **local**). The 06:00 boundary is computed in local time and converted to UTC for queries (events/traffic are stored UTC).

### `DigestGenerator`

`Task<DigestReport> GenerateAsync(DateTime startUtc, DateTime endUtc)`:
- Queries `DeviceEvents` (Appeared/Disappeared in window), `Devices` (first-seen in window, unknown set, online/offline counts), and `TrafficRollups` (aggregated bytes per process in window; top 10 by total).
- Builds the `DigestSummary` and headline, persists a `DigestReport`, returns it.

### `DigestWorker : BackgroundService`

- **On startup — catch-up:** determine the most recent **scheduled** report (`IsScheduled = true`; manual runs are ignored so they can't advance the catch-up cursor and skip a day). For each missed daily 06:00 boundary from then until the latest past 06:00 — bounded by the retention window — generate the missing report(s) with `IsScheduled = true`.
- **Schedule:** compute the delay to the next 06:00 local, generate, then repeat daily.
- **Retention purge:** delete reports with `GeneratedAt` older than `DigestPurgeDays` (default 90). Folded into the existing purge cadence.
- **Completion event:** raises an event after each generation so `MainWindow` can toast and the Reports page can refresh.

### Manual generation

A **"Generate now"** command produces a report for the **trailing 24h ending now** (`GeneratedAt = now`, `PeriodEnd = now`, `PeriodStart = now − 24h`, `IsScheduled = false`). Because it is flagged unscheduled, an ad-hoc manual run never suppresses the next automatic daily digest.

---

## UI — Reports page

- New **NavigationView item "Reports"** in `MainWindow`, routing to `ReportsPage`.
- **Two-tab** layout (a `SelectorBar`, matching `SettingsPage`; DI'd `ReportsViewModel`, following existing page + XAML-formatting conventions):
  - **"Daily Digest" tab:** renders the **latest** report from its `DigestSummary` snapshot — headline banner, then the section charts. Toolbar: `Generate now`, `Export PDF`, `Export CSV` (act on the latest report).
  - **"History" tab:** master–detail — a **list** of all reports newest-first (date + headline) on the left, the **selected** report rendered from its snapshot on the right. Toolbar: `Export PDF`, `Export CSV`, `Delete` (act on the selected report).
- The headline + chart render is a shared `DigestReportView` UserControl (a `DigestSummary` dependency property) hosted by both tabs, so the rendering and chart-to-bitmap code lives in one place.
- Rendering from the snapshot makes opening any report instant and purge-proof.

---

## Charts

A **`DigestChartRenderer`** service draws each chart with **Win2D** (`CanvasRenderTarget` → PNG bytes) at a fixed size/DPI. The **same PNG** is shown on the page (`Image`) and embedded in the PDF, so they are pixel-identical. Charts render **on demand from the snapshot** (images are not stored).

| Section | Chart |
|---|---|
| Traffic | Horizontal bar — top 10 apps by total bytes |
| New devices | Bar — count by device type (unknown highlighted) |
| Device activity | Grouped bars by hour — appeared vs disappeared |
| Unknown present | Bar — unknown count by device type |

Win2D offscreen rendering (no `CanvasControl`) works in the unpackaged app.

---

## Exports (per report)

- **PDF — `DigestPdfExporter` (QuestPDF):** composes the document in canonical order — headline, then each section's chart image + table. Saved via `FileSavePicker` (same pattern as `ApprovedDevicesPage` export). Default filename `Umnatha Digest <yyyy-MM-dd>.pdf`.
- **CSV — `DigestCsvExporter`:** exports all stats from the snapshot in the same section order, as labelled blocks (a header line per section followed by rows). Follows `DeviceCsvExporter` style. Saved via `FileSavePicker`.

---

## Settings (new "Reports" group)

Added to `Settings.cs` and the Settings page:

| Setting | Default | Purpose |
|---|---|---|
| `DigestPurgeDays` | 90 | retention for generated reports |
| `DigestGenerationHour` | 6 | daily generation time (local, 24h) |
| `DigestNotify` | true | show a toast when a digest is ready |

---

## Notifications

When `DigestWorker` finishes generating, it raises an event; `MainWindow` shows a **toast** with the report headline (reusing the existing `ToastNotificationManager` infrastructure), gated by `DigestNotify`. The toast is informational — clicking activates the app (best-effort); no new COM activator plumbing is added.

---

## New dependency

- **QuestPDF** added to `NetworkMonitor.csproj`. Community license (free for companies under USD 1M annual revenue — applicable to Jazzz Software).

---

## Testing

- xUnit tests (linking the relevant source files into `NetworkMonitor.Tests`, as the existing tests do) for **`DigestGenerator`** and **`DigestSummary`**: feed synthetic `DeviceEvent` / `Device` / traffic rollup data for a window and assert computed counts, top-10 ordering, new-device detection, online/offline counts, and headline text.
- Chart rendering and PDF/CSV file output are not unit-tested (UI/IO), consistent with the current test scope.

---

## Out of scope

- Emailing reports.
- Clickable toast deep-link into a specific report (toast is informational only).
- Per-section configurability (which sections appear) — the set is fixed for v1.
- Weekly/monthly digests.
