# UI Automated Testing — Design

**Date:** 2026-08-20
**Status:** Design, awaiting review

## Goal

One repeatable command that drives the app through its user interface, exercises everything the UI
can reach, and leaves a verdict behind. The operator types `go`, walks away for a coffee, and comes
back to a report. Target wall-clock is about **15 minutes** unattended, including a real
uninstall/install/update cycle.

The suite is a **release gate**, not a unit-test replacement. It answers "does the shipped build
still work when a person uses it", which the 501 existing unit tests cannot answer at all.

## Why this is needed

Three facts about the codebase as it stands on 2026-08-20:

- `NetworkMonitor.Tests` targets `net10.0` and references Models and Core only. Everything in
  `NetworkMonitor.Services` and in the app project is unreachable from it. Two commits on
  2026-08-20 alone had to carry the note "Not unit-testable". That is **4,401 lines of view model
  across 15 files, 11 pages, and the whole of Services with no automated coverage**.
- Regression checking is manual. `Documents/Code Review/2026-07-27/smoke-checklist.md` is a
  20-minute human walkthrough, and its own header says most of that time is spent waiting.
- The release path — uninstall, install, update banner, update install — has never been tested end
  to end. Smoke items 46 to 48 have been carried as "test these at the next installer build" since
  the 2026-07-27 review.

## Decisions

Settled with the operator on 2026-08-20:

| # | Decision | Choice |
|---|---|---|
| 1 | Structure | Console runner at `Tools/UITests/`, run with `dotnet run` |
| 2 | Driver library | FlaUI 5.0.0 (`FlaUI.Core` + `FlaUI.UIA3`) |
| 3 | Target build | The **installed release** in `C:\Program Files\Umnatha Network Monitor` |
| 4 | Test data | Throwaway data folder seeded with a fixture database |
| 5 | Charts | Renderer publishes a UIA-readable draw summary |
| 6 | Destructive flows | All of them, including a real uninstall/install/update cycle |
| 7 | Update baseline | Latest-minus-one, resolved from GitHub at run time |
| 8 | Elevation | The whole run starts elevated — one UAC prompt, then unattended |
| 9 | Real data safety | Full copy aside before the update phase, restored afterwards |
| 10 | Output | HTML report, plus a screenshot **and** a UIA tree dump per failure |
| 11 | Environment | The operator's own machine, on demand. No CI. |

### Why FlaUI over Appium

Microsoft's testing guidance (updated 2026-07-22) recommends Appium with `appium-windows-driver`.
This design does not follow that recommendation, for three reasons:

- Appium needs Node.js, a globally installed driver and a running server process. That is three
  moving parts to keep alive before a single assertion runs, on a machine whose only other
  requirement is the .NET SDK.
- Underneath, `appium-windows-driver` still drives WinAppDriver, which Microsoft's own page
  describes as "no longer under active development" and which has had no maintenance since 2022.
- FlaUI is a plain NuGet reference (`FlaUI.UIA3` 5.0.0, ~4.2M downloads) targeting
  `net8.0-windows7.0`, which a `net10.0-windows` project consumes directly. It runs in-process as
  the driver, so the runner is one executable with no daemon.

Both sit on the same Windows UI Automation API, so neither can see anything the other can.

### Why `/Tools/` and not a test project

`CLAUDE.md` reserves `/Tools/` for "things you run, not things that ship", registered in the slnx as
folders of files and deliberately **not** as buildable projects, so `dotnet build
NetworkMonitor.slnx` stays clean. `Tools/MigrationVerify` and `Tools/RetentionProbe` set the
precedent.

This suite belongs there rather than in an xUnit project for a specific reason: it is **one ordered
scenario that mutates global machine state**, not a bag of independent tests. It uninstalls the
application. An xUnit project sitting in the solution means a routine `dotnet test` could uninstall
the operator's app as a side effect. Keeping it out of the solution makes that impossible.

## What the suite can and cannot reach

This is the honest boundary. "Tests everything" means everything the user interface exposes, and
that is less than everything the application does.

### Reachable

