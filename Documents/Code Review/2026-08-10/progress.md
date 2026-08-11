# Code Review — 2026-08-10 (ledger)

**Read this file first on every resume.** Procedure: `../code-review-procedure.md`.

---

## Status: CO-REVIEW COMPLETE — fix phase ready to start

All five chunks reviewed and co-reviewed. **50 findings (50 reviewer + 0 user) — 1 fixed, 49 open.** Fix phase in progress: batch 1 of 9 complete.

Co-review ran 2026-08-11, one chunk at a time. **Every finding in all five chunks was confirmed for fixing.** None rejected, none deferred, none marked `won't-fix`, no `U`-IDs added. That includes the findings each report flagged as candidates to close — C1-4, C1-9, C1-10 (not currently reachable) and C4-8 (does not breach the documented rule) — which the user chose to fix anyway. Nothing has been fixed yet.

**Count corrected at co-review.** This ledger previously said 49. `chunk-4-conventions-db-hygiene.md` contains nine findings (C4-1…C4-9, 2 RISK + 7 CLEANUP) but its header claimed eight, and that undercount was carried into the chunk table and the total. Verified by counting the `## C<n>-<n>` headings in all five files: 10 + 11 + 11 + 9 + 9 = **50**. By tag: 7 BUG · 14 RISK · 3 PERF · 26 CLEANUP.

Baseline state at the time of review: **307 tests green**, clean build.

## Scope

The work done **since the 2026-07-27 review closed** at `c07260c Close the 2026-07-27 review: Core update orchestration, fixed query shapes and co-review.` (2026-07-28), through `b215581 Add the r/homelab launch post and what the first attempt taught us.` (2026-08-10).

**80 commits · 51 code files · +5,222 / −154 lines.**

Substantially one feature in two releases:

- **v0.0.10** — the floating mini graph: a frameless, always-on-top widget carrying the Internet and Local charts, a speed-test line and an unknown-devices line, with hover-to-opaque, a right-click menu and persisted placement.
- **v0.0.11** — a second **horizontal strip** orientation for the same window, designed to sit on the taskbar, with a derived (not dragged) width.

Plus the supporting cast: `LiveTrafficFeed` / `LiveRateBuffer` / `FlushSpread` (the always-on data feed behind the widget), `AxisScale`, `MiniGraphFormatter`, `HorizontalStripMetrics`, `TaskbarTopmostGuard`, `MiniGraphState`, `DatabaseCheckpoint`, tray/toolbar/Settings wiring, and the new `Tools/RetentionProbe/` diagnostic.

**Important framing:** `abf4608 Release v0.0.11.` is *inside* this range. Both releases are already public and auto-updated to users. Every finding here is a **follow-up**, not a merge gate.

Nothing was declared out of scope by the user; the range was reviewed whole.

## Review dimensions

Same five as 2026-06-23 and 2026-07-27:

1. Correctness — logic bugs, edge cases, off-by-one, null/empty handling.
2. Concurrency & async — `async void`, dispatcher marshalling, background-loop cancellation.
3. Resource lifetime — `IDisposable`, native handles, DB connections, `using` coverage.
4. Error handling — empty `catch {}`: deliberate vs swallowing.
5. Conventions — CLAUDE.md rules (single exit, blank-line blocks, no `var`, member order, backing-field-above-property, braces, naming, XAML attribute order).

Plus reuse / simplification / efficiency, and — new for this review, because the feature is a native-interop window — **coordinate spaces and DPI**.

## Chunks

Chunked by review dimension rather than by subsystem, because the feature is one subsystem and the risks in it are of very different kinds. Five read-only auditors ran in parallel, one per chunk.

| # | Chunk | State | Findings | Actioned |
|---|-------|-------|----------|----------|
| 1 | Window lifetime, state & threading | complete · **co-reviewed** · all 10 confirmed | 10 (1 BUG · 4 RISK · 5 CLEANUP) | 1 (C1-3) |
| 2 | DPI, geometry & multi-monitor | complete · **co-reviewed** · all 11 confirmed | 11 (3 BUG · 3 RISK · 5 CLEANUP) | 0 |
| 3 | Traffic data pipeline & concurrency | complete · **co-reviewed** · all 11 confirmed | 11 (2 BUG · 1 RISK · 8 CLEANUP/PERF) | 0 |
| 4 | Conventions, DB & project hygiene | complete · **co-reviewed** · all 9 confirmed | 9 (0 BUG · 2 RISK · 7 CLEANUP) | 0 |
| 5 | Tests & incidentally-touched code | complete · **co-reviewed** · all 9 confirmed | 9 (2 BUG · 5 RISK · 1 PERF · 1 CLEANUP) | 0 |

