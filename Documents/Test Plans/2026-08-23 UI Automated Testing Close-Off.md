# UI Automated Testing — Phase Close-Off

**Closed 2026-08-23.** Merged to `master`, branch deleted.
Spec: `Documents/superpowers/specs/2026-08-20-ui-automated-testing-design.md`
Plan: `Documents/superpowers/plans/2026-08-20-ui-automated-testing.md`

One elevated command drives the real app through every page it exposes, against a seeded
throwaway database, and proves the uninstall → install → update cycle.

| | |
|---|---|
| Result | **145 passed, 0 failed, 2 skipped** |
| Duration | 79s (eight phases) |
| Phases | 9 (the ninth is opt-in) |
| Size | 12,410 lines under `Tools/UITests` |
| Commits | 43 |

The result above is a default run. The full nine-phase run has not been re-measured since the
skip work landed on 2026-08-23; its last recorded figure was 146 passed / 0 failed / 7 skipped,
from before those skips became assertions.

---

## Running it

```
dotnet run --project Tools/UITests
dotnet run --project Tools/UITests -- --all-with-update-lifecycle
```

The first drives the eight non-destructive phases. The second adds phase 09, which really does
uninstall the product.

It must run from an **elevated terminal** on an **active local desktop** — it installs and
uninstalls the app, and drives real OS-level input. It also needs a **working internet
connection**, because phase 04 runs a genuine speed test.

Exit `0` passed, `1` a real test failure, `2` preflight refused to start. A self-contained HTML
report lands in `%TEMP%\umnatha-uitests-run\` and opens itself.

---

## The nine phases

The order carries information. Each phase asserts against what the earlier ones left behind,
which is why Purge runs last of the driving phases — it deletes the rows the others check.

1. **Launch** — starts the build and waits for its shell. The only phase that aborts the run.
2. **Devices** — the grids, editing, and CSV export and import through real file dialogs.
3. **Traffic** — chart redraws per range, the app and device lenses, drill-down, bucket pinning.
4. **Speed Test** — seeded history and charts, then a real speed test against the line.
5. **Reports** — digest generation, PDF and CSV export through external handlers.
6. **Settings** — around twenty settings, each changed, verified on disk, then restored.
7. **Mini Graph** — sections, the last-section rule, eleven orientation changes.
8. **Purge** — retention windows narrowed, purges confirmed and cancelled.
9. **Update Lifecycle** — opt-in. Genuinely uninstalls, installs the previous release, drives its
   update banner, and restores the data folder.

---

## What the final day of work found

Ten commits. Two were real defects in the shipped app; the rest were the harness misreporting
itself.

| Finding | What was actually wrong | Kind |
|---|---|---|
| **Purge Now did nothing** | It deleted raw entries older than *days*, while `TrafficTracker` already removed everything older than an hour — so the query could never match a row. It now runs the same rollup sweep `ScanWorker` does. | Product |
| **ETW session orphaned** | The installer relaunches the app after updating; that instance opened a trace session, and the phase killed it without reaching `OnExitApp`, which is what stops one. Preflight then refused the next run. | Product |
| **Evidence photographed late** | Failures were captured when the batch was logged rather than when they failed, so a screenshot showed a screen the failure never happened on. It sent one diagnosis down the wrong path entirely. | Harness |
| **Estimates never measured** | Hand-set phase durations summed to nine times reality, demanding 28.5 minutes of screen-saver headroom to protect a two-minute suite. Recalibrated against four runs; a stock 15-minute saver now clears it. | Harness |
| **Five skips became assertions** | Chart floors now read live from the window instead of a constant, the toast check box is driven by enabling its parent first, and a real speed test runs. | Coverage |

### Purge Now — worth knowing as the app's owner

The button was a no-op for two independent reasons, and the second only became visible after
fixing the first: `ScanWorker` runs the identical retention sweep **immediately at startup** and
then every 24 hours. So even now, Purge Now only does visible work if you narrow the retention
setting and press it before the next daily sweep. Note also that `TrafficPurgeDaysBox` caps at 7
— traffic retention cannot be set beyond a week through the UI, by design.

---

## The pattern underneath

Six separate bugs turned out to be one mistake repeated: **accepting a signal that was available
as proof of the thing that actually mattered.**

- Column headers waited for, instead of the rows.
- The grid element waited for, instead of its contents.
- A stop command's exit code trusted, instead of the session's real state.
- A seeded constant asserted, instead of the live newest value.
- A dialog closing treated as a deletion landing.
- A typed value read back before the control had decided to reject it.

Four of the six produced a *passing* result while wrong, which is why they survived so long. The
sixth was introduced while fixing the fifth.

If a future step behaves oddly, this is the first thing to check: is the thing being waited on the
thing that actually matters, or just the nearest signal that was easy to observe?

---

## Deliberately not covered

Both remaining skips are recorded decisions — reasoning and date sit in the skip text the report
prints, so they read as settled rather than pending.

- **Logging toggle** — `SettingsViewModel.LoggingToggleEnabled` compiles to false in Debug, and
  the suite drives Debug by design. Reaching it would need a whole second pass against a Release
  build, which is a great deal of machinery for one setting.
- **Run at startup** — driving it writes a real logon task pointing at the Debug build. A run
  killed between creating and removing it would leave the machine launching a throwaway binary at
  logon, silently. The suite no longer contains any code that can call `schtasks.exe`.

### Still genuinely uncovered

Documented in the phases that would own them, not tracked as debt:

- **The Local live-rate chip** — needs a live LAN flow above 0.5 Mb/s, which a fixture cannot
  manufacture.
- **Mixed-DPI** — C2-2 and C2-5 still need a second monitor at a different scale factor
  (`manual-test-plan.md` Part 1).
- **The 24-hour digest schedule** — bound to wall-clock time; only its output is reachable.

---

## What can fail a run from outside the fixture

A failing report now lists these itself, because when this suite fails the cause is often the
machine rather than the code.

The screen saver activating mid-run is the worst of them: Windows switches to a separate desktop,
`SendInput` starts throwing `Win32Exception (5): Access is denied`, and every step after that
point fails while everything before it passed. Then — someone using the machine while it drives, a
session that is not an active local desktop, DPI or monitor changes, antivirus, an external file
handler showing a modal prompt, the network during phase 09, machine load, leftovers from a
previous run, the real app already running, and a stale build.

The suite changes none of these settings and contains no code that could. Where something needs
changing it says so and leaves the decision to the operator, including putting it back.

---

## Notes for whoever picks this up next

- `Tools/UITests` is deliberately **outside** the solution build. `dotnet build
  NetworkMonitor.slnx` never reaches it; build it with
  `dotnet build Tools/UITests/UITests.csproj`.
- Phase duration estimates feed the screen-saver headroom check, so keep them honest. They are set
  at roughly three times the worst of four measured runs. If you find yourself being asked to
  lengthen a screen saver to run a two-minute suite, re-measure rather than change the setting.
- Every wait routes through `Waits.Until`. There is one poll interval in the whole suite and one
  place to audit it — do not reintroduce `Thread.Sleep` as a synchronisation device.
- There is no `StepLog.AddRange`, and that is deliberate: collecting steps into a list and adding
  the batch afterwards moves evidence capture away from the moment of failure, which is the defect
  that made a screenshot lie.
