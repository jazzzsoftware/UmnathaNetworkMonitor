using System.Diagnostics;

namespace NetworkMonitor.Services.Platform
{
    public class WindowsStartupService
    {
        private const string TaskName = "Umnatha Network Monitor";

        public async Task<bool> IsEnabledAsync()
        {
            int exitCode = await RunSchTasksAsync($"/query /tn \"{TaskName}\"");
            bool enabled = exitCode == 0;

            return enabled;
        }

        public async Task EnableAsync()
        {
            string exePath = Environment.ProcessPath ?? string.Empty;

            if (!string.IsNullOrEmpty(exePath))
            {
                string arguments = $"/create /tn \"{TaskName}\" /tr \"\\\"{exePath}\\\" --minimized\" /sc onlogon /rl highest /f";

                await RunSchTasksAsync(arguments);
            }

        }

        public async Task DisableAsync()
        {
            await RunSchTasksAsync($"/delete /tn \"{TaskName}\" /f");
        }

        private static async Task<int> RunSchTasksAsync(string arguments)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            int exitCode = -1;

            using (Process? process = Process.Start(startInfo))
            {

                if (process is not null)
                {
                    Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
                    Task<string> standardError = process.StandardError.ReadToEndAsync();

                    await process.WaitForExitAsync();
                    await Task.WhenAll(standardOutput, standardError);

                    exitCode = process.ExitCode;
                }

            }

            return exitCode;
        }
    }
}