**50 findings total.** IDs: `C<chunk>-<n>` reviewer, `U<chunk>-<n>` user.

No finding is Critical in the "data loss at rest" sense. The closest to it is **C1-3**, which can leave the app unable to start.

## Database impact — NONE

`NetworkMonitor.Services/Data/AppDbContext.cs` is **byte-identical** between `c07260c` and `b215581`. No new `DbSet`, no new or changed entity property, no changed key or index. The three files added under `NetworkMonitor.Models/` (`Formatting/MiniGraphFormatter.cs`, `Formatting/TrafficRateFormatter.cs`, `Widget/MiniGraphOrientation.cs`) are not entities and are not referenced by any mapped property.

The ~96 new lines in `NetworkMonitor.Services/Data/Settings.cs` (`DevicesOnlineOnly` plus fifteen `MiniGraph*` keys) are **`settings.json` preferences, not schema** — `Settings` is a POCO serialised by `System.Text.Json` and is never a `DbSet`. Old settings files upgrade cleanly: every new key carries a C# property initialiser, so absent keys deserialise to their declared default, and the risky ones are clamped on read rather than trusted (`MiniGraphState.cs:80` clamps opacity 50–100; `HorizontalStripMetrics.ClampHeight` bounds strip height 40–120 on both save and restore).

`DatabaseCheckpoint.cs` runs a `PRAGMA`, not DDL.

**No migration was required for this range and none is missing.** See **C4-1** for the separate, pre-existing migration-baseline debt.

## Cross-cutting themes

1. **The widget's second orientation was bolted onto a window built for the first.** `_appliedOrientation` (what is on screen) and `_state.Orientation` (what is wanted) are both read, by different methods, with no single rule about which. It is correct at the one place that writes settings (C1-5 documents where it is not).
2. **Geometry is derived from three different heights.** Window height, panel height and saved DIP height each feed some part of the strip's font scale and width, and they disagree by the invisible resize frame — producing a strip ~17% wider than its content needs (C2-3, C2-6, C1-5).
3. **Mixed-DPI multi-monitor was never exercised.** The DIP/physical contract is stated and honoured on a single monitor, but restore picks its scale from the monitor under the window's *corner* while save picks the monitor holding the window's *majority*, and nothing reconciles them (C2-1, C2-2, C2-5).
4. **The always-on widget doesn't obey the settings that exist to restrain it.** `ChartSmoothScrolling` is applied by both full-page charts and ignored by the mini one (C3-2) — the only chart that runs 24/7 is the only one that can't be turned down.
5. **Time is assumed to move forward.** `LiveRateBuffer.Advance` ignores a backward epoch entirely, so a sub-window clock step silently stacks new traffic onto stale buckets and freezes the chart's right edge in the future (C3-1).
6. **What makes the widget *correct* was placed where it cannot be tested.** The two filters that keep the widget's numbers in step with the Internet and Local tabs, and the rule that the last section can't be turned off, all live in `Services` (C5-3) — against CLAUDE.md's own "pure logic goes in Core" rule that the rest of this feature followed well.
7. **One long-standing file-write race got much easier to hit.** `AtomicFile` has always used a fixed temp path with no lock, and `ScanWorker` has always written from a background thread; this feature added continuous UI-thread writes on every drag, resize, opacity step and toggle (C1-3).

## Suggested fix batches

Rebuilt at co-review on 2026-08-11. The previous list referenced three IDs that do not exist (`C5-10`, `C5-11`, `C5-12` — chunk 5 stops at C5-9), filed C5-6 and C5-8 under "widget lifetime" when they are the username leak and a digest-worker perf issue, and **omitted C4-1 entirely**. Every one of the 50 findings now appears in exactly one batch.

