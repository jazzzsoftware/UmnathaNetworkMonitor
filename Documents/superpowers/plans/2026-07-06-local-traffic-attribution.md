# Local Traffic Attribution Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a "Local" tab to the Traffic view that isolates LAN-local network traffic, broken down by remote endpoint (resolved to a known device name where possible) with upload, download, current rate, and peak — so a NAS backup shows as "→ Synology NAS" instead of being lost under the System process.

**Architecture:** The ETW collector already aggregates bytes by PID; we leave that path untouched and add a *second, parallel* in-memory dictionary that accumulates bytes keyed by the remote IPv4 address, but only when a cheap classifier says the address is LAN-local. A dedicated per-minute `LocalTrafficRollup` table stores the aggregates. The UI resolves each stored IP to a device name at display time against the current known-device list, so names are always current and no staleness policy is needed.

**Tech Stack:** .NET 10, WinUI 3, EF Core 10 + SQLite (EnsureCreated, no migrations), CommunityToolkit.Mvvm, Microsoft.Diagnostics.Tracing (TraceEvent), xUnit v3.

## Global Constraints

Copied verbatim from the resolved decision spec (`Documents/superpowers/specs/2026-07-05-local-traffic-attribution-design.md`, section 8) and `CLAUDE.md`:

- **v1 is IPv4-only.** Do not wire the `...IPV6` ETW variants. The in-memory key is a packed `uint`; the DB `RemoteIp` column stores the **canonical string** so IPv6 fits later with no schema change.
- **v1 is endpoint-only.** No app-level (handle-enumeration) attribution.
- **Do not re-key the existing `pid` dictionary.** Add a separate LAN-only dictionary; internet packets must be classified out with a handful of integer compares and never touch it.
- **One new table only** — `LocalTrafficRollup (MinuteEpoch, RemoteIp, BytesUploaded, BytesDownloaded)`. No changes to `TrafficEntry` / `TrafficRollup` schema.
- **Name resolution at display time**, against the current `Devices` table (`IpAddress` → `DisplayName`); unmatched → bare IP. No staleness threshold.
- **Retention** follows the existing `Settings.TrafficPurgeDays` policy — no new setting.
- **DB impact:** adding the table requires a **one-time local DB delete** on upgrade (EnsureCreated, no migrations). State this in the completion summary.
- **Coding conventions (CLAUDE.md):** no `var`; no single-character names; always curly braces; `string.Empty` not `""`; single exit point (one `return` at the end, value assigned to a local first); blank lines around every block and at method boundaries; class member order Fields → Constructor → Properties → Public → Override → Private; backing field directly above its hand-written `SetProperty` property (no `[ObservableProperty]`); property `{`/`get;`/`set;` each on their own line; no underscores except leading `_` on private fields.
- **XAML conventions:** `DevicesPage.xaml` is the canonical reference — blank line after `<?xml?>`, one attribute per line indented 4 spaces, simple assignments → event handlers/Command → value bindings, blank line around every element.
- **slnx:** every new root/Documents file must be added to `NetworkMonitor.slnx` or it won't appear in Solution Explorer.

---

## File Structure

**New files:**

| File | Responsibility |
|---|---|
| `NetworkMonitor/Services/Traffic/LanClassifier.cs` | Pure classifier: pack an `IPAddress` to IPv4 `uint`, decide if it is LAN-local, format `uint` back to dotted string. Builds active-subnet ranges from `NetworkInterface`, refreshed on network change. |
| `NetworkMonitor/Models/LocalTrafficRollup.cs` | EF entity for the per-minute LAN aggregate. |
| `NetworkMonitor/Models/LocalTrafficRow.cs` | Immutable display row for the grid (endpoint IP, display name, up, down, current rate, peak). |
| `NetworkMonitor/Services/Traffic/LocalTrafficDelta.cs` | Record carried on the flush event: one endpoint's bytes for the just-completed interval. |
| `NetworkMonitor/Services/Traffic/LocalTrafficAggregator.cs` | Pure: turn rollup rows + device-name map into sorted `LocalTrafficRow`s (totals + peak). |
| `NetworkMonitor/Services/Traffic/LocalTrafficNameResolver.cs` | Pure: resolve an IP against a name map, falling back to the bare IP. |
| `NetworkMonitor/ViewModels/LocalTrafficViewModel.cs` | Loads rollups over the time range, resolves names, applies live flush deltas. |
| `NetworkMonitor/Views/LocalTrafficPage.xaml` (+ `.xaml.cs`) | DataGrid of endpoints; subscribes to `TrafficTracker.Flushed`. |
| `NetworkMonitor.Tests/LanClassifierTests.cs` | Unit tests for classification + packing. |
| `NetworkMonitor.Tests/LocalTrafficNameResolverTests.cs` | Unit tests for name resolution. |
| `NetworkMonitor.Tests/LocalTrafficAggregatorTests.cs` | Unit tests for aggregation (totals + peak). |

**Modified files:**

| File | Change |
|---|---|
| `NetworkMonitor/Services/Traffic/TrafficCollector.cs` | Inject `LanClassifier`; capture remote address per event; second LAN dictionary; `DrainAndResetLocal()`. |
| `NetworkMonitor/Services/Traffic/TrafficFlushedEventArgs.cs` | Add `LocalDeltas` payload. |
| `NetworkMonitor/Services/Traffic/TrafficTracker.cs` | Drain LAN snapshot; upsert `LocalTrafficRollup`; raise deltas on flush. |
| `NetworkMonitor/Data/AppDbContext.cs` | `DbSet<LocalTrafficRollup>` + unique index. |
| `NetworkMonitor/Services/Scanning/ScanWorker.cs` | Purge old `LocalTrafficRollups` alongside the existing traffic purge. |
| `NetworkMonitor/Views/TrafficHostPage.xaml` (+ `.xaml.cs`) | Add "Local" `SelectorBarItem` + frame + navigation. |
| `NetworkMonitor/App.xaml.cs` | Register `LanClassifier` and `LocalTrafficViewModel`. |
| `NetworkMonitor.slnx` | Add every new file above + this plan. |

---

## Task 1: LanClassifier (pure) + IPv4 packing

**Files:**
- Create: `NetworkMonitor/Services/Traffic/LanClassifier.cs`
- Test: `NetworkMonitor.Tests/LanClassifierTests.cs`

**Interfaces:**
- Produces:
  - `bool LanClassifier.TryClassifyLocal(System.Net.IPAddress address, out uint packed)` — `true` only when `address` is IPv4 **and** LAN-local; `packed` is the network-order packed value `(b0<<24)|(b1<<16)|(b2<<8)|b3`.
  - `static bool LanClassifier.TryPackIpv4(System.Net.IPAddress address, out uint packed)` — `true` for any IPv4 address.
  - `static string LanClassifier.Format(uint packed)` — dotted string.
  - `void LanClassifier.Refresh()` — rebuild active-subnet ranges.
  - Constructor `LanClassifier()` builds ranges once and subscribes to `NetworkChange.NetworkAddressChanged`.
