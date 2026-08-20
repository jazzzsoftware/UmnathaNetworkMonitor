using System.Diagnostics;
using System.Security.Principal;
using System.Threading;
using Microsoft.Win32;
using NetworkMonitor.UITests.Fixtures;

namespace NetworkMonitor.UITests.Runner
{
    public static class Preflight
    {
        public const string UninstallKeyPath =
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{7074c3a8-a61b-4e4a-9e6c-dedc9a62ae94}_is1";

        // The process Application.Launch starts (NetworkMonitor.exe, App.xaml.cs's single-instance
        // mutex hands off to it rather than starting a second copy) and the kernel ETW session it
        // opens (TrafficCollector.cs:13) — amendments A and B from the 2026-08-20 Task 7 checkpoint.
        private const string RunningProcessName = "NetworkMonitor";
        private const string TrafficSessionName = "NetworkMonitorTraffic";
        private const string StopTrafficSessionCommand = "logman stop NetworkMonitorTraffic -ets";

        // logman against a local ETW session answers in well under a second; ten seconds is
        // generous headroom for a slow trace subsystem, not a wait this check expects to hit.
        private static readonly TimeSpan LogmanQueryTimeout = TimeSpan.FromSeconds(10);

        private const long RequiredFreeBytes = 3L * 1024L * 1024L * 1024L;

        // Matches all three suffixes RealDataGuard can leave behind: a copy of the operator's
        // real data (backup), a validated copy waiting to be swapped in (restore-staging), or
        // the pre-swap original waiting to be discarded (displaced). None of them are data-losing
        // by themselves, but each is a full copy of the operator's history and none should be
        // left lying around silently.
        private static readonly string[] StrandedFolderPatterns =
        {
            "UmnathaNetworkMonitor.uitest-backup-*",
            "UmnathaNetworkMonitor.uitest-restore-staging-*",
            "UmnathaNetworkMonitor.uitest-displaced-*"
        };

        public static async Task<PreflightResult> CheckAsync(CancellationToken cancellationToken)
        {
            List<string> blockers = new List<string>();
            bool elevated = IsElevated();

            if (!elevated)
            {
                blockers.Add("Not elevated. The suite installs and uninstalls the app; start it from an elevated terminal.");
            }

            int[] runningProcessIds = FindRunningProcessIds();

            if (runningProcessIds.Length > 0)
            {
                string processIdList = string.Join(", ", runningProcessIds);
                string processIdWord = runningProcessIds.Length == 1 ? "id" : "ids";

                blockers.Add(
                    $"Umnatha Network Monitor is already running (process {processIdWord} {processIdList}). A second "
                    + "launch would hand off to it (App.xaml.cs's single-instance mutex) and drive your real "
                    + "database instead of the fixture. Exit it from the tray icon — right-click it, then Exit — "
                    + "before running this suite. The runner will not close it for you.");
            }
            else
            {
                string staleSessionBlocker = FindStaleTrafficSessionBlocker();

                if (staleSessionBlocker.Length > 0)
                {
                    blockers.Add(staleSessionBlocker);
                }

            }

            string installedVersion = ReadInstalledVersion();

            if (installedVersion.Length == 0)
            {

                if (elevated)
                {
                    (bool installed, string installMessage) = await ReleaseInstaller.EnsureInstalledAsync(cancellationToken);

                    Console.WriteLine(installMessage);

                    if (!installed)
                    {
                        blockers.Add(installMessage);
                    }

                }
                else
                {
                    blockers.Add(
                        "Umnatha Network Monitor is not installed, and the suite cannot install it without "
                        + "elevation. Re-run from an elevated terminal so it can acquire and install the latest "
                        + "release itself — see Tools/UITests/README.md.");
                }

            }

            string strandedBackup = FindStrandedBackup();

            if (strandedBackup.Length > 0)
            {
                blockers.Add(
                    "A previous run left a data-folder backup, restore-staging or displaced-original folder at "
                    + $"{strandedBackup}. Restore or delete it before running again — this suite will not run while "
                    + "your history is parked.");
            }

            DriveInfo systemDrive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\");

            if (systemDrive.AvailableFreeSpace < RequiredFreeBytes)
            {
                blockers.Add(
                    $"Only {systemDrive.AvailableFreeSpace / 1024 / 1024} MB free on {systemDrive.Name}. "
                    + "The update phase downloads two ~75 MB installers and copies the data folder aside; 3 GB is the floor.");
            }

            PreflightResult result = new PreflightResult(blockers);

            return result;
        }

        public static string ReadInstalledVersion()
        {
            string version = string.Empty;

            foreach (RegistryView view in new RegistryView[] { RegistryView.Registry64, RegistryView.Registry32 })
            {

                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                using (RegistryKey? key = baseKey.OpenSubKey(UninstallKeyPath))
                {

                    if (key is not null && version.Length == 0)
                    {
                        version = key.GetValue("DisplayVersion") as string ?? string.Empty;
                    }

                }

            }

            return version;
        }

        private static bool IsElevated()
        {

            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                WindowsPrincipal principal = new WindowsPrincipal(identity);

                bool elevated = principal.IsInRole(WindowsBuiltInRole.Administrator);

                return elevated;
            }

        }

        private static string FindStrandedBackup()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string stranded = string.Empty;

            foreach (string pattern in StrandedFolderPatterns)
            {
                string[] candidates = Directory.GetDirectories(localAppData, pattern);

                if (candidates.Length > 0 && stranded.Length == 0)
                {
                    stranded = candidates[0];
                }

            }

            return stranded;
        }

        private static int[] FindRunningProcessIds()
        {
            Process[] processes = Process.GetProcessesByName(RunningProcessName);
            int[] processIds = new int[processes.Length];

            for (int processIndex = 0; processIndex < processes.Length; processIndex++)
            {
                processIds[processIndex] = processes[processIndex].Id;
                processes[processIndex].Dispose();
            }

            return processIds;
        }

        // Only reached when no NetworkMonitor process is running — amendment B's second half. A
        // session surviving with nothing behind it is almost always InstalledApp.ShutDown's own
        // Kill() fallback failing to clean up, or an earlier run of this suite being killed itself
        // (Ctrl+C, a crashed host); either way TrafficCollector.StopOrphanedSession's own
        // attach-and-Stop() on the app's next launch is the code that reproducibly does not
        // succeed after a kill, so the runner has to clear it before that launch is attempted.
        private static string FindStaleTrafficSessionBlocker()
        {
            string blocker = string.Empty;

            if (TrafficSessionExists())
            {
                blocker =
                    $"The '{TrafficSessionName}' ETW trace session is running with no {RunningProcessName} process "
                    + "behind it — most likely a previous hard kill left it orphaned. The next launch hangs before "
                    + $"it reaches its shell while this is present. Stop it first: `{StopTrafficSessionCommand}`.";
            }

            return blocker;
        }

        private static bool TrafficSessionExists()
        {
            bool exists;

            try
            {

                using (Process query = new Process())
                {
                    query.StartInfo = new ProcessStartInfo("logman", $"query {TrafficSessionName} -ets")
                    {
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };

                    query.Start();
                    query.WaitForExit((int)LogmanQueryTimeout.TotalMilliseconds);

                    exists = query.ExitCode == 0;
                }

            }
            catch (Exception exception)
            {
                Console.WriteLine($"Preflight: 'logman query {TrafficSessionName} -ets' threw and was treated as \"no session\": {exception.Message}");
                exists = false;
            }

            return exists;
        }
    }
}
