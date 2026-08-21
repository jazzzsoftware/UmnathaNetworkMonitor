using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using Microsoft.Win32;
using NetworkMonitor.Core.Common;
using NetworkMonitor.UITests.Driving;
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

        // When the screen saver activates, Windows switches the input desktop to a separate one;
        // synthetic input aimed at the original desktop is then refused (SendInput ->
        // Win32Exception (5): Access is denied) and there is no foreground window at all
        // (GetForegroundWindow() returns zero). Confirmed live: an operator's screen saver fired
        // partway through an overnight run and turned 17 passed / 1 failed into 13 passed / 4
        // failed with no code change -- exactly the kind of non-deterministic, near-the-end
        // failure pattern that reads as flaky tests rather than an environment problem. Synthetic
        // input resets the idle timer while a run is actually driving the UI, so what happened
        // overnight was idle time accumulating *between* runs until the saver came up, and the
        // next run then started straight into it -- SPI_GETSCREENSAVERRUNNING below is the check
        // that would have caught that at the start rather than partway through.
        //
        // This check is the softer of the two: it looks ahead at whether the saver is on course
        // to fire mid-run, using how long *this* run's own registered phases are expected to take
        // (Program.cs's BuildPhases/SumExpectedDuration) rather than the plan's eventual
        // nine-phase target. At two phases that sum is well under a minute, so a normal desktop
        // default (15 minutes) is nowhere near a real risk; a fixed floor pinned to the eventual
        // target would refuse every run until then for no real reason, which is worse than the
        // problem it is meant to prevent.
        private const double ScreenSaverSafetyMarginMultiplier = 1.5;

        private const string DesktopRegistryKeyPath = @"Control Panel\Desktop";
        private const string ScreenSaveActiveValueName = "ScreenSaveActive";
        private const string ScreenSaveTimeOutValueName = "ScreenSaveTimeOut";
        private const uint SpiGetScreenSaverRunning = 0x0072;

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

        public static async Task<PreflightResult> CheckAsync(CancellationToken cancellationToken, TimeSpan expectedRunDuration)
        {
            List<string> blockers = new List<string>();
            bool elevated = IsElevated();

            if (!elevated)
            {
                blockers.Add("Not elevated. The suite installs and uninstalls the app; start it from an elevated terminal.");
            }

            string screenSaverRunningBlocker = FindScreenSaverRunningBlocker();

            if (screenSaverRunningBlocker.Length > 0)
            {
                blockers.Add(screenSaverRunningBlocker);
            }

            string screenSaverTimeoutBlocker = FindScreenSaverTimeoutBlocker(expectedRunDuration);

            if (screenSaverTimeoutBlocker.Length > 0)
            {
                blockers.Add(screenSaverTimeoutBlocker);
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

        // SPI_GETSCREENSAVERRUNNING answers "is the screen saver active on this desktop right
        // now" -- true for the seconds-to-minutes window between it starting and the operator (or
        // this check) dismissing it. A run must never start into that window: SendInput and
        // GetForegroundWindow are both already broken for as long as it lasts.
        private static string FindScreenSaverRunningBlocker()
        {
            string blocker = string.Empty;
            bool screenSaverRunning = false;
            bool queried = SystemParametersInfo(SpiGetScreenSaverRunning, 0, ref screenSaverRunning, 0);

            if (queried && screenSaverRunning)
            {
                blocker =
                    "The screen saver is currently running. It switches to a separate desktop, which refuses "
                    + "synthetic input aimed at the original one (SendInput -> Win32Exception (5): Access is "
                    + "denied) and leaves no foreground window at all (GetForegroundWindow() returns zero) -- "
                    + "every step from here on would fail. Move the mouse or press a key to dismiss it, then run "
                    + "again.";
            }

            return blocker;
        }

        // Distinct from FindScreenSaverRunningBlocker above: this refuses a run that has not hit
        // the screen saver yet but is on course to, because its configured timeout is shorter than
        // this specific run is expected to take. expectedRunDuration is the sum of every phase
        // actually registered for this run (Program.cs's BuildPhases/SumExpectedDuration), not a
        // fixed figure pinned to the plan's eventual nine-phase target -- so a short run today
        // does not get refused over a risk that does not yet exist, while a run that later grows
        // to approach a normal screen-saver default gets refused for a real reason. The 1.5x
        // margin on top absorbs a slower machine or a slightly-optimistic per-phase estimate
        // without demanding the operator hunt for an exact minimum. Never writes to the registry
        // -- only reads it and explains what the operator needs to change by hand, the same
        // principle amendment A already applies to a running app: refuse and explain, never touch
        // their configuration.
        private static string FindScreenSaverTimeoutBlocker(TimeSpan expectedRunDuration)
        {
            string blocker = string.Empty;

            using (RegistryKey? desktopKey = Registry.CurrentUser.OpenSubKey(DesktopRegistryKeyPath))
            {
                bool screenSaverEnabled = ReadRegistryFlag(desktopKey, ScreenSaveActiveValueName);

                if (screenSaverEnabled)
                {
                    int timeoutSeconds = ReadRegistryInt(desktopKey, ScreenSaveTimeOutValueName);
                    TimeSpan requiredMinimumTimeout = expectedRunDuration * ScreenSaverSafetyMarginMultiplier;

                    if (timeoutSeconds > 0 && TimeSpan.FromSeconds(timeoutSeconds) < requiredMinimumTimeout)
                    {
                        double timeoutMinutes = timeoutSeconds / 60.0;

                        blocker =
                            $"The screen saver is enabled with a {timeoutMinutes:0.#} minute timeout "
                            + $"(HKCU\\{DesktopRegistryKeyPath}\\{ScreenSaveTimeOutValueName}), but this run's "
                            + $"registered phases are expected to take about {expectedRunDuration.TotalSeconds:0}s, "
                            + $"which with a {ScreenSaverSafetyMarginMultiplier:0.#}x safety margin needs at least "
                            + $"{requiredMinimumTimeout.TotalMinutes:0.#} minutes of headroom -- otherwise the "
                            + "saver can activate mid-run, switch to a separate desktop, and start failing steps "
                            + "non-deterministically from that point on (SendInput -> Win32Exception (5): Access is "
                            + "denied, GetForegroundWindow() returning zero), in a way that looks like flaky tests "
                            + "rather than an environment problem. Disable the screen saver or raise its timeout to "
                            + $"at least {requiredMinimumTimeout.TotalMinutes:0.#} minutes for the duration of this "
                            + "run, then run again. This suite will not change that setting for you.";
                    }

                }

            }

            return blocker;
        }

        private static bool ReadRegistryFlag(RegistryKey? key, string valueName)
        {
            bool flagValue = false;

            if (key is not null)
            {
                string rawValue = key.GetValue(valueName) as string ?? string.Empty;

                flagValue = rawValue == "1";
            }

            return flagValue;
        }

        private static int ReadRegistryInt(RegistryKey? key, string valueName)
        {
            int intValue = 0;

            if (key is not null)
            {
                string rawValue = key.GetValue(valueName) as string ?? string.Empty;

                int.TryParse(rawValue, out intValue);
            }

            return intValue;
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

        // Fix round 3 (2026-08-20): Waits.cs claims every wait in this suite routes through it;
        // TrafficSessionExists's Process.WaitForExit(int) below was one of three places across
        // the suite that did not. Same Waits.Until(() => process.HasExited, ...) shape
        // AppUnderTest.WaitForExit(Application, TimeSpan) already used for the app process itself.
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

                    bool exited = WaitForProcessExit(query, LogmanQueryTimeout);

                    exists = exited && query.ExitCode == 0;
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

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SystemParametersInfo(uint action, uint parameter, ref bool value, uint updateFlags);
    }
}
