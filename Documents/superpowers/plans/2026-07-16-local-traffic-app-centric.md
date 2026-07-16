# Local Traffic App-Centric Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Re-point the **Local** tab from device-centric to **app-centric** — it lists *this PC's apps* and how much each talked on the LAN, with the remote device as a per-app drill-down — while making **Internet WAN-only** so the two tabs are complementary.

**Architecture:** The ETW collector already tags every flow with the originating PID. Route each flow by LAN classification: LAN bytes go **only** to a LAN counter keyed by `(pid, remoteIp)`; non-LAN bytes go **only** to the existing per-PID Internet counter. At flush, resolve `pid → ProcessName` (kernel/pid-less → `System`) and persist LAN aggregates keyed by `(MinuteEpoch, ProcessName, RemoteIp)` plus a raw per-flush table for sub-minute chart parity. The Local UI groups by app and drills into per-device rows.

**Tech Stack:** .NET 10, WinUI 3, EF Core 10 + SQLite (EnsureCreated, no migrations), CommunityToolkit.Mvvm, Microsoft.Diagnostics.Tracing (TraceEvent), xUnit.

**Design spec:** `Documents/superpowers/specs/2026-07-16-local-traffic-app-centric-design.md`. Supersedes the device-centric `2026-07-06-local-traffic-attribution.md` plan.

## Global Constraints

- **v1 is IPv4-only.** In-memory keys use a packed `uint`; the DB `RemoteIp` column stores the canonical string so IPv6 fits later with no schema change.
- **Internet = WAN-only, Local = LAN-only, complementary.** LAN bytes must never enter the per-PID Internet counter (`_counters`); non-LAN bytes must never enter the LAN counter.
- **Unattributable LAN bytes → `ProcessName = "System"`** (SMB, kernel-owned, and pid-less receives). No special label. The remote IP is still kept, so `System` drills down to the device.
- **Two LAN-only tables:** re-keyed `LocalTrafficRollup (MinuteEpoch, ProcessName, ProcessPath?, RemoteIp, BytesUploaded, BytesDownloaded)` and new raw `LocalTrafficEntry (Timestamp, ProcessName, ProcessPath?, RemoteIp, BytesUploaded, BytesDownloaded)`. `TrafficEntry` / `TrafficRollup` schema unchanged (now hold WAN-only bytes).
- **Name resolution at display time** against the current `Devices` table (`IpAddress` → `DisplayName`); unmatched → bare IP. `Device.DisplayName` is `[NotMapped]` → materialise `Devices` before building the IP→name map.
- **Retention** follows the existing `Settings.TrafficPurgeDays` policy — no new setting; wire both LAN tables into the existing purge.
- **DB impact:** re-keyed + new table ⇒ **one-time local DB delete on upgrade** (EnsureCreated, no migrations). Existing device-keyed LAN history is discarded. State this in the completion summary.
- **Naming rule (LOCKED):** `Traffic*` = common to both tabs; `Internet*` = Internet-specific; `Local*` / `Lan*` = Local-specific.
- **Coding conventions (CLAUDE.md):** no `var`; no single-character names; always curly braces; `string.Empty` not `""`; single exit point (one `return` at the end, value assigned to a local first); blank lines around every block and at method boundaries; class member order Fields → Constructor → Properties → Public → Override → Private; backing field directly above its hand-written `SetProperty` property (no `[ObservableProperty]`); property `{`/`get;`/`set;` each on their own line; no underscores except leading `_` on private fields; one type per file.
- **XAML conventions:** `InternetPage.xaml` is the canonical reference — blank line after `<?xml?>`, one attribute per line indented 4 spaces, simple assignments → event handlers/Command → value bindings, blank line around every element.
- **slnx:** every new file must be added to `NetworkMonitor.slnx` **at creation**.

## Starting point — reconcile parked work

The working tree has uncommitted parked changes to `TrafficCollector.cs`, `TrafficTracker.cs`, `LanClassifier.cs`, `LanClassifierTests.cs` (the `TryClassifyRemote` + pid-less receive fix). These tasks **supersede** those files' current state — Task 2/3 specify the full target. Keep `LanClassifier.TryClassifyRemote` (it correctly picks the non-local address as the peer for both send and recv); everything else is re-specified below. Before starting, confirm `LanClassifier.TryClassifyRemote(IPAddress source, IPAddress destination, out uint packedRemote)` exists and returns `true` only when exactly one side is LAN-local; if not, add it.

---

## File Structure

**New files:**

| File | Responsibility |
|---|---|
| `NetworkMonitor/Models/LocalTrafficEntry.cs` | Raw per-flush LAN row (sub-minute chart source). |
| `NetworkMonitor/Models/LocalTrafficAppRow.cs` | Immutable primary-grid row: app + up/down/total + peer summary + child device rows. |
| `NetworkMonitor/Services/Traffic/LocalFlowKey.cs` | `readonly record struct LocalFlowKey(int Pid, uint RemoteIp)` — LAN counter key. |
| `NetworkMonitor.Tests/LocalTrafficAggregatorTests.cs` | App grouping + per-device children + peer summary (replace device-only tests). |

**Modified files:**