- Every page, every navigation route, every control that can be clicked, typed into or toggled.
- Every grid's contents, via the DataGrid's UIA Grid and GridItem patterns.
- Every dialog, flyout, toast and info bar.
- Every setting, round-tripped to `settings.json` on disk and read back.
- Chart **data**, via the draw summary described below.
- The full release lifecycle: uninstall, install an older build, see the banner, update, verify.

### Not reachable, and why

| Not covered | Reason |
|---|---|
| Chart pixels — line shape, colours, smoothing | `TrafficAreaChart` renders into a Win2D `CanvasControl` with `IsHitTestVisible="False"`. To UI Automation that is one opaque bitmap with no children. The draw summary proves the data arrived; it cannot prove the drawing is correct. |
| The 24-hour digest schedule | Bound to wall-clock time. The digest *output* is testable from seeded data; the scheduler firing at the configured hour is not, inside 15 minutes. |
| The 6-hour and 1-hour live windows | Same reason. The 5-minute window is partly observable; the longer ones are seeded, not lived. |
| A device genuinely going offline | Requires real hardware to stop responding. Seeded history covers the *display* of gone devices; the detection transition does not. |
| A real speed test | Depends on the operator's internet at the moment of the run. Results are non-deterministic and slow. Seeded results cover the display path. |
| ETW capture of real traffic | `TrafficCollector` opens a kernel session and needs real packets to flow. Seeded traffic covers everything downstream of capture. |

These stay in the manual smoke checklist. The suite must not silently imply otherwise: the report
ends with a **"Not covered by this run"** section listing them, so the operator never mistakes a
green run for total coverage.

## Production changes required

The suite needs three changes inside shipping code. All are small, all are user-invisible, and each
is justified below because shipping test hooks needs a reason.

### 1. Data folder override — `AppPaths`

Today `AppPaths.AppDataFolder` is a hardcoded `Path.Combine(LocalApplicationData,
"UmnathaNetworkMonitor")` with no override. Any UI-driven run would therefore drive the operator's
real 74 MB database and the real `settings.json`.

The change: read an environment variable, `UMNATHA_DATA_FOLDER`, and use it when set and non-empty;
otherwise behave exactly as now. A single property, one branch, no behaviour change for users.

Beyond testing, this also gives support a way to point a user's install at a copied database for
diagnosis, and gives `Tools/HistoryRestore` a target that is not the live file.

### 2. Chart draw summary — `TrafficAreaChart`

`ChartCanvasDraw` already computes everything needed to draw. After drawing, it publishes a compact,
stable, culture-invariant string to `AutomationProperties.Name` on the chart's root `Grid`:

```
buckets=300 series=down,up peak=2411520 scale=4194304 range=5m
```

This makes the chart's *input data* assertable without pixel comparison. It does not make the
drawing assertable — that limitation is stated above and must not be blurred.

The property is set on a container that is already present, carries no visible text, and is read by
screen readers as a description of the chart, which is an accessibility improvement rather than a
cost. `SpeedTrendChart` gets the same treatment.

### 3. Automation identifiers — the XAML

There are currently **zero** `AutomationProperties.AutomationId` values in the entire XAML. Every
control the suite drives must be found by its display text, which breaks whenever a label changes.
This session already renamed "Resting opacity (%)" to "Resting opacity" — exactly the kind of edit
that silently breaks a text-matched test.

The change: add `AutomationProperties.AutomationId` to **only the controls the suite drives**, not
exhaustively across all 11 pages. Identifiers are stable, kebab-free, PascalCase, and named for the
thing not the layout — `ScanNowButton`, not `TopRightButton`.

This is additive XAML with no visual effect, and it improves screen-reader navigation.

## Architecture

