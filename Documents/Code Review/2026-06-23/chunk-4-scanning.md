# Chunk 4 — Scanning (reviewed 2026-06-23)

Overall: the scan/merge pipeline is correct and `ScanWorker` is the **resilience role model** for the codebase (both loops wrap their body in try/catch — exactly what Chunk 2's traffic loops lack). The real issues here are a **DB-efficiency/index** problem in the merge, a **concurrent-scan** gap, and a **retention inconsistency** that spans into `App.OnLaunched`.

Also resolves the Chunk 3 cross-reference **C3-5** — see C4-1.

---

## Findings

### C4-1 [RESOLVES C3-5] MAC normalization is actually consistent — vendor lookup works
`NetworkScanner.NormaliseMac` (`:117`) converts to **uppercase colon** form (`AA:BB:CC:DD:EE:FF`) before storing and before `oui.Lookup(mac)`. `OuiDatabase.Lookup` lowercases (`aa:bb:cc`) and matches the colon-lowercase keys. So the dash-vs-colon concern from C3-5 does **not** bite on the scan path. Residual nit only: `Lookup` is correct solely because every caller pre-normalizes — it would be more robust if it normalized its own input (dash→colon) internally.
Status: resolved (C3-5 downgraded to a minor robustness nit)

### C4-2 [PERF] Merge does an N+1 query whose `Replace/ToUpper` defeats the MAC unique index
`DeviceTracker.cs:46-50` — for **each** scanned device a separate query runs:
```
db.Devices.Where(d => d.MacAddress.Replace("-", ":").ToUpper() == macUpper) …
```
The `Replace`/`ToUpper` are translated into SQL (`upper(replace(...))`), so SQLite **cannot use the unique index on `MacAddress`** — every lookup is a full table scan, N times per scan cycle.
**Fix:** load `db.Devices` once into a `Dictionary<string, Device>` keyed by canonical MAC and match in memory (also removes the N round-trips). Since MACs are stored canonical (C4-1), an index-friendly equality match is possible.
Status: **FIXED 2026-06-25 (batch 2)** — `MergeAsync` now loads all devices once into a `Dictionary<string, Device>` keyed by `MacNormalizer.Normalize`, preserving the IsKnown/Id tie-break, and matches in-memory. The per-device `Replace/ToUpper` SQL query is gone.

### C4-3 [RISK] No mutual exclusion between manual `ScanNowAsync` and the periodic scan
`ScanWorker.cs` — `ScanNowAsync` (UI "Scan Network") and `RunScanLoopAsync` both call `RunScanAsync` with no shared gate. Clicking scan while a scheduled scan runs executes two merges concurrently (each with its own `DbContext`) → concurrent SQLite writes ("database is locked"), a doubled `ScanSession`, and possibly duplicate Appeared/Disappeared events.
**Fix:** guard `RunScanAsync` with a `SemaphoreSlim(1,1)` (skip-if-running or queue).
Status: **FIXED 2026-06-25 (batch 3)** — added `_scanGate = new SemaphoreSlim(1,1)`; `RunScanAsync` does `WaitAsync(ct)` / `Release()` in a finally (queue, not skip, so the manual button never silently no-ops). Gate disposed in a new `Dispose()` override.

### C4-4 [RISK] Traffic retention is inconsistent and partly hard-coded
Two places purge traffic with different rules:
- `App.xaml.cs:155-163` (startup) hard-codes a **7-day** delete of `TrafficEntries` **and** `TrafficRollups`.
- `ScanWorker.PurgeOldHistoryAsync` (`:79-85`) deletes `TrafficEntries` using `settings.TrafficPurgeDays`, and never touches `TrafficRollups`.

Consequences: if a user sets `TrafficPurgeDays > 7`, the startup purge still trims raw entries to 7 days (setting silently overridden); `TrafficRollups` are only ever trimmed at startup at a fixed 7 days, ignoring the setting entirely. Defaults happen to match (7), which masks the bug.
**Fix:** single source of truth — purge both tables in one place using `settings.TrafficPurgeDays`; remove the hard-coded startup figures.
Status: **FIXED 2026-06-25 (data-integrity batch)** — `ScanWorker.PurgeOldHistoryAsync` now purges **both** `TrafficEntries` and `TrafficRollups` using `settings.TrafficPurgeDays` (rollups by the matching `MinuteEpoch` cutoff). Removed the hard-coded 7-day retention block from `App.OnLaunched` entirely. Single source of truth; runs at startup (purge loop fires immediately) and every 24h.

### C4-5 [CLEANUP/correctness] Defensive MAC re-normalization implies non-canonical rows can exist
`DeviceTracker.cs:26,47` re-normalize stored `MacAddress` (`Replace("-",":").ToUpper()`) on read. That defensiveness only matters if some write path stores a non-canonical MAC (e.g. CSV import — `DeviceCsvImporter`, Chunk 6). Non-canonical rows could also dodge the unique index (`AA:BB…` vs `aa-bb…` treated as different), creating duplicate devices (which C4-2's `OrderByDescending(IsKnown)` then has to paper over).
**Fix:** normalize-on-write at every entry point (scanner already does; verify importer); then reads need no re-normalization (also enables C4-2's index use).
Status: **FIXED 2026-06-25 (batch 2)** — CSV importer now normalizes on write (C6-2); scanner + import VM + tracker all route through the new `MacNormalizer`. Merge still normalizes-on-read when building its in-memory dictionary, but cheaply (no index-defeating SQL) and only to stay robust to any legacy non-canonical rows.

### C4-6 [CLEANUP] `SemaphoreSlim` in `ScanAsync` not disposed
`NetworkScanner.cs:13` — `new SemaphoreSlim(...)` per scan is never disposed. Minor per-cycle leak; wrap in `using`.
Status: **FIXED 2026-06-25 (leaks batch)** — `using SemaphoreSlim semaphore = new(...)`. Safe: all ping tasks are awaited (`Task.WhenAll`) before the method returns; nothing uses the semaphore afterward.

### C4-7 [CLEANUP] Ping not cancellable mid-flight
`NetworkScanner.cs:54` — `SendPingAsync(ip, timeoutMs)` isn't passed `ct`; only the semaphore wait observes cancellation. In-flight pings finish after a cancel. Minor (timeout-bounded).
Status: open

---

## Notes (not findings)
- **Strength:** `ScanWorker` wraps both loop bodies in try/catch (`:29-47, 53-71`) — resilient, and the pattern Chunk 2's traffic loops should adopt (C2-4).
- **Strength:** `GetArpTableAsync` reads stdout to end *before* `WaitForExitAsync` (`:99-100`) — correct ordering, avoids the redirect deadlock flagged in C1-3 for `WindowsStartupService`.
- The ping∩arp intersection (`:26`) drops hosts that answer ping but aren't yet in the ARP cache — acceptable; they're picked up next cycle.
- `MergeAsync` commits everything in a single `SaveChangesAsync` (atomic per scan) — good.
- `DeviceNotification` is a clean record — no findings.

## Triage / actions
No fixes applied (record-only). Priority when fixing: C4-3 (concurrent-scan corruption), C4-4 (retention correctness), C4-2 (perf/index), C4-5 (canonical MAC on write). C4-6/C4-7 cosmetic.

---

## Files reviewed
- `NetworkMonitor/Services/NetworkScanner.cs`
- `NetworkMonitor/Services/DeviceTracker.cs`
- `NetworkMonitor/Services/ScanWorker.cs`
- `NetworkMonitor/Services/DeviceNotification.cs`

## User findings
None
