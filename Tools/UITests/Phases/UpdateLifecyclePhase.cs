using System.Diagnostics;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using Microsoft.Win32;
using NetworkMonitor.Core.Common;
using NetworkMonitor.Models.Update;
using NetworkMonitor.UITests.Driving;
using NetworkMonitor.UITests.Fixtures;
using NetworkMonitor.UITests.Runner;

namespace NetworkMonitor.UITests.Phases
{
    // Phase 09: the update lifecycle — uninstall what is installed, install the previous release,
    // let that build discover the newer one on its own, drive its banner, and prove the machine
    // ends up on the newer release. It is the only phase that changes anything outside the
    // throwaway fixture folder, and the only one whose subject is the INSTALLED build rather than
    // the local one: an update is a thing that happens to an installation.
    //
    // Because of that it is opt-in — `dotnet run --project Tools/UITests -- --all-with-update-lifecycle`
    // — and not part of a routine run. See Program.cs for why that decision was made rather than
    // registering it unconditionally.
    //
    // Two facts drive the shape of everything below:
    //
    // 1. The baseline build predates every automation identifier this suite added (Task 7), so its
    //    banner buttons are found BY NAME — "Update now", "Later", "Cancel". Do not "fix" these to
    //    use AutomationIds: the ids do not exist in the build being driven, and never will.
    // 2. The baseline build also predates UMNATHA_DATA_FOLDER, so it uses the operator's REAL data
    //    folder no matter what this suite sets. That is not a workaround, it is the reason
    //    RealDataGuard exists: the folder is copied aside before the first destructive act and
    //    restored in a finally, whatever happens in between.
    //
    // If that restore ever fails, the backup's location is printed in the loudest terms this suite
    // has — to the console and into the step's own failure message — and the run exits non-zero.
    // That is the single most important error path here.
    public static class UpdateLifecyclePhase
    {
        private const string UninstallExecutableName = "unins000.exe";
        private const string UninstallArguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART";

        private const string AppProcessName = "NetworkMonitor";
        private const string UpdateNowButtonName = "Update now";
        private const string InfoBarAutomationId = "UpdateBanner";

        private static readonly TimeSpan UninstallTimeout = TimeSpan.FromMinutes(3);

        // Two separate waits, because the first real run conflated them and failed on the wrong
        // one. A cold start of the baseline build is not a short thing here: it opens the
        // OPERATOR'S real database — 162 MB on the machine this was written against — and runs
        // DatabaseInitializer's baseline-then-migrate against it before the shell appears at all.
        private static readonly TimeSpan ShellTimeout = TimeSpan.FromMinutes(2);

        // Measured from the shell appearing, not from launch. UpdateCheckWorker waits 10 seconds
        // after startup before its first check (InitialDelay), and UpdateService then allows itself
        // 20 seconds to complete it — so the banner cannot appear sooner than about 30 seconds and
        // has no reason to take much longer. 90 seconds is that budget with room for a slow
        // network, and it now measures the check rather than the cold start that used to eat it.
        private static readonly TimeSpan BannerTimeout = TimeSpan.FromSeconds(90);

        // Downloading ~75 MB, verifying its SHA-256 and running a silent install, with the app
        // exiting partway through. Generous because the network is not this suite's to control.
        private static readonly TimeSpan UpdateTimeout = TimeSpan.FromMinutes(10);

        private static readonly TimeSpan ControlTimeout = TimeSpan.FromSeconds(15);

        // How long the close loop keeps sweeping for app processes before giving up on getting a
        // clean field. Generous: an installer that relaunches the app can take a moment to do it.
        private static readonly TimeSpan AppCloseTimeout = TimeSpan.FromSeconds(30);

        // CloseMainWindow only sends this app to the tray, so waiting long for an exit that is not
        // coming just delays the kill that actually works.
        private static readonly TimeSpan TrayCloseGrace = TimeSpan.FromSeconds(2);

        public static async Task<IReadOnlyList<StepResult>> RunAsync(PhaseContext context)
        {
            StepLog steps = new StepLog(context);

            // The fixture-driven app is still running from phase 01 and holds the install this
            // phase is about to remove. It is shut down here, and the session cleared, so the
            // runner's own teardown does not try to shut down a process that no longer exists.
            ShutDownFixtureApp(context);

            ReleasePair? releases = await ResolveReleasesAsync(steps);

            if (releases is not null)
            {
                await RunLifecycleAsync(context, steps, releases);
            }

            IReadOnlyList<StepResult> result = steps.Steps;

            return result;
        }

