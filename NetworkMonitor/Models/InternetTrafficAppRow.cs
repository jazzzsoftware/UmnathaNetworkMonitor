using System.IO;

namespace NetworkMonitor.Models
{
    public record InternetTrafficAppRow(string? ProcessName, long BytesUploaded, long BytesDownloaded, string? ProcessPath, double RateBytesPerSec = 0.0)
    {
        private const double RateThresholdBytesPerSec = 64_000.0;

        public long TotalBytes => BytesUploaded + BytesDownloaded;
        public bool IsAllApps => ProcessName is null;
        public string DisplayName => ProcessPath is not null ? Path.GetFileName(ProcessPath) : ProcessName ?? "All Apps";
        public bool CanOpen => !IsAllApps && ProcessPath is not null;
        public bool HasRate => RateBytesPerSec >= RateThresholdBytesPerSec;
        public string RateText => $"{RateBytesPerSec * 8.0 / 1_000_000.0:0.0} Mb/s · {RateBytesPerSec / 1_000_000.0:0.0} MB/s";
    }
}
