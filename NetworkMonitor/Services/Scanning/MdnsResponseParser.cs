namespace NetworkMonitor.Services.Scanning
{
    public readonly record struct MdnsAddressRecord(string Host, string Ip);

    public readonly record struct MdnsPointerRecord(string Service, string Instance);

    public readonly record struct MdnsServiceRecord(string Instance, string TargetHost);

    public readonly record struct MdnsTextRecord(string Name, IReadOnlyList<string> Entries);

    public static class MdnsResponseParser
    {
        private static readonly string[] ModelKeys = { "model", "md", "rpmd" };

        public static IReadOnlyDictionary<string, MdnsInfo> Parse(
            IReadOnlyList<MdnsAddressRecord> addressRecords,
            IReadOnlyList<MdnsPointerRecord> pointerRecords,
            IReadOnlyList<MdnsServiceRecord> serviceRecords,
            IReadOnlyList<MdnsTextRecord> textRecords)
        {
            Dictionary<string, string> hostToIp = new(StringComparer.OrdinalIgnoreCase);

            foreach (MdnsAddressRecord addressRecord in addressRecords)
            {

                if (!string.IsNullOrEmpty(addressRecord.Host) && !string.IsNullOrEmpty(addressRecord.Ip))
                {
                    hostToIp[Trim(addressRecord.Host)] = addressRecord.Ip;
                }

            }

            Dictionary<string, string> instanceToHost = new(StringComparer.OrdinalIgnoreCase);

            foreach (MdnsServiceRecord serviceRecord in serviceRecords)
            {

                if (!string.IsNullOrEmpty(serviceRecord.Instance) && !string.IsNullOrEmpty(serviceRecord.TargetHost))
                {
                    instanceToHost[Trim(serviceRecord.Instance)] = Trim(serviceRecord.TargetHost);
                }

            }

            Dictionary<string, MutableInfo> byIp = new(StringComparer.OrdinalIgnoreCase);

            foreach (MdnsPointerRecord pointerRecord in pointerRecords)
            {
                string instance = Trim(pointerRecord.Instance);
                string friendly = FriendlyLabel(instance, Trim(pointerRecord.Service));

                if (!string.IsNullOrEmpty(friendly)
                    && instanceToHost.TryGetValue(instance, out string? host)
                    && hostToIp.TryGetValue(host, out string? ip))
                {
                    MutableInfo info = GetOrAdd(byIp, ip);

                    if (string.IsNullOrEmpty(info.Name))
                    {
                        info.Name = friendly;
                    }

                }

            }

            foreach (MdnsTextRecord textRecord in textRecords)
            {
                string instance = Trim(textRecord.Name);
                string model = ExtractModel(textRecord.Entries);

                if (!string.IsNullOrEmpty(model)
                    && instanceToHost.TryGetValue(instance, out string? host)
                    && hostToIp.TryGetValue(host, out string? ip))
                {
                    MutableInfo info = GetOrAdd(byIp, ip);

                    if (string.IsNullOrEmpty(info.Model))
                    {
                        info.Model = model;
                    }

                }

            }

            Dictionary<string, MdnsInfo> result = new();

            foreach (KeyValuePair<string, MutableInfo> pair in byIp)
            {
                result[pair.Key] = new MdnsInfo(NullIfEmpty(pair.Value.Name), NullIfEmpty(pair.Value.Model));
            }

            return result;
        }

        private static string Trim(string value)
        {
            string result = value.Trim().TrimEnd('.');

            return result;
        }

        private static string FriendlyLabel(string instance, string service)
        {
            string label = instance;
            string suffix = "." + service;

            if (instance.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                label = instance.Substring(0, instance.Length - suffix.Length);
            }

            string result = label;

            return result;
        }

        private static string ExtractModel(IReadOnlyList<string> entries)
        {
            string result = string.Empty;

            foreach (string entry in entries)
            {
                int separator = entry.IndexOf('=');

                if (separator > 0)
                {
                    string key = entry.Substring(0, separator).Trim().ToLowerInvariant();
                    string value = entry.Substring(separator + 1).Trim();

                    if (value.Length > 0 && Array.IndexOf(ModelKeys, key) >= 0)
                    {
                        result = value;

                        break;
                    }

                }

            }

            return result;
        }

        private static MutableInfo GetOrAdd(Dictionary<string, MutableInfo> byIp, string ip)
        {

            if (!byIp.TryGetValue(ip, out MutableInfo? info))
            {
                info = new MutableInfo();
                byIp[ip] = info;
            }

            MutableInfo result = info;

            return result;
        }

        private static string? NullIfEmpty(string value)
        {
            string? result = string.IsNullOrEmpty(value) ? null : value;

            return result;
        }

        private sealed class MutableInfo
        {
            public string Name
            {
                get;
                set;
            } = string.Empty;

            public string Model
            {
                get;
                set;
            } = string.Empty;
        }
    }
}
