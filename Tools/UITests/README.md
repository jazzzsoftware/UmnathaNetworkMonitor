# UITests

An automated UI test suite that drives the **installed** Umnatha Network Monitor build through
FlaUI: it launches the app, exercises its pages, drives the update banner against an older
release, and restores things afterward. Because it installs, uninstalls and reinstalls the real
product and touches the operator's real data folder, it refuses to run at all unless the machine
is genuinely ready for that — see Preflight below.

**Current state:** the runner, the FlaUI driving layer, the seeded database fixture, the HTML
report and all nine phases exist and are wired into `Program.cs`.

- A plain run drives the eight non-destructive phases: 138 assertions in about 85 seconds, touching
  nothing outside `%TEMP%`.
- `--all-with-update-lifecycle` adds phase 09, the update lifecycle: 146 assertions in about two
  minutes. It uninstalls the installed app, installs the previous release, drives its update banner
  and restores the data folder afterwards.
- `--pick` opens a dialog listing every phase, all ticked, and starts by itself after thirty
  seconds unless you touch something.

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
- The screen saver is not currently running, and is not enabled with a timeout short enough to
  fire before this specific run's registered phases are expected to finish (see Why the desktop
  must stay interactive, below).

Any blocker prints to the console and the process exits **2** — distinct from **1** (a real test
failure) and **0** (everything passed). `InstalledApp.ShutDown` also plays its part here: whenever
it has to fall back to `Kill()` rather than a graceful tray exit, it stops the orphaned
`NetworkMonitorTraffic` session itself and reports whether that succeeded, precisely so the
stale-session blocker above is the exception, not the routine case.

## Why the desktop must stay interactive

This suite drives the app with real OS-level input — FlaUI's `Click()` and the keyboard helpers
in `Waits`/`DevicesPhase` ultimately call `SendInput`, and several steps check
`GetForegroundWindow()` to confirm no other window has stolen focus before continuing. Both calls
assume the session's original desktop is the one actually receiving input.

The Windows screen saver breaks that assumption: when it activates, Windows switches the
workstation to a separate desktop object, and synthetic input aimed at the original desktop is
refused from that point on. A real run confirmed this directly — `SendInput` started throwing
`Win32Exception (5): Access is denied` and `GetForegroundWindow()` started returning zero, and the
run's pass count dropped from 17 passed / 1 failed to 13 passed / 4 failed with no code change in
between. Synthetic input resets Windows' own idle timer, so a saver essentially never fires while
a run is actively driving the UI — what actually happened was idle time building up *between*
runs until the saver activated, and the next run then started straight into it.

`Preflight.cs` refuses to start a run in two situations:

- **The screen saver is already running right now** (`SPI_GETSCREENSAVERRUNNING`) — a hard,
  unconditional refusal, and the check that would have caught the overnight failure at the start
  instead of partway through.
- **Its configured timeout (`HKCU\Control Panel\Desktop\ScreenSaveTimeOut`) is shorter than this
  specific run needs.** "Needs" is derived from the phases actually registered for this run
  (`Program.cs`'s `BuildPhases`/`SumExpectedDuration`, each phase carrying its own
  `Phase.ExpectedDuration`, set at roughly three times the worst of four measured runs) with a
  1.5x safety margin on top. The eight non-destructive phases sum to 230s and need under six
  minutes of headroom; all nine sum to 530s and need about 13.5, so **a stock 15-minute screen
  saver clears either run** and no setting needs changing to use this suite.

  Those estimates were hand-set guesses until 2026-08-23, when four runs showed they summed to
  nine times the real runtime: 19 minutes declared for a suite that takes about 2, which with the
  margin demanded 28.5 minutes of headroom and refused ordinary machines outright. If you find
  yourself being asked to lengthen a screen saver to run a two-minute suite, re-measure rather
  than change the setting.

Preflight cannot stop a screen saver that activates for some other reason mid-run (a policy
change, a second interactive session locking the screen). If a run fails partway through with
exactly these symptoms, this is the first thing to rule out, not the phase code.

## Phase 09 really uninstalls the app

Phase 09 genuinely runs the Inno Setup uninstaller against the real installed copy, installs the
previous release over the operator's real data folder, and drives its update banner end to end.
This is not a dry run or a simulation — do not point it at a machine that cannot afford to have
the app uninstalled and reinstalled. It only runs when asked for by name
(`--all-with-update-lifecycle`, or ticked in `--pick`).

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
