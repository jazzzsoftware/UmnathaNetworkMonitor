using NetworkMonitor.Models.Formatting;

namespace NetworkMonitor.Models.Traffic
{
    public record LocalTrafficLeafRow(string Key, string DisplayName, string? SubLabel, long BytesUploaded, long BytesDownloaded, string? ServiceTag)
    {
        public long TotalBytes => BytesUploaded + BytesDownloaded;

        public bool HasServiceTag => ServiceTag is not null;

        public bool HasSubLabel => SubLabel is not null;

        public string DownloadText => ByteSizeFormatter.Format(BytesDownloaded);

        public string UploadText => ByteSizeFormatter.Format(BytesUploaded);

        public string TotalText => ByteSizeFormatter.Format(TotalBytes);
    }
}
