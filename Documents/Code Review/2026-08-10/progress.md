# Code Review — 2026-08-10 (ledger)

**Read this file first on every resume.** Procedure: `../code-review-procedure.md`.

---

## Status: CLOSED — 47 of 49 fixed; C2-2 and C2-5 await a second monitor

All five chunks reviewed and co-reviewed. **49 live findings — 47 fixed, 2 open.** Ten batches complete. C4-6 is `partially fixed` by explicit decision (batch 8). The two remaining (C2-2, C2-5) are code-complete and awaiting hardware verification, not unstarted.

**Count moved 50 → 49 on 2026-08-12: C2-7 was withdrawn.** The manual test run showed the finding was wrong and the spec it "corrected" was right — see *Manual test run — 2026-08-12*. That run also raised two new defects (U-1, U-2), both fixed in the same batch, and reopened C3-2, whose batch-4b fix was real but incomplete. New findings raised by a test run are counted separately from the original 50 rather than folded into it.

Co-review ran 2026-08-11, one chunk at a time. **Every finding in all five chunks was confirmed for fixing.** None rejected, none deferred, none marked `won't-fix`, no `U`-IDs added. That includes the findings each report flagged as candidates to close — C1-4, C1-9, C1-10 (not currently reachable) and C4-8 (does not breach the documented rule) — which the user chose to fix anyway.

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
| 1 | Window lifetime, state & threading | complete · **co-reviewed** · all 10 confirmed | 10 (1 BUG · 4 RISK · 5 CLEANUP) | 10 — all |
| 2 | DPI, geometry & multi-monitor | complete · **co-reviewed** · all 11 confirmed | 11 (3 BUG · 3 RISK · 5 CLEANUP) | 9 fixed + 2 pending hardware (C2-2, C2-5) |
| 3 | Traffic data pipeline & concurrency | complete · **co-reviewed** · all 11 confirmed | 11 (2 BUG · 1 RISK · 8 CLEANUP/PERF) | 11 — all |
| 4 | Conventions, DB & project hygiene | complete · **co-reviewed** · all 9 confirmed | 9 (0 BUG · 2 RISK · 7 CLEANUP) | 8 fixed + C4-6 partial |
| 5 | Tests & incidentally-touched code | complete · **co-reviewed** · all 9 confirmed | 9 (2 BUG · 5 RISK · 1 PERF · 1 CLEANUP) | 9 — all |

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

## Fix phase — batch 2: migration baseline (C4-1)

**2026-08-11. C4-1 `fixed`. Build x64 clean, 0 warnings. 308/308 tests pass. `Tools/MigrationVerify` — 37/37 checks pass.**

The repo had no `Migrations/` folder at all and `App.xaml.cs` called `EnsureCreatedAsync`, which is a no-op against an existing file. The first developer to add a column would have got a working dev machine and `SqliteException: no such column` on every v0.0.8–v0.0.11 install in the field.

- **`NetworkMonitor.Services/Data/Migrations/`** — `20260811150332_InitialCreate` plus its designer and `AppDbContextModelSnapshot`, generated from the current model. Generated code, left as EF emits it; it does not follow the house conventions and should not be hand-edited, because regenerating would discard the edits.
- **`NetworkMonitor.Services/Data/DatabaseInitializer.cs`** — the baseline. If the database has application tables (probed via `Devices` in `sqlite_master`) but no `__EFMigrationsHistory`, it creates that table and inserts the first migration id as **already applied**, so `MigrateAsync` skips it rather than replaying `CREATE TABLE` onto populated tables. Then `MigrateAsync`, then the WAL pragma that used to sit in `App.xaml.cs`. The migration id is read from `db.Database.GetMigrations()` rather than hardcoded.
- **`NetworkMonitor.Services/Data/AppDbContextDesignTimeFactory.cs`** — `AppDbContext` has no parameterless constructor, so EF needs this to build the model at design time.
- **`NetworkMonitor/App.xaml.cs`** — `EnsureCreatedAsync` + the raw WAL pragma replaced by one `DatabaseInitializer.InitializeAsync(db)` call. There is now no `EnsureCreated` anywhere in the codebase.
- **`Tools/MigrationVerify/`** — new, registered in `NetworkMonitor.slnx` as a folder of files (not a `<Project>`), so `dotnet build NetworkMonitor.slnx` stays clean. Pins `SQLitePCLRaw.bundle_e_sqlite3 3.0.3` as the app does.
- **`CLAUDE.md`** — the Database section rewritten: the "still needs converting to `MigrateAsync()`" note is replaced by how the baseline works and an instruction never to reintroduce `EnsureCreated`; how to generate a migration; and the requirement to run `MigrationVerify` after adding one. `/Tools/` list and Key Files table updated.

**Why migrations are generated through a tool project.** `NetworkMonitor.Services` is `net10.0-windows` with `UseWinUI`, and the EF design host cannot load it — it dies resolving `runtimepack.Microsoft.Windows.SDK.NET.Ref`. Restoring with an explicit RID and building self-contained both failed the same way. The DB layer is platform-neutral, so `MigrationVerify` compiles those files into a plain `net10.0` host via `<Compile Include>` links. No source is duplicated.

