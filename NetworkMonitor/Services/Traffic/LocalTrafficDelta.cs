namespace NetworkMonitor.Services.Traffic
{
    public record LocalTrafficDelta(string RemoteIp, long BytesUploaded, long BytesDownloaded);
}