| File | Change |
|---|---|
| `NetworkMonitor/Models/LocalTrafficRollup.cs` | Add `ProcessName`, `ProcessPath`; keep `RemoteIp`. |
| `NetworkMonitor/Data/AppDbContext.cs` | `DbSet<LocalTrafficEntry>`; re-key `LocalTrafficRollup` unique index to `(MinuteEpoch, ProcessName, RemoteIp)`. |
| `NetworkMonitor/Services/Traffic/TrafficCollector.cs` | Route LAN→`_localCounters` keyed by `LocalFlowKey`; non-LAN→`_counters`; `DrainAndResetLocal()` returns `LocalFlowKey`-keyed dict. |
| `NetworkMonitor/Services/Traffic/LocalTrafficDelta.cs` | Add `ProcessName`, `ProcessPath`. |
| `NetworkMonitor/Services/Traffic/TrafficFlushedEventArgs.cs` | (unchanged shape — still carries `IReadOnlyList<LocalTrafficDelta>`). |
| `NetworkMonitor/Services/Traffic/TrafficTracker.cs` | Resolve LAN pids→process; write `LocalTrafficEntry` + upsert re-keyed `LocalTrafficRollup`; per-(app,device) deltas. |
| `NetworkMonitor/Services/Traffic/LocalTrafficAggregator.cs` | Group by `ProcessName` into `LocalTrafficAppRow` with per-`RemoteIp` children. |
| `NetworkMonitor/Services/Scanning/ScanWorker.cs` | Purge `LocalTrafficEntries` (by `Timestamp`) alongside `LocalTrafficRollups`. |
| `NetworkMonitor/ViewModels/LocalViewModel.cs` | App-centric rows (`Apps`), `SelectedApp`, per-app drill-down, sub-minute chart from `LocalTrafficEntries`. |
| `NetworkMonitor/Views/LocalPage.xaml` (+ `.xaml.cs`) | App grid + **Peers** column + expandable per-device breakdown. |
| `NetworkMonitor/Models/LocalTrafficDeviceRow.cs` | Keep as the drill-down child row (Device · Download · Upload · Total). |
| `NetworkMonitor/Models/LocalTrafficDeviceSummary.cs` → digest | Repurpose to app-keyed (see Task 8). |
| `NetworkMonitor/Services/Digest/DigestSummaryBuilder.cs` | Build `TopLocalApps` grouped by `ProcessName`. |
| `NetworkMonitor/Services/Digest/DigestChartRenderer.cs` | `RenderLocalTrafficSplitChart` over top local **apps**. |
| `NetworkMonitor/Services/Digest/DigestPdfExporter.cs`, `DigestCsvExporter.cs`, `Views/Controls/DigestReportView.xaml(.cs)` | App-keyed Local section. |
| `NetworkMonitor/Models/DigestSummary.cs` | `TopLocalDevices` → `TopLocalApps`. |
| `NetworkMonitor.slnx` | Add every new file. |

Note: **no `InternetViewModel` query change is needed for WAN-only** — because the collector stops routing LAN bytes into `_counters` (Task 2), `TrafficRollups`/`TrafficEntries` become WAN-only at the source. The existing "exclude System from Internet" predicate stays.

---

## Task 1 — Data model: re-key LocalTrafficRollup + add LocalTrafficEntry

**Files:**
- Modify: `NetworkMonitor/Models/LocalTrafficRollup.cs`
- Create: `NetworkMonitor/Models/LocalTrafficEntry.cs`
- Modify: `NetworkMonitor/Data/AppDbContext.cs`

**Interfaces produced:**
- `LocalTrafficRollup { int Id; long MinuteEpoch; string ProcessName; string? ProcessPath; string RemoteIp; long BytesUploaded; long BytesDownloaded; }`
- `LocalTrafficEntry { int Id; DateTime Timestamp; string ProcessName; string? ProcessPath; string RemoteIp; long BytesUploaded; long BytesDownloaded; }`

- [ ] **Step 1: Add fields to `LocalTrafficRollup`** — insert `ProcessName` (`= string.Empty`) and nullable `ProcessPath` above `RemoteIp`, following the existing property style (each `{ get; set; }`):

```csharp
public long MinuteEpoch
{
    get;
    set;
}

public string ProcessName
{
    get;
    set;
} = string.Empty;

public string? ProcessPath
{
    get;
    set;
}

public string RemoteIp
{
    get;
    set;
} = string.Empty;
```