**What was actually verified**, not assumed — four scenarios, 37 checks:

1. A pre-migration database created by `EnsureCreated`, seeded with a device row: history table created, `InitialCreate` recorded as applied, nothing pending, the row survives with its content intact.
2. A fresh install with no file: tables created, migration recorded, an insert works against the result.
3. A second launch against an already-baselined database: still exactly one applied migration, data untouched.
4. **Schema equivalence** — one database built with `EnsureCreated`, one with `MigrateAsync`, `sqlite_master` compared object by object. All 21 application tables and indexes match byte-for-byte. This is the check the whole baseline rests on: if `InitialCreate` did not reproduce what `EnsureCreated` produced, the mismatch would be silent.

**One real difference found by check 4.** `MigrateAsync` creates `__EFMigrationsLock`, which `EnsureCreated` never did, so user databases gain that table on first launch after this ships. It is an EF-internal migration concurrency lock holding no application data. Excluded from the comparison, recorded here because it is a visible change to a user's file.

**DB impact: this batch is the DB change.** No entity, property or index was altered — `InitialCreate` is generated from the model exactly as it stands, so the schema is unchanged. What changes is that the database gains `__EFMigrationsHistory` (with one row) and `__EFMigrationsLock`. No user needs to delete anything, and no history is lost.

## Fix phase — batch 3: silent-failure paths (C5-1, C4-2, C1-7, C3-9)

**2026-08-11. All four `fixed`. Build x64 clean, 0 warnings. 309/309 tests pass** (308 plus one new).

The theme: four places that fail without saying so, where the absence of a log line is indistinguishable from working.

- **C5-1 — `ReleaseInfoParser` / `UpdateChecker` / `UpdateCheckerTests`.** Took the harder of the two fixes the report offered. `TryParseVersionTag` gained a three-argument overload reporting *why* it failed — malformed JSON, valid JSON with no `tag_name`, a tag that will not version-compare, or an empty body — with the two-argument form kept as a wrapper. `Evaluate` logs that through `_logError` on the branch it previously took silently, so a corrupt payload, an HTML error page or a rate-limit body now leaves evidence. The test that was named `AnUnreadableResponseIsStillLoggedAsAnError` and asserted nothing about logging now asserts it; a second test covers the valid-JSON-no-tag case.
- **C4-2 — `DatabaseCheckpoint`.** `ExecuteNonQuery` replaced by `ExecuteReader`, so the `(busy, log, checkpointed)` row `PRAGMA wal_checkpoint(TRUNCATE)` returns is actually read; `busy != 0` is logged with all three figures. Connection string built with `SqliteConnectionStringBuilder` and `Mode = ReadWrite` rather than the `Data Source=` default of `ReadWriteCreate`, so a wrong `DbPath` fails loudly instead of silently creating an empty database. `ClearAllPools()` added after close, matching `RetentionProbe`.
- **C1-7 — `TaskbarTopmostGuard`.** Logs when `SetWinEventHook` returns a null handle. A failed hook leaves the guard inert forever, and the symptom is exactly the buried-strip bug the class exists to prevent.
- **C3-9 — `LiveTrafficFeed`.** A `_stopping` `CancellationTokenSource`, cancelled in `StopAsync`, now feeds both `CreateDbContextAsync` and `CountUnapprovedAsync` in place of `CancellationToken.None`. `OperationCanceledException` and `ObjectDisposedException` are caught quietly, so a scan landing during shutdown stops filing an error for an expected condition.

**One thing worth knowing for future tests.** `Assert.IsType<T>` in xUnit v3 is an exact-type match, and `JsonDocument.Parse` throws the derived `JsonReaderException` — the first version of the C5-1 test failed on that. `Assert.IsAssignableFrom<JsonException>` is the correct assertion.

**DB impact: none.** `DatabaseCheckpoint` runs a `PRAGMA`, not DDL; the connection-mode change affects how the file is opened, not its schema. No entity, column or index touched, so no migration.

## Fix phase — batch 4a: bucket spreading & time discontinuities (C4-3, C3-4, C5-2, C3-1, C3-3)

**2026-08-11. Five `fixed`. Build x64 clean, 0 warnings. 319/319 tests pass** (309 plus ten new).

Batch 4 was split. This half is the cluster that all lives in one code path — how a flush's bytes are spread across chart buckets, and what happens when time does not move forwards. **C4-3 was pulled forward from batch 7** so that path moved into Core *before* its defects were fixed, rather than being fixed twice in the UI project and then moved.

