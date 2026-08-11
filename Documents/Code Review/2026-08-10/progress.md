# Code Review — 2026-08-10 (ledger)

**Read this file first on every resume.** Procedure: `../code-review-procedure.md`.

---

## Status: REVIEW RECORDED — fix phase not started

All five chunks reviewed. **49 findings (49 reviewer + 0 user) — 0 fixed, 49 open.**

Co-review with the user has **not** happened yet: no `U`-IDs assigned, no findings confirmed or rejected. Nothing has been fixed.

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
| 1 | Window lifetime, state & threading | complete · **not** co-reviewed | 10 (1 BUG · 4 RISK · 5 CLEANUP) | 0 |
| 2 | DPI, geometry & multi-monitor | complete · **not** co-reviewed | 11 (3 BUG · 3 RISK · 5 CLEANUP) | 0 |
| 3 | Traffic data pipeline & concurrency | complete · **not** co-reviewed | 11 (2 BUG · 1 RISK · 8 CLEANUP/PERF) | 0 |
| 4 | Conventions, DB & project hygiene | complete · **not** co-reviewed | 8 (0 BUG · 2 RISK · 6 CLEANUP) | 0 |
| 5 | Tests & incidentally-touched code | complete · **not** co-reviewed | 9 (2 BUG · 4 RISK · 3 CLEANUP/PERF) | 0 |

**49 findings total.** IDs: `C<chunk>-<n>` reviewer, `U<chunk>-<n>` user.

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

1. **Startup integrity** — C1-3 alone. It is the only finding that can leave a user unable to launch the app.
2. **Silent-failure paths** — C5-1 (auto-update logs nothing on a garbage response), C4-2 (WAL checkpoint can't report a busy result), C1-7.
3. **Always-on cost & live-chart correctness** — C3-2, C3-1, C5-2, C3-4, C3-5.
4. **Widget lifetime** — C1-1, C1-2, C1-4, C5-6, C5-8.
5. **Geometry** — C2-1, C2-2, C2-3, C2-4, C2-5, C2-6, C1-5, C1-6.
6. **Testability & layering** — C5-3, C4-3, C2-11, plus the coverage gaps in `chunk-5-tests-and-peripheral.md`.
7. **Public-repo hygiene** — C5-10, C5-11, C5-12.
8. **Conventions and docs last** — C4-4 … C4-9, C2-7, C3-11, and the remaining CLEANUPs.

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

## Next step

Co-review. Per `../code-review-procedure.md` §5 the user reads each chunk, adds their own findings (assigned `U`-IDs), and confirms or rejects the reviewer findings — **before** any fix phase begins. The 2026-07-27 review skipped the per-chunk pause at the user's request and co-reviewed all four at the end; that option applies here too.
