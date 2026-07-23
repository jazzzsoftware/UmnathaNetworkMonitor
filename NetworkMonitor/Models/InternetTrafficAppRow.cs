using System.IO;
using NetworkMonitor.Services.Traffic;

namespace NetworkMonitor.Models
{
    public record InternetTrafficAppRow(string? ProcessName, long BytesUploaded, long BytesDownloaded, string? ProcessPath, double RateBytesPerSec = 0.0)
    {
        private const double RateThresholdBytesPerSec = 62_500.0;

        public long TotalBytes => BytesUploaded + BytesDownloaded;
        public bool IsAllApps => ProcessName is null;
        public string DisplayName => ProcessPath is not null ? Path.GetFileName(ProcessPath) : ProcessName ?? "All Apps";
        public bool CanOpen => !IsAllApps && ProcessPath is not null;
        public bool HasRate => RateBytesPerSec >= RateThresholdBytesPerSec;
        public string RateText => TrafficRateFormatter.Composite((long)RateBytesPerSec, 1.0);
    }
}
