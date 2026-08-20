using System.Diagnostics;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using Microsoft.Win32;
using NetworkMonitor.Core.Common;
using NetworkMonitor.UITests.Runner;

namespace NetworkMonitor.UITests.Fixtures
{
    // Launches and shuts down the installed release, never a dev build — InstallLocation comes
    // from the same Inno Setup uninstall key Preflight already reads. ShutDown prefers the tray
    // Exit path because that is the only route that reaches OnExitApp and checkpoints the WAL
    // (MainWindow.xaml.cs: closing the main window alone leaves the app running from the tray);
    // Close()-then-Kill() is a deliberately blunter fallback for when the tray path cannot be
    // found or driven.
    public static class InstalledApp
    {
        private const string ExecutableFileName = "NetworkMonitor.exe";
        private const string TrayIconName = "Umnatha Network Monitor";
        private const string ShowHiddenIconsName = "Show hidden icons";
        private const string ExitMenuItemName = "Exit";

        private static readonly TimeSpan TrayInteractionTimeout = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan GracefulExitTimeout = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);

        public static Application Launch(string dataFolder)
        {
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

            if (!exitedGracefully)
            {
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
                        }

                    }

                }

            }
            catch (Exception)
            {
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

        private static AutomationElement? WaitForNamedElement(UIA3Automation automation, string name, TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            AutomationElement? found = null;

            while (DateTime.UtcNow < deadline && found is null)
            {
                AutomationElement desktop = automation.GetDesktop();

                found = desktop.FindFirstDescendant(conditionFactory => conditionFactory.ByName(name));

                if (found is null)
                {
                    Thread.Sleep(PollInterval);
                }

            }

            return found;
        }

        private static bool WaitForExit(Application application, TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            bool exited = application.HasExited;

            while (DateTime.UtcNow < deadline && !exited)
            {
                Thread.Sleep(PollInterval);
                exited = application.HasExited;
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
            catch (Exception)
            {
            }

            bool exited = WaitForExit(application, GracefulExitTimeout);

            if (!exited)
            {

                try
                {
                    application.Kill();
                }
                catch (Exception)
                {
                }

            }

        }
    }
}
