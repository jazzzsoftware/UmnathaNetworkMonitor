using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using NetworkMonitor.UITests.Driving;
using NetworkMonitor.UITests.Fixtures;
using NetworkMonitor.UITests.Runner;

namespace NetworkMonitor.UITests.Phases
{
    // Phase 01: a cold start against the seeded fixture. abortsRun is true (wired in Program.cs)
    // because every later phase assumes a working, driveable app — if this phase's throwing
    // checks never resolve, nothing after it can mean anything.
    //
    // Only "the launched build is writing to the fixture" and "the splash closes and the shell
    // appears" are allowed to throw and abort the run; the rest (splash window gone, title, mini
    // graph, no error dialog) are recorded as pass/fail and do not stop later phases, because none
    // of them means the app itself is broken.
    //
    // Fix round 1 (2026-08-20): drives AppUnderTest.LaunchLocalBuild, not the installed release —
    // see that type's header comment for why. The fixture-write check below is the other half of
    // that finding: a real run against the (then) installed release silently ignored
    // UMNATHA_DATA_FOLDER and drove the operator's real data folder for the run's whole duration,
    // failing only later, opaquely, at the shell-ready timeout.
    public static class LaunchPhase
    {
        // MainWindow.xaml:7 sets this literally; the running build does not embed a version number
        // in the window title (confirmed by reading MainWindow.xaml and App.xaml.cs — the version
        // only appears on the Settings page, via SettingsPage.xaml.cs's VersionText). This check is
        // therefore "the window identifies itself as the real app", not a version comparison; the
        // installed version itself is already captured independently in the report's Environment
        // section (RunEnvironment.AppVersionBefore/After, both read from the registry).
        private const string ExpectedWindowTitle = "Umnatha Network Monitor";

        private const string FixtureDatabaseFileName = "networkmonitor.db";
        private const string FixtureWalSuffix = "-wal";

        // DatabaseInitializer.InitializeAsync sets `PRAGMA journal_mode=WAL`
        // (NetworkMonitor.Services/Data/DatabaseInitializer.cs:19) inside the Task.Run OnLaunched
        // awaits before AppHost.StartAsync, the main window, or the splash's close — well before
        // the shell exists. If the launched build is genuinely writing to the fixture folder, its
        // -wal sidecar appears within a second or two of the process starting; twenty seconds is
        // generous headroom, not a wait this is expected to hit. Deliberately short relative to
        // ShellReadyTimeout below, so a wrong-folder failure is fast and specific instead of a
        // 45-second timeout that looks identical to a genuinely broken shell.
        private static readonly TimeSpan FixtureWriteTimeout = TimeSpan.FromSeconds(20);

        // Covers DatabaseInitializer.InitializeAsync (baseline-then-migrate) and OuiDatabase.Load
        // running behind the splash before the shell's NavigationView appears — the same landmark
        // and the same reasoning Program.cs's --dump-tree diagnostic already relies on.
        private static readonly TimeSpan ShellReadyTimeout = TimeSpan.FromSeconds(45);

        // The fixture's settings.json turns the mini graph on (DataFolderFixture's FixtureSettings.
        // ShowMiniGraph = true). Restoring MiniGraphState's last placement and constructing
        // MiniGraphWindow is normally sub-second; generous because a false timeout here would fail
        // a step that is not the abort-worthy part of a cold start.
        private static readonly TimeSpan MiniGraphTimeout = TimeSpan.FromSeconds(15);

        public static Task<IReadOnlyList<StepResult>> RunAsync(PhaseContext context)
        {
            StepLog steps = new StepLog(context);
            Application application = AppUnderTest.LaunchLocalBuild(context.DataFolder);
            AppSession session = new AppSession(application);

            // Assigned before the throwing waits below so Program.cs can still shut the app down
            // (and stop the orphaned ETW session on any non-graceful exit — amendment B) even if
            // this phase never gets past them.
            context.Session = session;

            string fixtureWalPath = Path.Combine(context.DataFolder, FixtureDatabaseFileName + FixtureWalSuffix);

            Waits.Until(
                () => File.Exists(fixtureWalPath),
                FixtureWriteTimeout,
                $"the fixture database's WAL file to appear at {fixtureWalPath} — proof the launched build is "
                + "actually writing to the fixture folder and not a different one (see the fix-round-1 finding in "
                + "this file's header comment)");

            steps.Add(StepResult.Pass("The launched build is writing to the fixture data folder"));

            Waits.Until(
                () => ShellIsReady(session),
                ShellReadyTimeout,
                "the splash screen to close and the main shell (Traffic/Devices/Reports/Settings navigation) to appear");

            steps.Add(StepResult.Pass("The splash screen closes and the main shell appears"));
            steps.Add(CheckSplashWindowIsGone(session));
            steps.Add(CheckWindowTitle(session));
            steps.Add(CheckMiniGraphWindow(session));
            steps.Add(CheckNoErrorDialog(session));

            IReadOnlyList<StepResult> result = steps.Steps;
            Task<IReadOnlyList<StepResult>> completed = Task.FromResult(result);

            return completed;
        }