- Consumes: nothing (leaf).

**Design notes for the implementer:**
- A "range" is an inclusive `(uint Start, uint End)` pair. The fixed ranges are RFC1918 + IPv4 link-local:
  - `10.0.0.0/8` → `0x0A000000`–`0x0AFFFFFF`
  - `172.16.0.0/12` → `0xAC100000`–`0xAC1FFFFF`
  - `192.168.0.0/16` → `0xC0A80000`–`0xC0A8FFFF`
  - `169.254.0.0/16` → `0xA9FE0000`–`0xA9FEFFFF`
- Active-subnet ranges come from each `NetworkInterface.GetAllNetworkInterfaces()` interface that is `OperationalStatus.Up`, over its `GetIPProperties().UnicastAddresses` entries whose `Address.AddressFamily == AddressFamily.InterNetwork`, using `IPv4Mask`. `Start = ipPacked & maskPacked`, `End = Start | ~maskPacked`. These are unioned with the fixed ranges (v1 definition of "local" per spec section 5).
- `TryClassifyLocal` returns `false` immediately if `TryPackIpv4` fails (IPv6). Then a linear scan over the range array (`packed >= Start && packed <= End`) — cheap integer compares, array is tiny.
- Store the range array in a `private volatile (uint Start, uint End)[] _ranges;` so `Refresh()` can swap it atomically without locking the hot path.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Net;
using NetworkMonitor.Services.Traffic;
using Xunit;

namespace NetworkMonitor.Tests
{
    public class LanClassifierTests
    {
        [Theory]
        [InlineData("10.0.0.1")]
        [InlineData("10.255.255.254")]
        [InlineData("172.16.0.5")]
        [InlineData("172.31.255.1")]
        [InlineData("192.168.1.50")]
        [InlineData("192.168.255.255")]
        [InlineData("169.254.10.20")]
        public void ClassifiesRfc1918AndLinkLocalAddressesAsLocal(string address)
        {
            LanClassifier classifier = new LanClassifier();

            bool isLocal = classifier.TryClassifyLocal(IPAddress.Parse(address), out uint packed);

            Assert.True(isLocal);
            Assert.NotEqual(0u, packed);
        }

        [Theory]
        [InlineData("8.8.8.8")]
        [InlineData("1.1.1.1")]
        [InlineData("172.32.0.1")]
        [InlineData("11.0.0.1")]
        [InlineData("192.169.0.1")]
        public void ClassifiesPublicAddressesAsNotLocal(string address)
        {
            LanClassifier classifier = new LanClassifier();

            bool isLocal = classifier.TryClassifyLocal(IPAddress.Parse(address), out uint packed);

            Assert.False(isLocal);
        }

        [Fact]
        public void RejectsIpv6Addresses()
        {
            LanClassifier classifier = new LanClassifier();

            bool isLocal = classifier.TryClassifyLocal(IPAddress.Parse("fe80::1"), out uint packed);

            Assert.False(isLocal);
        }

