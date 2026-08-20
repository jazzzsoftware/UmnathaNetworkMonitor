namespace NetworkMonitor.Core.Charting
{
    public static class ChartDrawRange
    {
        public static string FromBucketSeconds(double bucketSeconds)
        {
            string range;

            if (bucketSeconds <= 1.5)
            {
                range = "5m";
            }
            else if (bucketSeconds <= 90.0)
            {
                range = "1h";
            }
            else
            {
                range = "6h";
            }

            return range;
        }
    }
}
