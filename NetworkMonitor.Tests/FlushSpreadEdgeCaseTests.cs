using NetworkMonitor.Core.Traffic;
using Xunit;

namespace NetworkMonitor.Tests
{
    // The gaps the review inventory called out for FlushSpread: a negative total, an empty bucket
    // list, and a non-positive bucketSeconds. None was covered, and each one silently returns
    // something rather than throwing, so a regression here would be invisible.
    public class FlushSpreadEdgeCaseTests
    {
        private static readonly DateTime Start = new DateTime(2026, 8, 11, 12, 0, 0, DateTimeKind.Utc);

        [Fact]
        public void ANegativeTotalIsDroppedRatherThanDistributed()
        {
            List<DateTime> buckets = new() { Start, Start.AddSeconds(1), Start.AddSeconds(2) };

            long[] shares = FlushSpread.Distribute(-5_000, buckets, 1.0, Start, Start.AddSeconds(3));

            Assert.All(shares, share => Assert.Equal(0, share));
        }

        [Fact]
        public void AnEmptyBucketListReturnsAnEmptyArrayRatherThanIndexingPastTheEnd()
        {
            long[] shares = FlushSpread.Distribute(1_000, new List<DateTime>(), 1.0, Start, Start.AddSeconds(3));

            Assert.Empty(shares);
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(-1.0)]
        public void ANonPositiveBucketSizeFallsBackToTheNewestBucket(double bucketSeconds)
        {
            List<DateTime> buckets = new() { Start, Start.AddSeconds(1), Start.AddSeconds(2) };

            long[] shares = FlushSpread.Distribute(900, buckets, bucketSeconds, Start, Start.AddSeconds(3));

            Assert.Equal(0, shares[0]);
            Assert.Equal(0, shares[1]);
            Assert.Equal(900, shares[2]);
        }

        [Fact]
        public void AnIntervalEntirelyOutsideTheBucketsFallsBackToTheNewestBucket()
        {
            List<DateTime> buckets = new() { Start, Start.AddSeconds(1), Start.AddSeconds(2) };

            long[] shares = FlushSpread.Distribute(750, buckets, 1.0, Start.AddHours(-2), Start.AddHours(-2).AddSeconds(3));

            Assert.Equal(750, shares[2]);
        }

        [Fact]
        public void AZeroTotalStaysZeroAcrossEveryBucket()
        {
            List<DateTime> buckets = new() { Start, Start.AddSeconds(1) };

            long[] shares = FlushSpread.Distribute(0, buckets, 1.0, Start, Start.AddSeconds(2));

            Assert.All(shares, share => Assert.Equal(0, share));
        }
    }
}