        [Fact]
        public void PacksAndFormatsIpv4RoundTrip()
        {
            bool packed = LanClassifier.TryPackIpv4(IPAddress.Parse("192.168.1.50"), out uint value);

            Assert.True(packed);
            Assert.Equal(0xC0A80132u, value);
            Assert.Equal("192.168.1.50", LanClassifier.Format(value));
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test NetworkMonitor.Tests --filter FullyQualifiedName~LanClassifierTests`
Expected: FAIL — `LanClassifier` does not exist / does not compile.

- [ ] **Step 3: Write the implementation**

```csharp
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace NetworkMonitor.Services.Traffic
{
    public class LanClassifier
    {
        private static readonly (uint Start, uint End)[] FixedRanges =
        {
            (0x0A000000u, 0x0AFFFFFFu),
            (0xAC100000u, 0xAC1FFFFFu),
            (0xC0A80000u, 0xC0A8FFFFu),
            (0xA9FE0000u, 0xA9FEFFFFu)
        };

        private volatile (uint Start, uint End)[] _ranges = FixedRanges;

        public LanClassifier()
        {
            Refresh();
            NetworkChange.NetworkAddressChanged += (sender, args) => Refresh();
        }

        public bool TryClassifyLocal(IPAddress address, out uint packed)
        {
            bool isLocal = false;

            if (TryPackIpv4(address, out packed))
            {
                (uint Start, uint End)[] ranges = _ranges;

                foreach ((uint Start, uint End) range in ranges)
                {

                    if (packed >= range.Start && packed <= range.End)
                    {
                        isLocal = true;

                        break;
                    }

                }

            }

            return isLocal;
        }

        public void Refresh()
        {
            List<(uint Start, uint End)> ranges = new List<(uint Start, uint End)>(FixedRanges);

            foreach (NetworkInterface networkInterface in NetworkInterface.GetAllNetworkInterfaces())
            {

                if (networkInterface.OperationalStatus == OperationalStatus.Up)
                {

                    foreach (UnicastIPAddressInformation unicast in networkInterface.GetIPProperties().UnicastAddresses)
                    {

                        if (unicast.Address.AddressFamily == AddressFamily.InterNetwork
                            && TryPackIpv4(unicast.Address, out uint ipPacked)
                            && TryPackIpv4(unicast.IPv4Mask, out uint maskPacked)
                            && maskPacked != 0)
                        {
                            uint start = ipPacked & maskPacked;
                            uint end = start | ~maskPacked;
                            ranges.Add((start, end));
                        }

                    }

                }

            }

            _ranges = ranges.ToArray();
        }

        public static bool TryPackIpv4(IPAddress address, out uint packed)
        {
            packed = 0;
            bool success = false;

            if (address.AddressFamily == AddressFamily.InterNetwork)
            {
                Span<byte> bytes = stackalloc byte[4];

                if (address.TryWriteBytes(bytes, out int written) && written == 4)
                {
                    packed = ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
                    success = true;
                }

            }

            return success;
        }

        public static string Format(uint packed)
        {
            byte first = (byte)(packed >> 24);
            byte second = (byte)(packed >> 16);
            byte third = (byte)(packed >> 8);
            byte fourth = (byte)packed;

            string result = $"{first}.{second}.{third}.{fourth}";

            return result;
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test NetworkMonitor.Tests --filter FullyQualifiedName~LanClassifierTests`
Expected: PASS (all cases). Note: the "public address" cases assume the test host has no interface whose subnet contains the sampled public IP — true for RFC1918 LANs.

- [ ] **Step 5: Add to slnx and commit**

Add `LanClassifier.cs` and `LanClassifierTests.cs` to `NetworkMonitor.slnx` (Services/Traffic folder and the test project folder respectively).

```bash
git add NetworkMonitor/Services/Traffic/LanClassifier.cs NetworkMonitor.Tests/LanClassifierTests.cs NetworkMonitor.slnx
git commit -m "Add LanClassifier for IPv4 LAN-local classification."
```

---

## Task 2: LocalTrafficRollup entity + DbContext mapping

**Files:**
- Create: `NetworkMonitor/Models/LocalTrafficRollup.cs`
- Modify: `NetworkMonitor/Data/AppDbContext.cs:14` (add DbSet), `:56` (add index in `OnModelCreating`)

**Interfaces:**
- Produces: `LocalTrafficRollup { int Id; long MinuteEpoch; string RemoteIp; long BytesUploaded; long BytesDownloaded; }` and `AppDbContext.LocalTrafficRollups`.
- Consumes: nothing.

- [ ] **Step 1: Create the entity**

```csharp
namespace NetworkMonitor.Models
{
    public class LocalTrafficRollup
    {
        public int Id
        {
            get;
            set;
        }

        public long MinuteEpoch
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

- [ ] **Step 2: Add the DbSet to `AppDbContext`**

In `AppDbContext.cs`, after the `SpeedTestResults` DbSet (line 14):

```csharp
        public DbSet<LocalTrafficRollup> LocalTrafficRollups => Set<LocalTrafficRollup>();
```

- [ ] **Step 3: Add the unique index in `OnModelCreating`**

In `AppDbContext.cs`, after the `TrafficRollup` index block (around line 38):

```csharp
            modelBuilder.Entity<LocalTrafficRollup>()
                .HasIndex(rollup => new { rollup.MinuteEpoch, rollup.RemoteIp })
                .IsUnique();
```

- [ ] **Step 4: Build to verify the model compiles and maps**

Run: `dotnet build NetworkMonitor/NetworkMonitor.csproj -c Debug -p:Platform=x64`
Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Add to slnx and commit**

Add `LocalTrafficRollup.cs` to `NetworkMonitor.slnx` (Models folder).

```bash
git add NetworkMonitor/Models/LocalTrafficRollup.cs NetworkMonitor/Data/AppDbContext.cs NetworkMonitor.slnx
git commit -m "Add LocalTrafficRollup entity and DbContext mapping."
```

---

## Task 3: TrafficCollector — second LAN dictionary

**Files:**
- Modify: `NetworkMonitor/Services/Traffic/TrafficCollector.cs`

**Interfaces:**
- Consumes: `LanClassifier.TryClassifyLocal(IPAddress, out uint)` (Task 1).
- Produces:
  - Constructor `TrafficCollector(LanClassifier lanClassifier)`.
  - `Dictionary<uint, (long Upload, long Download)> DrainAndResetLocal()` — same drain-and-reset semantics as the existing `DrainAndReset()`, keyed by packed IPv4.
  - Existing `DrainAndReset()` (by PID) unchanged.

**Design notes:** The TCP/UDP handlers must pass the remote address — `daddr` on send, `saddr` on recv. `AddBytes` keeps its existing PID accumulation exactly as-is and additionally accumulates into the LAN dictionary only when `TryClassifyLocal` succeeds. Internet packets pay only the classifier's integer scan.

- [ ] **Step 1: Add the field, constructor, and second dictionary**

Replace the top of the class (the two field declarations) so it reads:

```csharp
    public class TrafficCollector(LanClassifier lanClassifier) : BackgroundService
    {
        private const string SessionName = "NetworkMonitorTraffic";
        private readonly ConcurrentDictionary<int, long[]> _counters = new();
        private readonly ConcurrentDictionary<uint, long[]> _localCounters = new();
        private TraceEventSession? _session;
```

(Remove the standalone `private TraceEventSession? _session;` from its old position — it now lives in the block above.)

- [ ] **Step 2: Add `DrainAndResetLocal` next to `DrainAndReset`**

```csharp
        public Dictionary<uint, (long Upload, long Download)> DrainAndResetLocal()
        {
            Dictionary<uint, (long Upload, long Download)> snapshot = new();

            foreach (KeyValuePair<uint, long[]> entry in _localCounters)
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

- [ ] **Step 3: Pass the remote address into the handlers**

Replace the four handler subscriptions in `ExecuteAsync`:

```csharp
                _session.Source.Kernel.TcpIpSend += args => AddBytes(args.ProcessID, args.daddr, args.size, upload: true);
                _session.Source.Kernel.TcpIpRecv += args => AddBytes(args.ProcessID, args.saddr, args.size, upload: false);
                _session.Source.Kernel.UdpIpSend += args => AddBytes(args.ProcessID, args.daddr, args.size, upload: true);
                _session.Source.Kernel.UdpIpRecv += args => AddBytes(args.ProcessID, args.saddr, args.size, upload: false);
```

- [ ] **Step 4: Extend `AddBytes` to accumulate LAN traffic**

Replace the whole `AddBytes` method:

```csharp
        private void AddBytes(int pid, System.Net.IPAddress remote, int bytes, bool upload)
        {

            if (pid >= 0 && bytes > 0)
            {
                int slot = upload ? 0 : 1;

                long[] counter = _counters.GetOrAdd(pid, static key => new long[2]);

                Interlocked.Add(ref counter[slot], bytes);

                if (lanClassifier.TryClassifyLocal(remote, out uint packed))
                {
                    long[] localCounter = _localCounters.GetOrAdd(packed, static key => new long[2]);

                    Interlocked.Add(ref localCounter[slot], bytes);
                }

            }

        }
```

- [ ] **Step 5: Build to verify**

Run: `dotnet build NetworkMonitor/NetworkMonitor.csproj -c Debug -p:Platform=x64`
Expected: Build succeeded. (If `args.daddr` / `args.saddr` do not resolve, verify the TraceEvent property names on `TcpIpTraceData` / `UdpIpTraceData` — they are `saddr` and `daddr`; adjust if the installed TraceEvent version differs.)

- [ ] **Step 6: Commit**

```bash
git add NetworkMonitor/Services/Traffic/TrafficCollector.cs
git commit -m "Add LAN-local byte accumulation to TrafficCollector."
```

---

## Task 4: Flush args + TrafficTracker upsert

**Files:**
- Create: `NetworkMonitor/Services/Traffic/LocalTrafficDelta.cs`
- Modify: `NetworkMonitor/Services/Traffic/TrafficFlushedEventArgs.cs`
- Modify: `NetworkMonitor/Services/Traffic/TrafficTracker.cs`

**Interfaces:**
- Produces:
  - `LocalTrafficDelta(string RemoteIp, long BytesUploaded, long BytesDownloaded)` record.
  - `TrafficFlushedEventArgs.LocalDeltas` (`IReadOnlyList<LocalTrafficDelta>`).
- Consumes: `TrafficCollector.DrainAndResetLocal()` (Task 3), `LanClassifier.Format(uint)` (Task 1), `AppDbContext.LocalTrafficRollups` (Task 2).

- [ ] **Step 1: Create the delta record**

```csharp
namespace NetworkMonitor.Services.Traffic
{
    public record LocalTrafficDelta(string RemoteIp, long BytesUploaded, long BytesDownloaded);
}
```

- [ ] **Step 2: Extend `TrafficFlushedEventArgs`**

```csharp
using NetworkMonitor.Models;

namespace NetworkMonitor.Services.Traffic
{
    public class TrafficFlushedEventArgs(IReadOnlyList<TrafficEntry> entries, IReadOnlyList<LocalTrafficDelta> localDeltas) : EventArgs
    {
        public IReadOnlyList<TrafficEntry> Entries
        {
            get;
        } = entries;

        public IReadOnlyList<LocalTrafficDelta> LocalDeltas
        {
            get;
        } = localDeltas;
    }
}
```

- [ ] **Step 3: Drain the LAN snapshot and build deltas in `FlushAsync`**

In `TrafficTracker.FlushAsync`, immediately after `Dictionary<int, (long Upload, long Download)> snapshot = collector.DrainAndReset();` add:

```csharp
            Dictionary<uint, (long Upload, long Download)> localSnapshot = collector.DrainAndResetLocal();
```

Change the outer guard from `if (snapshot.Count > 0)` to:

```csharp
            if (snapshot.Count > 0 || localSnapshot.Count > 0)
```

- [ ] **Step 4: Build the local delta list and upsert rollups inside `FlushAsync`**

Inside the guarded block, after the existing per-PID `foreach` that fills `entries` and before the `if (entries.Count > 0)` block, build the LAN delta list:

```csharp
                List<LocalTrafficDelta> localDeltas = new();

                foreach (KeyValuePair<uint, (long Upload, long Download)> localPair in localSnapshot)
                {
                    string remoteIp = LanClassifier.Format(localPair.Key);
                    localDeltas.Add(new LocalTrafficDelta(remoteIp, localPair.Value.Upload, localPair.Value.Download));
                }
```

Then adjust the persistence block so LAN rollups are written and the flush event always carries the deltas. Replace the existing `if (entries.Count > 0) { ... }` block with:

```csharp
                if (entries.Count > 0 || localDeltas.Count > 0)
                {
                    await using AppDbContext db = await dbFactory.CreateDbContextAsync(ct);

                    if (entries.Count > 0)
                    {
                        db.TrafficEntries.AddRange(entries);
                        await db.SaveChangesAsync(ct);

                        await UpsertRollupsAsync(db, timestamp, entries, ct);
                    }

                    if (localDeltas.Count > 0)
                    {
                        await UpsertLocalRollupsAsync(db, timestamp, localDeltas, ct);
                    }

                    Flushed?.Invoke(this, new TrafficFlushedEventArgs(entries, localDeltas));
                }
```

- [ ] **Step 5: Add the `UpsertLocalRollupsAsync` method**

Add next to `UpsertRollupsAsync` (mirrors it, keyed by `RemoteIp`):

```csharp
        private static async Task UpsertLocalRollupsAsync(AppDbContext db, DateTime timestamp, List<LocalTrafficDelta> deltas, CancellationToken ct)
        {
            long minuteEpoch = ((long)(timestamp - DateTime.UnixEpoch).TotalSeconds / 60) * 60;

            await db.Database.OpenConnectionAsync(ct);

            DbConnection connection = db.Database.GetDbConnection();

            await using (DbTransaction transaction = await connection.BeginTransactionAsync(ct))
            await using (DbCommand command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO LocalTrafficRollups (MinuteEpoch, RemoteIp, BytesUploaded, BytesDownloaded)
                    VALUES ($minute, $ip, $upload, $download)
                    ON CONFLICT(MinuteEpoch, RemoteIp) DO UPDATE SET
                        BytesUploaded = BytesUploaded + excluded.BytesUploaded,
                        BytesDownloaded = BytesDownloaded + excluded.BytesDownloaded
                    """;

                DbParameter minuteParameter = command.CreateParameter();
                minuteParameter.ParameterName = "$minute";
                minuteParameter.Value = minuteEpoch;
                command.Parameters.Add(minuteParameter);

                DbParameter ipParameter = command.CreateParameter();
                ipParameter.ParameterName = "$ip";
                command.Parameters.Add(ipParameter);

                DbParameter uploadParameter = command.CreateParameter();
                uploadParameter.ParameterName = "$upload";
                command.Parameters.Add(uploadParameter);

                DbParameter downloadParameter = command.CreateParameter();
                downloadParameter.ParameterName = "$download";
                command.Parameters.Add(downloadParameter);

                foreach (LocalTrafficDelta delta in deltas)
                {
                    ipParameter.Value = delta.RemoteIp;
                    uploadParameter.Value = delta.BytesUploaded;
                    downloadParameter.Value = delta.BytesDownloaded;

                    await command.ExecuteNonQueryAsync(ct);
                }

                await transaction.CommitAsync(ct);
            }

        }
```

Add `using NetworkMonitor.Services.Traffic;` is not needed (same namespace); ensure `LanClassifier` is reachable (same namespace `NetworkMonitor.Services.Traffic`).

- [ ] **Step 6: Build to verify**

Run: `dotnet build NetworkMonitor/NetworkMonitor.csproj -c Debug -p:Platform=x64`
Expected: Build succeeded. (`TrafficPage.OnTrafficFlushed` reads only `args.Entries`, so it is unaffected by the new constructor argument.)

- [ ] **Step 7: Commit**

Add `LocalTrafficDelta.cs` to `NetworkMonitor.slnx`.

```bash
git add NetworkMonitor/Services/Traffic/LocalTrafficDelta.cs NetworkMonitor/Services/Traffic/TrafficFlushedEventArgs.cs NetworkMonitor/Services/Traffic/TrafficTracker.cs NetworkMonitor.slnx
git commit -m "Persist LAN rollups and carry LAN deltas on traffic flush."
```

---

## Task 5: Purge wiring

**Files:**
- Modify: `NetworkMonitor/Services/Scanning/ScanWorker.cs:118-136` (inside `PurgeOldHistoryAsync`, the `TrafficPurgeDays` block)

**Interfaces:**
- Consumes: `Settings.TrafficPurgeDays`, `AppDbContext.Database`.

- [ ] **Step 1: Add the LocalTrafficRollups delete**

Inside `PurgeOldHistoryAsync`, in the `if (settings.TrafficPurgeDays > 0)` block, after the existing `DELETE FROM TrafficRollups ...` call and before the `SpeedTestResults` delete:

```csharp
                await db.Database.ExecuteSqlRawAsync(
                    "DELETE FROM LocalTrafficRollups WHERE MinuteEpoch < {0}",
                    new object[] { rollupCutoffEpoch },
                    ct);
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build NetworkMonitor/NetworkMonitor.csproj -c Debug -p:Platform=x64`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add NetworkMonitor/Services/Scanning/ScanWorker.cs
git commit -m "Purge LocalTrafficRollups under the traffic retention policy."
```

---

## Task 6: LocalTrafficNameResolver (pure)

**Files:**
- Create: `NetworkMonitor/Services/Traffic/LocalTrafficNameResolver.cs`
- Test: `NetworkMonitor.Tests/LocalTrafficNameResolverTests.cs`

**Interfaces:**
- Produces: `static string LocalTrafficNameResolver.Resolve(string remoteIp, IReadOnlyDictionary<string, string> namesByIp)` — returns the mapped display name, or `remoteIp` if unmapped/empty.
- Consumes: nothing.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Collections.Generic;
using NetworkMonitor.Services.Traffic;
using Xunit;

namespace NetworkMonitor.Tests
{
    public class LocalTrafficNameResolverTests
    {
        [Fact]
        public void ReturnsDeviceNameWhenIpIsKnown()
        {
            Dictionary<string, string> names = new Dictionary<string, string>
            {
                ["192.168.1.10"] = "Synology NAS"
            };

            string result = LocalTrafficNameResolver.Resolve("192.168.1.10", names);

            Assert.Equal("Synology NAS", result);
        }

        [Fact]
        public void FallsBackToBareIpWhenUnknown()
        {
            Dictionary<string, string> names = new Dictionary<string, string>();

            string result = LocalTrafficNameResolver.Resolve("192.168.1.99", names);

            Assert.Equal("192.168.1.99", result);
        }

        [Fact]
        public void FallsBackToBareIpWhenNameIsEmpty()
        {
            Dictionary<string, string> names = new Dictionary<string, string>
            {
                ["192.168.1.10"] = string.Empty
            };

            string result = LocalTrafficNameResolver.Resolve("192.168.1.10", names);

            Assert.Equal("192.168.1.10", result);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test NetworkMonitor.Tests --filter FullyQualifiedName~LocalTrafficNameResolverTests`
Expected: FAIL — type does not exist.

- [ ] **Step 3: Write the implementation**

```csharp
namespace NetworkMonitor.Services.Traffic
{
    public static class LocalTrafficNameResolver
    {
        public static string Resolve(string remoteIp, IReadOnlyDictionary<string, string> namesByIp)
        {
            string result = remoteIp;

            if (namesByIp.TryGetValue(remoteIp, out string? name) && !string.IsNullOrWhiteSpace(name))
            {
                result = name;
            }

            return result;
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test NetworkMonitor.Tests --filter FullyQualifiedName~LocalTrafficNameResolverTests`
Expected: PASS.

- [ ] **Step 5: Add to slnx and commit**

```bash
git add NetworkMonitor/Services/Traffic/LocalTrafficNameResolver.cs NetworkMonitor.Tests/LocalTrafficNameResolverTests.cs NetworkMonitor.slnx
git commit -m "Add LocalTrafficNameResolver for display-time IP naming."
```

---

## Task 7: LocalTrafficRow model + LocalTrafficAggregator (pure)

**Files:**
- Create: `NetworkMonitor/Models/LocalTrafficRow.cs`
- Create: `NetworkMonitor/Services/Traffic/LocalTrafficAggregator.cs`
- Test: `NetworkMonitor.Tests/LocalTrafficAggregatorTests.cs`

**Interfaces:**
- Produces:
  - `LocalTrafficRow(string RemoteIp, string DisplayName, long BytesUploaded, long BytesDownloaded, long CurrentRateBytesPerSecond, long PeakBytesPerSecond)` with computed `long TotalBytes => BytesUploaded + BytesDownloaded;`.
  - `static IReadOnlyList<LocalTrafficRow> LocalTrafficAggregator.Build(IReadOnlyList<LocalTrafficMinute> minutes, IReadOnlyDictionary<string, string> namesByIp)` — groups per-minute rows by IP into totals; `PeakBytesPerSecond` is the largest single-minute total divided by 60; rows sorted by `TotalBytes` descending; `CurrentRateBytesPerSecond` is 0 (the ViewModel fills it from live flushes).
  - `LocalTrafficMinute(long MinuteEpoch, string RemoteIp, long BytesUploaded, long BytesDownloaded)` — the shape read from the rollup table.
- Consumes: `LocalTrafficNameResolver.Resolve` (Task 6).

- [ ] **Step 1: Write the failing tests**

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
        public void SumsBytesPerEndpointAndResolvesName()
        {
            List<LocalTrafficMinute> minutes = new List<LocalTrafficMinute>
            {
                new LocalTrafficMinute(60, "192.168.1.10", 100, 200),
                new LocalTrafficMinute(120, "192.168.1.10", 300, 400)
            };
            Dictionary<string, string> names = new Dictionary<string, string> { ["192.168.1.10"] = "NAS" };

            IReadOnlyList<LocalTrafficRow> rows = LocalTrafficAggregator.Build(minutes, names);

            Assert.Single(rows);
            Assert.Equal("NAS", rows[0].DisplayName);
            Assert.Equal(400, rows[0].BytesUploaded);
            Assert.Equal(600, rows[0].BytesDownloaded);
        }

        [Fact]
        public void PeakIsLargestSingleMinuteTotalPerSecond()
        {
            List<LocalTrafficMinute> minutes = new List<LocalTrafficMinute>
            {
                new LocalTrafficMinute(60, "192.168.1.10", 600, 0),
                new LocalTrafficMinute(120, "192.168.1.10", 6000, 0)
            };
            Dictionary<string, string> names = new Dictionary<string, string>();

            IReadOnlyList<LocalTrafficRow> rows = LocalTrafficAggregator.Build(minutes, names);

            Assert.Equal(100, rows[0].PeakBytesPerSecond);
        }

        [Fact]
        public void SortsByTotalBytesDescending()
        {
            List<LocalTrafficMinute> minutes = new List<LocalTrafficMinute>
            {
                new LocalTrafficMinute(60, "192.168.1.10", 10, 10),
                new LocalTrafficMinute(60, "192.168.1.20", 5000, 5000)
            };
            Dictionary<string, string> names = new Dictionary<string, string>();

            IReadOnlyList<LocalTrafficRow> rows = LocalTrafficAggregator.Build(minutes, names);

            Assert.Equal("192.168.1.20", rows[0].RemoteIp);
            Assert.Equal("192.168.1.10", rows[1].RemoteIp);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test NetworkMonitor.Tests --filter FullyQualifiedName~LocalTrafficAggregatorTests`
Expected: FAIL — types do not exist.

- [ ] **Step 3: Create `LocalTrafficRow`**

```csharp
namespace NetworkMonitor.Models
{
    public record LocalTrafficRow(
        string RemoteIp,
        string DisplayName,
        long BytesUploaded,
        long BytesDownloaded,
        long CurrentRateBytesPerSecond,
        long PeakBytesPerSecond)
    {
        public long TotalBytes => BytesUploaded + BytesDownloaded;
    }
}
```

- [ ] **Step 4: Create `LocalTrafficAggregator` (defines `LocalTrafficMinute`)**

```csharp
using NetworkMonitor.Models;

namespace NetworkMonitor.Services.Traffic
{
    public record LocalTrafficMinute(long MinuteEpoch, string RemoteIp, long BytesUploaded, long BytesDownloaded);

    public static class LocalTrafficAggregator
    {
        public static IReadOnlyList<LocalTrafficRow> Build(IReadOnlyList<LocalTrafficMinute> minutes, IReadOnlyDictionary<string, string> namesByIp)
        {
            Dictionary<string, (long Upload, long Download, long PeakMinuteBytes)> totals = new();

            foreach (LocalTrafficMinute minute in minutes)
            {
                (long Upload, long Download, long PeakMinuteBytes) current = totals.TryGetValue(minute.RemoteIp, out (long Upload, long Download, long PeakMinuteBytes) existing)
                    ? existing
                    : (0L, 0L, 0L);

                long minuteBytes = minute.BytesUploaded + minute.BytesDownloaded;
                long peak = Math.Max(current.PeakMinuteBytes, minuteBytes);

                totals[minute.RemoteIp] = (current.Upload + minute.BytesUploaded, current.Download + minute.BytesDownloaded, peak);
            }

            List<LocalTrafficRow> rows = new List<LocalTrafficRow>(totals.Count);

            foreach (KeyValuePair<string, (long Upload, long Download, long PeakMinuteBytes)> pair in totals)
            {
                string displayName = LocalTrafficNameResolver.Resolve(pair.Key, namesByIp);
                long peakPerSecond = pair.Value.PeakMinuteBytes / 60;

                rows.Add(new LocalTrafficRow(pair.Key, displayName, pair.Value.Upload, pair.Value.Download, 0, peakPerSecond));
            }

            rows.Sort((left, right) => right.TotalBytes.CompareTo(left.TotalBytes));

            IReadOnlyList<LocalTrafficRow> result = rows;

            return result;
        }
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test NetworkMonitor.Tests --filter FullyQualifiedName~LocalTrafficAggregatorTests`
Expected: PASS.

- [ ] **Step 6: Add to slnx and commit**

```bash
git add NetworkMonitor/Models/LocalTrafficRow.cs NetworkMonitor/Services/Traffic/LocalTrafficAggregator.cs NetworkMonitor.Tests/LocalTrafficAggregatorTests.cs NetworkMonitor.slnx
git commit -m "Add LocalTrafficRow and LocalTrafficAggregator with tests."
```

---

## Task 8: LocalTrafficViewModel

**Files:**
- Create: `NetworkMonitor/ViewModels/LocalTrafficViewModel.cs`
- Modify: `NetworkMonitor/App.xaml.cs` (register `LanClassifier` and `LocalTrafficViewModel`)

**Interfaces:**
- Consumes: `IDbContextFactory<AppDbContext>`, `Settings`, `LocalTrafficAggregator.Build`, `LocalTrafficMinute`, `LanClassifier.Format` (not needed here — IPs already strings), `Device.IpAddress`/`Device.DisplayName`, `TrafficFlushedEventArgs.LocalDeltas`.
- Produces:
  - `ObservableCollection<LocalTrafficRow> Endpoints`
  - `string StatusText`
  - `bool IsLoading`
  - `Task LoadAsync(bool showLoading = false)`
  - `void ApplyLiveFlush(IReadOnlyList<LocalTrafficDelta> deltas)`

**Design notes:** Mirror `TrafficViewModel`'s structure (fields → constructor → properties → public methods → private methods; hand-written `SetProperty` properties). `LoadAsync` reads the last `TrafficPurgeDays`-bounded window is overkill; use the same time-range concept but keep v1 simple — load **all** current `LocalTrafficRollups` from the last 24h (`MinuteEpoch >= nowEpoch - 86400`). Build the name map from `db.Devices` (`IpAddress` → `DisplayName`). Current rate starts at 0; `ApplyLiveFlush` sets each endpoint's `CurrentRateBytesPerSecond = delta bytes / TrafficIntervalSeconds`, adds the delta to totals, and rebuilds the collection (endpoints absent from the flush get current rate 0).

- [ ] **Step 1: Create the ViewModel**

```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.EntityFrameworkCore;
using NetworkMonitor.Data;
using NetworkMonitor.Models;
using NetworkMonitor.Services.Traffic;

namespace NetworkMonitor.ViewModels
{
    public partial class LocalTrafficViewModel : ObservableObject
    {
        private const long WindowSeconds = 86400;

        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly Settings _settings;
        private Dictionary<string, (long Upload, long Download, long PeakBytesPerSecond, string DisplayName)> _windowTotals = new();

        public LocalTrafficViewModel(IDbContextFactory<AppDbContext> dbFactory, Settings settings)
        {
            _dbFactory = dbFactory;
            _settings = settings;
        }

        private ObservableCollection<LocalTrafficRow> _endpoints = [];

        public ObservableCollection<LocalTrafficRow> Endpoints
        {
            get => _endpoints;
            set => SetProperty(ref _endpoints, value);
        }

        private string _statusText = string.Empty;

        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        private bool _isLoading;

        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public async Task LoadAsync(bool showLoading = false)
        {

            if (showLoading)
            {
                IsLoading = true;
            }

            try
            {
                IReadOnlyList<LocalTrafficRow> rows = await Task.Run(BuildRowsAsync);

                SeedWindow(rows);
                Endpoints = new ObservableCollection<LocalTrafficRow>(rows);
                StatusText = BuildStatus(rows);
            }
            finally
            {

                if (showLoading)
                {
                    IsLoading = false;
                }

            }

        }

        public void ApplyLiveFlush(IReadOnlyList<LocalTrafficDelta> deltas)
        {

            if (_windowTotals.Count == 0 && deltas.Count == 0)
            {
                return;
            }

            Dictionary<string, long> ratesByIp = new();

            foreach (LocalTrafficDelta delta in deltas)
            {
                long deltaBytes = delta.BytesUploaded + delta.BytesDownloaded;
                ratesByIp[delta.RemoteIp] = deltaBytes / Math.Max(1, _settings.TrafficIntervalSeconds);

                (long Upload, long Download, long PeakBytesPerSecond, string DisplayName) current = _windowTotals.TryGetValue(delta.RemoteIp, out (long Upload, long Download, long PeakBytesPerSecond, string DisplayName) existing)
                    ? existing
                    : (0L, 0L, 0L, delta.RemoteIp);

                long thisRate = deltaBytes / Math.Max(1, _settings.TrafficIntervalSeconds);
                long peak = Math.Max(current.PeakBytesPerSecond, thisRate);

                _windowTotals[delta.RemoteIp] = (current.Upload + delta.BytesUploaded, current.Download + delta.BytesDownloaded, peak, current.DisplayName);
            }

            List<LocalTrafficRow> rows = new List<LocalTrafficRow>(_windowTotals.Count);

            foreach (KeyValuePair<string, (long Upload, long Download, long PeakBytesPerSecond, string DisplayName)> pair in _windowTotals)
            {
                ratesByIp.TryGetValue(pair.Key, out long currentRate);
                rows.Add(new LocalTrafficRow(pair.Key, pair.Value.DisplayName, pair.Value.Upload, pair.Value.Download, currentRate, pair.Value.PeakBytesPerSecond));
            }

            rows.Sort((left, right) => right.TotalBytes.CompareTo(left.TotalBytes));

            Endpoints = new ObservableCollection<LocalTrafficRow>(rows);
            StatusText = BuildStatus(rows);
        }

        private async Task<IReadOnlyList<LocalTrafficRow>> BuildRowsAsync()
        {
            long cutoffEpoch = (long)(DateTime.UtcNow - DateTime.UnixEpoch).TotalSeconds - WindowSeconds;

            await using AppDbContext db = await _dbFactory.CreateDbContextAsync();

            List<LocalTrafficMinute> minutes = await db.LocalTrafficRollups
                .Where(rollup => rollup.MinuteEpoch >= cutoffEpoch)
                .Select(rollup => new LocalTrafficMinute(rollup.MinuteEpoch, rollup.RemoteIp, rollup.BytesUploaded, rollup.BytesDownloaded))
                .ToListAsync();

            Dictionary<string, string> namesByIp = await db.Devices
                .Where(device => device.IpAddress != string.Empty)
                .ToDictionaryAsync(device => device.IpAddress, device => device.DisplayName);

            IReadOnlyList<LocalTrafficRow> rows = LocalTrafficAggregator.Build(minutes, namesByIp);

            return rows;
        }

        private void SeedWindow(IReadOnlyList<LocalTrafficRow> rows)
        {
            _windowTotals = new Dictionary<string, (long Upload, long Download, long PeakBytesPerSecond, string DisplayName)>();

            foreach (LocalTrafficRow row in rows)
            {
                _windowTotals[row.RemoteIp] = (row.BytesUploaded, row.BytesDownloaded, row.PeakBytesPerSecond, row.DisplayName);
            }

        }

        private static string BuildStatus(IReadOnlyList<LocalTrafficRow> rows)
        {
            long totalBytes = 0;

            foreach (LocalTrafficRow row in rows)
            {
                totalBytes += row.TotalBytes;
            }

            string label = rows.Count == 1 ? "endpoint" : "endpoints";
            string result = $"{rows.Count} {label} · {TrafficViewModel.FormatBytes(totalBytes)} total";

            return result;
        }
    }
}
```

Note: `Device.DisplayName` is `[NotMapped]` (`FriendlyName ?? Hostname ?? IpAddress`), so it cannot be translated into SQL. The `ToDictionaryAsync` above will fail if EF tries to translate `device.DisplayName`. **Fix:** materialise devices first, then project in memory. Use this body for the name map instead:

```csharp
            List<Device> devices = await db.Devices
                .Where(device => device.IpAddress != string.Empty)
                .ToListAsync();

            Dictionary<string, string> namesByIp = new();

            foreach (Device device in devices)
            {
                namesByIp[device.IpAddress] = device.DisplayName;
            }
```

- [ ] **Step 2: Register in DI**

In `App.xaml.cs`, add `LanClassifier` before `TrafficCollector` (line ~103) so it can be injected:

```csharp
                        services.AddSingleton<LanClassifier>();
```

And register the ViewModel next to `TrafficViewModel` (line ~132):

```csharp
                        services.AddSingleton<LocalTrafficViewModel>();
```

Ensure `using NetworkMonitor.Services.Traffic;` and `using NetworkMonitor.ViewModels;` are present in `App.xaml.cs` (they are — the file already registers `TrafficViewModel` and traffic services).

- [ ] **Step 3: Build to verify**

Run: `dotnet build NetworkMonitor/NetworkMonitor.csproj -c Debug -p:Platform=x64`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add NetworkMonitor/ViewModels/LocalTrafficViewModel.cs NetworkMonitor/App.xaml.cs NetworkMonitor.slnx
git commit -m "Add LocalTrafficViewModel and register LAN traffic services."
```

---

## Task 9: LocalTrafficPage view + host tab

**Files:**
- Create: `NetworkMonitor/Views/LocalTrafficPage.xaml` + `NetworkMonitor/Views/LocalTrafficPage.xaml.cs`
- Modify: `NetworkMonitor/Views/TrafficHostPage.xaml` (add tab + frame)
- Modify: `NetworkMonitor/Views/TrafficHostPage.xaml.cs` (navigation)

**Interfaces:**
- Consumes: `LocalTrafficViewModel` (Task 8), `TrafficTracker.Flushed` (Task 4), `TrafficRateFormatter.BytesPerSecond` / `TrafficViewModel.FormatBytes` for cell text.

- [ ] **Step 1: Create `LocalTrafficPage.xaml`**

Follow `DevicesPage.xaml` formatting. Use a `CommunityToolkit.WinUI.UI.Controls.DataGrid` (already referenced by the project) with columns Device, Download, Upload, Current, Peak.

```xml
<?xml version="1.0" encoding="utf-8"?>

<Page
    x:Class="NetworkMonitor.Views.LocalTrafficPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:controls="using:CommunityToolkit.WinUI.UI.Controls"
    xmlns:models="using:NetworkMonitor.Models"
    Background="{ThemeResource ApplicationPageBackgroundThemeBrush}">

    <Grid
        RowDefinitions="Auto,*"
        Margin="24,8,24,16">

        <TextBlock
            Grid.Row="0"
            Margin="0,0,0,8"
            Style="{ThemeResource BodyStrongTextBlockStyle}"
            Text="{x:Bind ViewModel.StatusText, Mode=OneWay}" />

        <controls:DataGrid
            Grid.Row="1"
            AutoGenerateColumns="False"
            IsReadOnly="True"
            GridLinesVisibility="Horizontal"
            ItemsSource="{x:Bind ViewModel.Endpoints, Mode=OneWay}">

            <controls:DataGrid.Columns>

                <controls:DataGridTextColumn
                    Header="Device"
                    Width="2*"
                    Binding="{Binding DisplayName}" />

                <controls:DataGridTextColumn
                    Header="Endpoint"
                    Width="*"
                    Binding="{Binding RemoteIp}" />

                <controls:DataGridTextColumn
                    Header="Download"
                    Width="*"
                    Binding="{Binding BytesDownloaded}" />

                <controls:DataGridTextColumn
                    Header="Upload"
                    Width="*"
                    Binding="{Binding BytesUploaded}" />

                <controls:DataGridTextColumn
                    Header="Current"
                    Width="*"
                    Binding="{Binding CurrentRateBytesPerSecond}" />

                <controls:DataGridTextColumn
                    Header="Peak"
                    Width="*"
                    Binding="{Binding PeakBytesPerSecond}" />

            </controls:DataGrid.Columns>

        </controls:DataGrid>

    </Grid>

</Page>
```

Note: `DataGridTextColumn.Binding` uses classic `{Binding}` (DataGrid does not support `x:Bind` in cell bindings). Raw byte/rate longs are shown here for a first pass; formatting is refined in Step 5.

- [ ] **Step 2: Create `LocalTrafficPage.xaml.cs`**

Mirror `TrafficPage.xaml.cs`'s Flushed subscribe/unsubscribe lifecycle.

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using NetworkMonitor.Services.Platform;
using NetworkMonitor.Services.Traffic;
using NetworkMonitor.ViewModels;

namespace NetworkMonitor.Views
{
    public sealed partial class LocalTrafficPage : Page
    {
        private readonly TrafficTracker _trafficTracker;

        public LocalTrafficPage()
        {
            InitializeComponent();
            ViewModel = App.AppHost.Services.GetRequiredService<LocalTrafficViewModel>();
            _trafficTracker = App.AppHost.Services.GetRequiredService<TrafficTracker>();
        }

        public LocalTrafficViewModel ViewModel
        {
            get;
        }

        protected override void OnNavigatedTo(NavigationEventArgs eventArgs)
        {
            base.OnNavigatedTo(eventArgs);
            _trafficTracker.Flushed -= OnTrafficFlushed;
            _trafficTracker.Flushed += OnTrafficFlushed;
            _ = ViewModel.LoadAsync(true);
        }

        protected override void OnNavigatedFrom(NavigationEventArgs eventArgs)
        {
            base.OnNavigatedFrom(eventArgs);
            _trafficTracker.Flushed -= OnTrafficFlushed;
        }

        private void OnTrafficFlushed(object? sender, TrafficFlushedEventArgs args)
        {

            try
            {
                DispatcherQueue.TryEnqueue(() => ViewModel.ApplyLiveFlush(args.LocalDeltas));
            }
            catch (Exception exception)
            {
                AppLog.Error("LocalTrafficPage.OnTrafficFlushed", exception);
            }

        }
    }
}
```

- [ ] **Step 3: Add the "Local" tab to `TrafficHostPage.xaml`**

Add a `SelectorBarItem` between "Traffic" and "Speed Test", and a matching frame after `TrafficFrame`:

```xml
            <SelectorBarItem
                Tag="Local"
                Text="Local" />
```

```xml
        <Frame
            Grid.Row="1"
            x:Name="LocalFrame"
            Visibility="Collapsed"
            Margin="0,8,0,0" />
```

- [ ] **Step 4: Wire navigation in `TrafficHostPage.xaml.cs`**

In `TabBarSelectionChanged`, add lazy navigation and visibility for the Local tab (mirroring the SpeedTest handling):

```csharp
                if (selectedTag == "Local" && LocalFrame.Content is null)
                {
                    LocalFrame.Navigate(typeof(LocalTrafficPage));
                }

                LocalFrame.Visibility = selectedTag == "Local" ? Visibility.Visible : Visibility.Collapsed;
```

Place the `LocalFrame.Visibility` line next to the existing `TrafficFrame.Visibility` / `SpeedTestFrame.Visibility` assignments.

- [ ] **Step 5: Format the cell values**

Replace the raw-long columns with formatted text via a value converter or a formatted read-only property on `LocalTrafficRow`. Simplest, convention-friendly approach: add formatted display properties to `LocalTrafficRow` (record computed props are allowed) and bind those.

Add to `LocalTrafficRow` (in `NetworkMonitor/Models/LocalTrafficRow.cs`):

```csharp
        public string DownloadText => NetworkMonitor.ViewModels.TrafficViewModel.FormatBytes(BytesDownloaded);
        public string UploadText => NetworkMonitor.ViewModels.TrafficViewModel.FormatBytes(BytesUploaded);
        public string CurrentText => NetworkMonitor.Services.Traffic.TrafficRateFormatter.BytesPerSecond(CurrentRateBytesPerSecond, 1);
        public string PeakText => NetworkMonitor.Services.Traffic.TrafficRateFormatter.BytesPerSecond(PeakBytesPerSecond, 1);
```

Change the four numeric column bindings to `{Binding DownloadText}`, `{Binding UploadText}`, `{Binding CurrentText}`, `{Binding PeakText}`.

- [ ] **Step 6: Add both view files to slnx, then build**

Add `LocalTrafficPage.xaml` and `LocalTrafficPage.xaml.cs` to `NetworkMonitor.slnx` (Views folder).

Run: `dotnet build NetworkMonitor/NetworkMonitor.csproj -c Debug -p:Platform=x64`
Expected: Build succeeded.

- [ ] **Step 7: Manual end-to-end verification (spec section 10)**

1. Delete the local DB once (schema changed): close the app, delete `%LOCALAPPDATA%\...\networkmonitor.db` (path from `AppDbContext.DbPath`).
2. Run the app (VS, x64, Debug). Open **Traffic → Local**. It should be empty initially.
3. Run a NAS/SMB copy (e.g. a Macrium backup or a large file copy to the NAS share).
4. Within one traffic interval, a row for the NAS endpoint should appear, showing a live **Current** rate climbing, growing **Download/Upload** totals, and a **Peak**. If the NAS is a known device, the **Device** column shows its friendly name; otherwise the bare IP.
5. Confirm the same bytes still appear under System on the main **Traffic** tab (existing behaviour unchanged).

- [ ] **Step 8: Commit**

```bash
git add NetworkMonitor/Views/LocalTrafficPage.xaml NetworkMonitor/Views/LocalTrafficPage.xaml.cs NetworkMonitor/Views/TrafficHostPage.xaml NetworkMonitor/Views/TrafficHostPage.xaml.cs NetworkMonitor/Models/LocalTrafficRow.cs NetworkMonitor.slnx
git commit -m "Add Local Traffic tab with live per-endpoint view."
```

---

## Final: full test + build gate

- [ ] Run the full suite: `dotnet test NetworkMonitor.Tests`
  Expected: all existing tests plus the new `LanClassifierTests`, `LocalTrafficNameResolverTests`, `LocalTrafficAggregatorTests` PASS.
- [ ] Run: `dotnet build NetworkMonitor.slnx -c Release -p:Platform=x64` — Expected: Build succeeded.
- [ ] Completion summary must state: **a one-time local DB delete is required on upgrade** (new `LocalTrafficRollups` table via EnsureCreated).

---

## Self-Review Notes (author checklist, already applied)

- **Spec coverage:** foundation capture (Task 3) · classification §5 (Task 1) · endpoint→device resolution §6.3 (Tasks 6/8) · separate table §8.2 (Task 2) · parallel dictionary / hot path §8.3 (Task 3) · display-time naming §8.4 (Tasks 6/8) · IPv4-only, string column §8.5 (Tasks 1/2) · existing retention §8.6 (Task 5) · Local Traffic tab §3 (Task 9) · tests §10 (Tasks 1/6/7 + Task 9 manual). All covered.
- **Known adjustment baked in:** `Device.DisplayName` is `[NotMapped]`, so Task 8 materialises `Devices` before projecting the name map (called out inline).
- **TraceEvent property names** (`args.daddr` / `args.saddr`) are the one external assumption to confirm at first build of Task 3; fallback instruction included.
- **IPv6 explicitly out of scope**; `RemoteIp` stored as string keeps the door open with no schema change.
