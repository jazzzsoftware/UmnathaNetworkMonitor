# Code Review Procedure

How a full structured code review of NetworkMonitor is run. This is the repeatable process; the 2026-06-23 review (see `2026-06-23/`) is the worked example.

## 1. Scope & depth

A **full** review covers, for every file:

1. **Correctness** — logic bugs, edge cases, off-by-one, null/empty handling.
2. **Concurrency & async** — `async void`, dispatcher marshalling, interlocked/lock-free counters, background-loop cancellation.
3. **Resource lifetime** — `IDisposable`/native handles/DB connections/ETW session, `using` coverage.
4. **Error handling** — empty `catch {}` blocks: deliberate vs swallowing real failures.
5. **Conventions** — CLAUDE.md rules (single exit, blank-line blocks, no `var`, member order, backing-field-above-property, braces, naming).

Plus reuse / simplification / efficiency cleanups.

## 2. Layout (source of truth)

Create a date-stamped folder named for the review **start** date:

```
Documents/Review/<yyyy-MM-dd>/
    progress.md              ← the ledger; the cross-session source of truth
    chunk-1-<name>.md        ← per-chunk findings
    chunk-2-<name>.md
    ...
    manual-test-plan.md      ← written at the end of the fix phase (§7)
```

`progress.md` is read FIRST on every resume. It holds: scope/depth, the review-dimensions list, a chunk table (state + finding counts + actioned counts), cross-cutting themes, suggested fix priorities, and a running `## Log` / `## Fix phase —` section.

After adding/moving/removing any file under `Documents/`, update `NetworkMonitor.slnx` to match before committing.

## 3. Chunk the codebase by risk

Break the code into subsystem chunks, highest-risk first, e.g. the 2026-06-23 split:

1. App lifecycle & infra
2. Traffic capture
3. Data layer
4. Scanning
5. Daily digest
6. Devices & Reports UI
7. Backup

## 4. Review each chunk (record-only)

Review one chunk at a time, **recording findings only — do not fix anything during the review.** Each finding gets:

- An **ID**: `C<chunk>-<n>` for reviewer findings (e.g. `C2-4`), `U<chunk>-<n>` for user findings.
- A **tag**: `[BUG]` / `[RISK]` / `[CLEANUP]` (add `[PERF]` where useful).
- `file:line`, a short rationale, and a proposed fix.
- A **status**: `open` · `fixed` · `deferred` · `won't-fix`.

### Per-chunk report template

Each `chunk-N-<name>.md` ends with a `## Files reviewed` list, immediately followed by a `## User findings` section (a placeholder the user fills in, later reconciled and assigned `U`-IDs).

## 5. Co-review

Go one chunk at a time and **pause after each** for the user to co-review the report and add their own findings. Reconcile user findings, assign `U`-IDs, fold duplicates into existing findings, and capture any **decisions** (e.g. "binary units", "3-day retention") in the ledger. Only after every chunk is co-reviewed does the fix phase begin.

## 6. Fix phase

Batch fixes by theme (resilience, a single cross-cutting cluster like MAC-canonicalization, UI-thread offload, leaks, structural/convention last). For **each batch**:

1. Apply the fixes.
2. Build x64 (`dotnet build -p:Platform=x64`) — 0 errors.
3. `dotnet test` — all green.
4. State whether a **DB delete** is necessary (always say, even if "no"); never create EF migrations unless told.
5. Update `progress.md` with a `## Fix phase — <name>` entry (what changed, where, why).
6. Commit + push (ask the user for the subject line; show the full message first).

Do high-churn structural work (folder/namespace reorgs) and the **convention scan last**, so earlier fixes don't get re-touched.

### Useful techniques

- **Parallel sub-agent audits** for breadth — e.g. the convention scan dispatched one read-only auditor per folder group, each returning `file:line` + violated rule; fixes were then applied by hand and re-verified.
- **Adversarial / second-pass verification** before claiming a finding is real or a fix is done — run the build/tests and read the output, don't assert success blind.

## 7. Manual test plan

**Every review ends with a `manual-test-plan.md` in its dated folder.** Write it once the fix phase is done, before declaring completion — it is the deliverable that turns "the tests pass" into something the user can actually act on.

This exists because most of what a review finds in this app is **unreachable from `NetworkMonitor.Tests`**, which references Models and Core only. Window lifetime, DPI and geometry, XAML, the database against real history — a green suite says the fix phase broke nothing and says nothing about whether those paths are now correct. Do not let a passing build stand in for verification it cannot provide.

Shape:

- **Checkboxes**, one per observable step, so the user can work through it in order and stop and resume.
- **Part 1 is whatever still gates the review** — the findings that cannot close without hardware. Say so plainly, and say what happens to the ledger when it passes.
- Later parts are **regression confirmation** for work already marked `fixed`, grouped by fix batch.
- Each step states its **expected** result, and cites the finding ID it exercises so a failure maps straight back to a report.
- Include any **setup and safety** the step needs — back up the database before schema checks, restore automatic time after a clock-step test, point destructive tools at a copy.
- End with **how to record the outcome**: which findings a pass closes, and that a failure in a regression part is a *new* finding rather than a reopened one, so the history stays honest.

Note the hardware or configuration a part needs (a second monitor at a different scale factor, a touchscreen), so the user knows up front what they cannot do today.

Register the file in `NetworkMonitor.slnx` like any other doc.

## 8. Completion

The review is complete when every finding (reviewer + user) is `fixed`/`deferred`/`won't-fix` and committed, and the manual test plan is written. Record the final state at the top of `progress.md`.

A finding whose code fix has landed but whose behaviour has only been checked by a passing build **is not `fixed`** if the path it changes has never been executed. Leave it `open` with the code-fix noted, and point at the test-plan step that closes it. A ledger that overstates what was verified is worse than one that admits what is outstanding.
