namespace NetworkMonitor.Services.Traffic
{
    public record LocalTrafficDelta(string ProcessName, string? ProcessPath, string RemoteIp, long BytesUploaded, long BytesDownloaded);
}
