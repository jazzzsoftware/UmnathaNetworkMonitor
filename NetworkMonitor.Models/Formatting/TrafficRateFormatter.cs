using NetworkMonitor.Models.Charting;

namespace NetworkMonitor.Models.Formatting
{
    public static class TrafficRateFormatter
    {
        public static RateUnitMode Mode
        {
            get;
            set;
        } = RateUnitMode.Both;

        public static string Composite(long bytes, double seconds)
        {
            string result = Mode switch
            {
                RateUnitMode.Bits => BitsPerSecond(bytes, seconds),
                RateUnitMode.Bytes => BytesPerSecond(bytes, seconds),
                _ => $"{BitsPerSecond(bytes, seconds)} · {BytesPerSecond(bytes, seconds)}"
            };

            return result;
        }

        public static string BitsPerSecond(long bytes, double seconds)
        {
            double bitsPerSecond = bytes * 8.0 / seconds;

            string result = bitsPerSecond switch
            {
                >= 1_000_000_000 => $"{bitsPerSecond / 1_000_000_000:F0} Gb/s",
                >= 1_000_000 => $"{bitsPerSecond / 1_000_000:F0} Mb/s",
                >= 1000 => $"{bitsPerSecond / 1000:F0} Kb/s",
                _ => $"{bitsPerSecond:F0} b/s"
            };

            return result;
        }

        public static string BytesPerSecond(long bytes, double seconds)
        {
            double bytesPerSecond = bytes / seconds;

            string result = bytesPerSecond switch
            {
                >= 1_000_000_000 => $"{bytesPerSecond / 1_000_000_000:F0} GB/s",
                >= 1_000_000 => $"{bytesPerSecond / 1_000_000:F0} MB/s",
                >= 1000 => $"{bytesPerSecond / 1000:F0} KB/s",
                _ => $"{bytesPerSecond:F0} B/s"
            };

            return result;
        }

        public static double BucketSeconds(IReadOnlyList<ChartPoint> points)
        {
            double bucketSeconds = 5.0;

            if (points.Count >= 2)
            {
                bucketSeconds = (points[1].BucketStart - points[0].BucketStart).TotalSeconds;
            }

            if (bucketSeconds <= 0)
            {
                bucketSeconds = 5.0;
            }

            return bucketSeconds;
        }
    }
}
