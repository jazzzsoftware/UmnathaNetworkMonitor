using System.Diagnostics;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using Microsoft.Win32;
using NetworkMonitor.Core.Common;
using NetworkMonitor.UITests.Driving;
using NetworkMonitor.UITests.Runner;

namespace NetworkMonitor.UITests.Fixtures
{
    // Launches and shuts down whichever build is actually under test — the call site chooses
    // explicitly between LaunchLocalBuild and LaunchInstalledBuild, there is no implicit default.
    //
    // Fix round 1 (2026-08-20): every phase through Task 11 drives LaunchLocalBuild. The plan as
    // originally written called for the installed release throughout, but the installed build
    // predates UMNATHA_DATA_FOLDER, the AutomationIds and the chart draw summaries this suite
    // depends on — a real run against it silently ignored the override and drove the operator's
    // real data folder while timing out looking for identifiers that do not exist in that build.
    // Only Task 12's UpdateLifecyclePhase uses LaunchInstalledBuild, and only there because the
    // installed build is the subject of that test rather than the instrument driving it.
    //
    // ShutDown prefers the tray Exit path because that is the only route that reaches OnExitApp
    // and checkpoints the WAL (MainWindow.xaml.cs: closing the main window alone leaves the app
    // running from the tray); Close()-then-Kill() is a deliberately blunter fallback for when the
    // tray path cannot be found or driven, and does not checkpoint the WAL — every branch below
    // logs which path was actually taken so a graceful exit is never indistinguishable from a
    // forced one.
    public static class AppUnderTest
    {
        private const string ExecutableFileName = "NetworkMonitor.exe";
        private const string TrayIconName = "Umnatha Network Monitor";
        private const string ShowHiddenIconsName = "Show hidden icons";
        private const string ExitMenuItemName = "Exit";

        // TrafficCollector.cs:13 names its kernel ETW session; anything short of the graceful tray
        // Exit never reaches the ct.Register(() => startedSession.Stop()) a graceful shutdown
        // relies on (TrafficCollector.cs:114), so the session survives the process and hangs the
        // next launch before it reaches its shell (reproduced at the 2026-08-20 Task 7 checkpoint,
        // and again at the fix-round-1 checkpoint via a Close()-only exit — amendment B).
        // CloseThenKill stops it itself on every path through that method, not only the Kill() one.
        private const string TrafficSessionName = "NetworkMonitorTraffic";

        private static readonly TimeSpan TrayInteractionTimeout = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan GracefulExitTimeout = TimeSpan.FromSeconds(15);

        // logman against a local ETW session answers in well under a second; ten seconds is
        // generous headroom for a slow trace subsystem, not a wait this is expected to hit.
        private static readonly TimeSpan LogmanTimeout = TimeSpan.FromSeconds(10);

        // Searches NetworkMonitor/bin/x64/Debug rather than assuming one fixed TFM-shaped path, so
        // a future TargetFramework bump does not silently break this; picks the most recently
        // built exe if more than one configuration is present. The single source of "where is the
        // local build" for LaunchLocalBuild, Preflight's override-marker check, and Program.cs's
        // --dump-tree diagnostic — none of those three keep their own copy of this search.
        public static string FindLocalBuildExecutablePath()
        {
            string executablePath = string.Empty;
            string? repositoryRoot = FindRepositoryRoot(AppContext.BaseDirectory);

            if (repositoryRoot is not null)
            {
                string searchRoot = Path.Combine(repositoryRoot, "NetworkMonitor", "bin", "x64", "Debug");

                if (Directory.Exists(searchRoot))
                {
                    string[] candidates = Directory.GetFiles(searchRoot, ExecutableFileName, SearchOption.AllDirectories);

                    if (candidates.Length > 0)
                    {
                        executablePath = candidates.OrderByDescending(File.GetLastWriteTimeUtc).First();
                    }

                }

            }

            return executablePath;
        }