        private static async Task<ReleasePair?> ResolveReleasesAsync(StepLog steps)
        {
            const string stepName = "GitHub offers a release to update from and one to update to";
            ReleasePair? releases = null;

            try
            {
                releases = await ReleaseResolver.ResolveAsync(CancellationToken.None);

                steps.Add(StepResult.Pass(stepName));
            }
            catch (Exception failure)
            {
                steps.Add(StepResult.Fail(stepName, "two releases on GitHub, newest first", failure.Message));
            }

            return releases;
        }

        // Everything destructive lives inside this try. The finally restores the operator's data
        // folder whatever happened — including when the phase threw halfway through an install.
        private static async Task RunLifecycleAsync(PhaseContext context, StepLog steps, ReleasePair releases)
        {
            string backupFolder = string.Empty;

            try
            {
                backupFolder = RealDataGuard.CopyAside();

                steps.Add(StepResult.Pass($"The live data folder is copied aside to {backupFolder}"));

                CloseEveryAppProcess();

                bool uninstalled = RunUninstall(steps);

                if (uninstalled)
                {
                    bool baselineInstalled = await RunInstallBaselineAsync(steps, releases.Baseline);

                    if (baselineInstalled)
                    {
                        await RunUpdateFromBaselineAsync(steps, releases);
                    }

                }

            }
            catch (Exception failure)
            {
                steps.Add(StepResult.Fail("The update lifecycle runs to completion", "no unhandled failure", failure.Message));
            }
            finally
            {
                // The installer relaunches the app when it finishes, so this is not redundant with
                // the shutdown inside the update step — the process being closed here is usually a
                // different one, started after that step ended.
                CloseEveryAppProcess();

                steps.Add(RunRestore(backupFolder));
            }

        }

        private static bool RunUninstall(StepLog steps)
        {
            const string stepName = "The installed build is uninstalled";
            bool uninstalled = false;

            try
            {
                string uninstallerPath = ResolveUninstallerPath();

                RunProcessToCompletion(uninstallerPath, UninstallArguments, UninstallTimeout);

                Waits.Until(
                    () => Preflight.ReadInstalledVersion().Length == 0,
                    UninstallTimeout,
                    "the uninstall entry to disappear from the registry");

                steps.Add(StepResult.Pass(stepName));

                uninstalled = true;
            }
            catch (Exception failure)
            {
                steps.Add(StepResult.Fail(stepName, "no Umnatha Network Monitor install left in the registry", failure.Message));
            }

            return uninstalled;
        }

        private static async Task<bool> RunInstallBaselineAsync(StepLog steps, AvailableUpdate baseline)
        {
            string stepName = $"The baseline release {baseline.VersionTag} installs";
            bool installed = false;

            try
            {
                (bool Installed, string Message) outcome = await ReleaseInstaller.InstallAsync(baseline, CancellationToken.None);

                if (!outcome.Installed)
                {
                    steps.Add(StepResult.Fail(stepName, $"{baseline.VersionTag} installed silently", outcome.Message));
                }
                else
                {
                    string installedVersion = Preflight.ReadInstalledVersion();
                    bool matches = string.Equals(installedVersion, baseline.NormalizedVersion, StringComparison.Ordinal);

                    if (matches)
                    {
                        steps.Add(StepResult.Pass(stepName));

                        installed = true;
                    }
                    else
                    {
                        steps.Add(StepResult.Fail(stepName, $"the registry to report {baseline.NormalizedVersion}", $"it reports '{installedVersion}'"));
                    }

                }

            }
            catch (Exception failure)
            {
                steps.Add(StepResult.Fail(stepName, $"{baseline.VersionTag} installed silently", failure.Message));
            }

            return installed;
        }

