using NetworkMonitor.Core.Common;

namespace NetworkMonitor.Services.Data
{
    public static class AppPaths
    {
        public static string AppDataFolder =>
            AppDataFolderResolver.Resolve(
                Environment.GetEnvironmentVariable(AppDataFolderResolver.OverrideVariableName),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
    }
}
