using System.Diagnostics;
using System.Security.Principal;
using System.Text;
using System.Threading;
using Microsoft.Win32;
using NetworkMonitor.Core.Common;
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

            string localBuildBlocker = FindLocalBuildBlocker();

            if (localBuildBlocker.Length > 0)
            {
                blockers.Add(localBuildBlocker);
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
        // session surviving with nothing behind it is almost always AppUnderTest.ShutDown's own
        // Close()/Kill() fallback failing to clean up, or an earlier run of this suite being killed itself
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

        // Fix round 1 (2026-08-20): a real run drove the installed release — which predates
        // UMNATHA_DATA_FOLDER — because every phase through Task 11 launches AppUnderTest.
        // LaunchLocalBuild, and the local build that happened to exist on disk was stale. The
        // shell was never found (a 45s timeout) and, silently, the fixture was bypassed entirely:
        // the app fell back to the operator's real data folder and wrote to it for the run's
        // whole duration. This check exists so that failure is a Preflight refusal, not a
        // teardown-time discovery. AppDataFolderResolver.OverrideVariableName is a const, so its
        // literal value is baked into every assembly that references it — including
        // NetworkMonitor.Core.dll itself, which declares it — as UTF-16, the encoding every .NET
        // string literal is stored in; confirmed present in a real build's Core.dll before this
        // check was written.
        private static string FindLocalBuildBlocker()
        {
            string blocker = string.Empty;
            string executablePath = AppUnderTest.FindLocalBuildExecutablePath();

            if (executablePath.Length == 0)
            {
                blocker =
                    "No locally built NetworkMonitor.exe was found under NetworkMonitor/bin/x64/Debug. The phases "
                    + "drive a local build, not the installed release — build one first: dotnet build "
                    + "NetworkMonitor.slnx -c Debug -p:Platform=x64.";
            }
            else
            {
                string overrideMarkerBlocker = FindMissingOverrideMarkerBlocker(executablePath);

                if (overrideMarkerBlocker.Length > 0)
                {
                    blocker = overrideMarkerBlocker;
                }

            }

            return blocker;
        }

        private static string FindMissingOverrideMarkerBlocker(string executablePath)
        {
            string blocker = string.Empty;
            string? buildFolder = Path.GetDirectoryName(executablePath);
            string coreDllPath = buildFolder is null ? string.Empty : Path.Combine(buildFolder, "NetworkMonitor.Core.dll");

            if (coreDllPath.Length == 0 || !File.Exists(coreDllPath))
            {
                blocker =
                    $"Could not find NetworkMonitor.Core.dll next to {executablePath} to confirm the build carries "
                    + "the UMNATHA_DATA_FOLDER override. Rebuild: dotnet build NetworkMonitor.slnx -c Debug -p:Platform=x64.";
            }
            else if (!BinaryContainsOverrideMarker(coreDllPath))
            {
                blocker =
                    $"{executablePath} predates the UMNATHA_DATA_FOLDER override (NetworkMonitor.Core.dll next to "
                    + "it does not carry the marker) and would silently drive the operator's real data folder "
                    + "instead of the fixture. Rebuild: dotnet build NetworkMonitor.slnx -c Debug -p:Platform=x64.";
            }

            return blocker;
        }

        private static bool BinaryContainsOverrideMarker(string assemblyPath)
        {
            byte[] assemblyBytes = File.ReadAllBytes(assemblyPath);
            byte[] markerBytes = Encoding.Unicode.GetBytes(AppDataFolderResolver.OverrideVariableName);
            bool found = assemblyBytes.AsSpan().IndexOf(markerBytes.AsSpan()) >= 0;

            return found;
        }
    }
}
