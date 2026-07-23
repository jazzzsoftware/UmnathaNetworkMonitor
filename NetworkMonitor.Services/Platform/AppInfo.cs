using System.Reflection;

namespace NetworkMonitor.Services.Platform
{
    public static class AppInfo
    {
        public static string GetVersion()
        {
            Assembly assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            string? informationalVersion = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;
            string version;

            if (string.IsNullOrWhiteSpace(informationalVersion))
            {
                Version? assemblyVersion = assembly.GetName().Version;
                version = assemblyVersion is null ? "0.0.0" : assemblyVersion.ToString();
            }
            else
            {
                int metadataIndex = informationalVersion.IndexOf('+');
                version = metadataIndex >= 0 ? informationalVersion.Substring(0, metadataIndex) : informationalVersion;
            }

            return version;
        }
    }
}
