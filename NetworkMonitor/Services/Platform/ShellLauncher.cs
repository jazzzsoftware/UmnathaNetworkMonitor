using System.Diagnostics;

namespace NetworkMonitor.Services.Platform
{
    public static class ShellLauncher
    {
        public static void Open(string path)
        {

            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                };

                Process.Start(startInfo);
            }
            catch (Exception exception)
            {
                AppLog.Error("ShellLauncher.Open", exception);
            }

        }
    }
}