- **C4-3** — new `NetworkMonitor.Core/Traffic/ChartPointSpreader.cs`. The ~25 lines duplicated byte-for-byte in `InternetViewModel` and `LocalViewModel` now exist once, in Core, where the test project can reach them. Both `SpreadAcrossBuckets` methods are one delegating line.
- **C3-4** — `LiveRateBuffer.AddInterval` scales the byte totals by `retainedSeconds / intervalSeconds` when the interval start clamps to the oldest held second, **and** passes the clamped `effectiveStartUtc` to `Distribute`. Both halves were needed: scaling alone leaves `totalOverlap` short of the interval and re-inflates the result.
- **C5-2** — `_lastFlushUtc` reset in `SeedWindowState` in both view models, so a flush arriving after a page revisit no longer claims a minutes-long interval and smears itself across every bucket as a phantom floor.
- **C3-1** — `LiveRateBuffer.Advance` treats a backward step as a discontinuity, and `LiveTrafficFeed.OnFlushed` drops a flush whose `nowUtc` precedes `_lastFlushUtc`.
- **C3-3** — fixed incidentally: the `_lastFlushUtc` read and write moved inside `lock (_gate)` as part of the C3-1 guard.

**One deliberate narrowing of a proposed fix.** The report proposed an unguarded `else if (epoch < _lastEpoch)` branch in `Advance` that clears and re-seeds. Implemented literally, that reset the whole trace for *any* older sample and broke `LiveRateBufferTests.SamplesOlderThanTheWindowAreDropped` — long-standing, deliberate behaviour where an out-of-order arrival is dropped rather than costing five minutes of history. The branch is therefore guarded to a **sub-window** step (`epoch > _lastEpoch - _capacity`), which is exactly the case C3-1's own analysis identifies as the corrupting one; it says in terms that large steps are already safe because `IsHeld` drops them. The existing test still passes unchanged, and a new test pins the large-step behaviour so the distinction is not lost again.

**Ten new tests.** `ChartPointSpreaderTests` (5) covers the `bucketSeconds != 1.0` path production actually takes — total preservation across 60-second buckets, which buckets an interval touches, an even split across two wide buckets, accumulation onto existing values, and an empty window. `LiveRateBufferTimeDiscontinuityTests` (5) covers the sub-window backward step, the right edge following the clock back, the large step still being dropped, a gap longer than the window keeping only the visible share, and an ordinary in-window interval still keeping every byte.

**DB impact: none.** Pure in-memory chart arithmetic and view-model state. No entity, column or index touched, so no migration.

## Fix phase — batch 4b: always-on cost & live-chart polish (C3-2, C3-5, C3-8, C3-10, C5-8)

**2026-08-11. Five `fixed`. Build x64 clean, 0 warnings. 319/319 tests pass, no test changed.**

- **C3-2** — new `SmoothScrolling` dependency property on `MiniTrafficSection` forwarding to `SectionChart`; `MiniGraphWindow`'s constructor sets it on both sections from `_settings.ChartSmoothScrolling`. The chart that runs all day can now be turned down like the two that don't. Per co-review, the snapshot decimation in the same fix note is a separate optimisation and was not required to close the finding.
- **C3-5** — fixed in `TrafficAreaChart.BuildPoints`, **not** in `Snapshot`. See below.
- **C3-8** — `MiniGraphViewModel.Refresh` reads `UnapprovedDeviceCount` once into a local, so the text and the warning flag cannot disagree.
- **C3-10** — `LiveTrafficFeed.StartAsync` wraps the seed in `Task.Run`. Still awaited: the feed must be seeded before the events are subscribed, and dropping the await would trade a race for a few milliseconds.
- **C5-8** — new `Settings.DigestCatchUpHighWaterUtc`, recording how far catch-up has *looked* rather than how far it has *generated*.

**C3-5 was implemented in the other location the finding cites, and that was the right call.** Truncating `Snapshot` to end at the last complete second — the fix as first proposed — broke **six** existing `LiveRateBufferTests`. All six encode one contract: what is written at time T is visible when snapshotting at T. Six tests agreeing is a specification, not an obstacle, and C3-5 is a CLEANUP whose own stated impact is "under 2% of the chart width". Changing what every consumer of the buffer reads to fix a rendering artefact is the wrong trade. `BuildPoints` now extends the lead point from `values[count - 2]`, the last complete bucket. Same defect, fixed where it manifests, no test rewritten to accommodate the fix.

That is now twice in one batch where a proposed fix needed adjusting on contact (C3-1's narrowing in 4a, C3-5's relocation here). Worth carrying into batches 5–9: the findings are sound about *what* is wrong and less reliable about *where* to change it.

**DB impact: none — and deliberately so.** C5-8's high-water mark went into `settings.json`, not a new column. It is process bookkeeping rather than user data, safe to lose, and putting it in the DB would have meant a migration for something that does not warrant one. `Settings` is a `System.Text.Json` POCO and never a `DbSet`; an older file without the key deserialises to `null` and falls back to the previous behaviour. No entity, column or index touched.

## Fix phase — batch 5: widget lifetime (C1-1, C1-2, C1-4, C5-4, C5-5, C1-9, C1-10)

**2026-08-11. Seven `fixed`. Build x64 clean, 0 warnings. 319/319 tests pass.**

The destroy path cleaned up what was subscribed by hand, but not what the XAML compiler wired, not what was already sitting in the dispatcher queue, and not the case where construction itself fails.

