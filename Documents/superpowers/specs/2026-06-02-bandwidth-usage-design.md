# Bandwidth Usage Page — Design Spec

**Date:** 2026-06-02
**Status:** Approved

---

## Overview

Add a dedicated **Bandwidth** page that monitors internet traffic per application using ETW (Event Tracing for Windows). Data is persisted to the database on every scan tick and displayed as a GlassWire-style filled area chart with a per-app breakdown table. The app must run elevated (Administrator) for ETW to function.

---

## Architecture

| File | Change |
|---|---|
| `NetworkMonitor/Models/BandwidthEntry.cs` | New entity |
| `NetworkMonitor/Data/AppDbContext.cs` | Add `DbSet<BandwidthEntry>`, index on `(Timestamp, ProcessName)` |
| `NetworkMonitor/Services/BandwidthCollector.cs` | New singleton — wraps ETW session, accumulates bytes per PID |
| `NetworkMonitor/Services/BandwidthTracker.cs` | New singleton — on each scan tick, drains collector, resolves PID→name, writes to DB |
| `NetworkMonitor/ViewModels/BandwidthViewModel.cs` | New |
| `NetworkMonitor/Views/BandwidthPage.xaml` | New |
| `NetworkMonitor/Views/BandwidthPage.xaml.cs` | New |
| `NetworkMonitor/MainWindow.xaml` | Add nav item |
| `NetworkMonitor/MainWindow.xaml.cs` | Add route |
| `NetworkMonitor/App.xaml.cs` | Register `BandwidthCollector` and `BandwidthTracker` via `AddHostedService` |

NuGet dependency: `Microsoft.Diagnostics.Tracing.TraceEvent`

---

## Data Model

```csharp
public class BandwidthEntry
{
    public int Id { get; set; }
    public DateTime Timestamp { get; set; }   // UTC, set at drain time
    public string ProcessName { get; set; }   // e.g. "chrome.exe"
    public long BytesSent { get; set; }
    public long BytesReceived { get; set; }
}
```

`AppDbContext` additions:
- `DbSet<BandwidthEntry> BandwidthEntries`
- Composite index on `(Timestamp, ProcessName)` for fast time-range queries

Purging: reuses the existing `PurgeDays` setting from `ScannerSettings` — cleaned up by the same background job that purges device history.

---

## BandwidthCollector

`BackgroundService` registered via `AddHostedService`, consistent with `ScanWorker`.

**Startup:** Opens a named ETW session subscribing to the `Microsoft-Windows-Kernel-Network` provider. Handles four event types: `TcpIpSend`, `TcpIpRecv`, `UdpIpSend`, `UdpIpRecv`. Each event carries a PID and byte count.

**Accumulation:** Bytes are accumulated in a `ConcurrentDictionary<int, (long Sent, long Received)>` keyed by PID. Each event handler atomically increments the appropriate counter.

**DrainAndReset():** Atomically replaces the dictionary with a fresh empty one and returns the snapshot. Called by `BandwidthTracker` on each scan tick.

**Shutdown:** ETW session torn down cleanly in `Dispose()`.

---

## BandwidthTracker

Singleton service. Subscribes to `ScanWorker.ScanCompleted` — no additional timer.

On each `ScanCompleted` event:
1. Calls `BandwidthCollector.DrainAndReset()` to get the current snapshot.
2. For each PID in the snapshot, calls `Process.GetProcessById()` to resolve the process name. If the process has already exited, the entry is skipped.
3. Filters out entries where both `BytesSent` and `BytesReceived` are zero.
4. Writes one `BandwidthEntry` per remaining process to the DB using `IDbContextFactory<AppDbContext>`, with `Timestamp = DateTime.UtcNow`.

---

## BandwidthViewModel

### Observable properties

