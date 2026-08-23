using System.Diagnostics;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Patterns;
using FlaUI.UIA3;
using Microsoft.Data.Sqlite;
using NetworkMonitor.Core.Common;
using NetworkMonitor.UITests.Driving;
using NetworkMonitor.UITests.Evidence;
using NetworkMonitor.UITests.Fixtures;
using NetworkMonitor.UITests.Launcher;
using NetworkMonitor.UITests.Phases;
using NetworkMonitor.UITests.Runner;

if (args.Contains("--selftest"))
{
    int selfTestExitCode = await RunSelfTest();

    return selfTestExitCode;
}

if (args.Contains("--guard-selftest"))
{
    bool guardSelfTestPassed = await RunGuardSelfTest();
    int guardSelfTestExitCode = guardSelfTestPassed ? 0 : 1;

    return guardSelfTestExitCode;
}

if (args.Contains("--dump-tree"))
{
    int dumpTreeExitCode = await RunDumpTree(args);

    return dumpTreeExitCode;
}

// Phase 09 is opt-in, and deliberately not part of a routine run. It uninstalls the operator's
// installed app, downloads and installs the previous release, drives its update banner and lets it
// install the newest one again — several minutes and roughly 150 MB of downloads, with the
// operator's real data folder copied aside and restored around it. Making that the price of every
// run would have destroyed the fast feedback loop the other eight phases depend on (85 seconds,
// repeatable, touching nothing outside %TEMP%), and would have uninstalled and reinstalled the app
// on this machine dozens of times during Tasks 9 to 11 alone. The plan registered it
// unconditionally; this is a deviation, recorded in the Task 12 amendments.
bool includeUpdateLifecycle = args.Contains("--all-with-update-lifecycle");
bool pickPhases = args.Contains("--pick");

List<Phase> allPhases = BuildPhases(includeUpdateLifecycle || pickPhases);
IReadOnlyList<Phase> registeredPhases = allPhases;

// --pick opens a small dialog to tick phases rather than remembering flags — worth it when you
// are iterating on one phase and do not want to sit through the other eight. It offers the update
// lifecycle too (unticked), which is why the list above includes it whenever --pick is passed.
// A run with no arguments stays non-interactive so nothing scripted ever waits on a dialog.
if (pickPhases)
{
    IReadOnlyList<Phase>? chosen = PhasePicker.Choose(allPhases);

    if (chosen is null)
    {
        Console.WriteLine("No phases chosen — nothing was run.");

        return 0;
    }

    registeredPhases = chosen;
}

TimeSpan expectedRunDuration = SumExpectedDuration(registeredPhases);

if (includeUpdateLifecycle && !pickPhases)
{
    Console.WriteLine("Phase 09 (the update lifecycle) IS included in this run.");
    Console.WriteLine("It will uninstall Umnatha Network Monitor, install the previous release, update it, and");
    Console.WriteLine("restore your data folder afterwards. Your data folder is copied aside first.");
    Console.WriteLine();
}
else if (!pickPhases)
{
    Console.WriteLine("Phase 09 (the update lifecycle) is NOT included. Pass --all-with-update-lifecycle to run it,");
    Console.WriteLine("or --pick to choose phases from a dialog. It uninstalls and reinstalls the app, so it is");
    Console.WriteLine("opt-in either way.");
    Console.WriteLine();
}

PreflightResult preflight = await Preflight.CheckAsync(CancellationToken.None, expectedRunDuration);

if (!preflight.Ready)
{
    Console.WriteLine("Preflight failed. The suite did not start:");
    Console.WriteLine();

    foreach (string blocker in preflight.Blockers)
    {
        Console.WriteLine($"  - {blocker}");
    }

    Console.WriteLine();

    return 2;
}

Console.WriteLine($"Preflight passed. Installed version: {Preflight.ReadInstalledVersion()}");
Console.WriteLine($"Expected run duration (sum of registered phases' own estimates): {expectedRunDuration.TotalSeconds:0}s");
Console.WriteLine();

int runExitCode = await RunSuiteAsync(registeredPhases);

return runExitCode;