- **C1-1** — `Bindings.StopTracking()` added to `Teardown()`. For a `Window`-rooted `x:Bind` the compiler emits no `Unloaded`/`StopTracking` wiring at all, unlike a `Page`, so the generated tracking object kept a strong handler on a singleton view model that outlives every window instance.
- **C1-2** — `OnStateChangedOnUiThread`, `ApplySpeedTestText`, `ApplyUnknownDevicesBrush` and `OnSavePlacementTimerTick` are guarded on `_teardownStarted`. Teardown unsubscribes the sources but cannot recall work already queued, and the two-hop enqueue (`OnFeedUpdated` → `Refresh` → `PropertyChanged` → one of these) makes an Alt+F4 landing between turns reachable.
- **C1-4** — the `MiniGraphState.Changed` subscription in `App` marshals through the main window's `DispatcherQueue`. This was the only handler that did not marshal and the only one that constructs a `Window`. `GetRequiredService<MiniGraphState>()` moved inside the `try`.
- **C5-4** — `_miniGraphVisible` is assigned only after the show or hide succeeds, so a window that throws during construction no longer leaves the flag reading "visible" with nothing behind it and every later toggle comparing equal.
- **C5-5** — `DigestReportView` gains an `Unloaded` handler stopping the timer and unsubscribing `Tick`, `Loaded`, `SizeChanged` and itself.
- **C1-9** — both `OnPageLoaded` handlers `-=` before `+=`.
- **C1-10** — the constructor subscription stays (both types are singletons with identical lifetimes, so there is nothing to leak), but `OnStateChanged` no longer refreshes unless `_attached`. Every opacity step previously rebuilt two 300-point snapshots with the widget closed.

**A convention note worth carrying forward.** The guards were first written as early `return` statements, which breaks CLAUDE.md's single-exit rule. They are wrapping `if (!_teardownStarted) { … }` blocks instead. Batch 9 is the conventions sweep; introducing violations in batch 5 and cleaning them up in batch 9 would be self-inflicted work.

**Not verifiable by the test suite.** Every finding here is in the app project. The 319 green tests confirm nothing broke; they say nothing about whether the teardown path is now correct. The Alt+F4-then-reopen loop in *Manual verification* is what actually exercises C1-1, C1-2 and C5-4.

**DB impact: none.** UI lifetime and event wiring only. No entity, column or index touched, so no migration.

## Fix phase — batch 6: geometry (C2-1 … C2-6, C2-8, C2-9, C2-10, C1-5, C1-6)

**2026-08-11. Nine `fixed`, two code-complete but deliberately left `open`. Build x64 clean, 0 warnings. 319/319 tests pass.**

Cross-cutting theme 2 of this review — "geometry is derived from three different heights" — turned out to be one root cause with several faces: nothing said which box it meant, the window box or the visible content. That is now stated once.

- **New `NetworkMonitor.Core/Widget/FrameInsets.cs`** and one `MeasureFrameInsets(scale)` on the window. The insets are measured from DWM, rescaled to the scale being reasoned about, and fall back to the nominal 7 DIP when the query fails **or returns a degenerate frame** — the `visible == outer` case C2-5 identifies, which passed the old non-negative test while silently reinstating the very overhang the expansion exists to remove.
- **C2-1** — `RestorePlacement` re-reads `GetCurrentScale()` after `MoveAndResize` and re-applies the size at the live scale if it differs from the `GetScaleForPoint` value used to size. Restore and save now agree on which monitor decides. New `ComputeRestoreSize` is the single sizing rule.
- **C2-3 / C1-6** — `DerivedStripWidth` subtracts the frame from the window height, so it derives the font scale from the same panel height the layout uses.
- **C2-6** — new `DerivedStripWindowWidth(scale)` adds the horizontal frame when converting the content metric to a window width.
- **C2-4** — `ClampStripSize` uses `MoveAndResize` holding the bottom and right edges instead of origin-anchored `Resize`.
- **C1-5** — `ClampMinimumSize`, `SectionsPanelSizeChanged` and `ComputeShowPeak` read `_appliedOrientation`.
- **C2-8** — dragging requires `PointerDeviceType.Mouse`. **C2-9** — the grab offset rescales mid-drag. **C2-10** — `ShowWidget` re-clamps when the orientation has not changed.

**C2-2 and C2-5 are code-complete and still `open`, on purpose.** Both turn on WinUI's own `WM_DPICHANGED` behaviour, and the DPI *transition* path has still never been walked on real hardware — `8023ffa` was verified on a primary 4K at 200%, where no transition occurs. The C2-1 reconciliation makes the outcome deterministic either way, which is what both findings asked for, but a clean build is not evidence about a code path nobody has executed. They close after the mixed-DPI walkthrough in *Manual verification*, not before.

**C2-11 was not pulled forward, unlike C4-3 in batch 4a.** The comparison was considered and rejected: C4-3 was a verbatim duplicate, so moving it was cheap and made the subsequent fixes testable for free. The placement path is not that — it is entangled with `AppWindow`, `DisplayArea` and DWM interop, so extracting a pure `PlacementMath` is a design task rather than a move, and doing it *while* changing nine behaviours would have made both harder to review. It stays in batch 7, where the arithmetic these fixes introduced (`ComputeRestoreSize`, the inset conversions, the clamp) can be extracted and pinned with the fixes already settled.