```
Tools/UITests/
  UITests.csproj              net10.0-windows10.0.19041.0, x64, not in the slnx
  Program.cs                  Entry point: preflight, phase sequence, report, exit code
  Runner/
    Preflight.cs              Elevation, installed build, disk space, stranded-backup checks
    PhaseRunner.cs            Ordered phase execution, timing, failure capture
    Phase.cs                  One phase: name, steps, whether a failure aborts the run
    StepResult.cs             Pass / fail / skipped, message, evidence paths
  Environment/
    DataFolderFixture.cs      Throwaway folder, env var, seeded database
    RealDataGuard.cs          Copies the live folder aside, restores in a finally
    SeedDatabase.cs           Builds the fixture .db from known rows
    InstalledApp.cs           Locates, launches, and shuts down the installed build
  Driving/
    AppSession.cs             FlaUI Application + main window + mini-graph window
    Navigator.cs              Nav-route helpers (SelectionItemPattern, not Invoke)
    Grid.cs                   DataGrid row and cell reads
    Waits.cs                  Condition-based waiting, never Thread.Sleep
  Evidence/
    ScreenshotWriter.cs       Window capture on failure
    UiaTreeDumper.cs          Automation subtree dump on failure
    HtmlReport.cs             The single-file report
  Phases/
    01_Launch.cs              Cold start, splash, main window, both windows present
    02_Devices.cs             All / Approved / Unapproved / History, CSV, edit, delete
    03_Traffic.cs             Internet and Local, lenses, drill-down, chart summaries
    04_SpeedTest.cs           Seeded results, grid, trend chart summary
    05_Reports.cs             Digest list, render, PDF export
    06_Settings.cs            Every setting, round-tripped through settings.json
    07_MiniGraph.cs           Both orientations, sections, opacity, placement
    08_Purge.cs               Retention purge, the one-way-door paths
    09_UpdateLifecycle.cs     Uninstall, install old, banner, update, verify
```

### Phase ordering and failure policy

Phases run in the order listed. Two classes of failure:

- **Step failure** — an assertion fails. Evidence is captured, the step is marked failed, and the
  phase continues. Later steps in that phase may fail as a consequence; the report groups them.
- **Phase abort** — the app crashed, a window vanished, or the environment is wrong. The phase stops
  and the runner moves to the next phase after restarting the app.

`09_UpdateLifecycle` is last and is the only phase that mutates the machine outside the throwaway
folder. Everything before it runs against a single long-lived app session where possible, restarting
only where a phase needs a cold start.

### The update lifecycle phase, in detail

This is the phase with real consequences, so it is specified step by step.

1. **Guard.** Refuse to run if a previous run left a stranded data-folder backup. Refuse if not
   elevated. Refuse if the working copy has uncommitted installer changes.
2. **Copy the live data folder aside.** `%LOCALAPPDATA%\UmnathaNetworkMonitor` is *copied* to
   `UmnathaNetworkMonitor.uitest-backup-<timestamp>`. The original stays in place. A copy rather
   than a rename is deliberate: a hard kill mid-phase leaves the original where the app expects it.
3. **Resolve versions.** Query `https://api.github.com/repos/jazzzsoftware/UmnathaNetworkMonitor/releases`
   and take the newest release as the target and the second-newest as the baseline. Today that is
   v0.0.12 and v0.0.11. Fail the phase if fewer than two releases exist.
4. **Uninstall the current build** via the registry uninstall string —
   `"C:\Program Files\Umnatha Network Monitor\unins000.exe" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART` —
   and wait for the uninstall entry to disappear.
5. **Download and install the baseline.** Fetch the baseline release's installer asset, verify its
   SHA-256 against the release notes, and run it `/SILENT /SUPPRESSMSGBOXES /NORESTART`.
6. **Assert the baseline is installed** — registry `DisplayVersion` equals the baseline version.
7. **Launch the baseline build and wait for the banner.** The old build ignores
   `UMNATHA_DATA_FOLDER` — it predates the override — so it uses the real folder, which is why
   step 2 exists. Wait for the `InfoBar` in `MainWindow` and assert its message names the target
   version. `UpdateService` has a 20-second check deadline, so the wait is 45 seconds before failing.
8. **Drive the update.** Click the update action, wait for download and SHA-256 verification, and let
   `InstallerLauncher` run the installer `/SILENT` and exit the app.
9. **Assert the target is installed** — registry `DisplayVersion` equals the target version — and
   relaunch, asserting the in-app version matches.
10. **Restore.** In a `finally`: delete whatever data folder now exists, restore the backup copy over
    it, and verify the restored database opens and reports the same row counts recorded in step 2.

If step 10 cannot complete, the runner writes the backup location in large letters into the report
and to the console, and exits non-zero. It never leaves the operator guessing where their history
went.

### Seeded fixture data

