using NetworkMonitor.Core.Traffic;
using NetworkMonitor.Models.Charting;
using Xunit;

namespace NetworkMonitor.Tests
{
    public class LiveRateBufferTimeDiscontinuityTests
    {
        private static readonly DateTime Base = new DateTime(2026, 8, 11, 12, 0, 0, DateTimeKind.Utc);

        [Fact]
        public void ASubWindowBackwardStepRestartsRatherThanStackingOntoStaleBuckets()
        {
            LiveRateBuffer buffer = new LiveRateBuffer(300);

            buffer.AddInterval(Base, Base.AddSeconds(5), 5_000, 0);

            // The clock goes back 20 seconds — inside the 300s window, so IsHeld does not drop it.
            // This is the case that silently doubled buckets and froze the right edge in the future.
            buffer.AddInterval(Base.AddSeconds(-20), Base.AddSeconds(-15), 1_000, 0);

            IReadOnlyList<ChartPoint> points = buffer.Snapshot(Base.AddSeconds(-15));
            long total = points.Sum(point => point.BytesDownloaded);

            Assert.Equal(1_000, total);
        }

        // Deliberately different from the sub-window case above: a sample older than the whole window
        // is an out-of-order arrival, not a clock step, and dropping it is long-standing behaviour
        // pinned by LiveRateBufferTests.SamplesOlderThanTheWindowAreDropped. Restarting the trace on
        // one of those would throw away five minutes of history for a stale packet.
        [Fact]
        public void ASampleOlderThanTheWholeWindowIsStillDroppedRatherThanRestartingTheTrace()
        {
            LiveRateBuffer buffer = new LiveRateBuffer(300);

            buffer.AddInterval(Base, Base.AddSeconds(5), 5_000, 0);
            buffer.AddInterval(Base.AddHours(-2), Base.AddHours(-2).AddSeconds(5), 1_000, 0);

            IReadOnlyList<ChartPoint> points = buffer.Snapshot(Base.AddSeconds(5));
            long total = points.Sum(point => point.BytesDownloaded);

            Assert.Equal(5_000, total);
        }

        [Fact]
        public void TheRightEdgeFollowsTheClockBackwardsInsteadOfStayingInTheFuture()
        {
            LiveRateBuffer buffer = new LiveRateBuffer(300);

            buffer.AddInterval(Base, Base.AddSeconds(5), 5_000, 0);
            buffer.AddInterval(Base.AddSeconds(-20), Base.AddSeconds(-15), 1_000, 0);

            IReadOnlyList<ChartPoint> points = buffer.Snapshot(Base.AddSeconds(-15));
            DateTime lastBucket = points[points.Count - 1].BucketStart;

            Assert.True(lastBucket <= Base.AddSeconds(-15), $"right edge was {lastBucket:HH:mm:ss}, expected no later than {Base.AddSeconds(-15):HH:mm:ss}");
        }

        [Fact]
        public void AGapLongerThanTheWindowKeepsOnlyTheShareStillVisible()
        {
            LiveRateBuffer buffer = new LiveRateBuffer(300);

            buffer.AddInterval(Base, Base.AddSeconds(1), 0, 0);

            // Three hours of traffic arriving in one flush. Only the last ~300 seconds can be shown,
            // so keeping all of it would draw the window at roughly 36x the real rate.
            DateTime end = Base.AddHours(3);
            long threeHoursOfBytes = 36_000_000;

            buffer.AddInterval(Base, end, threeHoursOfBytes, 0);

            IReadOnlyList<ChartPoint> points = buffer.Snapshot(end);
            long total = points.Sum(point => point.BytesDownloaded);
            long fullWindowShare = threeHoursOfBytes * 300 / (3 * 60 * 60);

            Assert.True(total < threeHoursOfBytes / 10, $"kept {total} of {threeHoursOfBytes}, which is still most of a three-hour flush");
            Assert.InRange(total, fullWindowShare / 2, fullWindowShare * 2);
        }

        [Fact]
        public void AnIntervalInsideTheWindowStillKeepsEveryByte()
        {
            LiveRateBuffer buffer = new LiveRateBuffer(300);

            buffer.AddInterval(Base, Base.AddSeconds(1), 0, 0);
            buffer.AddInterval(Base.AddSeconds(1), Base.AddSeconds(6), 12_345, 6_789);

            IReadOnlyList<ChartPoint> points = buffer.Snapshot(Base.AddSeconds(6));

            Assert.Equal(12_345, points.Sum(point => point.BytesDownloaded));
            Assert.Equal(6_789, points.Sum(point => point.BytesUploaded));
        }
    }
}
