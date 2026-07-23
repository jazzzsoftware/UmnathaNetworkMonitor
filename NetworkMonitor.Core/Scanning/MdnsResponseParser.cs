using System.Text;

namespace NetworkMonitor.Core.Scanning
{
    public readonly record struct MdnsAddressRecord(string Host, string Ip);

    public readonly record struct MdnsPointerRecord(string Service, string Instance);

    public readonly record struct MdnsServiceRecord(string Instance, string TargetHost);

    public readonly record struct MdnsTextRecord(string Name, IReadOnlyList<string> Entries);

    public static class MdnsResponseParser
    {
        private static readonly string[] ModelKeys = { "model", "md", "rpmd" };

        private static readonly string[] FallbackModelKeys = { "type" };

        private static readonly string[] OpaqueServiceTypes =
        {
            "_remotepairing", "_apple-mobdev", "_sleep-proxy", "_rdlink"
        };

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
                string service = Trim(pointerRecord.Service);
                string friendly = FriendlyLabel(instance, service);

                if (!string.IsNullOrEmpty(friendly)
                    && !IsOpaqueName(friendly, service)
                    && instanceToHost.TryGetValue(instance, out string? host)
                    && hostToIp.TryGetValue(host, out string? ip))
                {
                    MutableInfo info = GetOrAdd(byIp, ip);

                    if (string.IsNullOrEmpty(info.Name))
                    {
                        info.Name = Unescape(friendly);
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

        private static string Unescape(string value)
        {
            StringBuilder builder = new(value.Length);
            int index = 0;

            while (index < value.Length)
            {
                char current = value[index];

                if (current == '\\' && index + 3 < value.Length
                    && char.IsDigit(value[index + 1])
                    && char.IsDigit(value[index + 2])
                    && char.IsDigit(value[index + 3]))
                {
                    int code = ((value[index + 1] - '0') * 100) + ((value[index + 2] - '0') * 10) + (value[index + 3] - '0');
                    builder.Append((char) code);
                    index += 4;
                }
                else if (current == '\\' && index + 1 < value.Length)
                {
                    builder.Append(value[index + 1]);
                    index += 2;
                }
                else
                {
                    builder.Append(current);
                    index += 1;
                }

            }

            string result = builder.ToString();

            return result;
        }

        private static bool IsOpaqueName(string label, string service)
        {
            bool opaque = Guid.TryParse(label, out _);

            if (!opaque)
            {

                foreach (string opaqueService in OpaqueServiceTypes)
                {

                    if (service.Contains(opaqueService, StringComparison.OrdinalIgnoreCase))
                    {
                        opaque = true;

                        break;
                    }

                }

            }

            return opaque;
        }

        private static string ExtractModel(IReadOnlyList<string> entries)
        {
            Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);

            foreach (string entry in entries)
            {
                int separator = entry.IndexOf('=');

                if (separator > 0)
                {
                    string key = entry.Substring(0, separator).Trim().ToLowerInvariant();
                    string value = entry.Substring(separator + 1).Trim();

                    if (value.Length > 0 && !values.ContainsKey(key))
                    {
                        values[key] = value;
                    }

                }

            }

            string result = SelectByKeys(values, ModelKeys);

            if (string.IsNullOrEmpty(result))
            {
                result = SelectByKeys(values, FallbackModelKeys);
            }

            return result;
        }

        private static string SelectByKeys(Dictionary<string, string> values, string[] keys)
        {
            string result = string.Empty;

            foreach (string key in keys)
            {

                if (values.TryGetValue(key, out string? value))
                {
                    result = value;

                    break;
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