// Each phase carries its own ExpectedDuration, used by Preflight.CheckAsync - via the sum computed
// above - to judge whether the operator's screen-saver timeout will survive a real run. It is not
// the phase's internal hard timeouts, which are far longer. Built here, before Preflight runs, so
// that check reasons about the phases actually registered rather than a hardcoded number.
//
// Recalibrated 2026-08-23 against four consecutive full runs. The original figures were hand-set
// guesses that had never been checked, and they summed to 19 minutes for a suite that takes about
// 2. With Preflight's 1.5x margin on top, that demanded 28.5 minutes of screen-saver headroom to
// protect a 2-minute run and refused machines with an ordinary 15-minute saver. Each value below
// is roughly three times the worst of the four measured runs, so a considerably slower machine
// still fits, and the 1.5x margin still applies after that. The eight non-destructive phases now
// sum to 230s and all nine to 530s, so a stock 15-minute saver clears either.
static List<Phase> BuildPhases(bool includeUpdateLifecycle)
{
    List<Phase> phases = new List<Phase>
    {
        // Measured 2.5-5.0s across four runs. 15s leaves room for a slower machine or a
        // first-run DB migration.
        new Phase("01 Launch", true, LaunchPhase.RunAsync, TimeSpan.FromSeconds(15)),

        // Measured 14.1-20.3s, including a native file dialog round trip for CSV export/import.
        // 45s covers a slower shell-handler launch on a loaded machine.
        new Phase("02 Devices", false, DevicesPhase.RunAsync, TimeSpan.FromSeconds(45)),

        // Six chart redraws (three ranges on each traffic tab, plus two more for the drill-down's
        // reload check), a lens switch and two bucket selections, each waiting on a real reload.
        // Measured 6.1-7.9s; 30s.
        new Phase("03 Traffic", false, TrafficPhase.RunAsync, TimeSpan.FromSeconds(30)),

        // Four chart summaries and a grid read against thirty seeded results, with no real speed
        // test run (see SpeedTestPhase's header for why). Measured 1.1-1.8s; 15s is the smallest
        // value this file bothers with rather than a figure worth tuning.
        new Phase("04 Speed Test", false, SpeedTestPhase.RunAsync, TimeSpan.FromSeconds(15)),

        // Two native save dialogs with an external file handler apiece, plus generating a digest
        // (which renders its charts through Win2D) and deleting one. The slowest of the local
        // phases at 18.3-25.3s, and the render is the slow part; 60s.
        new Phase("05 Reports", false, ReportsPhase.RunAsync, TimeSpan.FromSeconds(60)),

        // Around twenty settings, each changed, waited for on disk and restored; individually
        // fast, but the file wait sets the pace. Measured 7.3-11.3s; 30s.
        new Phase("06 Settings", false, SettingsPhase.RunAsync, TimeSpan.FromSeconds(30)),

        // Section switches, the last-section rule, and eleven orientation changes for the U-1
        // height invariant - each one a window teardown and rebuild. Measured 3.1-5.0s; 20s.
        new Phase("07 Mini Graph", false, MiniGraphPhase.RunAsync, TimeSpan.FromSeconds(20)),

        // Two confirmation dialogs each way and a handful of database reads. Measured 2.3-3.2s;
        // 15s. Registered last of the driving phases because it deletes the rows the others
        // assert against.
        new Phase("08 Purge", false, PurgePhase.RunAsync, TimeSpan.FromSeconds(15))
    };

    if (includeUpdateLifecycle)
    {
        // Two ~75 MB downloads, two silent installs, an uninstall and a cold start of an old
        // build. Measured 48.8-71.5s across four runs on a working connection. Five minutes is
        // deliberately more generous than the ~3x the local phases carry, because this is the one
        // phase whose length depends on the network rather than the machine, and it is the run
        // that must not be interrupted halfway through an uninstall. Still deliberately NOT the
        // sum of this phase's internal timeouts, which allow closer to twenty minutes between
        // them.
        phases.Add(new Phase("09 Update Lifecycle", false, UpdateLifecyclePhase.RunAsync, TimeSpan.FromMinutes(5)));
    }

    return phases;
}

static TimeSpan SumExpectedDuration(IReadOnlyList<Phase> phases)
{
    TimeSpan total = TimeSpan.Zero;

    foreach (Phase phase in phases)
    {
        total += phase.ExpectedDuration;
    }

    return total;
}

