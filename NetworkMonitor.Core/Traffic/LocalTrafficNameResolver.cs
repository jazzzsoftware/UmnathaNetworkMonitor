namespace NetworkMonitor.Core.Traffic
{
    public static class LocalTrafficNameResolver
    {
        public static string Resolve(string remoteIp, IReadOnlyDictionary<string, string> namesByIp)
        {
            string resolved = remoteIp;

            if (namesByIp.TryGetValue(remoteIp, out string? name) && !string.IsNullOrWhiteSpace(name))
            {
                resolved = name;
            }

            return resolved;
        }
    }
}