        // The part that is actually about the product: an older build, left alone, notices there is
        // a newer one and can bring itself up to date.
        private static async Task RunUpdateFromBaselineAsync(StepLog steps, ReleasePair releases)
        {
            const string bannerStepName = "The baseline build finds the newer release and offers it";
            const string driveStepName = "Update now downloads, verifies and installs it";
            string arrivedStepName = $"The machine ends up on {releases.Target.VersionTag}";
            Application? application = null;

            try
            {
                // The installer starts the app itself when it finishes. Left alone, the launch
                // below would then hit App.xaml.cs's single-instance mutex, hand off to that
                // instance and exit immediately — leaving FlaUI holding a dead process id while
                // the app sits there on screen, perfectly usable and completely unreachable.
                //
                // That is exactly what the first run of this phase did: its evidence dump shows
                // the desktop containing "Umnatha Network Monitor", its "Traffic" nav item, and a
                // "Software update" banner with an "Update now" button, while the phase timed out
                // insisting no shell had appeared. Closing what the installer started means the
                // launch below is the one that wins the mutex, so the session owns the process it
                // is driving.
                CloseEveryAppProcess();

                application = AppUnderTest.LaunchInstalledBuild(RealDataFolder());

                AppSession session = new AppSession(application);

                WaitForShell(session);

                string bannerText = WaitForBanner(session, releases.Target.NormalizedVersion);

                steps.Add(StepResult.Pass(bannerStepName));
                Console.WriteLine($"UpdateLifecyclePhase: the banner reads \"{bannerText}\".");

                ClickByName(session, UpdateNowButtonName);

                // The app installs the update and exits on its own; the registry reporting the
                // target version is what "it worked" means, and waiting for it also covers the
                // download and the SHA-256 verification that precede it.
                Waits.Until(
                    () => string.Equals(Preflight.ReadInstalledVersion(), releases.Target.NormalizedVersion, StringComparison.Ordinal),
                    UpdateTimeout,
                    $"the registry to report {releases.Target.NormalizedVersion} after driving the update");

                steps.Add(StepResult.Pass(driveStepName));
                steps.Add(StepResult.Pass(arrivedStepName));
            }
            catch (Exception failure)
            {
                steps.Add(StepResult.Fail(driveStepName, $"the update to complete and leave {releases.Target.VersionTag} installed", failure.Message));
            }
            finally
            {

                if (application is not null)
                {
                    ShutDownQuietly(application);
                }

            }

            await Task.CompletedTask;
        }

        // The most important error path in the suite. If the operator's data cannot be put back,
        // saying so quietly in a report they might not read is not good enough.
        private static StepResult RunRestore(string backupFolder)
        {
            const string stepName = "The operator's data folder is restored";
            StepResult result;

            if (backupFolder.Length == 0)
            {
                result = StepResult.Skip(stepName, "Nothing was copied aside, so there is nothing to restore.");
            }
            else
            {

                bool restored;

                try
                {
                    restored = RealDataGuard.Restore(backupFolder);
                }
                catch (Exception failure)
                {
                    Console.WriteLine(BuildRestoreAlarm(backupFolder, failure.Message));

                    restored = false;
                }

                if (restored)
                {
                    result = StepResult.Pass(stepName);
                }
                else
                {
                    string alarm = BuildRestoreAlarm(backupFolder, "Restore reported failure.");

                    Console.WriteLine(alarm);

                    result = StepResult.Fail(stepName, "the real data folder restored from the backup", alarm);
                }

            }

            return result;
        }

        private static string BuildRestoreAlarm(string backupFolder, string detail)
        {
            string alarm =
                "\n"
                + "***********************************************************************\n"
                + "*** YOUR DATA FOLDER COULD NOT BE RESTORED AUTOMATICALLY            ***\n"
                + "***********************************************************************\n"
                + $"*** A complete copy of it is here, and has not been touched:\n"
                + $"***   {backupFolder}\n"
                + "*** Close Umnatha Network Monitor, then follow 'Recovering a stranded\n"
                + "*** backup' in Tools/UITests/README.md to put it back by hand.\n"
                + $"*** What went wrong: {detail}\n"
                + "***********************************************************************\n";

            return alarm;
        }

        // The shell, not merely a window: the splash is up while the database is migrated, and the
        // update check's own clock does not start until the app is properly running. Waited for
        // through the same landmark LaunchPhase uses, and tolerant of the exception AppSession
        // throws while no window carries the shell's title yet.
        private static void WaitForShell(AppSession session)
        {
            Waits.Until(
                () => ShellIsUp(session),
                ShellTimeout,
                "the baseline build's shell to appear (it migrates the operator's real database first, which is not quick)");
        }