**DB impact: none.** Window geometry and pointer handling only. No entity, column or index touched, so no migration.

## Fix phase — batch 7: testability & layering (C2-11, C5-3)

**2026-08-11. Two `fixed`. Build x64 clean, 0 warnings. 347/347 tests pass — 28 new.**

Cross-cutting theme 6 — "what makes the widget *correct* was placed where it cannot be tested" — closed. This is the batch that pays back the previous six: everything fixed in batches 4a and 6 was fixed in code no test could reach, and the only verification available was a manual walkthrough.

- **C2-11** — new `NetworkMonitor.Core/Widget/PlacementMath.cs` and `PlacementRect.cs`, joining `FrameInsets.cs` from batch 6. `MiniGraphWindow` calls into them for the DIP→window size conversion, the panel-height inverse, the inset expansion, the clamp, the bottom-right resize anchor and the scale-reconcile test. The extraction is **wired**, not parallel code sitting beside the original.
- **C5-3** — `WidgetTrafficTotals.Wan/Lan` now hold the two filters that keep the widget's numbers in step with the Internet and Local tabs; `SectionVisibility.CountVisible/CanApply` hold the last-section invariant. `LiveTrafficFeed.OnFlushed` and `MiniGraphState` call into them.

**`PlacementRect` exists for a specific reason.** `Windows.Graphics.RectInt32` is a Windows SDK projection and is unavailable to a `net10.0` library — which is exactly why this arithmetic was stranded in the app project in the first place. A plain record struct at the boundary is what makes the layering rule satisfiable here.

**One type moved.** `LocalTrafficDelta` went from `NetworkMonitor.Services.Traffic` to `NetworkMonitor.Models.Traffic`: Core cannot reference Services, and per CLAUDE.md a plain data record belongs in Models regardless. All three existing consumers already imported `Models.Traffic`, so no using directives changed.

**28 new tests**, the largest single addition of the fix phase. `PlacementMathTests` (12) pins C2-1, C2-3, C2-4 and C2-6 — the two resize-anchor cases are written as concrete geometry (a top-edge over-drag keeping its bottom edge at 1100 rather than rising to 1020; a left-edge drag ending where it started), so a regression reads as a wrong number rather than a vague failure. `WidgetTrafficTotalsTests` (5) and `SectionVisibilityTests` (11) cover C5-3.

**What this does *not* close.** The C2-2 / C2-5 hardware verification is unaffected — `PlacementMath` is pure arithmetic and says nothing about how WinUI responds to a real DPI transition. The remaining coverage-gap items from `chunk-5` (`FlushSpread` negative totals, `MiniGraphFormatter` sub-0.05 rates, `AxisScale` sub-unit values, `Watchdog` simultaneous cancel/timeout) are conventions-adjacent and go with batch 9.

**DB impact: none.** Code movement between projects plus new tests. No entity, column or index touched, so no migration.

## Fix phase — batch 8: public-repo hygiene (C5-6, C5-7, C5-9 part, C4-6 part)

**2026-08-11. Two `fixed`, two `partially fixed`. Build x64 clean, 0 warnings. 347/347 tests pass. Guard smoke-tested by hand.**

`Tools/RetentionProbe` is a tool that **deletes rows**, in a public repo, whose output is meant to be pasted into issues. Both of those facts had a hole in them.

- **C5-6** — the report header prints `Path.GetFileName(dbPath)`. The full path of a copy under a user profile contains their Windows username, and it was the one piece of identifying data on stdout. The `No such file:` message still prints the full path, deliberately: it fires before any report exists, produces nothing anyone would paste, and showing the resolved path is the entire point of that message.
- **C5-7** — the delete guard now canonicalises both paths through a new `ResolveDirectory` (`GetFullPath` → `DirectoryInfo.ResolveLinkTarget(true)` → `TrimEndingDirectorySeparator`) and compares whole segments via `IsSameOrBeneath`. A junction, symbolic link, UNC path or `subst` drive pointing at the live folder can no longer slip past. A path that cannot be resolved is left as `GetFullPath` returned it and therefore fails to match — erring towards allowing the run, with the file-header warning as the backstop, exactly as before.
- **C5-9's `int.Parse`** — `int.TryParse` with explicit `NumberStyles.Integer` and invariant culture; a typo now prints `Not a number: <arg>` and the usage line instead of an unhandled `FormatException`.
- **C4-6** — `CompareMinutes`' early `return` is gone (its tail moved to a new `ReportMinuteComparison`, so both methods have a single exit) and the implicit `new[]` is now `new int[]`.

**Verified by running it, not by reading it.** Three cases: the live folder is refused and prints only the file name; `UmnathaNetworkMonitorBackup` now gets *past* the guard, which the old bare `StartsWith` wrongly refused; a bad number prints usage rather than a stack trace.

**C4-6 is deliberately not complete, and this is flagged rather than buried.** The four top-level `return 1` guards remain. `Program.cs` uses top-level statements, which have no method body to give a single exit — satisfying the rule literally means wrapping ~250 lines of program in a function purely to move where the returns are. CLAUDE.md's single-exit rule is written about methods, and every method in the file now obeys it. The user's call; the wrapper is a mechanical change if wanted.

