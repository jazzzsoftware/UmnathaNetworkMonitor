namespace NetworkMonitor.Core.Charting
{
    public readonly record struct ChartDrawValues(
        int Buckets,
        string Series,
        long Peak,
        long Scale,
        string Range);
}
