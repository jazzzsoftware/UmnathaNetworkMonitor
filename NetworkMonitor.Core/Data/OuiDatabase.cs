namespace NetworkMonitor.Core.Data
{
    public class OuiDatabase
    {
        private readonly Dictionary<string, string> _vendors = new(StringComparer.OrdinalIgnoreCase);

        public void Load(string filePath)
        {

            if (File.Exists(filePath))
            {

                foreach (string line in File.ReadLines(filePath))
                {
                    int hexIdx = line.IndexOf("(hex)", StringComparison.Ordinal);

                    if (hexIdx >= 0)
                    {
                        string prefix = line[..hexIdx].Trim().Replace("-", ":").ToLowerInvariant();
                        string vendor = line[(hexIdx + 5)..].Trim();

                        if (prefix.Length == 8 && !string.IsNullOrEmpty(vendor))
                        {
                            _vendors[prefix] = vendor;
                        }

                    }

                }

            }

        }

        public string? Lookup(string macAddress)
        {
            string? vendor = null;

            if (macAddress.Length >= 8)
            {
                Dictionary<string, string>.AlternateLookup<ReadOnlySpan<char>> lookup = _vendors.GetAlternateLookup<ReadOnlySpan<char>>();
                lookup.TryGetValue(macAddress.AsSpan(0, 8), out vendor);
            }

            return vendor;
        }
    }
}
