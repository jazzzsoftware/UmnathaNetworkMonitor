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
    // Launches and shuts down the installed release, never a dev build — InstallLocation comes
    // from the same Inno Setup uninstall key Preflight already reads. ShutDown prefers the tray
    // Exit path because that is the only route that reaches OnExitApp and checkpoints the WAL
    // (MainWindow.xaml.cs: closing the main window alone leaves the app running from the tray);
    // Close()-then-Kill() is a deliberately blunter fallback for when the tray path cannot be
    // found or driven, and does not checkpoint the WAL — every branch below logs which path was
    // actually taken so a graceful exit is never indistinguishable from a forced one.
    public static class InstalledApp
    {
        private const string ExecutableFileName = "NetworkMonitor.exe";
        private const string TrayIconName = "Umnatha Network Monitor";
        private const string ShowHiddenIconsName = "Show hidden icons";
        private const string ExitMenuItemName = "Exit";

        // TrafficCollector.cs:13 names its kernel ETW session; a hard Kill() never reaches the
        // ct.Register(() => startedSession.Stop()) that a graceful shutdown relies on
        // (TrafficCollector.cs:114), so the session survives the process and hangs the next launch
        // before it reaches its shell (reproduced twice at the 2026-08-20 Task 7 checkpoint —
        // amendment B). CloseThenKill stops it itself whenever it actually had to kill.
        private const string TrafficSessionName = "NetworkMonitorTraffic";

        private static readonly TimeSpan TrayInteractionTimeout = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan GracefulExitTimeout = TimeSpan.FromSeconds(15);

        // logman against a local ETW session answers in well under a second; ten seconds is
        // generous headroom for a slow trace subsystem, not a wait this is expected to hit.
        private static readonly TimeSpan LogmanTimeout = TimeSpan.FromSeconds(10);

        public static Application Launch(string dataFolder)
        {

            if (string.IsNullOrWhiteSpace(dataFolder))
            {
                throw new ArgumentException(
                    "Launch requires an explicit, existing data folder. AppDataFolderResolver treats a null, empty or "
                    + "whitespace override as \"no override\" and falls back to the operator's real folder, so a blank "
                    + "value here would silently point the driven app at the operator's live database.",
                    nameof(dataFolder));
            }

            if (!Directory.Exists(dataFolder))
            {
                throw new ArgumentException($"Launch was given a data folder that does not exist: {dataFolder}", nameof(dataFolder));
            }

            string installLocation = ReadInstallLocation();

            if (installLocation.Length == 0)
            {
                throw new InvalidOperationException(
                    "Umnatha Network Monitor's InstallLocation could not be read from the uninstall registry key. "
                    + "Preflight should have caught a missing install before this ran.");
            }

            string executablePath = Path.Combine(installLocation, ExecutableFileName);
            ProcessStartInfo startInfo = new ProcessStartInfo(executablePath)
            {
                UseShellExecute = false,
                WorkingDirectory = installLocation
            };

            startInfo.Environment[AppDataFolderResolver.OverrideVariableName] = dataFolder;

            Application application = Application.Launch(startInfo);

            return application;
        }

        public static void ShutDown(Application application)
        {
            bool exitedGracefully = TryExitViaTray(application);

            if (exitedGracefully)
            {
                Console.WriteLine("InstalledApp.ShutDown: exited via the tray Exit menu item (WAL checkpointed).");
            }
            else
            {
                Console.WriteLine("InstalledApp.ShutDown: tray Exit path unavailable or failed; falling back to Close()/Kill().");
                CloseThenKill(application);
            }

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
                                Console.WriteLine("InstalledApp: clicked the tray Exit menu item, but the process did not exit within the timeout.");
                            }

                        }
                        else
                        {
                            Console.WriteLine("InstalledApp: found the tray icon but not the Exit menu item within the timeout.");
                        }

                    }
                    else
                    {
                        Console.WriteLine("InstalledApp: could not find the tray icon (directly or via 'Show hidden icons').");
                    }

                }

            }
            catch (Exception exception)
            {
                Console.WriteLine($"InstalledApp: tray Exit path threw and was abandoned: {exception.Message}");
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
                Console.WriteLine($"InstalledApp: Close() threw and was ignored: {exception.Message}");
            }

            bool exited = WaitForExit(application, GracefulExitTimeout);

            if (exited)
            {
                Console.WriteLine("InstalledApp: exited after Close() (WAL not necessarily checkpointed — Close() is not the graceful tray path).");
            }
            else
            {
                Console.WriteLine("InstalledApp: did not exit after Close(); force-killing the process. The WAL was NOT checkpointed.");

                try
                {
                    application.Kill();
                }
                catch (Exception exception)
                {
                    Console.WriteLine($"InstalledApp: Kill() threw and was ignored: {exception.Message}");
                }

                StopTrafficSessionAfterKill();
            }

        }

        // Amendment B: whenever ShutDown had to kill rather than exit gracefully, the kernel ETW
        // session it owned is orphaned (see the TrafficSessionName field comment) and must be
        // stopped here — the app's own next-launch cleanup (TrafficCollector.StopOrphanedSession)
        // does not reliably succeed against a session left behind by a kill. Best-effort and
        // always reported, never thrown: a failed cleanup here must not mask the Kill() outcome
        // above, and Preflight's stale-session check is the backstop if this does not succeed.
        private static void StopTrafficSessionAfterKill()
        {
            bool stopped = TryRunLogman($"stop {TrafficSessionName} -ets");

            if (stopped)
            {
                Console.WriteLine($"InstalledApp: stopped the orphaned '{TrafficSessionName}' ETW session left behind by the kill.");
            }
            else
            {
                Console.WriteLine(
                    $"InstalledApp: could not confirm the '{TrafficSessionName}' ETW session was stopped after the "
                    + $"kill. If the next launch hangs before its shell appears, stop it by hand: logman stop {TrafficSessionName} -ets");
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
                    logman.WaitForExit((int)LogmanTimeout.TotalMilliseconds);

                    succeeded = logman.ExitCode == 0;
                }

            }
            catch (Exception exception)
            {
                Console.WriteLine($"InstalledApp: running 'logman {arguments}' threw and was treated as failure: {exception.Message}");
                succeeded = false;
            }

            return succeeded;
        }
    }
}
