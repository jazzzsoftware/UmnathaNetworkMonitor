using System.Security.Principal;
using Microsoft.Win32;

namespace NetworkMonitor.UITests.Runner
{
    public static class Preflight
    {
        public const string UninstallKeyPath =
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{7074c3a8-a61b-4e4a-9e6c-dedc9a62ae94}_is1";

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

        public static PreflightResult Check()
        {
            List<string> blockers = new List<string>();

            if (!IsElevated())
            {
                blockers.Add("Not elevated. The suite installs and uninstalls the app; start it from an elevated terminal.");
            }

            string installedVersion = ReadInstalledVersion();

            if (installedVersion.Length == 0)
            {
                blockers.Add(
                    "Umnatha Network Monitor is not installed. The suite drives the installed release, "
                    + "not a dev build. Install the latest release first — see Tools/UITests/README.md.");
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
    }
}