        private static bool ShellIsUp(AppSession session)
        {
            bool up;

            try
            {
                up = session.MainWindow.FindFirstDescendant(conditionFactory => conditionFactory.ByName("Traffic")) is not null;
            }
            catch (Exception)
            {
                up = false;
            }

            return up;
        }

        // Returns the banner's own words, not the element's Name.
        //
        // An InfoBar's Name is its Title — "Software update" — and the sentence naming the version
        // is a separate Text child; the container the buttons sit in has no Name at all. Reading
        // the element found by the button's parent therefore produced an empty string and a failed
        // assertion against a banner that was on screen and perfectly correct. This reads every
        // Text in the window instead and returns the one naming the version, which is the thing
        // being asserted.
        private static string WaitForBanner(AppSession session, string expectedVersion)
        {
            string bannerText = string.Empty;

            Waits.Until(
                () =>
                {
                    bannerText = FindTextNaming(session, expectedVersion);

                    bool found = bannerText.Length > 0;

                    return found;
                },
                BannerTimeout,
                $"the update banner to name {expectedVersion} (UpdateCheckWorker waits 10s after startup, then "
                + "UpdateService allows itself 20s to check)");

            return bannerText;
        }

        private static string FindTextNaming(AppSession session, string expectedVersion)
        {
            string matched = string.Empty;

            try
            {
                Window mainWindow = session.MainWindow;
                AutomationElement[] texts = mainWindow.FindAllDescendants(
                    conditionFactory => conditionFactory.ByControlType(FlaUI.Core.Definitions.ControlType.Text));

                foreach (AutomationElement text in texts)
                {
                    string value = UiaText.NameOrEmpty(text);

                    if (value.Contains(expectedVersion, StringComparison.Ordinal))
                    {
                        matched = value;

                        break;
                    }

                }

            }
            catch (Exception)
            {
                matched = string.Empty;
            }

            return matched;
        }

        // By AutomationId first for a build new enough to have one, then by the button the banner
        // always carries. The baseline build predates Task 7's identifiers, so the second path is
        // the one that actually runs today — and must not be removed as redundant.
        private static AutomationElement? FindBanner(AppSession session)
        {
            AutomationElement? banner = null;

            try
            {
                Window mainWindow = session.MainWindow;

                banner = mainWindow.FindFirstDescendant(InfoBarAutomationId)
                    ?? mainWindow.FindFirstDescendant(conditionFactory => conditionFactory.ByName(UpdateNowButtonName))?.Parent;
            }
            catch (Exception)
            {
                banner = null;
            }

            return banner;
        }

        private static void ClickByName(AppSession session, string buttonName)
        {
            AutomationElement button = Waits.UntilFound(
                () => session.MainWindow.FindFirstDescendant(conditionFactory => conditionFactory.ByName(buttonName)),
                ControlTimeout,
                $"the banner's '{buttonName}' button (found by name: the baseline build has no automation identifiers)");

            button.Click();
        }

        private static void ShutDownFixtureApp(PhaseContext context)
        {

            if (context.Session is not null)
            {
                AppUnderTest.ShutDown(context.Session.Application);
                context.Session.Dispose();

                context.Session = null;
            }

        }

        private static void ShutDownQuietly(Application application)
        {

            try
            {
                AppUnderTest.ShutDown(application);
            }
            catch (Exception exception)
            {
                Console.WriteLine($"UpdateLifecyclePhase: could not shut the baseline build down cleanly: {exception.Message}");
            }

        }