| Property | Type | Purpose |
|---|---|---|
| `TimeRangeHours` | `double` | Query window: 1 / 6 / 24 / 168 / 720 |
| `SelectedApp` | `string?` | `null` = All Apps; process name = single-app filter |
| `ChartPoints` | `ObservableCollection<ChartPoint>` | Aggregated per-bucket for the area chart |
| `Apps` | `ObservableCollection<BandwidthAppRow>` | Per-app totals, sorted by `TotalBytes` descending |
| `StatusText` | `string` | e.g. "14 apps · 38.7 GB total" |

### Supporting types

```csharp
public record ChartPoint(DateTime BucketStart, long BytesSent, long BytesReceived);
public record BandwidthAppRow(string ProcessName, long BytesSent, long BytesReceived)
{
    public long TotalBytes => BytesSent + BytesReceived;
}
```

### Bucketing

| Time range | Bucket size | Buckets |
|---|---|---|
| 1h | 5 minutes | 12 |
| 6h | 30 minutes | 12 |
| 24h | 1 hour | 24 |
| 7d | 6 hours | 28 |
| 30d | 1 day | 30 |

### LoadAsync()

Called on page load and when `TimeRangeHours` or `SelectedApp` changes.

1. Queries `BandwidthEntries` within the selected time window.
2. If `SelectedApp` is set, filters to that process name only.
3. Groups entries into time buckets, summing `BytesSent` and `BytesReceived` per bucket → `ChartPoints`.
4. Groups entries by `ProcessName`, summing totals → `Apps` (sorted by `TotalBytes` descending).
5. Updates `StatusText`.

---

## BandwidthPage UI

### Layout

```
┌─────────────────────────────────────────────────┐
│  Bandwidth                    [30d][7d][24h][6h][1h] │
│  14 apps · 38.7 GB total                        │
├─────────────────────────────────────────────────┤
│  [Chart label / current filter]  ■ Received  ■ Sent │
│  ┌─────────────────────────────────────────┐   │
│  │  SVG filled area chart (sent + received) │   │
│  └─────────────────────────────────────────┘   │
│  00:00        06:00        12:00        now     │
├─────────────────────────────────────────────────┤
│  Application          Sent    Received   Total ↓ │
│  ► All Apps        1.7 GB     11.8 GB   13.5 GB  │
│    chrome.exe      1.2 GB      8.4 GB    9.6 GB  │
│    steam.exe       340 MB      2.1 GB    2.4 GB  │
│    ...                                           │
└─────────────────────────────────────────────────┘
```

### Chart

Rendered as an SVG `<Path>` element with two filled areas (received behind, sent in front), each with a vertical gradient fill fading to transparent at the bottom. Smooth cubic Bézier curves between data points.

- Received: blue (`#1976D2`) stroke, blue gradient fill
- Sent: purple (`#AB47BC`) stroke, purple gradient fill
- Chart label updates to show the selected app name or "All Apps"

The chart is implemented as a custom WinUI control that converts `ChartPoints` to XAML path geometry. It uses a `Canvas` with two `Microsoft.UI.Xaml.Shapes.Path` elements whose `Data` strings are computed from the bucket values. Smooth Bézier curves are calculated from the point series. No charting library dependency required for v1.

### App table

Implemented with `CommunityToolkit.WinUI.UI.Controls.DataGrid`, consistent with other pages.

- **All Apps row** is always first. When selected: highlighted with a left accent border in `#4FC3F7`, process name shown in accent colour.
- **Per-app rows** are selectable. Clicking a row sets `SelectedApp` and reloads the chart.
- Clicking the already-selected app row deselects it (returns to All Apps).
- Columns: Application (process name), Sent, Received, Total (sort default).
- Sent values coloured purple (`#AB47BC`), Received coloured blue (`#1976D2`).
- Byte values formatted as KB / MB / GB depending on magnitude.

### Navigation

New item added to `MainWindow.xaml` NavigationView as the **first entry**:

- **Label:** Bandwidth
- **Tag:** `bandwidth`
- **Routes to:** `BandwidthPage`

---

## Out of Scope

- Real-time chart updates while the page is open (chart refreshes on next scan tick)
- Per-device (MAC address) bandwidth breakdown
- Export to CSV
- Data usage alerts / threshold notifications
- Firewall / blocking functionality