- [ ] **Step 2: Create `LocalTrafficEntry.cs`** (mirror `TrafficEntry`'s shape but with `RemoteIp`):

```csharp
namespace NetworkMonitor.Models
{
    public class LocalTrafficEntry
    {
        public int Id
        {
            get;
            set;
        }

        public DateTime Timestamp
        {
            get;
            set;
        }

        public string ProcessName
        {
            get;
            set;
        } = string.Empty;

        public string? ProcessPath
        {
            get;
            set;
        }

        public string RemoteIp
        {
            get;
            set;
        } = string.Empty;

        public long BytesUploaded
        {
            get;
            set;
        }

        public long BytesDownloaded
        {
            get;
            set;
        }
    }
}
```

- [ ] **Step 3: Wire `AppDbContext`** — add the `DbSet`, re-key the rollup index, add an entry index. Find the existing `LocalTrafficRollups` `DbSet` and `OnModelCreating` index and change them:

```csharp
public DbSet<LocalTrafficRollup> LocalTrafficRollups => Set<LocalTrafficRollup>();

public DbSet<LocalTrafficEntry> LocalTrafficEntries => Set<LocalTrafficEntry>();
```

In `OnModelCreating`, replace the old `LocalTrafficRollup` unique index with:

```csharp
modelBuilder.Entity<LocalTrafficRollup>()
    .HasIndex(rollup => new { rollup.MinuteEpoch, rollup.ProcessName, rollup.RemoteIp })
    .IsUnique();

modelBuilder.Entity<LocalTrafficEntry>()
    .HasIndex(entry => entry.Timestamp);
```

- [ ] **Step 4: Build to verify mapping**

Run: `dotnet build NetworkMonitor/NetworkMonitor.csproj -c Debug -p:Platform=x64`
Expected: Build succeeded.

- [ ] **Step 5: Add `LocalTrafficEntry.cs` to `NetworkMonitor.slnx`** (under the Models folder, in alpha order) and commit.

```bash
git add NetworkMonitor/Models/LocalTrafficRollup.cs NetworkMonitor/Models/LocalTrafficEntry.cs NetworkMonitor/Data/AppDbContext.cs NetworkMonitor.slnx
git commit -m "Re-key LocalTrafficRollup by app+endpoint and add raw LocalTrafficEntry."
```

**DB note:** schema change ⇒ one-time DB delete on upgrade.

---

## Task 2 — Collector: route LAN by (pid, remoteIp); keep LAN out of the Internet counter

**Files:**
- Create: `NetworkMonitor/Services/Traffic/LocalFlowKey.cs`
- Modify: `NetworkMonitor/Services/Traffic/TrafficCollector.cs`

**Interfaces produced:**
- `readonly record struct LocalFlowKey(int Pid, uint RemoteIp)`
- `Dictionary<LocalFlowKey, (long Upload, long Download)> TrafficCollector.DrainAndResetLocal()`

**Interfaces consumed:** `LanClassifier.TryClassifyRemote(IPAddress source, IPAddress destination, out uint packedRemote)` (Task 0 reconcile).

- [ ] **Step 1: Create `LocalFlowKey.cs`:**

```csharp
namespace NetworkMonitor.Services.Traffic
{
    public readonly record struct LocalFlowKey(int Pid, uint RemoteIp);
}
```

- [ ] **Step 2: Re-type the LAN counter** in `TrafficCollector` — change `_localCounters` to key on `LocalFlowKey`:

```csharp
private readonly ConcurrentDictionary<LocalFlowKey, long[]> _localCounters = new();
```

- [ ] **Step 3: Rewrite `AddBytes`** so LAN and non-LAN are mutually exclusive (D1). Pid-less receives (`pid < 0`) keep a sentinel pid of `SystemPid` so they resolve to `System` later but still carry the remote IP:

```csharp
private const int SystemPid = 4;

private void AddBytes(int pid, IPAddress sourceAddress, IPAddress destinationAddress, int bytes, bool upload)
{

    if (bytes > 0)
    {
        int slot = upload ? 0 : 1;

        if (_lanClassifier.TryClassifyRemote(sourceAddress, destinationAddress, out uint packedRemote))
        {
            int keyPid = pid < 0 ? SystemPid : pid;
            LocalFlowKey key = new LocalFlowKey(keyPid, packedRemote);
            long[] localCounter = _localCounters.GetOrAdd(key, static missingKey => new long[2]);

            Interlocked.Add(ref localCounter[slot], bytes);
        }
        else if (pid >= 0)
        {
            long[] counter = _counters.GetOrAdd(pid, static missingPid => new long[2]);

            Interlocked.Add(ref counter[slot], bytes);
        }

    }

}
```

- [ ] **Step 4: Replace `DrainAndResetLocal`** to drain the `LocalFlowKey` dictionary:

```csharp
public Dictionary<LocalFlowKey, (long Upload, long Download)> DrainAndResetLocal()
{
    Dictionary<LocalFlowKey, (long Upload, long Download)> snapshot = new();

    foreach (KeyValuePair<LocalFlowKey, long[]> entry in _localCounters)
    {
        long[] counter = entry.Value;

        long upload = Interlocked.Exchange(ref counter[0], 0);
        long download = Interlocked.Exchange(ref counter[1], 0);

        if (upload > 0 || download > 0)
        {
            snapshot[entry.Key] = (upload, download);
        }

    }

    return snapshot;
}
```

- [ ] **Step 5: Build.** Run: `dotnet build NetworkMonitor/NetworkMonitor.csproj -c Debug -p:Platform=x64` → Build succeeded.

**Test note:** `AddBytes` is a private ETW hot-path method with no unit harness (consistent with the existing collector). The LAN/non-LAN split is verified by (a) `LanClassifierTests` covering `TryClassifyRemote`, and (b) the Task-Final manual e2e (LAN bytes absent from Internet). Do **not** add a collector unit test.

- [ ] **Step 6: Add `LocalFlowKey.cs` to slnx; commit.**

```bash
git add NetworkMonitor/Services/Traffic/LocalFlowKey.cs NetworkMonitor/Services/Traffic/TrafficCollector.cs NetworkMonitor.slnx
git commit -m "Route LAN bytes by (pid, endpoint) and keep them out of the Internet counter."
```

---

## Task 3 — Tracker: resolve LAN pids, write both tables, per-(app,device) deltas

**Files:**
- Modify: `NetworkMonitor/Services/Traffic/LocalTrafficDelta.cs`
- Modify: `NetworkMonitor/Services/Traffic/TrafficTracker.cs`

**Interfaces produced:**
- `LocalTrafficDelta(string ProcessName, string? ProcessPath, string RemoteIp, long BytesUploaded, long BytesDownloaded)`

**Interfaces consumed:** `collector.DrainAndResetLocal()` (Task 2); `ResolveProcessInfo(int pid, Process process)` (existing private).

- [ ] **Step 1: Extend `LocalTrafficDelta`:**

```csharp
namespace NetworkMonitor.Services.Traffic
{
    public record LocalTrafficDelta(string ProcessName, string? ProcessPath, string RemoteIp, long BytesUploaded, long BytesDownloaded);
}
```

- [ ] **Step 2: Add a pid→process resolver that never throws for kernel/pid-less** — a small helper in `TrafficTracker` returning `("System", null)` when the pid is `SystemPid` (4) or the process is gone:

```csharp
private const int SystemPid = 4;

private (string Name, string? Path) ResolveLocalProcess(int pid)
{
    (string Name, string? Path) resolved;

    if (pid == SystemPid)
    {
        resolved = ("System", null);
    }
    else
    {

        try
        {
            using Process process = Process.GetProcessById(pid);
            resolved = ResolveProcessInfo(pid, process);
        }
        catch (ArgumentException)
        {
            resolved = ("System", null);
        }

    }

    return resolved;
}
```

- [ ] **Step 3: Rebuild the LAN branch of `FlushAsync`** — drain `LocalFlowKey` snapshot, resolve each pid, format each remote IP, and produce `LocalTrafficEntry` rows + `LocalTrafficDelta`s. Replace the existing `localSnapshot`/`localDeltas` block:

```csharp
Dictionary<LocalFlowKey, (long Upload, long Download)> localSnapshot = collector.DrainAndResetLocal();

List<LocalTrafficEntry> localEntries = new();
List<LocalTrafficDelta> localDeltas = new();

foreach (KeyValuePair<LocalFlowKey, (long Upload, long Download)> pair in localSnapshot)
{
    (string processName, string? processPath) = ResolveLocalProcess(pair.Key.Pid);
    string remoteIp = LanClassifier.Format(pair.Key.RemoteIp);

    localEntries.Add(new LocalTrafficEntry
    {
        Timestamp = timestamp,
        ProcessName = processName,
        ProcessPath = processPath,
        RemoteIp = remoteIp,
        BytesUploaded = pair.Value.Upload,
        BytesDownloaded = pair.Value.Download
    });

    localDeltas.Add(new LocalTrafficDelta(processName, processPath, remoteIp, pair.Value.Upload, pair.Value.Download));
}
```

- [ ] **Step 4: Persist both LAN tables.** In the `if (entries.Count > 0 || localDeltas.Count > 0)` block, after the Internet writes, add the raw insert and the re-keyed upsert:

```csharp
if (localEntries.Count > 0)
{
    db.LocalTrafficEntries.AddRange(localEntries);
    await db.SaveChangesAsync(ct);

    await UpsertLocalRollupsAsync(db, timestamp, localDeltas, ct);
}
```

Keep the `Flushed?.Invoke(this, new TrafficFlushedEventArgs(entries, localDeltas))` call (now carrying app-keyed deltas). Update the outer guard to `entries.Count > 0 || localEntries.Count > 0`.

- [ ] **Step 5: Rewrite `UpsertLocalRollupsAsync`** to the 3-column key. Change the SQL and parameters:

```csharp
command.CommandText = """
    INSERT INTO LocalTrafficRollups (MinuteEpoch, ProcessName, ProcessPath, RemoteIp, BytesUploaded, BytesDownloaded)
    VALUES ($minute, $name, $path, $ip, $upload, $download)
    ON CONFLICT(MinuteEpoch, ProcessName, RemoteIp) DO UPDATE SET
        BytesUploaded = BytesUploaded + excluded.BytesUploaded,
        BytesDownloaded = BytesDownloaded + excluded.BytesDownloaded,
        ProcessPath = COALESCE(ProcessPath, excluded.ProcessPath)
    """;
```

Add `$name`, `$path` parameters (mirroring `UpsertRollupsAsync`), and in the loop set `nameParameter.Value = delta.ProcessName; pathParameter.Value = delta.ProcessPath is null ? (object)DBNull.Value : delta.ProcessPath; ipParameter.Value = delta.RemoteIp;` plus upload/download.

- [ ] **Step 6: Build.** Run: `dotnet build NetworkMonitor/NetworkMonitor.csproj -c Debug -p:Platform=x64` → Build succeeded.

- [ ] **Step 7: Commit.**

```bash
git add NetworkMonitor/Services/Traffic/LocalTrafficDelta.cs NetworkMonitor/Services/Traffic/TrafficTracker.cs
git commit -m "Persist app-keyed LAN rollups plus raw entries and carry app deltas on flush."
```

---

## Task 4 — Purge both LAN tables

**Files:** Modify `NetworkMonitor/Services/Scanning/ScanWorker.cs`.

- [ ] **Step 1: Add the raw-entry purge** in `PurgeOldHistoryAsync`, inside the `TrafficPurgeDays > 0` block, next to the existing `LocalTrafficRollups` delete:

```csharp
await db.LocalTrafficEntries
    .Where(entry => entry.Timestamp < trafficCutoff)
    .ExecuteDeleteAsync(ct);
```

The existing `DELETE FROM LocalTrafficRollups WHERE MinuteEpoch < {rollupCutoffEpoch}` stays (its key still has `MinuteEpoch`).

- [ ] **Step 2: Build + commit.**

```bash
git add NetworkMonitor/Services/Scanning/ScanWorker.cs
git commit -m "Purge raw LocalTrafficEntries under the traffic retention policy."
```

---

## Task 5 — Models + aggregator: LocalTrafficAppRow with per-device children

**Files:**
- Create: `NetworkMonitor/Models/LocalTrafficAppRow.cs`
- Modify: `NetworkMonitor/Models/LocalTrafficDeviceRow.cs` (keep; it is the child row — no change if it already exposes `RemoteIp, DisplayName, BytesUploaded, BytesDownloaded, TotalBytes, DownloadText, UploadText, TotalText`)
- Modify: `NetworkMonitor/Services/Traffic/LocalTrafficAggregator.cs`
- Create/replace: `NetworkMonitor.Tests/LocalTrafficAggregatorTests.cs`

**Interfaces produced:**
- `LocalTrafficAppRow(string ProcessName, string DisplayName, long BytesUploaded, long BytesDownloaded, IReadOnlyList<LocalTrafficDeviceRow> Peers)` with computed `TotalBytes`, `DownloadText`, `UploadText`, `TotalText`, `PeerSummary`, `PeerTooltip`, `HasMultiplePeers`.
- `LocalTrafficMinute(long MinuteEpoch, string ProcessName, string RemoteIp, long BytesUploaded, long BytesDownloaded)`
- `LocalTrafficAggregator.Build(IReadOnlyList<LocalTrafficMinute> minutes, IReadOnlyDictionary<string,string> namesByIp) : IReadOnlyList<LocalTrafficAppRow>`

- [ ] **Step 1: Create `LocalTrafficAppRow.cs`:**

```csharp
using NetworkMonitor.Services.Common;

namespace NetworkMonitor.Models
{
    public record LocalTrafficAppRow(string ProcessName, string DisplayName, long BytesUploaded, long BytesDownloaded, IReadOnlyList<LocalTrafficDeviceRow> Peers)
    {
        public long TotalBytes => BytesUploaded + BytesDownloaded;

        public string DownloadText => ByteSizeFormatter.Format(BytesDownloaded);

        public string UploadText => ByteSizeFormatter.Format(BytesUploaded);

        public string TotalText => ByteSizeFormatter.Format(TotalBytes);

        public bool HasMultiplePeers => Peers.Count > 1;

        public string PeerSummary => Peers.Count switch
        {
            0 => string.Empty,
            1 => Peers[0].DisplayName,
            _ => $"{Peers[0].DisplayName} +{Peers.Count - 1}"
        };

        public string PeerTooltip => string.Join(", ", Peers.Select(peer => peer.DisplayName));
    }
}
```

Convention note: the `PeerSummary` switch and `PeerTooltip` are expression-bodied members (exempt from the single-exit rule).

- [ ] **Step 2: Write the failing aggregator test** — replace `LocalTrafficAggregatorTests.cs` contents:

```csharp
using System.Collections.Generic;
using NetworkMonitor.Models;
using NetworkMonitor.Services.Traffic;
using Xunit;

namespace NetworkMonitor.Tests
{
    public class LocalTrafficAggregatorTests
    {
        [Fact]
        public void GroupsByAppWithPerDeviceChildrenSortedByTotal()
        {
            List<LocalTrafficMinute> minutes = new()
            {
                new LocalTrafficMinute(60, "System", "192.168.1.50", 100, 4000),
                new LocalTrafficMinute(120, "System", "192.168.1.50", 0, 1000),
                new LocalTrafficMinute(60, "System", "192.168.1.99", 10, 20),
                new LocalTrafficMinute(60, "chrome", "192.168.1.10", 5, 5)
            };
            Dictionary<string, string> namesByIp = new()
            {
                { "192.168.1.50", "SurfratNas" }
            };

            IReadOnlyList<LocalTrafficAppRow> rows = LocalTrafficAggregator.Build(minutes, namesByIp);

            Assert.Equal(2, rows.Count);
            Assert.Equal("System", rows[0].ProcessName);
            Assert.Equal(5130, rows[0].TotalBytes);
            Assert.Equal(2, rows[0].Peers.Count);
            Assert.Equal("SurfratNas", rows[0].Peers[0].DisplayName);
            Assert.Equal(5100, rows[0].Peers[0].TotalBytes);
            Assert.Equal("192.168.1.99", rows[0].Peers[1].DisplayName);
            Assert.Equal("SurfratNas +1", rows[0].PeerSummary);
            Assert.Equal("chrome", rows[1].ProcessName);
            Assert.Equal("192.168.1.10", rows[1].PeerSummary);
        }
    }
}
```

- [ ] **Step 3: Run it to verify failure.** Run: `dotnet test NetworkMonitor.Tests --filter FullyQualifiedName~LocalTrafficAggregatorTests` → FAIL (Build error: `Build` signature / `LocalTrafficMinute` mismatch).

- [ ] **Step 4: Rewrite `LocalTrafficAggregator`:**

```csharp
using NetworkMonitor.Models;

namespace NetworkMonitor.Services.Traffic
{
    public static class LocalTrafficAggregator
    {
        public static IReadOnlyList<LocalTrafficAppRow> Build(IReadOnlyList<LocalTrafficMinute> minutes, IReadOnlyDictionary<string, string> namesByIp)
        {
            Dictionary<string, Dictionary<string, (long Upload, long Download)>> byApp = new();

            foreach (LocalTrafficMinute minute in minutes)
            {

                if (!byApp.TryGetValue(minute.ProcessName, out Dictionary<string, (long Upload, long Download)>? peers))
                {
                    peers = new Dictionary<string, (long Upload, long Download)>();
                    byApp[minute.ProcessName] = peers;
                }

                peers.TryGetValue(minute.RemoteIp, out (long Upload, long Download) current);
                peers[minute.RemoteIp] = (current.Upload + minute.BytesUploaded, current.Download + minute.BytesDownloaded);
            }

            List<LocalTrafficAppRow> rows = new();

            foreach (KeyValuePair<string, Dictionary<string, (long Upload, long Download)>> appEntry in byApp)
            {
                List<LocalTrafficDeviceRow> peerRows = new();
                long appUpload = 0;
                long appDownload = 0;

                foreach (KeyValuePair<string, (long Upload, long Download)> peerEntry in appEntry.Value)
                {
                    string displayName = LocalTrafficNameResolver.Resolve(peerEntry.Key, namesByIp);

                    peerRows.Add(new LocalTrafficDeviceRow(peerEntry.Key, displayName, peerEntry.Value.Upload, peerEntry.Value.Download));
                    appUpload += peerEntry.Value.Upload;
                    appDownload += peerEntry.Value.Download;
                }

                peerRows.Sort((left, right) => right.TotalBytes.CompareTo(left.TotalBytes));
                rows.Add(new LocalTrafficAppRow(appEntry.Key, appEntry.Key, appUpload, appDownload, peerRows));
            }

            List<LocalTrafficAppRow> sorted = rows.OrderByDescending(row => row.TotalBytes).ToList();

            return sorted;
        }
    }
}
```

Define `LocalTrafficMinute` in its own file `NetworkMonitor/Services/Traffic/LocalTrafficMinute.cs` (one-type-per-file): `public record LocalTrafficMinute(long MinuteEpoch, string ProcessName, string RemoteIp, long BytesUploaded, long BytesDownloaded);` — add to slnx.

- [ ] **Step 5: Run tests to verify pass.** Run: `dotnet test NetworkMonitor.Tests --filter FullyQualifiedName~LocalTrafficAggregatorTests` → PASS.

- [ ] **Step 6: Add new files to slnx; commit.**

```bash
git add NetworkMonitor/Models/LocalTrafficAppRow.cs NetworkMonitor/Services/Traffic/LocalTrafficMinute.cs NetworkMonitor/Services/Traffic/LocalTrafficAggregator.cs NetworkMonitor.Tests/LocalTrafficAggregatorTests.cs NetworkMonitor.slnx
git commit -m "Aggregate LAN traffic into app rows with per-device children."
```

---

## Task 6 — LocalViewModel: app-centric, drill-down, sub-minute chart parity

**Files:** Modify `NetworkMonitor/ViewModels/LocalViewModel.cs`.

**Interfaces consumed:** `LocalTrafficAggregator.Build` (Task 5); `LocalTrafficMinute`; `LocalTrafficAppRow`; `InternetViewModel.BucketSizeFor` (existing shared helper).

**Approach: keep the existing `LocalViewModel` scaffolding (chart window machinery, `TimeRangeHours`, `LoadAsync`, `ApplyLiveFlushAsync`, `SeedWindowState`, bucket math) and change the dimension from endpoint to app.**

- [ ] **Step 1: Swap the primary collection + selection.** Replace `ObservableCollection<LocalTrafficDeviceRow> Devices` with `ObservableCollection<LocalTrafficAppRow> Apps` (hand-written `SetProperty` property, backing field above it), and `SelectedEndpoint` (string) with `SelectedApp` (string ProcessName). Update `ApplyFlushToWindow` / `RebuildDeviceRows` references accordingly (rename to `RebuildAppRows`).

- [ ] **Step 2: Load app rows via the aggregator.** In `BuildDataAsync`, replace `LoadDeviceRowsAsync` with a loader that reads per-`(ProcessName, RemoteIp)` sums and feeds the aggregator. The SQL groups by both key parts:

```csharp
command.CommandText = $"""
    SELECT ProcessName, RemoteIp,
           SUM(BytesUploaded)   AS Upload,
           SUM(BytesDownloaded) AS Download
    FROM LocalTrafficRollups
    WHERE {whereClause}
    GROUP BY ProcessName, RemoteIp
    """;
```

Read rows into `List<LocalTrafficMinute>` (use `MinuteEpoch = 0` — it is unused by `Build`), then `IReadOnlyList<LocalTrafficAppRow> appRows = LocalTrafficAggregator.Build(minutes, namesByIp);`. Build the "All Apps" summary row the same way Internet builds "All Apps" (sum of all app rows; `Peers` empty). Status text: `$"{appRows.Count} app{(appRows.Count == 1 ? string.Empty : "s")} · {ByteSizeFormatter.Format(total)} {scopeText}"`.

- [ ] **Step 3: Chart filters by app.** In `LoadChartBucketsAsync`, rename the `$endpoint` parameter to `$app` and change the predicate to `($app IS NULL OR ProcessName = $app)`, bound from `SelectedApp`.

- [ ] **Step 4: Sub-minute parity (D2).** Mirror `InternetViewModel`'s raw-vs-rollup selection: when `bucketSeconds < 60`, read the chart buckets from `LocalTrafficEntries` (group by `CAST((strftime('%s', Timestamp) - $cutoffEpoch) / $bucketSeconds AS INTEGER)`, filtered by `$app` on `ProcessName`); otherwise read `LocalTrafficRollups` as in Step 3. Copy the exact threshold and raw-query shape from `InternetViewModel.LoadChartBucketsAsync` (which already branches on `TrafficEntries` vs `TrafficRollups`), substituting the LAN tables and `ProcessName` filter. The per-minute grid loader (Step 2) always uses `LocalTrafficRollups`.

- [ ] **Step 5: Live flush by app.** In `ApplyFlushToWindow` / `SeedWindowState`, key `_windowDeviceTotals` (rename `_windowAppTotals`) by `ProcessName`, and accumulate `delta.BytesUploaded/Downloaded` per `delta.ProcessName`; the chart delta includes a delta when `SelectedApp is null || delta.ProcessName == SelectedApp`. `RebuildAppRows` rebuilds `Apps` from the in-memory per-app totals — but note the live path cannot cheaply rebuild per-device children; on live flush, rebuild only app totals and set `Peers` to the last-loaded children for that app (or empty), and rely on the next full `LoadAsync` for exact children. Simplest correct choice: **on every live flush, if any delta's `ProcessName` is new, call `LoadAsync()` instead of the in-memory patch** (children stay exact); otherwise patch app totals in place and leave `Peers` untouched. Document this in a code comment.

- [ ] **Step 6: Build + run full test suite.** Run: `dotnet build NetworkMonitor/NetworkMonitor.csproj -c Debug -p:Platform=x64` then `dotnet test NetworkMonitor.Tests` → Build succeeded, all PASS.

- [ ] **Step 7: Commit.**

```bash
git add NetworkMonitor/ViewModels/LocalViewModel.cs
git commit -m "Make LocalViewModel app-centric with per-app device drill-down and sub-minute chart."
```

---

## Task 7 — LocalPage: app grid + Peers column + expandable device breakdown

**Files:** Modify `NetworkMonitor/Views/LocalPage.xaml` (+ `.xaml.cs`).

**Approach: keep the existing `LocalPage` chart/range/badge/pause structure; change only the grid.** The grid becomes a `DataGrid` (or `ListView` with an expander) over `ViewModel.Apps`.

- [ ] **Step 1: Retarget the grid.** Set the grid `x:DataType` to `models:LocalTrafficAppRow`, `ItemsSource="{x:Bind ViewModel.Apps, Mode=OneWay}"`. Columns: **App** (`ProcessName`), **Peers** (`PeerSummary`, with `ToolTipService.ToolTip="{x:Bind PeerTooltip}"`), **Download** (`DownloadText`), **Upload** (`UploadText`), **Total** (`TotalText`). Follow the XAML attribute-order + blank-line conventions; use `InternetPage.xaml`'s grid as the column-template reference.

- [ ] **Step 2: Drill-down.** For the per-device breakdown, use a `DataGridTemplateColumn` on the App column whose row detail (or an inline `Expander` bound to `Peers`) lists the child `LocalTrafficDeviceRow`s (Device `DisplayName` over `RemoteIp`, Download, Upload, Total). If `DataGrid` row-details are awkward, switch the primary list to a `ListView` with an `Expander` per item: header = the app summary line (App · Peers · Total), content = an inner `ItemsControl`/small grid over `Peers`. Choose whichever matches the existing control usage best; keep visuals consistent with `InternetPage`.

- [ ] **Step 3: Row click filters the chart.** Wire selection/click to set `ViewModel.SelectedApp = row.ProcessName` and reload (mirror `InternetPage`'s click-row-to-filter handler, which sets `SelectedApp` and calls `LoadAsync`). Keep the "All Apps" row clearing the filter (`SelectedApp = null`).

- [ ] **Step 4: Code-behind.** In `LocalPage.xaml.cs`, keep the identical `Flushed` subscribe-on-`Loaded`/unsubscribe-on-`Unloaded` lifecycle and `OnTrafficFlushed` → `ViewModel.ApplyLiveFlushAsync(args.LocalDeltas)`. Update any `Devices`/`SelectedEndpoint` references to `Apps`/`SelectedApp`.

- [ ] **Step 5: Build; manual smoke (app runs, Local tab renders app rows, expander shows peers).** Run: `dotnet build NetworkMonitor/NetworkMonitor.csproj -c Debug -p:Platform=x64` → Build succeeded.

- [ ] **Step 6: Commit.**

```bash
git add NetworkMonitor/Views/LocalPage.xaml NetworkMonitor/Views/LocalPage.xaml.cs
git commit -m "Show apps with a Peers column and per-device drill-down on the Local page."
```

---

## Task 8 — Reports: app-keyed Local section

**Files:** `Models/DigestSummary.cs`, `Models/LocalTrafficDeviceSummary.cs`, `Services/Digest/DigestSummaryBuilder.cs`, `DigestChartRenderer.cs`, `DigestPdfExporter.cs`, `DigestCsvExporter.cs`, `Views/Controls/DigestReportView.xaml(.cs)`, `NetworkMonitor.Tests/DigestSummaryBuilderTests.cs` / `DigestCsvExporterTests.cs`.

**Interfaces produced:** `LocalTrafficAppSummary(string ProcessName, string Peer, long BytesDownloaded, long BytesUploaded)`; `DigestSummary.TopLocalApps`.

- [ ] **Step 1: Rename the summary model** — `Models/LocalTrafficDeviceSummary.cs` → `LocalTrafficAppSummary.cs` (`git mv`), fields `(string ProcessName, string Peer, long BytesDownloaded, long BytesUploaded)` with the existing computed text/total props. In `DigestSummary.cs`, rename `TopLocalDevices` → `TopLocalApps` (type `IReadOnlyList<LocalTrafficAppSummary>`).

- [ ] **Step 2: Builder.** In `DigestSummaryBuilder`, build `TopLocalApps` by querying `LocalTrafficRollups` over the period grouped by `ProcessName`, summing bytes, and setting `Peer` to that app's top `RemoteIp` resolved against the period device list (fallback bare IP). Take the top N by total (same N the Internet top-apps uses).

- [ ] **Step 3: Chart.** In `DigestChartRenderer`, change `RenderLocalTrafficSplitChart` to plot Download-vs-Upload over the top local **apps** (label = `ProcessName`), reusing `DrawGroupedBars`.

- [ ] **Step 4: PDF/CSV/View.** Update `DigestPdfExporter`, `DigestCsvExporter`, and `DigestReportView.xaml(.cs)` Local section headings/columns to **App · Peer · Download · Upload · Total** (CSV keeps Raw+Friendly paired byte columns). Update `x:DataType` to `models:LocalTrafficAppSummary`.

- [ ] **Step 5: Fix tests.** Update `DigestSummaryBuilderTests` / `DigestCsvExporterTests` for the renamed field + app-keyed rows.

- [ ] **Step 6: Build + full test suite.** Run: `dotnet build NetworkMonitor.slnx -c Release -p:Platform=x64` and `dotnet test NetworkMonitor.Tests` → Build succeeded, all PASS.

- [ ] **Step 7: Add renamed/new files to slnx; commit.**

```bash
git add -A
git commit -m "Make the digest Local section app-keyed with peer device."
```

---

## Final: full test + build gate

- [ ] `dotnet test NetworkMonitor.Tests` — all existing + `LocalTrafficAggregatorTests` (app-keyed) + `LanClassifierTests` PASS.
- [ ] `dotnet build NetworkMonitor.slnx -c Release -p:Platform=x64` — Build succeeded.
- [ ] **Manual end-to-end:** delete the DB once; run; open **Traffic → Local**; run a NAS/SMB copy → within one interval a **`System`** app row grows and **expands to show `SurfratNas`** with the bytes; open a device's local web UI in a browser → attributes to the **browser** app; confirm those same LAN bytes are **absent** from the **Internet** tab (D1); confirm the **5-minute** chart is populated (D2); confirm the digest's Local section is **app-keyed** (chart + table + CSV Raw/Friendly).
- [ ] Completion summary must state: **a one-time local DB delete is required on upgrade** (re-keyed `LocalTrafficRollups` + new `LocalTrafficEntries`), and that **Internet totals now exclude LAN traffic**.

---

## Notes / carried assumptions

- `Device.DisplayName` is `[NotMapped]` → materialise `Devices` before building the IP→name map (in `LocalViewModel` and `DigestSummaryBuilder`).
- TraceEvent `args.saddr` / `args.daddr` remain the one external assumption; already relied on by the shipped collector.
- IPv6 explicitly out of scope; `RemoteIp` stored as string keeps the door open.
- The live-flush path keeps exact per-device children only across a full `LoadAsync`; a new app mid-window triggers a reload (Task 6 Step 5) rather than an approximate in-memory child rebuild.