1. **Startup integrity** — C1-3 alone. The only finding that can leave a user unable to launch the app. Ships first, on its own.
2. **Migration baseline** — C4-1 alone. Pre-existing debt this range did not cause, but it must land **before** the next entity change, never alongside one. Its own commit.
3. **Silent-failure paths** — C5-1 (auto-update logs nothing on a garbage response), C4-2 (WAL checkpoint can't report a busy result), C1-7 (`SetWinEventHook` result unchecked), C3-9 (unbounded, uncancellable refresh).
4. **Live-chart correctness & always-on cost** — C3-1 + C5-9's stale-flush item (backward clock step), C5-2 (stale `_lastFlushUtc` across navigation), C3-4 (long-gap compression), C3-5 (live edge reads low), C3-2 (widget ignores `ChartSmoothScrolling`), C3-10 (`SeedAsync` on the UI thread), C5-8 (digest worker re-scans empty windows), C3-8 (torn count read).
5. **Widget lifetime** — C1-1 (`Bindings.StopTracking`), C1-2 (queued dispatcher callbacks), C1-4 (unmarshalled `Changed` handler), C5-4 (`_miniGraphVisible` set too early), C5-5 (`DigestReportView` never unsubscribes), C1-9, C1-10.
6. **Geometry** — C2-1, C2-2, C2-3, C2-4, C2-5, C2-6, C2-8, C2-9, C2-10, C1-5, C1-6. C2-2 and C2-5 cannot be closed on a green build alone — see *Manual verification*.
7. **Testability & layering** — C2-11 (`PlacementMath` into Core), C5-3 (widget filters + last-section invariant into Core), C4-3 (`SpreadAcrossBuckets` into Core), plus the coverage-gap inventory in `chunk-5-tests-and-peripheral.md`. **Do C4-3 before C3-4**, so that defect is fixed once in a tested place rather than twice in the UI project. The `bucketSeconds != 1.0` gap is the highest-value item here.
8. **Public-repo hygiene** — C5-6 (`RetentionProbe` prints the user's Windows username), C5-7 (delete guard is a bare prefix compare), C5-9's `int.Parse` item, C4-6 (`RetentionProbe` conventions).
9. **Conventions and docs last** — C1-8, C3-3, C3-6, C3-7, C3-11, C2-7, C4-4, C4-5, C4-7, C4-8, C4-9, and C5-9's `_manualResult` item.

## Manual verification that will be needed

Not covered by unit tests; all of it needs the real app on real hardware.

- **A second monitor at a different scale factor.** This is the big one, and it is what C2-1, C2-2 and C2-5 all turn on. Save the widget on a 200% external, restart, confirm the size is unchanged; repeat with the widget straddling the boundary. The v0.0.10 DPI fix (`8023ffa`) was verified on a *primary* 4K at 200%, where no DPI transition occurs — so the transition path has never been walked.
- **Strip resize gestures** — drag the top edge past the 120 DIP ceiling and confirm the strip stays on the taskbar; drag the left edge and confirm the strip does not walk left (C2-4).
- **A backward clock step** — set the clock back 20 seconds with the widget open and watch the trace and the right edge (C3-1).
- **Alt+F4 on the widget, then reopen**, repeatedly, watching for a disconnected-element throw (C1-1, C1-2).
- **Touch or pen drag** of the widget (C2-8).
- **DB delete is NOT required** for anything in this range.

## Log

- **2026-08-10** — Review opened at the user's request; scope agreed as `c07260c..HEAD` (option 1 of three offered). Five read-only auditors dispatched in parallel, one per dimension, each given the range and a dimension-specific brief. All five returned. **49 findings, no Critical.** Two findings raised in the coordinator's own first pass were **withdrawn on verification**: (a) an alleged double-count of the shared boundary second in `LiveRateBuffer.AddInterval` — the intervals tile exactly, because each flush's start *is* the previous flush's end and `Advance` never zeroes below `_lastEpoch + 1`; (b) an alleged member-order violation on `LiveRateBuffer._capacity` — it backs the `Capacity` property, so its position is what CLAUDE.md requires. Recorded here because both were reported to the user before being checked.

- **2026-08-11** — Co-review opened and completed in one session, one chunk at a time. **All five chunks confirmed in full: 50 of 50 findings accepted for fixing**, no user findings added, nothing rejected, deferred or marked `won't-fix`. The user accepted the four findings their own reports offered as candidates to close (C1-4, C1-9, C1-10 as not currently reachable; C4-8 as not breaching the documented rule), and put the chunk-5 coverage-gap inventory in scope as work rather than as a record. Fixes were deliberately **not** applied per chunk, so the cross-chunk batches stay intact.
- **2026-08-11** — Two ledger defects found and corrected while closing co-review, both in this file rather than in the code. (a) **The finding total was wrong**: `chunk-4`'s header claimed 8 findings against the 9 it actually contains, and the undercount propagated here — the true total is **50**, verified by counting `## C<n>-<n>` headings across all five chunk files. (b) **The suggested-fix-batch list was unusable**: it named three IDs that do not exist (`C5-10`…`C5-12`), mis-filed C5-6 and C5-8 under "widget lifetime", and left out **C4-1**, the migration baseline — the most consequential finding in the review. The list was rebuilt from scratch so every one of the 50 findings appears in exactly one batch.

## Fix phase — batch 1: startup integrity (C1-3)

**2026-08-11. C1-3 `fixed`. Build x64 clean, 0 warnings. 308/308 tests pass.**

Three changes, all on the path that could leave a user unable to launch:

- **`NetworkMonitor.Services/Data/AtomicFile.cs`** — the temp file is now `path + "." + Guid.NewGuid().ToString("N") + ".tmp"` instead of the single fixed `path + ".tmp"`, so two concurrent writers can no longer share, truncate or publish each other's temp file. `tempPath` moved above the `try` so the new cleanup can see it: the `catch` now deletes the orphan temp file it may have left behind, which the old fixed name did not need because the next write overwrote it.
- **`NetworkMonitor.Services/Data/Settings.cs`** — `Save()` body wrapped in `lock (_saveLock)` on a new `private static readonly object`, serialising the scan-thread writer (`ScanWorker.cs:238`) against the UI-thread writers that this feature made near-continuous (placement debounce on every drag and resize, plus opacity, section, orientation and border toggles).
- **`NetworkMonitor/App.xaml.cs`** — the startup read is wrapped. `JsonSerializer.Deserialize<Settings>` previously threw `JsonException` straight out of `Host.Build()` in the App constructor on a malformed file, and `?? new Settings()` only ever covered a literal `null` document. A corrupt or truncated `settings.json` now falls through to the same `appsettings.json` seed path the first-run branch uses, including `DetectSubnetBase()`.

**Why the failure is logged late rather than where it happens.** `AppLog.Write` no-ops while `IsEnabled` is false, and `AppLog.Initialize` runs at `OnLaunched`, long after `ConfigureServices`. Logging the corruption at the catch site would have been silently dropped — the exact silent-failure pattern C1-7 and C5-1 record. The exception is parked in `_settingsLoadFailure` and written once initialisation has happened, naming the file path, then cleared.

**One deliberate behaviour change beyond the finding.** A `settings.json` containing the literal document `null` previously produced bare `new Settings()`; it now takes the seed path and gets `SubnetBase` detected, the same as a missing file. Strictly better and effectively unreachable, but it is a change, not a refactor.

**Not covered by tests.** `AtomicFile` and `Settings` live in `NetworkMonitor.Services`, which `NetworkMonitor.Tests` cannot reference (Models + Core only). The 308 green tests prove nothing about this batch — they prove it broke nothing. This is the same structural gap as C5-3 and C2-11; verifying the concurrent-write fix needs the real app.

**DB impact: none.** `settings.json` is a `System.Text.Json` POCO, never a `DbSet`. No entity, column or index touched, so no migration. C4-1 (the missing migration baseline) is untouched and remains the gate on all future schema work — it is batch 2.

## Next step

**Batch 1 is done.** Next is **batch 2 — C4-1, the migration baseline**, on its own. Generate `InitialCreate` from the current model, switch `App.xaml.cs` from `EnsureCreatedAsync` to `MigrateAsync`, and baseline it so the v0.0.8–v0.0.11 databases already in the field (no `__EFMigrationsHistory` table) are treated as already at the initial migration rather than replayed onto. Then batches 3–9 in order.

For each batch: apply the fixes, build x64 (`dotnet build NetworkMonitor.slnx -p:Platform=x64`) with 0 errors, run `dotnet test` green, state the DB impact explicitly even when it is "none", add a `## Fix phase — <name>` entry here recording what changed and why, then commit and push with a subject line the user has approved.

Baseline to beat: **308 tests green** at `22aadfe` (307 at review time, plus one added since for the digest CSV speed-test columns).

Two standing constraints:

- **Batch 2 (C4-1) is the gate on all future schema work.** Until the baseline migration exists and `App.xaml.cs` calls `MigrateAsync`, no entity change can ship safely to the v0.0.8–v0.0.11 databases in the field.
- **C2-2 and C2-5 stay `open` after their code fix lands**, until the mixed-DPI multi-monitor walkthrough in *Manual verification* is actually performed. A green build does not close them.