`SeedDatabase.cs` builds a SQLite file by running the app's own EF migrations against an empty file
and then inserting known rows. Using the real migrations rather than a checked-in `.db` means the
fixture cannot drift from the schema, and a broken migration fails the suite loudly.

The fixture covers: 12 known devices across approved and unapproved, one renamed, one with notes; 48
hours of device events including arrivals and departures; traffic and rollup rows across the 5-minute,
1-hour and 6-hour windows for both WAN and LAN; local traffic across data and discovery
classifications; 30 speed-test results with a visible trend; and three generated digests.

Every assertion in the suite is written against these known values, so "the grid shows 12 devices" is
a real assertion rather than a tautology.

## Report

A single self-contained HTML file at `Tools/UITests/artifacts/<timestamp>/report.html`, opened
automatically when the run finishes.

Contents:

- **Verdict** at the top: passed, failed, or aborted, with counts and total wall-clock.
- **Phase timeline** — each phase, its duration, and its step results.
- **Each failure** — the assertion, the expected and actual values, an inline screenshot of the app
  at the moment of failure, and a collapsible UIA subtree dump rooted at the element the step was
  looking for. That last item is the one that turns "control not found" from a mystery into a
  five-second diagnosis.
- **Not covered by this run** — the fixed list from the boundary section above.
- **Environment** — app version before and after, OS build, DPI, theme, colour scheme, elevation.

Exit code is 0 only if every step passed. Anything else is non-zero, so the command can gate an
installer build.

## Flake policy

UI automation fails intermittently when it is written badly, and a suite that cries wolf gets
ignored. Three rules, enforced in review:

- **No `Thread.Sleep` as a synchronisation device.** Every wait is a condition poll with a timeout
  and a message naming what it was waiting for. `Waits.cs` is the only place a delay is written.
- **No retry-until-green.** A step that needs three attempts is a bug in the app or in the step, and
  hiding it defeats the point of the suite.
- **Every timeout is named and justified** where it is declared. The 45-second banner wait exists
  because `UpdateService` has a 20-second check deadline; that reasoning lives next to the number.

## Risks

| Risk | Mitigation |
|---|---|
| The suite leaves the machine without the app installed | The update phase installs the target release as its final act. Failure before that point leaves the baseline installed, which the report states explicitly. |
| A crash mid-update-phase strands the data backup | The backup is a copy, not a move, so the original is never absent. The runner refuses to start while a stranded backup exists, forcing a conscious cleanup. |
| DataGrid virtualisation hides rows from UIA | Assertions scroll the grid and read realised rows, and assert on row *count* via the grid's UIA pattern rather than by enumerating children. Verified during implementation, not assumed. |
| GitHub rate limits the releases query | The unauthenticated limit is 60 requests an hour and the suite makes one. If it does fail, the phase reports the limit rather than a confusing parse error. |
| The 15-minute budget is exceeded | Phase durations are recorded from the first run. If the total drifts past 15 minutes the report flags it, and the slowest phase is named. |
| Adding AutomationIds changes behaviour | They are additive attached properties with no visual or layout effect. The 501 unit tests and a manual pass over the touched pages confirm this. |

## Success criteria

- One command, `dotnet run --project Tools/UITests`, started elevated, running unattended.
- Every page and every UI-reachable control exercised against known seeded data.
- The full release lifecycle proven: uninstall, baseline install, banner, update, target install.
- The operator's real database and settings are byte-for-byte unchanged after a successful run, and
  recoverable after a failed one.
- A report that a person can read in under a minute and diagnose a failure from without re-running.
- Total wall-clock under 15 minutes.

## Out of scope

- CI. The suite needs an interactive desktop session and elevation, and the operator runs it locally.
- Tier 1 headless unit tests for view models and Services. That is a separate, worthwhile piece of
  work — retargeting `NetworkMonitor.Tests` to `net10.0-windows10.0.19041.0` would unblock it — but
  it is not this suite and should not be folded into it.
- Screenshot baseline comparison for chart pixels. Rejected as too brittle across DPI, theme and the
  five colour schemes.
- Accessibility scanning with Axe.Windows. A natural follow-on once the automation identifiers exist,
  but not part of this design.
