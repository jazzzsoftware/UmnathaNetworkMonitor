namespace NetworkMonitor.Models
{
    public record ChartPoint(DateTime BucketStart, long BytesUploaded, long BytesDownloaded);
}