**DB impact: none.** A diagnostic tool that is not part of the shipped app, plus its argument handling. No entity, column or index touched, so no migration.

## Fix phase — batch 9: conventions & docs (C1-8, C2-7, C3-6, C3-7, C3-11, C4-4, C4-5, C4-7, C4-8, C4-9, C5-9 remainder)

**2026-08-11. Eleven `fixed`, closing every remaining finding. Build x64 clean, 0 warnings. 363/363 tests pass — 16 new.**

- **C4-9** — `CLAUDE.md:88` named `DevicesPage.xaml` as the canonical XAML reference and no such file exists. Now `AllDevicesPage.xaml`, with `MiniGraphWindow.xaml` named as the example to copy for attribute order. Every agent and reviewer following that instruction had been pointed at nothing.
- **C2-7** — the spec's claim that the 34 DIP peak threshold is unreachable is corrected in both places it appears, marked as a correction rather than silently rewritten. `ComputeShowPeak` is fed the panel height (~32 at the 40 DIP minimum), so the peak **is** dropped there and always has been.
- **C3-6** — the two negative-total behaviours now agree: `Distribute` returns zeros, `Accumulate` drops a negative per counter. Reasoning recorded at both sites.
- **C3-11** — `LiveRateBuffer` carries a `NOT THREAD-SAFE, deliberately` note naming `LiveTrafficFeed._gate` as the lock every caller must hold.
- **C1-8, C4-4, C4-5, C4-7, C4-8** — all eight backing fields seeded; blank lines around object initializers and before `DrawCompactAxis`'s closing brace; `OnSettingChanged` moved below the public methods (correcting the pre-existing break, not only this range's part); `x:Name` leads both `TrafficHostPage` elements.
- **C5-9 remainder** — `_manualResult` is cleared by `OnCheckCompleted` once the matching broadcast has been recognised, with a comment saying it is a two-turn handoff rather than state.

**C3-7 was closed by deciding against the implied change.** Easing `PeakText` to match the drawn curve would have removed the number-versus-picture mismatch by making the number wrong — understating the real peak for as long as the trace is rising, which is exactly when it matters. The label reports what was measured; the easing is presentation and converges within `EaseTimeConstantSeconds`. Recorded on `UpdatePeakLabels` so it is not re-litigated as an oversight.

**A test I wrote was wrong, and the code was right.** Two new `AxisScale` cases asserted that degenerate input must produce a positive axis. `NiceMax` returns `0.0` by design and `TrafficAreaChart` applies the floor at the call site via `safeMax`. The tests were corrected to pin the real contract rather than `AxisScale` being changed to satisfy an assertion invented five minutes earlier — the same discipline applied to the six `LiveRateBufferTests` in batch 4b.

**16 new tests**, closing the coverage gaps this review found reachable: `FlushSpreadEdgeCaseTests` (negative total, empty bucket list, non-positive `bucketSeconds`, an interval entirely outside the buckets, a zero total) and `AxisScaleEdgeCaseTests` (sub-unit values, the exact decade boundary, and degenerate input).

**One coverage-gap item is deliberately left, and is a real if small finding.** `MiniGraphFormatter.Scaled` uses `"0.#"` below ten, so a rate under 0.05 renders as `"0"` — precisely the failure its own comment says it prevents ("a slow link reads as zero"). Fixing it changes displayed text in a width-constrained widget, which is a product decision rather than a cleanup. **Flagged for the user; not silently changed.**

**DB impact: none.** Documentation, comments, formatting, member order and tests. No entity, column or index touched, so no migration.

---

## Completion

**2026-08-11. REVIEW CLOSED. 48 of 50 findings `fixed`; C2-2 and C2-5 are code-complete and await the hardware walkthrough in `manual-test-plan.md`.**

The review is closed in the sense that the fix phase is finished and nothing is waiting on code. It is **not** claiming those two are verified. See *Manual test plan* below.

Nine batches, nine commits, `486d820` through this one. Tests **307 → 363**; build x64 clean and 0 warnings throughout.

**Still open, and only these:**

- **C2-2** and **C2-5** — the code fix landed in batch 6 and makes the outcome deterministic either way, but the DPI *transition* path has never been executed on real hardware. A green build is not evidence about code nobody has run. See *Manual verification* below.

**Partially fixed, by explicit decision:**

- **C4-6** — the four top-level `return 1` guards in `RetentionProbe/Program.cs` remain. Top-level statements have no method body to give a single exit; satisfying the rule literally means wrapping ~250 lines in a function to move where the returns are. Every *method* in the file now obeys it.

**Raised during the fix phase, not part of the original 50 — and since fixed:**

- `MiniGraphFormatter.Scaled` rendered a rate below 0.05 as `"0"`, contradicting its own comment ("a slow link reads as zero" is the failure the rule exists to prevent). Flagged to the user rather than changed silently, because it alters displayed text in a width-constrained widget; **the user chose to fix it on 2026-08-11.** Anything that would round to zero while still carrying traffic now reads `<0.1`. A genuinely idle link still reads `0` — the distinction being made is slow versus dead, and using `<0.1` for nothing at all would be a different lie. Five tests in `MiniGraphFormatterScaleTests`.