// The real run: seeds a throwaway fixture, launches a local build against it (never the
// operator's real data folder — see DataFolderFixture/AppUnderTest.LaunchLocalBuild, and
// LaunchPhase's own fixture-write guard) via whichever phase runs first, drives every registered
// phase and writes the HTML report.
static async Task<int> RunSuiteAsync(IReadOnlyList<Phase> phases)
{
    string artifactFolder = Path.Combine(
        Path.GetTempPath(),
        "umnatha-uitests-run",
        DateTime.Now.ToString("yyyyMMdd-HHmmss"));

    Directory.CreateDirectory(artifactFolder);

    RunEnvironment environment = RunEnvironment.Read();
    int exitCode;

    try
    {
        DataFolderFixture fixture = await DataFolderFixture.CreateAsync();

        Console.WriteLine($"Fixture data folder: {fixture.FolderPath}");
        Console.WriteLine($"Artifact folder: {artifactFolder}");
        Console.WriteLine();

        PhaseContext context = new PhaseContext(fixture.FolderPath, artifactFolder, fixture.Counts);

        try
        {
            RunOutcome outcome = await PhaseRunner.RunAsync(phases, context);

            environment.AppVersionAfter = Preflight.ReadInstalledVersion();

            string reportPath = HtmlReport.Write(outcome, environment, artifactFolder);

            Console.WriteLine();
            Console.WriteLine(
                $"Passed: {outcome.PassedCount}  Failed: {outcome.FailedCount}  Skipped: {outcome.SkippedCount}  "
                + $"Duration: {outcome.TotalDuration.TotalSeconds:0.0}s");
            Console.WriteLine($"Report: {reportPath}");

            OpenInBrowser(reportPath);

            exitCode = outcome.ExitCode;
        }
        finally
        {

            if (context.Session is not null)
            {
                AppUnderTest.ShutDown(context.Session.Application);
                context.Session.Dispose();
            }

        }

    }
    catch (Exception failure)
    {
        Console.WriteLine($"The suite failed before it could produce a report: {failure}");
        exitCode = 1;
    }

    return exitCode;
}

// Fabricates one passed, one failed (with a real screenshot and tree dump of the desktop) and
// one skipped StepResult, then renders them through the real HtmlReport. This is how the report
// gets changed and re-checked later without paying for a full 15-minute driven run — kept as a
// standing diagnostic, not deleted once the real phases exist. It also builds a fixture data
// folder and proves the one claim the rest of the suite depends on: seeding it never touches the
// operator's real database.
static async Task<int> RunSelfTest()
{
    string artifactFolder = Path.Combine(
        Path.GetTempPath(),
        "umnatha-uitests-selftest",
        DateTime.Now.ToString("yyyyMMdd-HHmmss"));

    Directory.CreateDirectory(artifactFolder);

    bool realDatabaseUntouched = await ProveFixtureBuildsAndRealDatabaseIsUntouched();

    StepResult passedStep = StepResult.Pass("Launches the main window");
    StepResult failedStep = BuildFailedSelfTestStep(artifactFolder);
    StepResult skippedStep = StepResult.Skip(
        "Drives the update banner",
        "Self-test does not touch the installed app.");

    List<StepResult> steps = new List<StepResult> { passedStep, failedStep, skippedStep };
    PhaseResult phase = new PhaseResult("Self-test phase", DateTime.Now, TimeSpan.FromSeconds(3), false, steps);
    List<PhaseResult> phases = new List<PhaseResult> { phase };
    RunOutcome outcome = new RunOutcome(phases, DateTime.Now, TimeSpan.FromSeconds(3));
    RunEnvironment environment = RunEnvironment.Read();

    environment.AppVersionAfter = environment.AppVersionBefore;

    string reportPath = HtmlReport.Write(outcome, environment, artifactFolder);

    Console.WriteLine($"Self-test report written to: {reportPath}");

    OpenInBrowser(reportPath);

    int exitCode = reportPath.Length > 0 && realDatabaseUntouched ? 0 : 1;

    return exitCode;
}

// Reads the real database's LastWriteTimeUtc before and after seeding a throwaway fixture, and
// prints both. This is the claim RealDataGuard and every later phase rest on, so it is checked
// explicitly rather than assumed from "SeedDatabase only ever opens dbPath".
static async Task<bool> ProveFixtureBuildsAndRealDatabaseIsUntouched()
{
    string realDatabasePath = RealDatabasePath();
    DateTime? beforeTimestamp = ReadLastWriteTimeUtcIfExists(realDatabasePath);

    Console.WriteLine();
    Console.WriteLine($"Real database path: {realDatabasePath}");
    Console.WriteLine($"Real database LastWriteTimeUtc before: {DescribeTimestamp(beforeTimestamp)}");

    DataFolderFixture fixture = await DataFolderFixture.CreateAsync();

    Console.WriteLine();
    Console.WriteLine($"Fixture data folder: {fixture.FolderPath}");
    Console.WriteLine("Seeded counts:");
    Console.WriteLine($"  KnownDevices:      {fixture.Counts.KnownDevices}");
    Console.WriteLine($"  ApprovedDevices:   {fixture.Counts.ApprovedDevices}");
    Console.WriteLine($"  UnapprovedDevices: {fixture.Counts.UnapprovedDevices}");
    Console.WriteLine($"  DeviceEvents:      {fixture.Counts.DeviceEvents}");
    Console.WriteLine($"  SpeedTestResults:  {fixture.Counts.SpeedTestResults}");
    Console.WriteLine($"  DigestReports:     {fixture.Counts.DigestReports}");

    DateTime? afterTimestamp = ReadLastWriteTimeUtcIfExists(realDatabasePath);
    bool unchanged = beforeTimestamp == afterTimestamp;

    Console.WriteLine();
    Console.WriteLine($"Real database LastWriteTimeUtc after:  {DescribeTimestamp(afterTimestamp)}");
    Console.WriteLine(unchanged ? "Real database untouched: PASS" : "Real database untouched: FAIL");
    Console.WriteLine();

    return unchanged;
}

