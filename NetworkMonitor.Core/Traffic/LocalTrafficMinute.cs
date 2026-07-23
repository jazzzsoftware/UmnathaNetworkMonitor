namespace NetworkMonitor.Core.Traffic
{
    public record LocalTrafficMinute(long MinuteEpoch, string ProcessName, string RemoteIp, long BytesUploaded, long BytesDownloaded);
}
