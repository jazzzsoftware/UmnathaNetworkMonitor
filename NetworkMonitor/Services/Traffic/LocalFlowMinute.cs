namespace NetworkMonitor.Services.Traffic
{
    public record LocalFlowMinute(string ProcessName, string RemoteIp, int Protocol, int RemotePort, long BytesUploaded, long BytesDownloaded);
}
