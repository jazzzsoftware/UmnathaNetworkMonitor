namespace NetworkMonitor.Services.Traffic
{
    public record LocalTrafficMinute(long MinuteEpoch, string RemoteIp, long BytesUploaded, long BytesDownloaded);
}
