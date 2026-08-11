using NetworkMonitor.Models.Charting;

namespace NetworkMonitor.Core.Traffic
{
    // The bucket-mapping wrapper around FlushSpread.Distribute: takes the chart points a page is
    // already showing and adds a live flush into them in place.
    //
    // It lived twice, byte for byte, in InternetViewModel and LocalViewModel — pure, deterministic
    // logic in the UI project where the test project cannot reach it, while the harder arithmetic it
    // calls sat in Core with tests. Both defects found in this path (a flush spread over the whole
    // window after a page revisit, and a long gap compressed into the visible window) were in code
    // nothing could test.
    public static class ChartPointSpreader
    {
        public static void Apply(
            IList<ChartPoint> points,
            long bytesUploaded,
            long bytesDownloaded,
            double bucketSeconds,
            DateTime intervalStartUtc,
            DateTime intervalEndUtc)
        {

            if (points.Count > 0)
            {
                List<DateTime> bucketStarts = new List<DateTime>(points.Count);

                foreach (ChartPoint point in points)
                {
                    bucketStarts.Add(point.BucketStart);
                }

                long[] uploadShares = FlushSpread.Distribute(bytesUploaded, bucketStarts, bucketSeconds, intervalStartUtc, intervalEndUtc);
                long[] downloadShares = FlushSpread.Distribute(bytesDownloaded, bucketStarts, bucketSeconds, intervalStartUtc, intervalEndUtc);

                for (int index = 0; index < points.Count; index++)
                {

                    if (uploadShares[index] != 0 || downloadShares[index] != 0)
                    {
                        ChartPoint point = points[index];
                        points[index] = point with
                        {
                            BytesUploaded = point.BytesUploaded + uploadShares[index],
                            BytesDownloaded = point.BytesDownloaded + downloadShares[index]
                        };
                    }

                }

            }

        }
    }
}