**What the fix phase changed about the codebase, beyond the findings:**

- A migration baseline now exists (`InitialCreate`), `EnsureCreated` is gone, and `Tools/MigrationVerify` proves a schema change ships safely to databases already in the field. CLAUDE.md documents both.
- `NetworkMonitor.Core/Widget/` gained `PlacementMath`, `PlacementRect`, `FrameInsets` and `SectionVisibility`; `Core/Traffic/` gained `ChartPointSpreader` and `WidgetTrafficTotals`. The widget's geometry and its correctness rules are now testable, which they were not when this review started.
- `LocalTrafficDelta` and `TrafficTotals` moved to Models, where the layering rule puts them.

## Manual test plan

**`manual-test-plan.md`** in this folder is the deliverable that closes the rest. Written 2026-08-11, checkbox-driven, seven parts:

1. **Mixed-DPI multi-monitor** — the only thing gating the review. Passing 1.1–1.4 closes C2-2 and C2-5 and takes this to 50 of 50.
2. Widget lifetime — the Alt+F4 loop, ten times.
3. Strip geometry — top-edge and left-edge drags, section toggles, the peak at minimum height.
4. Live chart correctness — the five-minute revisit, the backward clock step, the long gap.
5. Speed-test display — the `<0.1` case.
6. **Database and startup** — the highest-consequence part. `MigrationVerify` proves the schema logic against temporary databases; nothing has yet proved it against a real database with real history. Back up first.
7. Diagnostics and update — the `RetentionProbe` guard, the update log.

Parts 2–7 are regression confirmation for work already `fixed`. A failure there is a **new** finding, not a reopened one — recorded as such so this ledger's history stays honest.

The procedure (`../code-review-procedure.md` §7) now requires a plan like this at the end of every review, precisely because a green suite cannot speak for the paths `NetworkMonitor.Tests` cannot reach.

## Manual test run — 2026-08-12, and fix batch 10

**Run on a single monitor, so Part 1 never started.** Everything reachable was worked through. Build x64 clean, 0 warnings, **372 tests pass** (363 → 370 over the intervening commit → 372 here, 2 new).

**Result: three `[F]`s and two new defects, all now resolved. The count moves 50 → 49 findings, because C2-7 was withdrawn.**

### Two new findings, both fixed

- **U-1 `[BUG]` — the horizontal strip grew taller on every orientation switch.** `MiniGraphWindow.SaveCurrentPlacement:315` stored `AppWindow.Size.Height / scale` — the **window** height, invisible frame included — into `MiniGraphStripHeight`. Every reader treats that setting as a **panel** height and adds the frame back onto it: `ComputeRestoreSize:551` via `SizeFromDips`, and `ClampStripSize:936`. So each save/restore round trip fed the frame in twice, and an orientation switch is exactly one save plus one restore — ~7 DIP per switch, climbing until `ClampHeight` pinned it at the 120 DIP ceiling. It was the only place in the file not converting through `PlacementMath.PanelHeightInDips`, which is now what it does. Pinned by `SavingThePanelHeightKeepsTheStripTheSameSizeAcrossRepeatedRoundTrips` — five round trips, same height. **Retested on the real app and confirmed:** the strip now holds its position and height across orientation switches.

  Worth noting against batch 6: C2-1 was the *floating widget* shrinking a step per launch, and its fix was correct. This is the same class of defect on the strip's own save path, which batch 6 did not touch. The tests written then covered the arithmetic; nothing covered the round trip through the setting.

- **U-2 `[BUG]` — `ObjectDisposedException` from `ScanWorker:56` as a fatal dialog on tray Exit.** `ScanWorker` is registered twice (`App.xaml.cs:122-123` — `AddSingleton<ScanWorker>()` plus an `AddHostedService` factory resolving that same singleton), so MS DI tracks the one instance in its disposables list twice and disposes it twice at shutdown. The second pass called `Cancel()` on an already-disposed `CancellationTokenSource`, which throws. It only fired when a network change had armed `_networkChangeCts` during the session, which is why it had not been seen before.

  Seven services share that double-registration shape. `ScanWorker` is the only one whose `Dispose` calls something that throws after disposal — `SemaphoreSlim.Dispose` and `_session?.Dispose()` are repeat-safe — so it is the only one that surfaced. `Dispose` is now guarded by a `_disposed` flag, the same way `TrayIconService` and `TaskbarTopmostGuard` already were.

### One finding withdrawn

- **C2-7 — withdrawn, and its batch-9 "fix" reverted.** The finding claimed the spec was wrong to call the 34 DIP peak threshold unreachable. It was not. `ClampHeight` floors the **panel** height at 40 and every caller clamps the panel before asking; the frame is added on top of the clamped panel, never taken out of it. The finding read 40 as a window height and subtracted a frame that was never there.

  The manual test settled it: *"At the smallest strip height, check the peak figure. Expected: it is not shown"* → still visible. `e2ef93f`'s original wording was right. Spec, `PeakMinimumHeight`'s comment and the chunk-5 coverage note are all back to it, with the correction history kept in the spec rather than silently undone, and two tests now pin the relationship.

  **This is the second claim this review got backwards** (a9a17af corrected two others). The pattern is the same each time: reasoning about which coordinate space a number lives in, without a test to settle it. The tests added here are the answer to that, not the doc edit.

