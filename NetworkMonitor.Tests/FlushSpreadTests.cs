using NetworkMonitor.Core.Traffic;
using Xunit;

namespace NetworkMonitor.Tests
{
    public class FlushSpreadTests
    {
        private static List<DateTime> Buckets(int count)
        {
            DateTime start = new DateTime(2026, 7, 28, 14, 15, 30, DateTimeKind.Utc);
            List<DateTime> buckets = new List<DateTime>(count);

            for (int index = 0; index < count; index++)
            {
                buckets.Add(start.AddSeconds(index));
            }

            return buckets;
        }

        [Fact]
        public void IntervalInsideOneBucketPutsEverythingThere()
        {
            List<DateTime> buckets = Buckets(4);
            long[] allocated = FlushSpread.Distribute(
                1000,
                buckets,
                1.0,
                buckets[3].AddSeconds(0.1),
                buckets[3].AddSeconds(0.9));

            Assert.Equal(new long[] { 0, 0, 0, 1000 }, allocated);
        }

        [Fact]
        public void IntervalStraddlingABoundarySplitsByOverlap()
        {
            List<DateTime> buckets = Buckets(4);

            // 0.25s in bucket 2, 0.75s in bucket 3.
            long[] allocated = FlushSpread.Distribute(
                1000,
                buckets,
                1.0,
                buckets[2].AddSeconds(0.75),
                buckets[3].AddSeconds(0.75));

            Assert.Equal(250, allocated[2]);
            Assert.Equal(750, allocated[3]);
            Assert.Equal(0, allocated[0]);
            Assert.Equal(0, allocated[1]);
        }

        [Fact]
        public void ASlippedFlushNoLongerEmptiesABucket()
        {
            // The reported failure: a drain covering ~2 seconds used to dump everything into the
            // newest bucket, leaving the previous one empty and the newest reading double.
            List<DateTime> buckets = Buckets(4);
            long[] allocated = FlushSpread.Distribute(
                2000,
                buckets,
                1.0,
                buckets[2],
                buckets[4 - 1].AddSeconds(1.0));

            Assert.Equal(1000, allocated[2]);
            Assert.Equal(1000, allocated[3]);
        }

        [Fact]
        public void TotalIsAlwaysPreserved()
        {
            List<DateTime> buckets = Buckets(5);

            for (int offsetMs = 0; offsetMs < 1000; offsetMs += 37)
            {
                long[] allocated = FlushSpread.Distribute(
                    999_983,
                    buckets,
                    1.0,
                    buckets[2].AddMilliseconds(offsetMs),
                    buckets[4].AddMilliseconds(offsetMs));

                long sum = 0;

                foreach (long value in allocated)
                {
                    sum += value;
                }

                Assert.Equal(999_983, sum);
            }

        }

        [Fact]
        public void ZeroLengthIntervalFallsBackToTheNewestBucket()
        {
            List<DateTime> buckets = Buckets(3);
            long[] allocated = FlushSpread.Distribute(500, buckets, 1.0, buckets[1], buckets[1]);

            Assert.Equal(new long[] { 0, 0, 500 }, allocated);
        }

        [Fact]
        public void IntervalEntirelyBeforeTheWindowFallsBackToTheNewestBucket()
        {
            List<DateTime> buckets = Buckets(3);
            long[] allocated = FlushSpread.Distribute(
                500,
                buckets,
                1.0,
                buckets[0].AddSeconds(-10),
                buckets[0].AddSeconds(-9));

            Assert.Equal(new long[] { 0, 0, 500 }, allocated);
        }

        [Fact]
        public void ZeroBytesAllocatesNothing()
        {
            List<DateTime> buckets = Buckets(3);
            long[] allocated = FlushSpread.Distribute(0, buckets, 1.0, buckets[0], buckets[2]);

            Assert.Equal(new long[] { 0, 0, 0 }, allocated);
        }

        [Fact]
        public void AnIntervalRunningPastTheWindowKeepsOnlyTheOverlap()
        {
            List<DateTime> buckets = Buckets(3);

            // Covers bucket 2 fully plus a second beyond the window; only the in-window part counts.
            long[] allocated = FlushSpread.Distribute(
                1000,
                buckets,
                1.0,
                buckets[2],
                buckets[2].AddSeconds(2.0));

            Assert.Equal(1000, allocated[2]);
            Assert.Equal(0, allocated[0]);
            Assert.Equal(0, allocated[1]);
        }
    }
}
