namespace NetworkMonitor.Models.Charting
{
    public record ChartPoint(DateTime BucketStart, long BytesUploaded, long BytesDownloaded);
}
