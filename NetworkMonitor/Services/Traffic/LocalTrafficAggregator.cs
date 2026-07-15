using NetworkMonitor.Models;

namespace NetworkMonitor.Services.Traffic
{
    public static class LocalTrafficAggregator
    {
        public static IReadOnlyList<LocalTrafficDeviceRow> Build(IReadOnlyList<LocalTrafficMinute> minutes, IReadOnlyDictionary<string, string> namesByIp)
        {
            Dictionary<string, (long Upload, long Download)> totals = new();

            foreach (LocalTrafficMinute minute in minutes)
            {
                totals.TryGetValue(minute.RemoteIp, out (long Upload, long Download) current);

                totals[minute.RemoteIp] = (current.Upload + minute.BytesUploaded, current.Download + minute.BytesDownloaded);
            }

            List<LocalTrafficDeviceRow> rows = new();

            foreach (KeyValuePair<string, (long Upload, long Download)> entry in totals)
            {
                string displayName = LocalTrafficNameResolver.Resolve(entry.Key, namesByIp);

                rows.Add(new LocalTrafficDeviceRow(entry.Key, displayName, entry.Value.Upload, entry.Value.Download));
            }

            List<LocalTrafficDeviceRow> sorted = rows.OrderByDescending(row => row.TotalBytes).ToList();

            return sorted;
        }
    }
}
