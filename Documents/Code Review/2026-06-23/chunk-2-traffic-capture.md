# Chunk 2 — Traffic capture (reviewed 2026-06-23)

Overall: the core capture design is good — a lock-light `long[65536][]` counter array updated from the single ETW callback thread via `Interlocked`, drained atomically by the tracker thread via `Interlocked.Exchange`. That concurrency model is correct. The notable issues are **resilience** (unhandled exceptions in the background loops can tear down *all* background services) and a couple of **resource/coverage** gaps.

Severity note for this chunk: in .NET 6+ the default `BackgroundServiceExceptionBehavior` is **`StopHost`** — if any `BackgroundService.ExecuteAsync` throws, the host stops (signals shutdown / tears down every hosted service: scanning, traffic, digest, backup). So an unhandled exception in a traffic loop isn't local — it kills background functionality app-wide. That raises the priority of C2-2 and C2-4.

---

## Findings

### C2-1 [RISK] Leftover ETW session name collision on restart
`TrafficCollector.cs:41` — `new TraceEventSession("NetworkMonitorTraffic")` with no handling for a pre-existing session of that name. Combined with **C1-2** (host never stopped on exit, so the kernel session isn't reliably torn down), a crash/hard-exit can leave the named real-time session registered; the next launch may then fail or fault when constructing/starting it.
**Fix:** proactively stop any existing session first (`TraceEventSession.GetActiveSessionNames()` / stop-if-exists), or construct with restart semantics, and ensure deterministic teardown (see C1-2).
Status: **FIXED 2026-06-26 (traffic residuals)** — new `StopOrphanedSession()` runs before session construction: scans `GetActiveSessionNames()` and, if `NetworkMonitorTraffic` is found, attaches (`TraceEventSessionOptions.Attach`) and `Stop()`s the leftover. Runs inside the existing C2-2 try/catch, so cleanup failure can't fault the host.

### C2-2 [RISK] Unhandled exception starting/processing the ETW session faults the service → StopHost
`TrafficCollector.cs:39-51` — session creation, `EnableKernelProvider`, and `_session.Source.Process()` run with no try/catch. If the session can't start (conflict per C2-1, transient ETW error), the `ExecuteAsync` task faults → default `StopHost` tears down all background services.
**Fix:** wrap session setup/processing in try/catch (log + stop gracefully), or opt the host into `BackgroundServiceExceptionBehavior.Ignore` for these services.
Status: **FIXED 2026-06-25 (batch 1)** — `TrafficCollector.ExecuteAsync` is now `async` with session setup + `Process()` wrapped in try/catch; an ETW failure no longer faults the host. (C2-1 session-name collision still open — separate batch.)

### C2-3 [RISK] Per-PID counter array capped at 65536 — high-PID traffic silently dropped
`TrafficCollector.cs:10` (`new long[65536][]`) + `:63` (`pid < _counters.Length` guard). Windows process IDs are DWORDs and can exceed 65535 on busy/long-running systems; any such PID's bytes are silently discarded, under-counting traffic (and the per-app digest totals).
**Fix:** key by PID in a `ConcurrentDictionary<int, long[]>` (or grow dynamically) instead of a fixed 65536 array.
Status: **FIXED 2026-06-26 (traffic residuals)** — `_counters` is now `ConcurrentDictionary<int, long[]>`; `AddBytes` drops the `pid < Length` guard and uses `GetOrAdd(pid, static key => new long[2])` then `Interlocked.Add` on the slot. `DrainAndReset` iterates the map's KVPs. All PIDs counted regardless of value; lock-free single-writer/single-reader semantics preserved.

### C2-4 [RISK] TrafficTracker flush loop has no try/catch → a transient DB error faults the service (StopHost)
`TrafficTracker.cs:30-37` — `while (...) { await Task.Delay(...); await FlushAsync(); }` is unguarded. `FlushAsync` does DB writes (`SaveChangesAsync`, raw upserts); a transient failure (DB locked, disk) faults `ExecuteAsync` → StopHost. Note `DigestWorker` *does* wrap its loop body in try/catch — this loop should too.
**Fix:** try/catch around the loop body (swallow/log and continue), mirroring `DigestWorker`.
Status: **FIXED 2026-06-25 (batch 1)** — flush loop body wrapped in try/catch (OperationCanceledException + Exception), mirroring `DigestWorker`/`ScanWorker`.

### C2-5 [RISK/CLEANUP] `Process.GetProcessById(...)` result never disposed → handle/object leak each flush
`TrafficTracker.cs:56` — `Process.GetProcessById(kvp.Key).ProcessName` creates a `Process` (IDisposable) that is never disposed, every flush, for every active PID (~1/sec by default). The companion `GetProcessPath` correctly closes its `OpenProcess` handle, but this `Process` object leaks until finalized.
**Fix:** `using Process process = Process.GetProcessById(...)`, or cache pid→name to avoid the call entirely (see C2-6).
Status: **FIXED 2026-06-25 (leaks batch)** — `using Process process = Process.GetProcessById(kvp.Key);` then read `process.ProcessName`. Disposed each flush. (C2-6 pid→name caching still open as a perf item.)

### C2-6 [PERF] No pid→name/path caching; resolved every flush
`TrafficTracker.cs:54-66` — process name + full image path are re-resolved for every PID on every flush (default 1s). `GetProcessPath` opens/closes a process handle each time. With many active PIDs this is avoidable overhead.
**Fix:** cache pid→(name, path); invalidate on miss/exit. Reduces syscalls and sidesteps C2-5.
Status: **FIXED 2026-06-26 (start-time-keyed cache)** — user chose "do it properly". `TrafficTracker` now has `_pathCache` (`Dictionary<int,(DateTime StartTime, string? Path)>`); new `ResolveProcessPath(pid, process)` reads `process.StartTime` and reuses the cached path only when the cached StartTime matches — so a **recycled PID** (different process, different start time) is a cache miss and gets re-resolved, preventing mis-attribution. On a miss it calls `GetProcessPath` (the OpenProcess+QueryFullProcessImageName syscalls) and caches the result; on a hit those syscalls are skipped. If `StartTime` is inaccessible (access-denied/exited) it resolves fresh and does not cache (safe fallback). `ProcessName` is still read from the already-obtained `Process` (free). Mutated only on the single tracker-flush thread → no locking. DB delete: not necessary.

### C2-7 [CLEANUP] Unit-base inconsistency (SI vs binary)
`TrafficRateFormatter.cs` formats rates with SI 1000 (Kb/s, MB/s), while byte *sizes* elsewhere (`DigestChartRenderer.FormatBytes`, `TrafficViewModel.FormatBytes`) use binary 1024 (KB, MB). May be intentional (rates SI, sizes binary), but worth an explicit decision for consistency.
Status: **FIXED 2026-06-25 (decided cleanups)** — user chose **binary (1024)**. `TrafficRateFormatter.BitsPerSecond`/`BytesPerSecond` thresholds + divisors changed 1000→1024 (1_073_741_824 / 1_048_576 / 1024). Now consistent with the size formatters. `TrafficRateFormatterTests` InlineData updated to binary-clean inputs/outputs.
> **Superseded 2026-07-23** — decision reversed: the whole app (rates *and* sizes) now uses **decimal (SI, ÷1000)** units, since binary divisors would strictly require KiB/MiB labels and network speeds are universally quoted decimal.

### C2-8 [CLEANUP] `AppTrafficTotal` DTO lives in `Services`, not `Models`
`AppTrafficTotal.cs` — it's a pure data record used as digest input; sits in the `Services` namespace while sibling DTOs live in `Models`. Minor placement/consistency nit.
Status: **FIXED 2026-06-26 (with U5-1 reorg)** — moved to `Models/AppTrafficTotal.cs`, namespace `NetworkMonitor.Models`. Consumers (`DigestGenerator`, `DigestSummaryBuilder`, tests) already import `NetworkMonitor.Models`; test csproj link path updated.

---

## Notes (not findings)
- The `Interlocked.CompareExchange` lazy slot-allocation + `Interlocked.Add`/`Interlocked.Exchange` drain is a correct, lock-free pattern for the single-writer (ETW thread) / single-reader (tracker thread) case. **Strength, not a defect.**
- Short-lived processes that exit between capture and flush lose their bytes (`GetProcessById` throws `ArgumentException`, caught at `TrafficTracker.cs:68`, entry skipped). Acceptable tradeoff; noted for awareness.
- `TrafficWindow.AlignedCutoffEpoch` and `TrafficRateFormatter.BucketSeconds` are small and correct (BucketSeconds guards `<= 0`). Division-by-zero only if a caller passes `seconds == 0` to the rate formatters — callers use guarded bucket sizes, so low risk.

## Triage / actions
No fixes applied (record-only). Recommended priority when fixing: C2-4 and C2-2 (resilience — prevent app-wide background teardown), C2-5 (leak), C2-3 (coverage). C2-6 perf, C2-7/C2-8 cosmetic.

---

## Files reviewed
- `NetworkMonitor/Services/TrafficCollector.cs`
- `NetworkMonitor/Services/TrafficTracker.cs`
- `NetworkMonitor/Services/TrafficWindow.cs`
- `NetworkMonitor/Services/TrafficRateFormatter.cs`
- `NetworkMonitor/Services/AppTrafficTotal.cs`

## User findings (raw)
In TrafficRateFormatter use switch statements

## User findings (reconciled)

### U2-2 [CLEANUP/ACTION] Use switch statements in TrafficRateFormatter
`TrafficRateFormatter.cs` — refactor the rate-formatting if/else chains into `switch` (likely switch expression vs. classic switch — confirm at fix time, given single-exit + braces conventions). Relates to C2-7 (unit-base decision) — fold both into the same edit.
Status: **FIXED 2026-06-26 (traffic residuals)** — both `BitsPerSecond`/`BytesPerSecond` now assign a `switch` expression (relational patterns on the binary thresholds) to `result`, then return it (single-exit + stand-alone return preserved). Tests unchanged, 63/63 pass.