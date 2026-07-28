# Chunk 2 — Local traffic capture & storage

Reviewed 2026-07-27. Fix phase completed 2026-07-27 — see `progress.md`.

11 findings: **2 BUG · 3 RISK · 6 CLEANUP**. The LAN capture path is a clean extension of the existing WAN one (same interlocked-counter shape, same drain-and-flush loop, purge and unique index correctly extended to the new tables). The problems are all in the places where the LAN half and the WAN half **behave differently** without a reason, plus the write volume the second capture stream adds.

---

## C2-1 [BUG] WAN bytes with an unknown PID are dropped; LAN bytes aren't — status: fixed

`NetworkMonitor.Services/Traffic/TrafficCollector.cs:128-141`

```csharp
if (_lanClassifier.TryClassifyLocal(remote, out uint packed))
{
    int keyPid = pid < 0 ? SystemPid : pid;      // LAN: unknown PID → System (4)
    …
}
else if (pid >= 0)                              // WAN: unknown PID → discarded
{
    …
}
```

ETW `TcpIp`/`UdpIp` events report `ProcessID = -1` when the kernel can't attribute the packet (common for early-boot flows, some system components, and packets whose socket is already torn down). The LAN branch folds those into `System`; the WAN branch silently drops them, so the **Internet tab under-reports** and its totals can't be reconciled against any external counter.

**Proposed fix:** apply the same `pid < 0 → SystemPid` mapping in the WAN branch, so both halves account for every byte they observe.

---

## C2-2 [BUG] A process that exits during the interval loses all its WAN traffic — status: fixed

`NetworkMonitor.Services/Traffic/TrafficTracker.cs:75-92`

```csharp
try
{
    using Process process = Process.GetProcessById(kvp.Key);
    …
    entries.Add(new TrafficEntry { … });
}
catch (ArgumentException)
{
}
```