static string RealDatabasePath()
{
    string localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    string realFolder = AppDataFolderResolver.Resolve(null, localApplicationData);
    string databasePath = Path.Combine(realFolder, "networkmonitor.db");

    return databasePath;
}

static DateTime? ReadLastWriteTimeUtcIfExists(string path)
{
    DateTime? timestamp = null;

    if (File.Exists(path))
    {
        timestamp = File.GetLastWriteTimeUtc(path);
    }

    return timestamp;
}

static string DescribeTimestamp(DateTime? timestamp)
{
    string description = timestamp.HasValue
        ? timestamp.Value.ToString("O")
        : "(file does not exist — expected if the app has never run on this machine)";

    return description;
}

// Task 7's standing diagnostic: launches a locally built x64 Debug NetworkMonitor.exe — same
// build AppUnderTest.LaunchLocalBuild now drives every phase against (fix round 1) — and dumps
// one page's automation tree to a file under %TEMP%. This is the tool for adding and checking
// AutomationIds; it is meant to be run by hand, repeatedly, while a page is being tagged, so it
// seeds and points at a throwaway DataFolderFixture (same UMNATHA_DATA_FOLDER override
// AppUnderTest.LaunchLocalBuild uses) and never touches the operator's real database.
// Preflight.CheckAsync is deliberately not called for this path: it demands elevation, which a
// dev-build tree dump does not need.
static async Task<int> RunDumpTree(string[] commandLineArguments)
{
    string? pageArgument = ReadOptionValue(commandLineArguments, "--dump-tree");
    string? executableOverride = ReadOptionValue(commandLineArguments, "--exe");
    string executablePath = executableOverride ?? AppUnderTest.FindLocalBuildExecutablePath();
    int exitCode;

    if (executablePath.Length == 0 || !File.Exists(executablePath))
    {
        Console.WriteLine("--dump-tree: could not find a locally built NetworkMonitor.exe under NetworkMonitor/bin/x64/Debug.");
        Console.WriteLine("Build it first: dotnet build NetworkMonitor.slnx -c Debug -p:Platform=x64");
        Console.WriteLine("Or point at one explicitly: --dump-tree <page> --exe <path-to-NetworkMonitor.exe>");
        exitCode = 2;
    }
    else
    {
        exitCode = await DumpTreeFromExecutable(executablePath, pageArgument);
    }

    return exitCode;
}

static async Task<int> DumpTreeFromExecutable(string executablePath, string? pageArgument)
{
    // Generous: a cold launch runs DatabaseInitializer.InitializeAsync (baseline-then-migrate)
    // and loads the OUI vendor database before the splash hands off to the real shell.
    TimeSpan shellReadyTimeout = TimeSpan.FromSeconds(60);

    Console.WriteLine($"--dump-tree: launching {executablePath}");

    DataFolderFixture fixture = await DataFolderFixture.CreateAsync();
    string artifactFolder = Path.Combine(Path.GetTempPath(), "umnatha-uitests-dumptree", DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));

    Directory.CreateDirectory(artifactFolder);

    ProcessStartInfo startInfo = new ProcessStartInfo(executablePath)
    {
        UseShellExecute = false,
        WorkingDirectory = Path.GetDirectoryName(executablePath) ?? string.Empty
    };

    startInfo.Environment[AppDataFolderResolver.OverrideVariableName] = fixture.FolderPath;

    Application application = Application.Launch(startInfo);
    int exitCode;

    try
    {

        using (AppSession session = new AppSession(application))
        {
            // Waits for a shell landmark (the Traffic nav item) rather than for any window, so
            // every session.MainWindow read after this point is the real shell and not the splash
            // (App.xaml.cs shows it while DatabaseInitializer.InitializeAsync and the rest of
            // OnLaunched run).
            //
            // Task 9: TryFindShellLandmark, not a bare session.MainWindow read. AppSession's
            // fix-round-1 rewrite made MainWindow *throw* whenever no window is titled "Umnatha
            // Network Monitor" right now, which is the normal state for the first second or two of
            // every cold start — and Waits.Until propagates a thrown exception instead of treating
            // it as "not yet". This diagnostic therefore aborted on its very first poll, on every
            // run, with the message the wait was written to tolerate. LaunchPhase.ShellIsReady
            // already catches exactly this; the diagnostic had been left behind when that fix
            // landed.
            Waits.UntilFound(
                () => TryFindShellLandmark(session),
                shellReadyTimeout,
                "the shell (NavigationView with the Traffic nav item) to replace the splash screen");

            AutomationElement windowToDump = session.MainWindow;

            if (string.Equals(pageArgument, "minigraph", StringComparison.OrdinalIgnoreCase))
            {
                windowToDump = ShowMiniGraphWindow(session);
            }
            else if (pageArgument is not null)
            {
                NavigateToPage(session, pageArgument);
            }

            string dumpedPath = UiaTreeDumper.Dump(windowToDump, artifactFolder, pageArgument ?? "shell");

            Console.WriteLine($"--dump-tree: wrote {dumpedPath}");
            exitCode = dumpedPath.Length > 0 ? 0 : 1;
        }

    }
    catch (Exception failure)
    {
        Console.WriteLine($"--dump-tree: failed: {failure}");
        exitCode = 1;
    }
    finally
    {
        AppUnderTest.ShutDown(application);
    }

    return exitCode;
}

