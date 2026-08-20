namespace NetworkMonitor.Core.Common
{
    public static class AppDataFolderResolver
    {
        public const string OverrideVariableName = "UMNATHA_DATA_FOLDER";

        private const string ProductFolderName = "UmnathaNetworkMonitor";

        public static string Resolve(string? overrideValue, string localApplicationDataPath)
        {
            string resolved;

            if (string.IsNullOrWhiteSpace(overrideValue))
            {
                resolved = Path.Combine(localApplicationDataPath, ProductFolderName);
            }
            else
            {
                resolved = overrideValue.Trim();
            }

            return resolved;
        }
    }
}