        public static Application LaunchLocalBuild(string dataFolder)
        {
            ValidateDataFolder(dataFolder);

            string executablePath = FindLocalBuildExecutablePath();

            if (executablePath.Length == 0)
            {
                throw new InvalidOperationException(
                    "No locally built NetworkMonitor.exe was found under NetworkMonitor/bin/x64/Debug. Preflight "
                    + "should have caught this before this ran — build one first: dotnet build NetworkMonitor.slnx "
                    + "-c Debug -p:Platform=x64.");
            }

            Application application = LaunchExecutable(executablePath, dataFolder);

            return application;
        }

        public static Application LaunchInstalledBuild(string dataFolder)
        {
            ValidateDataFolder(dataFolder);

            string installLocation = ReadInstallLocation();

            if (installLocation.Length == 0)
            {
                throw new InvalidOperationException(
                    "Umnatha Network Monitor's InstallLocation could not be read from the uninstall registry key. "
                    + "Preflight should have caught a missing install before this ran.");
            }

            string executablePath = Path.Combine(installLocation, ExecutableFileName);
            Application application = LaunchExecutable(executablePath, dataFolder);

            return application;
        }

        // An app that has already exited is not a shutdown failure. UpdateLifecyclePhase drives
        // "Update now" and the app installs the update and exits on its own, then the phase's
        // finally block calls in here anyway. Hunting for a tray icon on that dead process failed,
        // fell through to Close()/Kill() and warned that the WAL was not checkpointed - three
        // alarming lines describing a process that had shut itself down normally minutes earlier.
        public static void ShutDown(Application application)
        {
            bool alreadyExited = HasAlreadyExited(application);

            if (alreadyExited)
            {
                Console.WriteLine("AppUnderTest.ShutDown: the process had already exited on its own; no shutdown was needed.");
                StopTrafficSession();
            }
            else
            {
                bool exitedGracefully = TryExitViaTray(application);

                if (exitedGracefully)
                {
                    Console.WriteLine("AppUnderTest.ShutDown: exited via the tray Exit menu item (WAL checkpointed).");
                }
                else
                {
                    Console.WriteLine("AppUnderTest.ShutDown: tray Exit path unavailable or failed; falling back to Close()/Kill().");
                    CloseThenKill(application);
                }

            }

        }

        // For callers that close the app themselves rather than through ShutDown.
        // UpdateLifecyclePhase does exactly that: CloseMainWindow only closes this app to the
        // tray, so it follows up with Kill, and neither route reaches OnExitApp where
        // TrafficCollector stops its own session. The installer relaunching the app after an
        // update is what makes that reachable - a real run left NetworkMonitorTraffic running
        // after the suite had already, correctly, confirmed it was gone at the earlier shutdown.
        public static void EnsureTrafficSessionStopped()
        {
            StopTrafficSession();
        }

        // Reading the state of a process that has gone can throw; still running is the safe
        // assumption, because it keeps the old Close()/Kill() path rather than skipping cleanup.
        private static bool HasAlreadyExited(Application application)
        {
            bool exited;

            try
            {
                exited = application.HasExited;
            }
            catch (Exception exception)
            {
                Console.WriteLine($"AppUnderTest: could not read the process state ({exception.Message}); assuming it is still running.");
                exited = false;
            }

            return exited;
        }

        private static void ValidateDataFolder(string dataFolder)
        {

            if (string.IsNullOrWhiteSpace(dataFolder))
            {
                throw new ArgumentException(
                    "Launch requires an explicit, existing data folder. AppDataFolderResolver treats a null, empty "
                    + "or whitespace override as \"no override\" and falls back to the operator's real folder, so a "
                    + "blank value here would silently point the driven app at the operator's live database.",
                    nameof(dataFolder));
            }

            if (!Directory.Exists(dataFolder))
            {
                throw new ArgumentException($"Launch was given a data folder that does not exist: {dataFolder}", nameof(dataFolder));
            }

        }

        private static Application LaunchExecutable(string executablePath, string dataFolder)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo(executablePath)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(executablePath) ?? string.Empty
            };

