using NetworkMonitor.Services.Traffic;
using Xunit;

namespace NetworkMonitor.Tests
{
    public class TrafficWindowTests
    {
        [Fact]
        public void AlignsCutoffToBucketGrid()
        {
            long cutoff = TrafficWindow.AlignedCutoffEpoch(1000, 5, 60);

            Assert.Equal(705, cutoff);
        }

        [Fact]
        public void CutoffIsStableBetweenFlushesWithinABucket()
        {
            long early = TrafficWindow.AlignedCutoffEpoch(1001, 5, 60);
            long late = TrafficWindow.AlignedCutoffEpoch(1004, 5, 60);

            Assert.Equal(early, late);
        }

        [Fact]
        public void CutoffAdvancesByOneBucketAtBoundary()
        {
            long before = TrafficWindow.AlignedCutoffEpoch(1000, 5, 60);
            long after = TrafficWindow.AlignedCutoffEpoch(1005, 5, 60);

            Assert.Equal(before + 5, after);
        }

        [Fact]
        public void NewestBucketContainsNow()
        {
            long bucketSeconds = 5;
            int totalBuckets = 60;
            long now = 1003;

            long cutoff = TrafficWindow.AlignedCutoffEpoch(now, bucketSeconds, totalBuckets);
            long windowEnd = cutoff + totalBuckets * bucketSeconds;

            Assert.True(windowEnd > now);
            Assert.True(windowEnd - bucketSeconds <= now);
        }

        [Fact]
        public void AlignsToMinuteScaleBuckets()
        {
            long cutoff = TrafficWindow.AlignedCutoffEpoch(3661, 60, 60);

            Assert.Equal(120, cutoff);
        }
    }
}
