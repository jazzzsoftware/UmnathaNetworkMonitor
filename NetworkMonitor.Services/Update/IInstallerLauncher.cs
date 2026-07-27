using System;

namespace NetworkMonitor.Services.Update
{
    public interface IInstallerLauncher
    {
        void LaunchAndExit(string installerPath, Action? beforeExit);
    }
}