            startInfo.Environment[AppDataFolderResolver.OverrideVariableName] = dataFolder;

            Application application = Application.Launch(startInfo);

            return application;
        }

        private static string? FindRepositoryRoot(string startDirectory)
        {
            DirectoryInfo? currentDirectory = new DirectoryInfo(startDirectory);
            string? repositoryRoot = null;

            while (currentDirectory is not null && repositoryRoot is null)
            {

                if (File.Exists(Path.Combine(currentDirectory.FullName, "NetworkMonitor.slnx")))
                {
                    repositoryRoot = currentDirectory.FullName;
                }

                currentDirectory = currentDirectory.Parent;
            }

            return repositoryRoot;
        }

        private static string ReadInstallLocation()
        {
            string installLocation = string.Empty;

            foreach (RegistryView view in new RegistryView[] { RegistryView.Registry64, RegistryView.Registry32 })
            {

                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                using (RegistryKey? key = baseKey.OpenSubKey(Preflight.UninstallKeyPath))
                {

                    if (key is not null && installLocation.Length == 0)
                    {
                        installLocation = key.GetValue("InstallLocation") as string ?? string.Empty;
                    }

                }

            }

            return installLocation;
        }

        private static bool TryExitViaTray(Application application)
        {
            bool exited = false;

            try
            {

                using (UIA3Automation automation = new UIA3Automation())
                {
                    AutomationElement? trayIcon = FindTrayIcon(automation);

                    if (trayIcon is not null)
                    {
                        trayIcon.RightClick();

                        AutomationElement? exitMenuItem = WaitForNamedElement(automation, ExitMenuItemName, TrayInteractionTimeout);

                        if (exitMenuItem is not null)
                        {
                            exitMenuItem.Click();

                            exited = WaitForExit(application, GracefulExitTimeout);

                            if (!exited)
                            {
                                Console.WriteLine("AppUnderTest: clicked the tray Exit menu item, but the process did not exit within the timeout.");
                            }

                        }
                        else
                        {
                            Console.WriteLine("AppUnderTest: found the tray icon but not the Exit menu item within the timeout.");
                        }

                    }
                    else
                    {
                        Console.WriteLine("AppUnderTest: could not find the tray icon (directly or via 'Show hidden icons').");
                    }

                }

            }
            catch (Exception exception)
            {
                Console.WriteLine($"AppUnderTest: tray Exit path threw and was abandoned: {exception.Message}");
                exited = false;
            }

            return exited;
        }

        private static AutomationElement? FindTrayIcon(UIA3Automation automation)
        {
            AutomationElement desktop = automation.GetDesktop();
            AutomationElement? trayIcon = desktop.FindFirstDescendant(conditionFactory => conditionFactory.ByName(TrayIconName));

            if (trayIcon is null)
            {
                AutomationElement? overflowChevron = desktop.FindFirstDescendant(conditionFactory => conditionFactory.ByName(ShowHiddenIconsName));

                if (overflowChevron is not null)
                {
                    overflowChevron.Click();

                    AutomationElement overflowDesktop = automation.GetDesktop();

                    trayIcon = overflowDesktop.FindFirstDescendant(conditionFactory => conditionFactory.ByName(TrayIconName));
                }

            }

            return trayIcon;
        }

        // Waits.UntilFound throws on timeout; callers here expect null instead, so the timeout is
        // caught and converted back rather than propagated — the polling itself (and its single
        // Thread.Sleep) now lives only in Waits.
        private static AutomationElement? WaitForNamedElement(UIA3Automation automation, string name, TimeSpan timeout)
        {
            AutomationElement? found;

            try
            {
                found = Waits.UntilFound(
                    () => automation.GetDesktop().FindFirstDescendant(conditionFactory => conditionFactory.ByName(name)),
                    timeout,
                    $"the '{name}' element to appear");
            }
            catch (TimeoutException)
            {
                found = null;
            }

            return found;
        }

        private static bool WaitForExit(Application application, TimeSpan timeout)
        {
            bool exited;

            try
            {
                Waits.Until(() => application.HasExited, timeout, "the app process to exit");
                exited = true;
            }
            catch (TimeoutException)
            {
                exited = false;
            }

            return exited;
        }

        // Fix round 3 (2026-08-20): the same Waits.Until(() => X.HasExited, ...) shape as
        // WaitForExit(Application, TimeSpan) above, for a raw Process (logman) rather than a
        // FlaUI Application — Waits.cs claims every wait in this suite routes through it, and
        // TryRunLogman's Process.WaitForExit(int) below was the one place in this file that did
        // not.
        private static bool WaitForProcessExit(Process process, TimeSpan timeout)
        {
            bool exited;

            try
            {
                Waits.Until(() => process.HasExited, timeout, "the process to exit");
                exited = true;
            }
            catch (TimeoutException)
            {
                exited = false;
            }

            return exited;
        }

        private static void CloseThenKill(Application application)
        {

            try
            {

                if (!application.HasExited)
                {
                    application.Close();
                }

            }
            catch (Exception exception)
            {
                Console.WriteLine($"AppUnderTest: Close() threw and was ignored: {exception.Message}");
            }

            bool exited = WaitForExit(application, GracefulExitTimeout);

            if (exited)
            {
                Console.WriteLine("AppUnderTest: exited after Close() (WAL not necessarily checkpointed — Close() is not the graceful tray path).");
            }
            else
            {
                Console.WriteLine("AppUnderTest: did not exit after Close(); force-killing the process. The WAL was NOT checkpointed.");

                try
                {
                    application.Kill();
                }
                catch (Exception exception)
                {
                    Console.WriteLine($"AppUnderTest: Kill() threw and was ignored: {exception.Message}");
                }

            }

            // Fix round 1 finding: neither branch above is a confirmed graceful tray Exit (only
            // that path reaches OnExitApp and TrafficCollector's own session.Stop()), so the
            // session is stopped here unconditionally — a real run previously exited cleanly via
            // Close() alone and still orphaned the session, because this call used to be wired
            // only to the Kill() branch below it.
            StopTrafficSession();

        }

        // Best-effort and always reported, never thrown: a failed cleanup here must not mask the
        // Close()/Kill() outcome above, and Preflight's stale-session check is the backstop if
        // this does not succeed.
        //
        // The outcome is read from a follow-up query, not from the stop's exit code. 'logman stop'
        // exits 2 when the session does not exist, and that is the GOOD case - the app released it
        // on the way out - so keying the message off that exit code announced a clean shutdown as an
        // unconfirmed one, in the same words used for a genuine orphan. Two real runs produced that
        // identical line, one having leaked the session and one not, and only a manual logman query
        // told them apart.
        private static void StopTrafficSession()
        {
            TryRunLogman($"stop {TrafficSessionName} -ets");

            bool stillRunning = TryRunLogman($"query {TrafficSessionName} -ets");

            if (stillRunning)
            {
                Console.WriteLine(
                    $"AppUnderTest: the '{TrafficSessionName}' ETW session is STILL RUNNING after the stop. The next "
                    + $"launch will hang before its shell appears; stop it by hand: logman stop {TrafficSessionName} -ets");
            }
            else
            {
                Console.WriteLine($"AppUnderTest: confirmed the '{TrafficSessionName}' ETW session is not running.");
            }

        }

        private static bool TryRunLogman(string arguments)
        {
            bool succeeded;

            try
            {

                using (Process logman = new Process())
                {
                    logman.StartInfo = new ProcessStartInfo("logman", arguments)
                    {
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };

                    logman.Start();

                    bool exited = WaitForProcessExit(logman, LogmanTimeout);

                    succeeded = exited && logman.ExitCode == 0;
                }

            }
            catch (Exception exception)
            {
                Console.WriteLine($"AppUnderTest: running 'logman {arguments}' threw and was treated as failure: {exception.Message}");
                succeeded = false;
            }

            return succeeded;
        }
    }
}
