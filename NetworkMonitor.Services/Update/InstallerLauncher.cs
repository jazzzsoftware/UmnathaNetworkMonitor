using System;
using System.Diagnostics;
using NetworkMonitor.Services.Platform;

namespace NetworkMonitor.Services.Update
{
    public sealed class InstallerLauncher : IInstallerLauncher
    {
        public void LaunchAndExit(string installerPath)
        {
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