static Window ShowMiniGraphWindow(AppSession session)
{
    Navigator navigator = new Navigator(session);

    navigator.GoTo(NavRoute.Traffic);

    AutomationElement miniGraphToggle = Waits.UntilFound(
        () => session.MainWindow.FindFirstDescendant("MiniGraphToggle"),
        TimeSpan.FromSeconds(10),
        "the mini graph toggle button to appear");

    ITogglePattern togglePattern = miniGraphToggle.Patterns.Toggle.Pattern;

    if (togglePattern.ToggleState.Value != ToggleState.On)
    {
        togglePattern.Toggle();
    }

    Window miniGraphWindow = Waits.UntilFound(
        () => session.MiniGraphWindow,
        TimeSpan.FromSeconds(10),
        "the mini graph window to appear after toggling it on");

    return miniGraphWindow;
}

// Drives NavRoute + a SelectorBarItem's AutomationId, both wired up in Task 7, rather than one
// page-specific method per surface — the same SelectionItemPattern.Select() approach Navigator
// itself uses, and for the same reason: Invoke() does not reliably change a SelectorBarItem's
// selection either.
// Polled from Waits.UntilFound, which retries on null but not on a thrown exception: until the
// splash closes there is no window with the shell's title and AppSession.MainWindow throws, which
// is expected on every early poll of a cold start rather than a reason to give up.
static AutomationElement? TryFindShellLandmark(AppSession session)
{
    AutomationElement? landmark;

    try
    {
        landmark = session.MainWindow.FindFirstDescendant("TrafficNavItem");
    }
    catch (Exception)
    {
        landmark = null;
    }

    return landmark;
}

static void NavigateToPage(AppSession session, string pageArgument)
{
    (NavRoute route, string? tabAutomationId) = ResolvePage(pageArgument);
    Navigator navigator = new Navigator(session);

    navigator.GoTo(route);

    if (tabAutomationId is not null)
    {
        AutomationElement tabItem = Waits.UntilFound(
            () => session.MainWindow.FindFirstDescendant(tabAutomationId),
            TimeSpan.FromSeconds(10),
            $"the '{tabAutomationId}' tab to appear");

        ISelectionItemPattern selectionItemPattern = tabItem.Patterns.SelectionItem.Pattern;

        selectionItemPattern.Select();

        Waits.Until(
            () => selectionItemPattern.IsSelected.Value,
            TimeSpan.FromSeconds(10),
            $"the '{tabAutomationId}' tab to report itself selected after Select()");
    }

}