`Process.GetProcessById` throws `ArgumentException` once the process has exited, and the `catch` discards the **whole entry** — the bytes are gone, not merely unnamed. Anything short-lived (an installer, a CLI tool, `curl`, the app's own update download if it were separate) systematically under-reports, and the effect is worst for exactly the traffic a user is most likely to go looking for.

`_infoCache` usually still holds that PID's name and path from an earlier flush, and the LAN path already degrades gracefully — `ResolveLocalProcess` (`:343-351`) catches the same exception and falls back to `("System", null)`. Only the WAN path throws the data away.

**Proposed fix:** resolve via `_infoCache` first and fall back to `("System", null)` (or `"(exited)"`) exactly as the LAN path does; never drop the counter.

---

## C2-3 [RISK] The collector's counter dictionaries are never pruned — status: fixed

`NetworkMonitor.Services/Traffic/TrafficCollector.cs:15-16,25-65`

`DrainAndReset` / `DrainAndResetLocal` zero each counter with `Interlocked.Exchange` but never **remove** the key, so `_counters` and `_localCounters` only ever grow for the life of the process, and every flush (default: **every second**, see C2-11) walks the full dictionary.

`_localCounters` is the sharp end, because its key includes the remote port:

```csharp
public readonly record struct LocalFlowKey(int Pid, uint RemoteIp, byte Protocol, ushort RemotePort);
```

For **inbound** connections — another device mounting this machine's SMB share, hitting a local web server, or any peer-to-peer app — the recorded port is the peer's ephemeral port, a fresh value per connection. A machine that serves LAN clients therefore accumulates one permanent dictionary entry per connection ever made, unbounded, and pays for it on every flush.

Note `_infoCache` in `TrafficTracker` already has exactly this protection (`MaxCacheEntries = 512` + `PruneInfoCache`); the collector's dictionaries have none.

**Proposed fix:** in the drain, remove keys whose counters were both zero this cycle. The remove-vs-writer race can lose at most the few bytes an idle flow records in the microseconds between `Exchange` and `TryRemove` — bound it by re-reading the array after removal and re-adding the key if it turned non-zero, or by only removing after N consecutive idle drains.

---

## C2-4 [RISK] Entries and rollups are not written atomically — status: fixed

`NetworkMonitor.Services/Traffic/TrafficTracker.cs:120-141`

A single flush commits **four** times: WAN entries (`SaveChangesAsync`), WAN rollups (own transaction), LAN entries (`SaveChangesAsync`), LAN rollups (own transaction). A crash, a power loss, or the 30-second `Watchdog` abort between any two leaves raw entries with no matching rollup, or the reverse.

That matters here because the two are read by *different UI ranges* — raw entries for the 5-minute view, rollups for everything else (`LocalViewModel.cs:484`, `InternetViewModel.cs:363`) — so the same minute can total differently depending on the selected range, with no way for the user to tell which is right.

**Proposed fix:** wrap the whole flush in one transaction (open the connection once, `BeginTransaction`, `SaveChangesAsync` both entry sets and run both upserts on that transaction, commit once). It's also fewer round-trips than the current four.

---

## C2-5 [RISK] Raw per-second rows are retained for 7 days to serve a 5-minute window — status: fixed

`NetworkMonitor.Services/Data/Settings.cs:98-108`, `NetworkMonitor.Services/Scanning/ScanWorker.cs:137-159`, `NetworkMonitor/ViewModels/InternetViewModel.cs:590-601`

`TrafficIntervalSeconds` defaults to **1**, so the tracker writes a row per active flow **per second** — and the LAN table is far more granular than the WAN one: `TrafficEntries` is one row per *process*, `LocalTrafficEntries` is one row per *(process, remote IP, protocol, remote port)*. Ten concurrent LAN flows is ~864 000 rows a day, before WAN.

Those raw rows are read by exactly one code path. `useRollup = bucketSeconds >= 60`, and `BucketSizeFor` returns a sub-minute bucket only for `hours <= 5.0/60.0` — the **5-minute** range. Every other range, and the entire daily digest (`DigestGenerator.cs:81,131` read `TrafficRollups`/`LocalTrafficRollups` only), reads rollups.

So raw entries older than five minutes are never read again, yet `PurgeOldHistoryAsync` keeps them for `TrafficPurgeDays` (default **7 days**) — 7 days of per-second per-flow rows, growing the DB, the WAL, the nightly backup and the purge delete, to serve a five-minute window.

**Proposed fix:** give the raw tables their own short retention (an hour is generous) separate from the rollup retention, purged on the flush loop rather than the 24-hour purge loop. The rollups already carry the long history.

**Fixed** — 1-hour retention on the flush loop (`TrafficTracker.PurgeRawEntriesAsync`, own 2-minute watchdog, 5-minute cadence). The now-redundant raw deletes were also removed from `ScanWorker.PurgeOldHistoryAsync`, which keeps the rollups and speed-test results only; leaving them would have been a daily query that can never match a row.

---

## C2-6 [PERF] `PruneInfoCache` ignores LAN-only processes — status: fixed

`NetworkMonitor.Services/Traffic/TrafficTracker.cs:143-146,150-169`

The prune drops every cached PID that isn't in the **WAN** snapshot, but the cache is shared with `ResolveLocalProcess`. A process doing only LAN traffic (a NAS client, a media server, `System` itself) is evicted on every prune and then re-resolved with `OpenProcess` + `QueryFullProcessImageName` on the next flush — a second later.

The trigger is wrong too: `if (snapshot.Count > 0 && …)` means a machine with no WAN traffic never prunes at all, however large the cache grows.

**Proposed fix:** pass both snapshots and keep a PID that appears in either; trigger the prune on cache size alone.

---

## C2-7 [CLEANUP] The two rollup upserts are near-identical 60-line blocks — status: fixed

`NetworkMonitor.Services/Traffic/TrafficTracker.cs:171-298`

`UpsertRollupsAsync` and `UpsertLocalRollupsAsync` differ only in the column list, the conflict target and the per-row parameter assignment; everything else — the `minuteEpoch` expression (duplicated verbatim at `:173` and `:230`), the open/transaction/command scaffolding, the parameter creation — is copied. That's the shape that drifts when one is fixed and the other isn't.

**Proposed fix:** one `UpsertAsync(connection, transaction, sql, rows, bind)` helper taking the SQL and a per-row bind action; hoist `minuteEpoch` to a shared static.

---

## C2-8 [CLEANUP] `OpenConnectionAsync` is never paired with a close — status: fixed

`NetworkMonitor.Services/Traffic/TrafficTracker.cs:175,232`

Both upserts call `db.Database.OpenConnectionAsync(ct)` and neither closes. Disposing the context does close the underlying connection, so this isn't a leak — but the open-count is left unbalanced, and the explicit open isn't needed at all since `BeginTransactionAsync` opens the connection itself.

**Proposed fix:** falls out of C2-4 — open once for the whole flush, or drop the explicit open.

---

## C2-9 [CLEANUP] Bare `catch (Exception)` around `process.StartTime` — status: fixed

`NetworkMonitor.Services/Traffic/TrafficTracker.cs:305-312`

The empty catch is deliberate (access-denied on protected processes is normal), but catching everything also swallows genuine faults and leaves `haveStartTime = false`, which quietly degrades the PID-recycling protection added by 2026-06-23's C2-6.

**Proposed fix:** narrow to `Win32Exception` / `InvalidOperationException` / `NotSupportedException`.

---

## C2-10 [CLEANUP] Collector setup runs on the host-start path — status: fixed

`NetworkMonitor.Services/Traffic/TrafficCollector.cs:67-84`

`BackgroundService.StartAsync` returns only when `ExecuteAsync` hits its first `await`. Here that's `await Task.Run(() => _session.Source.Process())` at `:84`, so `StopOrphanedSession()` (which enumerates every ETW session on the machine) and the whole kernel-provider setup run **synchronously inside `AppHost.StartAsync()`**, which `OnLaunched` awaits before the main window is created — i.e. on the splash-screen critical path.

**Proposed fix:** `await Task.Yield();` as the first line of `ExecuteAsync`.

---

## C2-11 [CLEANUP] Small lifetime/consistency items — status: fixed

- `TrafficCollector` is not `sealed` (every other worker in `Services` is a sealed or explicitly-shaped type); `TrafficTracker` isn't either.
- `ct.Register(() => _session.Stop())` (`TrafficCollector.cs:82`) discards the `CancellationTokenRegistration`; harmless for a token that lives as long as the process, but it also captures `_session` non-null-checked inside a lambda that can run after `Dispose`.
- `SystemPid = 4` is declared twice — `TrafficCollector.cs:14` and `TrafficTracker.cs:22` — for the same concept.

**Proposed fix:** seal both workers; keep the registration and dispose it in `Dispose`; move `SystemPid` to a single shared constant.

---

## Files reviewed

- `NetworkMonitor.Services/Traffic/TrafficCollector.cs`
- `NetworkMonitor.Services/Traffic/TrafficTracker.cs`
- `NetworkMonitor.Services/Traffic/LocalFlowKey.cs`
- `NetworkMonitor.Services/Traffic/LocalTrafficDelta.cs`
- `NetworkMonitor.Services/Traffic/TrafficFlushedEventArgs.cs`
- `NetworkMonitor.Models/Traffic/LocalTrafficEntry.cs`
- `NetworkMonitor.Models/Traffic/LocalTrafficRollup.cs`
- `NetworkMonitor.Services/Data/AppDbContext.cs` (Local traffic sets, indexes)
- `NetworkMonitor.Services/Scanning/ScanWorker.cs` (`PurgeOldHistoryAsync` only)
- `NetworkMonitor.Services/Digest/DigestGenerator.cs` (which tables the digest reads — context for C2-5)

## Notes carried to other chunks

- `Flushed` is raised only when a flush produced rows (`TrafficTracker.cs:120-141`), so an idle interval raises nothing. Whether the live rate badge then holds its last non-zero value is a **chunk 4** question.

## User findings

Co-reviewed 2026-07-28. **No user findings.** The three behaviour decisions in this chunk were put to the user explicitly and all three confirmed as-is:

- 1-hour retention for raw entries, with rollups keeping the full `TrafficPurgeDays` history (C2-5).
- An exited process keeps its bytes under its cached name, falling back to `System`, rather than being relabelled `(exited)` (C2-2).
- Idle flows are dropped after 60 consecutive idle drains, re-draining the removed array so racing bytes aren't stranded (C2-3).
