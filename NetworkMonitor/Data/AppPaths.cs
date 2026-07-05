namespace NetworkMonitor.Data
{
    public static class AppPaths
    {
        public static string AppDataFolder =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "UmnathaNetworkMonitor");
    }
}