static (NavRoute Route, string? TabAutomationId) ResolvePage(string pageArgument)
{
    string normalisedPageArgument = pageArgument.Trim().ToLowerInvariant();

    (NavRoute Route, string? TabAutomationId) resolved = normalisedPageArgument switch
    {
        "traffic" => (NavRoute.Traffic, null),
        "internet" => (NavRoute.Traffic, "InternetTab"),
        "local" => (NavRoute.Traffic, "LocalTab"),
        "speedtest" => (NavRoute.Traffic, "SpeedTestTab"),
        "devices" => (NavRoute.Devices, "AllDevicesTab"),
        "alldevices" => (NavRoute.Devices, "AllDevicesTab"),
        "approved" => (NavRoute.Devices, "ApprovedDevicesTab"),
        "unapproved" => (NavRoute.Devices, "UnapprovedDevicesTab"),
        "devicehistory" => (NavRoute.Devices, "DeviceHistoryTab"),
        "reports" => (NavRoute.Reports, "DigestTab"),
        "digest" => (NavRoute.Reports, "DigestTab"),
        "reportshistory" => (NavRoute.Reports, "ReportsHistoryTab"),
        "settings" => (NavRoute.Settings, null),
        "settingstraffic" => (NavRoute.Settings, "SettingsTrafficTab"),
        "settingsdevice" => (NavRoute.Settings, "SettingsDeviceTab"),
        "settingstheme" => (NavRoute.Settings, "SettingsThemeTab"),
        "settingsother" => (NavRoute.Settings, "SettingsOtherTab"),
        _ => throw new ArgumentException(
            $"--dump-tree does not recognise page '{pageArgument}'. Known pages: traffic, internet, local, speedtest, "
            + "devices, alldevices, approved, unapproved, devicehistory, reports, digest, reportshistory, settings, "
            + "settingstraffic, settingsdevice, settingstheme, settingsother, minigraph.")
    };

    return resolved;
}

static string? ReadOptionValue(string[] commandLineArguments, string optionName)
{
    int optionIndex = Array.IndexOf(commandLineArguments, optionName);
    string? optionValue = null;

    if (optionIndex >= 0 && optionIndex + 1 < commandLineArguments.Length && !commandLineArguments[optionIndex + 1].StartsWith("--", StringComparison.Ordinal))
    {
        optionValue = commandLineArguments[optionIndex + 1];
    }

    return optionValue;
}