### One finding reopened and completed

- **C3-2 — the fix was real but half-done.** Batch 4b added the `SmoothScrolling` dependency property and set it from `MiniGraphWindow`'s **constructor**, matching `InternetPage` and `LocalPage`. But those two are reconstructed on every navigation and the widget is created once and then hidden and shown, and `Settings` has no change notification — so a toggle could never reach the widget again for the life of the session. Manual test: *"Turn smooth chart scrolling OFF"* → mini graph stayed smooth.

  Fixed in two goes. The first applied it on `ShowWidget()` too, matching the pages' "takes effect when the view is next opened" contract — **and the user rejected it on test**: a widget has no natural reopen, so "hide and show it first" is not the setting working. `Settings.ChartSmoothScrolling` is now a hand-written property raising `ChartSmoothScrollingChanged`, which `MiniGraphWindow` subscribes to in its constructor, marshals through `DispatcherQueue.TryEnqueue`, and unsubscribes in `Teardown` next to the `MiniGraphState` and ViewModel handlers so it cannot become the leak C5-5 was. The toggle now lands on the live widget. It is the only setting with a live watcher, and the reason is recorded on the event.

### One `[F]` that is not a defect

- With the internet disconnected, the widget's Internet and Local lines read 8 b/s and 1 B/s rather than `0`. Those are the **traffic** lines, not the speed line Part 5 is about, and a few b/s of retried DNS, ARP and mDNS with the cable out is real traffic being reported honestly. **User's decision: leave as is, minor.** Recorded so it is not re-raised as a bug.

### Part 7 — complete, all passed

`RetentionProbe` refused the live folder naming only `networkmonitor.db` (no path, no username, exit 1); rejected `abc` with `Not a number: abc` plus the usage line and no stack trace; ran against a copy at exit 0 with no username anywhere in the report. That closes the evidence for C5-6, C5-7 and C4-6's argument handling. The user then ran the two log checks: **Check for updates** works and logs (C5-1), and a normal exit produces no `RefreshUnapprovedCount` error and no silent checkpoint failure (C3-9, C4-2).

**Parts 2 through 7 are now complete and all pass.**

**DB impact: none.** One widget placement conversion, one `Dispose` guard, one settings re-read, plus documentation and tests. No entity, column or index touched, so no migration.

### What is still not tested

**`manual-test-plan.md` Part 8** now collects it in one place: the whole of Part 1 (the second monitor, still the only thing gating the review), the monitor-disconnect and touch-drag checks, the backward clock step, the long-gap spike, the `<0.1` slow-link case, and two retests owed by the fixes above — the tray exit *after a network change*, and turning smooth scrolling off with the widget on screen. Items move back to their own part as they pass, so that list only shrinks. The strip-height retest has already passed and moved.

## Next step

**Batches 1–3 are done.** C4-1 no longer gates schema work — the baseline exists and is verified, so the next entity change can ship a migration normally (generate it through `Tools/MigrationVerify`, then run that tool).

Next is **batch 4 — live-chart correctness & always-on cost**: C3-1 with C5-9's stale-flush item (backward clock step), C5-2 (stale `_lastFlushUtc` across navigation), C3-4 (long-gap compression), C3-5 (live edge reads low), C3-2 (widget ignores `ChartSmoothScrolling`), C3-10 (`SeedAsync` on the UI thread), C5-8 (digest worker re-scans empty windows), C3-8 (torn count read).

**Batch 4 was split.** 4a (done) took the cluster that shares one code path: C4-3 pulled forward from batch 7, then C3-4, C5-2, C3-1 and — incidentally — C3-3.

**The fix phase is complete and the review is closed.** What remains is **`manual-test-plan.md`** in this folder — work through it in order. Part 1 (mixed-DPI multi-monitor) closes C2-2 and C2-5, the only two findings still open, and takes this review to 50 of 50. Parts 2–7 are regression confirmation. Nothing is waiting on code.


For each batch: apply the fixes, build x64 (`dotnet build NetworkMonitor.slnx -p:Platform=x64`) with 0 errors, run `dotnet test` green, state the DB impact explicitly even when it is "none", add a `## Fix phase — <name>` entry here recording what changed and why, then commit and push with a subject line the user has approved.

Baseline to beat: **308 tests green** at `22aadfe` (307 at review time, plus one added since for the digest CSV speed-test columns).

Two standing constraints:

- **Batch 2 (C4-1) is the gate on all future schema work.** Until the baseline migration exists and `App.xaml.cs` calls `MigrateAsync`, no entity change can ship safely to the v0.0.8–v0.0.11 databases in the field.
- **C2-2 and C2-5 stay `open` after their code fix lands**, until the mixed-DPI multi-monitor walkthrough in *Manual verification* is actually performed. A green build does not close them.
