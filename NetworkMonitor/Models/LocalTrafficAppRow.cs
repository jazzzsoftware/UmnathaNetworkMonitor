using NetworkMonitor.Services.Common;

namespace NetworkMonitor.Models
{
    public record LocalTrafficAppRow(string? ProcessName, string DisplayName, long BytesUploaded, long BytesDownloaded, IReadOnlyList<LocalTrafficDeviceRow> Peers)
    {
        public long TotalBytes => BytesUploaded + BytesDownloaded;

        public bool IsAllApps => ProcessName is null;

        public string DownloadText => ByteSizeFormatter.Format(BytesDownloaded);

        public string UploadText => ByteSizeFormatter.Format(BytesUploaded);

        public string TotalText => ByteSizeFormatter.Format(TotalBytes);

        public bool HasMultiplePeers => Peers.Count > 1;

        public string PeerSummary => Peers.Count switch
        {
            0 => string.Empty,
            1 => Peers[0].DisplayName,
            _ => $"{Peers[0].DisplayName} +{Peers.Count - 1}"
        };

        public string PeerTooltip => string.Join(", ", Peers.Select(peer => peer.DisplayName));
    }
}