// Exercises RealDataGuard.CopyAside/Restore end to end — the highest-consequence code in the
// whole suite — against a throwaway folder this function builds and seeds itself. Never the
// real %LOCALAPPDATA%\UmnathaNetworkMonitor: it uses the internal (string realFolder) overloads
// for exactly that reason. Every row count used for comparison is read independently of
// RealDataGuard's own counting code, so a shared bug in that code can't hide from this test.
static async Task<bool> RunGuardSelfTest()
{
    string rootFolder = Path.Combine(
        Path.GetTempPath(),
        "umnatha-uitests-guard-selftest",
        DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));

    string fakeRealFolder = Path.Combine(rootFolder, "fake-real");
    string fakeRealDatabasePath = Path.Combine(fakeRealFolder, "networkmonitor.db");

    Directory.CreateDirectory(fakeRealFolder);

    await SeedDatabase.BuildAsync(fakeRealDatabasePath, DateTime.UtcNow);

    Console.WriteLine();
    Console.WriteLine("=== --guard-selftest: RealDataGuard against a throwaway folder (never the real one) ===");
    Console.WriteLine($"Throwaway 'real' folder: {fakeRealFolder}");
    Console.WriteLine();

    Dictionary<string, long> originalCounts = CountRowsIndependently(fakeRealDatabasePath);
    bool allPassed = true;

    string firstBackupPath = RealDataGuard.CopyAside(fakeRealFolder);
    Dictionary<string, long> manifestCounts = ReadManifestIndependently(Path.Combine(firstBackupPath, "uitest-row-counts.txt"));

    allPassed = Check(
        "CopyAside's manifest matches the live database's row counts, captured before copying",
        DictionariesEqual(originalCounts, manifestCounts)) && allPassed;

    File.Delete(Path.Combine(firstBackupPath, "uitest-row-counts.txt"));

    bool corruptRestoreResult = RealDataGuard.Restore(firstBackupPath, fakeRealFolder);
    Dictionary<string, long> countsAfterCorruptRestore = CountRowsIndependently(fakeRealDatabasePath);

    allPassed = Check("Restore refuses a backup with a missing manifest", !corruptRestoreResult) && allPassed;
    allPassed = Check(
        "A refused restore left the target's row counts unchanged",
        DictionariesEqual(originalCounts, countsAfterCorruptRestore)) && allPassed;
    allPassed = Check("A refused restore left the target folder in place", Directory.Exists(fakeRealFolder)) && allPassed;

    string secondBackupPath = RealDataGuard.CopyAside(fakeRealFolder);

    Directory.Delete(fakeRealFolder, true);

    bool cleanRestoreResult = RealDataGuard.Restore(secondBackupPath, fakeRealFolder);
    Dictionary<string, long> countsAfterCleanRestore = CountRowsIndependently(fakeRealDatabasePath);

    allPassed = Check("Restore succeeds against a valid backup after the target was lost entirely", cleanRestoreResult) && allPassed;
    allPassed = Check("Restored row counts match the original", DictionariesEqual(originalCounts, countsAfterCleanRestore)) && allPassed;
    allPassed = Check("Restore deleted the backup only after a successful, verified restore", !Directory.Exists(secondBackupPath)) && allPassed;

    Dictionary<string, long> countsBeforeEmptyPathRestore = CountRowsIndependently(fakeRealDatabasePath);
    bool emptyPathRestoreResult = RealDataGuard.Restore(string.Empty, fakeRealFolder);
    Dictionary<string, long> countsAfterEmptyPathRestore = CountRowsIndependently(fakeRealDatabasePath);

    allPassed = Check("Restore(string.Empty) refuses", !emptyPathRestoreResult) && allPassed;
    allPassed = Check(
        "Restore(string.Empty) did not touch the target",
        DictionariesEqual(countsBeforeEmptyPathRestore, countsAfterEmptyPathRestore)) && allPassed;

    string nonExistentBackupPath = Path.Combine(rootFolder, "does-not-exist");
    Dictionary<string, long> countsBeforeMissingPathRestore = CountRowsIndependently(fakeRealDatabasePath);
    bool missingPathRestoreResult = RealDataGuard.Restore(nonExistentBackupPath, fakeRealFolder);
    Dictionary<string, long> countsAfterMissingPathRestore = CountRowsIndependently(fakeRealDatabasePath);

    allPassed = Check("Restore(<non-existent path>) refuses", !missingPathRestoreResult) && allPassed;
    allPassed = Check(
        "Restore(<non-existent path>) did not touch the target",
        DictionariesEqual(countsBeforeMissingPathRestore, countsAfterMissingPathRestore)) && allPassed;

    // Coverage gap 1: a backup whose manifest is present and parses, but whose recorded counts
    // disagree with what is actually in it — the exact case the manifest exists to catch. This
    // never happened above: every earlier refusal was a missing/empty/non-existent path, not a
    // tampered one.
    string tamperedBackupPath = RealDataGuard.CopyAside(fakeRealFolder);
    string tamperedManifestPath = Path.Combine(tamperedBackupPath, "uitest-row-counts.txt");
    List<string> tamperedManifestLines = File.ReadAllLines(tamperedManifestPath).ToList();

    for (int lineIndex = 0; lineIndex < tamperedManifestLines.Count; lineIndex++)
    {

        if (tamperedManifestLines[lineIndex].StartsWith("Devices=", StringComparison.Ordinal))
        {
            tamperedManifestLines[lineIndex] = "Devices=999999";
        }

    }

    File.WriteAllLines(tamperedManifestPath, tamperedManifestLines);

    Dictionary<string, long> countsBeforeTamperedRestore = CountRowsIndependently(fakeRealDatabasePath);
    bool tamperedRestoreResult = RealDataGuard.Restore(tamperedBackupPath, fakeRealFolder);
    Dictionary<string, long> countsAfterTamperedRestore = CountRowsIndependently(fakeRealDatabasePath);

    allPassed = Check(
        "Restore refuses when the manifest disagrees with the backup's actual contents",
        !tamperedRestoreResult) && allPassed;
    allPassed = Check(
        "A count-mismatch refusal left the target's row counts unchanged",
        DictionariesEqual(countsBeforeTamperedRestore, countsAfterTamperedRestore)) && allPassed;
    allPassed = Check("A count-mismatch refusal left the target folder in place", Directory.Exists(fakeRealFolder)) && allPassed;

    // Coverage gap 2: force the second Directory.Move inside SwapInStagedFolder to fail after the
    // first one (real -> displaced) has already happened, and confirm the rollback puts the
    // original back intact. Calls SwapInStagedFolder directly — a seam that exists for exactly
    // this — rather than the full Restore pipeline, which is already covered above.
    string swapTestFolder = Path.Combine(rootFolder, "swap-test");
    string swapTestDatabasePath = Path.Combine(swapTestFolder, "networkmonitor.db");

    Directory.CreateDirectory(swapTestFolder);
    await SeedDatabase.BuildAsync(swapTestDatabasePath, DateTime.UtcNow);

    Dictionary<string, long> swapOriginalCounts = CountRowsIndependently(swapTestDatabasePath);
    string swapStagingFolder = swapTestFolder + ".uitest-restore-staging-selftest";

    Directory.CreateDirectory(swapStagingFolder);
    File.WriteAllText(Path.Combine(swapStagingFolder, "marker.txt"), "this staged copy must never end up in place");

    bool swapThrew = false;

    try
    {
        RealDataGuard.SwapInStagedFolder(swapTestFolder, swapStagingFolder, () => Directory.Delete(swapStagingFolder, true));
    }
    catch (Exception)
    {
        swapThrew = true;
    }

    Dictionary<string, long> swapCountsAfterRollback = CountRowsIndependently(swapTestDatabasePath);

    allPassed = Check("SwapInStagedFolder throws when the second move fails partway through", swapThrew) && allPassed;
    allPassed = Check(
        "Rollback restored the original folder's contents after the swap failed",
        DictionariesEqual(swapOriginalCounts, swapCountsAfterRollback)) && allPassed;
    allPassed = Check(
        "Rollback left no marker file from the staged copy that never got swapped in",
        !File.Exists(Path.Combine(swapTestFolder, "marker.txt"))) && allPassed;

    Console.WriteLine();
    Console.WriteLine(allPassed ? "guard-selftest: ALL CHECKS PASSED" : "guard-selftest: SOME CHECKS FAILED");
    Console.WriteLine();

    TryCleanUpGuardSelfTestRoot(rootFolder);

    return allPassed;
}

