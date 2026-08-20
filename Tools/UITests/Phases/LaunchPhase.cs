using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using NetworkMonitor.UITests.Driving;
using NetworkMonitor.UITests.Fixtures;
using NetworkMonitor.UITests.Runner;

namespace NetworkMonitor.UITests.Phases
{
    // Phase 01: a cold start against the seeded fixture. abortsRun is true (wired in Program.cs)
    // because every later phase assumes a working, driveable app — if this phase's one throwing
    // check never resolves, nothing after it can mean anything.
    //
    // Only "the splash closes and the shell appears" is allowed to throw and abort the run; the
    // rest (splash window gone, title, mini graph, no error dialog) are recorded as pass/fail and
    // do not stop later phases, because none of them means the app itself is broken.
    public static class LaunchPhase
    {
        // MainWindow.xaml:7 sets this literally; the running build does not embed a version number
        // in the window title (confirmed by reading MainWindow.xaml and App.xaml.cs — the version
        // only appears on the Settings page, via SettingsPage.xaml.cs's VersionText). This check is
        // therefore "the window identifies itself as the real app", not a version comparison; the
        // installed version itself is already captured independently in the report's Environment
        // section (RunEnvironment.AppVersionBefore/After, both read from the registry).
        private const string ExpectedWindowTitle = "Umnatha Network Monitor";

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
            List<StepResult> steps = new List<StepResult>();
            Application application = InstalledApp.Launch(context.DataFolder);
            AppSession session = new AppSession(application);

            // Assigned before the throwing wait below so Program.cs can still shut the app down
            // (and, on a kill, stop the orphaned ETW session — amendment B) even if the shell never
            // becomes ready.
            context.Session = session;

            Waits.Until(
                () => ShellIsReady(session),
                ShellReadyTimeout,
                "the splash screen to close and the main shell (Traffic/Devices/Reports/Settings navigation) to appear");

            steps.Add(StepResult.Pass("The splash screen closes and the main shell appears"));
            steps.Add(CheckSplashWindowIsGone(session));
            steps.Add(CheckWindowTitle(session));
            steps.Add(CheckMiniGraphWindow(session));
            steps.Add(CheckNoErrorDialog(session));

            IReadOnlyList<StepResult> result = steps;
            Task<IReadOnlyList<StepResult>> completed = Task.FromResult(result);

            return completed;
        }

        private static bool ShellIsReady(AppSession session)
        {
            Window mainWindow = session.MainWindow;
            bool ready = mainWindow.FindFirstDescendant("TrafficNavItem") is not null
                && mainWindow.FindFirstDescendant("DevicesNavItem") is not null
                && mainWindow.FindFirstDescendant("ReportsNavItem") is not null
                && mainWindow.FindFirstDescendant("SettingsNavItem") is not null;

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
