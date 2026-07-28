# Code Review — Summary (2026-07-27)

Structured review of the two largest subsystems added since the 2026-06-23 review: **auto-update**
and the **Local traffic tab**. Reviewed 2026-07-27; fixes applied the same day; C1-15 and the
co-review of chunks 2–4 completed 2026-07-28. Procedure: `../code-review-procedure.md`. Ledger
(full detail): `progress.md`. Per-chunk findings: `chunk-N-<name>.md`. Manual walkthrough:
`smoke-checklist.md`.

Branch: `review/2026-07-27-fixes`.

## Outcome

**46 findings** (44 reviewer + `U1-1`, `U4-1`) across 4 risk-ordered chunks. **44 fixed, 2
won't-fix, 0 deferred.** No Critical or data-loss-at-rest bugs; the two closest were both
under-reporting — C2-2 (WAN bytes of a process that exits mid-interval were discarded) and C2-1
(WAN bytes the kernel couldn't attribute were dropped). The test suite grew **218 → 248**; every
fix batch built clean (x64, 0 warnings) with all tests green. **No DB delete required** — no
schema, entity or column changed.

Explicitly out of scope by user decision: the Models/Core/Services split, mDNS enrichment, the
speed-test rewrite, the SI-units change, digest changes, and everything covered by 2026-06-23.

## Findings by chunk

| # | Chunk | Findings | Headline issues |
|---|-------|----------|-----------------|
| 1 | Auto-update | 15 + U1-1 | installer launch bypassed the graceful shutdown (C1-1); an unreadable installed version reported "up to date" forever (C1-2); a check completing before the window existed was lost for 24 h (C1-3); download not cancellable (C1-4) |
| 2 | Local traffic capture & storage | 11 | unattributed-PID WAN bytes dropped (C2-1); exited process lost its whole entry (C2-2); unbounded counter dictionaries keyed on ephemeral ports (C2-3); four commits per flush (C2-4); per-second rows kept 7 days to serve 5 minutes (C2-5) |
| 3 | Local traffic classification & grouping | 7 | every `.255` treated as broadcast, discarding real hosts on /23+ and public unicast (C3-1) |
| 4 | Local traffic UI | 11 + U4-1 | the incremental path never ran on the default range (C4-1); a live refresh could never add a row (C4-2); no re-entrancy guard (C4-3); every page instance rooted for the window's life (C4-4) |

## Cross-cutting themes (and how they were resolved)

1. **The LAN half and the WAN half disagreed with each other.** Every divergence found was accidental: unknown-PID bytes kept on one side and dropped on the other (C2-1), an exited process degrading gracefully on one side and losing its data on the other (C2-2). Both halves now share one `ResolveProcess` and one `WellKnownPids`.
2. **Shutdown paths that bypassed the host.** C1-1 walked around the `StopHost` route added by 2026-06-23's C1-2, losing the pending flush, the WAL checkpoint, the tray icon and the window placement. `MainWindow.ShutdownForUpdate` now runs the same graceful path as a tray Exit before the installer launches.
3. **The live-refresh path didn't do what it was designed to do.** On the default range every tick was a full reload (C4-1); where the incremental path did run it couldn't add rows (C4-2), had no re-entrancy guard (C4-3) and re-read the device table each time (C4-7). The window now shifts bucket-by-bucket on both tabs.
4. **Write volume outran what anything reads.** Per-second per-flow rows retained 7 days to serve a 5-minute window (C2-5), with unbounded in-memory counters behind them (C2-3, C4-9). Raw tables now purge at 1 hour on the flush loop; rollups keep the full history.
5. **Mask-agnostic address tests.** `.255` assumed to be broadcast everywhere (C3-1), silently discarding valid hosts on wider subnets and valid public addresses.

## Notable decisions

- **C1-6** — `won't-fix`: the build isn't code-signed, so an Authenticode check would reject every update instead of protecting one. Revisit when signing lands.
- **C1-13** — the installer relaunches the app **visible** after a silent update.
- **C4-8** — `won't-fix`, **finding corrected on verification**: `TrafficAreaChart.ChartPoints` is a DependencyProperty that only redraws on identity change and never listens to `INotifyCollectionChanged`, so the proposed in-place mutation would have silently frozen the chart. C4-1 reduces how often the rebuild happens instead.
- **C4-11 item 1** — `MinimumSpinnerMs = 500` accepted as deliberate anti-flicker.
- **C3-2 sub-point 1** — the constructor's synchronous `Refresh()` is kept: deferring it would leave the first packets after startup classified against the fixed ranges only.
- **Classification decisions confirmed with the user**: all of RFC1918 counts as LAN (so a VPN-reached private network reports as Local), and CGNAT space counts as Internet (so a Tailscale peer appears on the Internet tab). Both are documented in `Documents/Overview.md`.
- **Retention decisions confirmed with the user**: raw entries 1 hour, rollups the full `TrafficPurgeDays`; an exited process keeps its bytes under its cached name rather than being relabelled.

## New shared components created during the fix phase

`NetworkMonitor.Core/Traffic/WellKnownPids`, `NetworkMonitor.Core/Traffic/RateWindow`,
`NetworkMonitor.Core/Update/UpdateChecker`, `UpdateDownloader`, `UpdateDownloadStream`,
`UpdateCheckOutcome`; `InternetAppTotals` and `AppBuckets` on the Internet side; a per-file
`AddParameter` helper in both traffic view models.

## Process notes

- The user chose to **skip the per-chunk co-review pause** during the review itself; all four chunks were reviewed first and the fixes applied in one pass. The co-review of chunks 2–4 was then run separately on 2026-07-28.
- **U4-1 is worth remembering**: verifying chunk 4 against the code rather than the ledger showed C4-10 and C4-11 marked "fixed" when only part of each had been applied. A fix-phase entry for a multi-part finding needs to say *which* parts, or the next reader inherits a false clean slate.
- C1-15 was closed by moving orchestration into Core rather than by pointing the test project at Services — the layering rule (tests reference Models + Core only) held.

## Outstanding (non-code)

- **The manual walkthrough in `smoke-checklist.md`** — the live window on both tabs, chart bucket drill-down after the SQL rewrite, the update banner including cancel, and the one-off raw-row backlog clear after upgrade. None of it is reachable from unit tests.
