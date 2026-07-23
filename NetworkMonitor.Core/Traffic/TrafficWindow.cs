namespace NetworkMonitor.Core.Traffic
{
    public static class TrafficWindow
    {
        public static long AlignedCutoffEpoch(long nowEpoch, long bucketSeconds, int totalBuckets)
        {
            long windowEnd = (nowEpoch / bucketSeconds + 1) * bucketSeconds;
            long cutoffEpoch = windowEnd - (long)totalBuckets * bucketSeconds;

            return cutoffEpoch;
        }
    }
}
