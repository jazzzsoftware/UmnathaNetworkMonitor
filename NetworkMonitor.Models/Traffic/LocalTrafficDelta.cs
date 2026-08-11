namespace NetworkMonitor.Models.Traffic
{
    public record LocalTrafficDelta(string ProcessName, string? ProcessPath, string RemoteIp, int Protocol, int RemotePort, long BytesUploaded, long BytesDownloaded);
}
