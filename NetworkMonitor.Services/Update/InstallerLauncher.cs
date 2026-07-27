using System;
using System.Diagnostics;
using NetworkMonitor.Services.Platform;

namespace NetworkMonitor.Services.Update
{
    public sealed class InstallerLauncher : IInstallerLauncher
    {
        public void LaunchAndExit(string installerPath, Action? beforeExit)
        {
            // Environment.Exit skips the window's closing path, so the caller supplies the graceful
            // shutdown (final traffic flush, WAL checkpoint, tray icon, window placement) here.
            if (beforeExit is not null)
            {

                try
                {
                    beforeExit();
                }
                catch (Exception exception)
                {
                    AppLog.Error("InstallerLauncher.BeforeExit", exception);
                }

            }

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = installerPath,
                Arguments = "/SILENT /SUPPRESSMSGBOXES /NORESTART",
                UseShellExecute = true
            };

            AppLog.Info($"Launching update installer: {installerPath}");
            Process.Start(startInfo);

            Environment.Exit(0);
        }
    }
}
