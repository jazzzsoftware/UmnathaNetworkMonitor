# UITests

An automated UI test suite that drives the **installed** Umnatha Network Monitor build through
FlaUI: it launches the app, exercises its pages, drives the update banner against an older
release, and restores things afterward. Because it installs, uninstalls and reinstalls the real
product and touches the operator's real data folder, it refuses to run at all unless the machine
is genuinely ready for that — see Preflight below.

**Current state:** the runner, the FlaUI driving layer, the seeded database fixture, the HTML
report and the first two phases (`Phases/LaunchPhase.cs`, `Phases/DevicesPhase.cs`) all exist and
are wired into `Program.cs`'s default run. The remaining seven phases (Traffic, Speed Test,
Reports, Settings, Mini Graph, Purge, the update lifecycle) are later work.

## The one command

```
dotnet run --project Tools/UITests
```

**This must be run from an elevated (Administrator) terminal.** The suite installs and
uninstalls the app, which Windows will not permit otherwise. If the terminal is not elevated,
preflight reports it as a blocker and the process exits with code 2 rather than failing partway
through an install or uninstall.

## Preflight

Before anything else runs, `Preflight.CheckAsync` verifies:

- The process is elevated.
- No `NetworkMonitor` process is already running. A second launch would hand off to it
  (`App.xaml.cs`'s single-instance mutex) and drive the operator's real database instead of the
  fixture — this is a refusal, naming the process id, and the runner never closes it for you.
  Exit it from the tray icon (right-click, then Exit) and run again.
- No `NetworkMonitorTraffic` ETW trace session is running with no `NetworkMonitor` process behind
  it — almost always a previous hard kill's leftover (`TrafficCollector.cs:13`); the next launch
  hangs before it reaches its shell while this is present. The blocker names the exact `logman
  stop NetworkMonitorTraffic -ets` command to clear it.
- Umnatha Network Monitor is installed. Unlike the checks above, **this one is not just a
  refusal**: if elevated, the runner downloads the latest GitHub release itself, verifies its
  SHA-256 against the release's own `.sha256` asset before ever executing it, and installs it
  `/SILENT /SUPPRESSMSGBOXES /NORESTART` — see `Fixtures/ReleaseInstaller.cs`. Only a failure to
  acquire or install it (or a lack of elevation to do so) becomes a blocker.
- No stranded data-folder backup is sitting in `%LOCALAPPDATA%` from a previous run that did not
  clean up after itself (see Recovering a stranded backup, below).
- At least 3 GB free on the system drive (the update phase downloads two ~75 MB installers and
  copies the data folder aside).

Any blocker prints to the console and the process exits **2** — distinct from **1** (a real test
failure) and **0** (everything passed). `InstalledApp.ShutDown` also plays its part here: whenever
it has to fall back to `Kill()` rather than a graceful tray exit, it stops the orphaned
`NetworkMonitorTraffic` session itself and reports whether that succeeded, precisely so the
stale-session blocker above is the exception, not the routine case.

## Phase 09 really uninstalls the app

Once the phase sequence exists (a later task), phase 09 genuinely runs the Inno Setup
uninstaller against the real installed copy, as part of driving the update banner end to end.
This is not a dry run or a simulation — do not point this suite at a machine that cannot afford
to have the app uninstalled and reinstalled.

## Where the report lands

A single self-contained HTML report is written to `%TEMP%\umnatha-uitests-run\<timestamp>\report.html`
— the run's artifact folder (`PhaseContext.ArtifactFolder`) — alongside any screenshots and UI
Automation tree dumps captured on step failure, and opened in the default browser once the run
finishes.

## Recovering a stranded backup

Before touching the real database, the suite copies `%LOCALAPPDATA%\UmnathaNetworkMonitor`
aside to `%LOCALAPPDATA%\UmnathaNetworkMonitor.uitest-backup-<timestamp>` and leaves the
original in place, so a hard kill mid-run leaves the app's real data exactly where it expects to
find it. Restoring it goes through a staging folder,
`%LOCALAPPDATA%\UmnathaNetworkMonitor.uitest-restore-staging-<timestamp>` — a validated copy that
gets swapped in only after its row counts are confirmed to match — and the swap itself briefly
renames the current folder to `%LOCALAPPDATA%\UmnathaNetworkMonitor.uitest-displaced-<timestamp>`
before discarding it. At the end of a normal run all three are gone: the backup and the displaced
original are deleted, and the staging folder no longer exists once it has been swapped into place.

If a run is killed or crashes before that cleanup happens, one of the three folders above can be
left behind and preflight will refuse to run again — it will not run while your history is parked
in more than one place at once. To recover by hand:

1. Make sure Umnatha Network Monitor is closed.
2. Find whichever suffix is present — `.uitest-backup-<timestamp>`,
   `.uitest-restore-staging-<timestamp>` or `.uitest-displaced-<timestamp>` — next to
   `%LOCALAPPDATA%\UmnathaNetworkMonitor`. Any of the three holds a full copy of your real data;
   the folder without a suffix may hold whatever the suite left behind instead.
3. Delete `%LOCALAPPDATA%\UmnathaNetworkMonitor`, then rename the suffixed folder to
   `UmnathaNetworkMonitor` (i.e. drop its suffix).
4. Inside it, delete `uitest-row-counts.txt` if present — that file is the suite's own manifest,
   not part of your data, and the automated restore path always excludes it; a manual rename does
   not, so remove it by hand.
5. Start the app once to confirm your devices and history are back, then re-run the suite.

## FlaUI version

The plan specifies FlaUI 5.0.0. `dotnet restore Tools/UITests/UITests.csproj` resolved
`FlaUI.Core 5.0.0` and `FlaUI.UIA3 5.0.0` cleanly — no substitution was needed.
