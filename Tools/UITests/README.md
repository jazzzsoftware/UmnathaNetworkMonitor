# UITests

An automated UI test suite that drives the **installed** Umnatha Network Monitor build through
FlaUI: it launches the app, exercises its pages, drives the update banner against an older
release, and restores things afterward. Because it installs, uninstalls and reinstalls the real
product and touches the operator's real data folder, it refuses to run at all unless the machine
is genuinely ready for that — see Preflight below.

**Current state:** only the preflight check and the phase-running skeleton exist so far
(`Runner/Preflight.cs`, `Runner/PhaseRunner.cs` and friends). Nothing drives the app yet — no
phases are wired into `Program.cs`. Later work adds the FlaUI driving layer, the seeded database
fixture, the nine phases, and the HTML report.

## The one command

```
dotnet run --project Tools/UITests
```

**This must be run from an elevated (Administrator) terminal.** The suite installs and
uninstalls the app, which Windows will not permit otherwise. If the terminal is not elevated,
preflight reports it as a blocker and the process exits with code 2 rather than failing partway
through an install or uninstall.

## Preflight

Before anything else runs, `Preflight.Check()` verifies:

- The process is elevated.
- Umnatha Network Monitor is installed (the suite drives the installed release, not a dev build
  — read from the Inno Setup uninstall registry key, both 32- and 64-bit views).
- No stranded data-folder backup is sitting in `%LOCALAPPDATA%` from a previous run that did not
  clean up after itself (see Recovering a stranded backup, below).
- At least 3 GB free on the system drive (the update phase downloads two ~75 MB installers and
  copies the data folder aside).

Any blocker prints to the console and the process exits **2** — distinct from **1** (a real test
failure once phases exist) and **0** (everything passed). Preflight refusing is the correct,
expected outcome on a machine that has never had the app installed; it is not a bug in the
runner.

## Phase 09 really uninstalls the app

Once the phase sequence exists (a later task), phase 09 genuinely runs the Inno Setup
uninstaller against the real installed copy, as part of driving the update banner end to end.
This is not a dry run or a simulation — do not point this suite at a machine that cannot afford
to have the app uninstalled and reinstalled.

## Where the report lands

Not built yet. Once `Evidence/HtmlReport.cs` exists, a single self-contained HTML report will be
written under the run's artifact folder (`PhaseContext.ArtifactFolder`), alongside any
screenshots and UI Automation tree dumps captured on step failure.

## Recovering a stranded backup

Before touching the real database, the suite copies `%LOCALAPPDATA%\UmnathaNetworkMonitor`
aside to `%LOCALAPPDATA%\UmnathaNetworkMonitor.uitest-backup-<timestamp>` and leaves the
original in place, so a hard kill mid-run leaves the app's real data exactly where it expects to
find it. At the end of a normal run the backup is restored and deleted automatically.

If a run is killed or crashes before that cleanup happens, the backup folder is left behind and
preflight will refuse to run again — it will not run while your history is parked in two places
at once. To recover by hand:

1. Make sure Umnatha Network Monitor is closed.
2. Compare `%LOCALAPPDATA%\UmnathaNetworkMonitor` against the backup folder
   `%LOCALAPPDATA%\UmnathaNetworkMonitor.uitest-backup-<timestamp>` — the backup holds your real
   history; the folder without the suffix may hold whatever the suite left behind.
3. Delete `%LOCALAPPDATA%\UmnathaNetworkMonitor`, then rename the backup folder to
   `UmnathaNetworkMonitor` (i.e. drop the `.uitest-backup-<timestamp>` suffix).
4. Inside it, delete `uitest-row-counts.txt` — that file is the suite's own manifest, not part of
   your data, and the automated restore path always excludes it; a manual rename does not, so
   remove it by hand.
5. Start the app once to confirm your devices and history are back, then re-run the suite.

## FlaUI version

The plan specifies FlaUI 5.0.0. `dotnet restore Tools/UITests/UITests.csproj` resolved
`FlaUI.Core 5.0.0` and `FlaUI.UIA3 5.0.0` cleanly — no substitution was needed.
