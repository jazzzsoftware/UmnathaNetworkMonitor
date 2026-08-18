namespace NetworkMonitor.Models.Charting
{
    // A struct rather than a class: every window rebuild, snapshot and shift produces 300 of these,
    // twice a second between the two live buffers, and they are read in the chart's tight per-frame
    // loops. As a record class that was 300 heap objects and 300 pointer chases per pass, all of it
    // immediately garbage. The type is immutable and three fields wide, so the copy is cheaper than
    // the indirection it replaces.
    public readonly record struct ChartPoint(DateTime BucketStart, long BytesUploaded, long BytesDownloaded);
}
