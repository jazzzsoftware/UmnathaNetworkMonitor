using NetworkMonitor.Models;

namespace NetworkMonitor.Services.Traffic
{
    public static class LocalTrafficAggregator
    {
        public static IReadOnlyList<LocalTrafficAppRow> Build(IReadOnlyList<LocalTrafficMinute> minutes, IReadOnlyDictionary<string, string> namesByIp)
        {
            Dictionary<string, Dictionary<string, (long Upload, long Download)>> byApp = new();

            foreach (LocalTrafficMinute minute in minutes)
            {

                if (!byApp.TryGetValue(minute.ProcessName, out Dictionary<string, (long Upload, long Download)>? peers))
                {
                    peers = new Dictionary<string, (long Upload, long Download)>();
                    byApp[minute.ProcessName] = peers;
                }

                peers.TryGetValue(minute.RemoteIp, out (long Upload, long Download) current);
                peers[minute.RemoteIp] = (current.Upload + minute.BytesUploaded, current.Download + minute.BytesDownloaded);
            }

            List<LocalTrafficAppRow> rows = new();

            foreach (KeyValuePair<string, Dictionary<string, (long Upload, long Download)>> appEntry in byApp)
            {
                List<LocalTrafficDeviceRow> peerRows = new();
                long appUpload = 0;
                long appDownload = 0;

                foreach (KeyValuePair<string, (long Upload, long Download)> peerEntry in appEntry.Value)
                {
                    string displayName = LocalTrafficNameResolver.Resolve(peerEntry.Key, namesByIp);

                    peerRows.Add(new LocalTrafficDeviceRow(peerEntry.Key, displayName, peerEntry.Value.Upload, peerEntry.Value.Download));
                    appUpload += peerEntry.Value.Upload;
                    appDownload += peerEntry.Value.Download;
                }

                peerRows.Sort((left, right) => right.TotalBytes.CompareTo(left.TotalBytes));
                rows.Add(new LocalTrafficAppRow(appEntry.Key, appEntry.Key, appUpload, appDownload, peerRows));
            }

            List<LocalTrafficAppRow> sorted = rows.OrderByDescending(row => row.TotalBytes).ToList();

            return sorted;
        }
    }
}
