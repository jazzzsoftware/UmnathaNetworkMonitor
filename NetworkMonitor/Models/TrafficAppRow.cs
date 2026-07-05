using System.IO;

namespace NetworkMonitor.Models
{
    public record TrafficAppRow(string? ProcessName, long BytesUploaded, long BytesDownloaded, string? ProcessPath)
    {
        public long TotalBytes => BytesUploaded + BytesDownloaded;
        public bool IsAllApps => ProcessName is null;
        public string DisplayName => ProcessPath is not null ? Path.GetFileName(ProcessPath) : ProcessName ?? "All Apps";
        public bool CanOpen => !IsAllApps && ProcessPath is not null;
    }
}