        // Closes every NetworkMonitor process, not only the one this phase launched.
        //
        // Two things learned from the first real run. The update installer starts the app again
        // when it finishes, so by the time the restore ran there was a process this phase had
        // never held a handle to — and RealDataGuard refused to touch the data folder, exactly as
        // it should ("a checkpoint landing mid-copy can tear the .db/-wal pair"). And the operator
        // pointed out the same thing from the other end: an Inno Setup installer asked to run
        // silently while the app is up does not wait for it, it exits with code 5. So this runs
        // before the uninstall, before each install, and before the restore.
        // Closes every NetworkMonitor process and keeps going until none is left.
        //
        // Two corrections from real runs, both of which cost a restore:
        //
        // 1. Enumerating once is not enough. An installer that starts the app when it finishes can
        //    produce a process AFTER the snapshot was taken, and the first version of this method
        //    closed one process, reported success, and then watched RealDataGuard refuse the
        //    restore three seconds later because another was running.
        // 2. CloseMainWindow does not exit this app — MainWindow.xaml.cs closes it to the TRAY, and
        //    the process stays alive by design. So the polite request gets a short grace period and
        //    then the process is killed. That is safe here specifically because the data folder has
        //    already been copied aside, and the copy is what gets restored.
        private static void CloseEveryAppProcess()
        {
            DateTime deadline = DateTime.UtcNow + AppCloseTimeout;
            bool anyLeft = true;

            // Re-enumeration is deliberate (see 1 above), but a process that is still terminating
            // keeps appearing in it, so the same pid used to be announced on every round - seven
            // times for one process in a real run. The intent is reported once per pid, which also
            // keeps a genuinely new process started by the installer visible as its own line.
            HashSet<int> announcedPids = new HashSet<int>();

            while (anyLeft && DateTime.UtcNow < deadline)
            {
                Process[] running = Process.GetProcessesByName(AppProcessName);

                anyLeft = running.Length > 0;

                foreach (Process process in running)
                {

                    try
                    {

                        if (announcedPids.Add(process.Id))
                        {
                            Console.WriteLine($"UpdateLifecyclePhase: closing NetworkMonitor (pid {process.Id}) before touching the install or the data folder.");
                        }

                        process.CloseMainWindow();

                        bool exited = WaitForExit(process, TrayCloseGrace);

                        if (!exited)
                        {
                            process.Kill();
                            WaitForExit(process, TrayCloseGrace);
                        }

                    }
                    catch (Exception exception)
                    {
                        Console.WriteLine($"UpdateLifecyclePhase: could not close pid {process.Id}: {exception.Message}");
                    }
                    finally
                    {
                        process.Dispose();
                    }

                }

                anyLeft = Process.GetProcessesByName(AppProcessName).Length > 0;
            }

        }

        private static bool WaitForExit(Process process, TimeSpan timeout)
        {
            bool exited;

            try
            {
                Waits.Until(() => process.HasExited, timeout, "the app to exit");

                exited = true;
            }
            catch (TimeoutException)
            {
                exited = false;
            }

            return exited;
        }

        private static string ResolveUninstallerPath()
        {
            string installLocation = ReadInstallLocation();

            if (installLocation.Length == 0)
            {
                throw new InvalidOperationException(
                    "The uninstall registry key has no InstallLocation, so there is nothing to uninstall. Preflight "
                    + "should have established that the app is installed before this phase ran.");
            }

            string uninstallerPath = Path.Combine(installLocation, UninstallExecutableName);

            if (!File.Exists(uninstallerPath))
            {
                throw new InvalidOperationException($"No uninstaller was found at {uninstallerPath}.");
            }

            return uninstallerPath;
        }

        private static string ReadInstallLocation()
        {
            string installLocation = string.Empty;

            foreach (RegistryView view in new RegistryView[] { RegistryView.Registry64, RegistryView.Registry32 })
            {

                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                {

                    using (RegistryKey? key = baseKey.OpenSubKey(Preflight.UninstallKeyPath))
                    {

                        if (key is not null)
                        {
                            installLocation = key.GetValue("InstallLocation") as string ?? string.Empty;
                        }

                    }

                }

                if (installLocation.Length > 0)
                {
                    break;
                }

            }

            return installLocation;
        }

        // The operator's real folder, resolved the same way RealDataGuard resolves it. The baseline
        // build would use this folder whatever this suite passed — it predates the override — so
        // passing it explicitly is honesty about where that build is about to write, not a redirect.
        private static string RealDataFolder()
        {
            string localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string folder = AppDataFolderResolver.Resolve(null, localApplicationData);

            return folder;
        }

        private static void RunProcessToCompletion(string executablePath, string arguments, TimeSpan timeout)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo(executablePath)
            {
                Arguments = arguments,
                UseShellExecute = false
            };

            using (Process? process = Process.Start(startInfo))
            {

                if (process is null)
                {
                    throw new InvalidOperationException($"{executablePath} did not start.");
                }

                Waits.Until(() => process.HasExited, timeout, $"{Path.GetFileName(executablePath)} to finish");

                // Inno Setup's uninstaller spawns a copy of itself and the first process exits
                // immediately, so its exit code says nothing useful. The registry check the caller
                // makes next is the real assertion.
            }

        }
    }
}