        // Polled from Waits.Until (see RunAsync above), which retries on false but not on a
        // thrown exception — session.MainWindow throws TimeoutException whenever no window is
        // currently titled "Umnatha Network Monitor" (AppSession.cs's own fix-round-1 comment),
        // which is expected and normal on every early poll of a cold start, so that case is
        // caught here and treated as "not ready yet", not a reason to give up.
        private static bool ShellIsReady(AppSession session)
        {
            bool ready;

            try
            {
                Window mainWindow = session.MainWindow;

                ready = mainWindow.FindFirstDescendant("TrafficNavItem") is not null
                    && mainWindow.FindFirstDescendant("DevicesNavItem") is not null
                    && mainWindow.FindFirstDescendant("ReportsNavItem") is not null
                    && mainWindow.FindFirstDescendant("SettingsNavItem") is not null;
            }
            catch (TimeoutException)
            {
                ready = false;
            }

            return ready;
        }

        // SplashWindow.xaml sets no Title, so once the shell is ready the only top-level window
        // with an empty title would be a splash that has not actually closed yet — App.xaml.cs's
        // root.Loaded handler (with a 5s fallback timer) is what closes it, independently of the
        // shell landmark this phase already waited for above.
        private static StepResult CheckSplashWindowIsGone(AppSession session)
        {
            Window[] topLevelWindows = session.TopLevelWindows();
            int emptyTitledWindowCount = topLevelWindows.Count(window => window.Title.Length == 0);
            StepResult result;

            if (emptyTitledWindowCount == 0)
            {
                result = StepResult.Pass("The splash screen has closed");
            }
            else
            {
                result = StepResult.Fail(
                    "The splash screen has closed",
                    "no top-level window with an empty title once the shell is ready",
                    $"{emptyTitledWindowCount} such window(s) still present");
            }

            return result;
        }

        private static StepResult CheckWindowTitle(AppSession session)
        {
            string actualTitle = session.MainWindow.Title;
            StepResult result;

            if (actualTitle == ExpectedWindowTitle)
            {
                result = StepResult.Pass("The main window identifies itself as Umnatha Network Monitor");
            }
            else
            {
                result = StepResult.Fail("The main window identifies itself as Umnatha Network Monitor", ExpectedWindowTitle, actualTitle);
            }

            return result;
        }

        private static StepResult CheckMiniGraphWindow(AppSession session)
        {
            StepResult result;

            try
            {
                Waits.UntilFound(
                    () => session.MiniGraphWindow,
                    MiniGraphTimeout,
                    "the mini graph window to appear (the fixture's settings.json turns it on)");

                result = StepResult.Pass("The mini graph window appears (ShowMiniGraph is on in the fixture settings)");
            }
            catch (TimeoutException timeoutException)
            {
                result = StepResult.Fail(
                    "The mini graph window appears (ShowMiniGraph is on in the fixture settings)",
                    "a top-level window titled 'Umnatha mini graph'",
                    timeoutException.Message);
            }

            return result;
        }

        // Best-effort: a WinUI ContentDialog's automation peer reports class name "ContentDialog".
        // A cold start against a freshly seeded fixture has no reason to show one, so this is a
        // sanity check that nothing on the startup path (a settings load failure, a migration
        // error surfaced to the user) put one up.
        private static StepResult CheckNoErrorDialog(AppSession session)
        {
            AutomationElement? dialog = session.MainWindow.FindFirstDescendant(
                conditionFactory => conditionFactory.ByClassName("ContentDialog"));
            StepResult result;

            if (dialog is null)
            {
                result = StepResult.Pass("No error dialog is present after a cold start");
            }
            else
            {
                result = StepResult.Fail(
                    "No error dialog is present after a cold start",
                    "no descendant with automation class name 'ContentDialog'",
                    $"found one named '{dialog.Name}'");
            }

            return result;
        }
    }
}