static void TryCleanUpGuardSelfTestRoot(string rootFolder)
{

    try
    {

        if (Directory.Exists(rootFolder))
        {
            Directory.Delete(rootFolder, true);
        }

    }
    catch (Exception exception)
    {
        Console.WriteLine(
            $"guard-selftest: could not clean up its own throwaway folder at {rootFolder} ({exception.Message}). "
            + "It is safe to delete by hand — none of it is real data.");
    }

}

static bool Check(string label, bool condition)
{
    string status = condition ? "PASS" : "FAIL";

    Console.WriteLine($"  [{status}] {label}");

    return condition;
}

// Deliberately duplicates RealDataGuard's table list and counting shape rather than calling
// back into it, so this test doesn't just confirm RealDataGuard agrees with itself.
static Dictionary<string, long> CountRowsIndependently(string databasePath)
{
    string[] tables =
    {
        "Devices",
        "ScanSessions",
        "DeviceEvents",
        "TrafficEntries",
        "TrafficRollups",
        "LocalTrafficEntries",
        "LocalTrafficRollups",
        "DigestReports",
        "SpeedTestResults"
    };

    Dictionary<string, long> counts = new Dictionary<string, long>();

    if (File.Exists(databasePath))
    {

        using (SqliteConnection connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly;Pooling=False"))
        {
            connection.Open();

            foreach (string table in tables)
            {

                using (SqliteCommand command = connection.CreateCommand())
                {
                    command.CommandText = $"SELECT COUNT(*) FROM \"{table}\";";

                    object? result = command.ExecuteScalar();

                    counts[table] = result is null ? 0 : Convert.ToInt64(result);
                }

            }

        }

    }
    else
    {

        foreach (string table in tables)
        {
            counts[table] = -1;
        }

    }

    return counts;
}

static Dictionary<string, long> ReadManifestIndependently(string manifestPath)
{
    Dictionary<string, long> counts = new Dictionary<string, long>();

    if (File.Exists(manifestPath))
    {

        foreach (string line in File.ReadAllLines(manifestPath))
        {
            string[] parts = line.Split('=');

            if (parts.Length == 2 && long.TryParse(parts[1], out long count))
            {
                counts[parts[0]] = count;
            }

        }

    }

    return counts;
}

static bool DictionariesEqual(Dictionary<string, long> first, Dictionary<string, long> second)
{
    bool equal = first.Count == second.Count;

    if (equal)
    {

        foreach (KeyValuePair<string, long> entry in first)
        {
            bool matches = second.TryGetValue(entry.Key, out long otherValue) && otherValue == entry.Value;

            if (!matches)
            {
                equal = false;
            }

        }

    }

    return equal;
}

static StepResult BuildFailedSelfTestStep(string artifactFolder)
{
    StepResult failedStep = StepResult.Fail(
        "Finds the Scan Now button",
        "an enabled button named 'Scan Now'",
        "no matching element");

    using (UIA3Automation automation = new UIA3Automation())
    {
        AutomationElement desktop = automation.GetDesktop();

        failedStep.ScreenshotPath = ScreenshotWriter.Write(desktop, artifactFolder, failedStep.Name);
        failedStep.TreeDumpPath = UiaTreeDumper.Dump(desktop, artifactFolder, failedStep.Name);
    }

    return failedStep;
}

static void OpenInBrowser(string reportPath)
{

    if (reportPath.Length > 0)
    {

        try
        {
            ProcessStartInfo startInfo = new ProcessStartInfo(reportPath)
            {
                UseShellExecute = true
            };

            Process.Start(startInfo);
        }
        catch (Exception failure)
        {
            Console.WriteLine($"Could not open the report automatically: {failure.Message}");
        }

    }

}
