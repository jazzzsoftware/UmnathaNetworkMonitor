using NetworkMonitor.Services.Common;

namespace NetworkMonitor.Models
{
    public record LocalTrafficLeafRow(string Key, string DisplayName, string? SubLabel, long BytesUploaded, long BytesDownloaded, string? ServiceTag)
    {
        public long TotalBytes => BytesUploaded + BytesDownloaded;

        public string DownloadText => ByteSizeFormatter.Format(BytesDownloaded);

        public string UploadText => ByteSizeFormatter.Format(BytesUploaded);

        public string TotalText => ByteSizeFormatter.Format(TotalBytes);
    }
}
