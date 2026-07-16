using NetworkMonitor.Services.Common;

namespace NetworkMonitor.Models
{
    public record LocalTrafficDeviceRow(string RemoteIp, string DisplayName, long BytesUploaded, long BytesDownloaded)
    {
        public long TotalBytes => BytesUploaded + BytesDownloaded;
        public string DownloadText => ByteSizeFormatter.Format(BytesDownloaded);
        public string UploadText => ByteSizeFormatter.Format(BytesUploaded);
        public string TotalText => ByteSizeFormatter.Format(TotalBytes);
    }
}
