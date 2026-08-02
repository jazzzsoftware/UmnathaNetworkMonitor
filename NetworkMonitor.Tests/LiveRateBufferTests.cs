using Xunit;
using NetworkMonitor.Core.Traffic;
using NetworkMonitor.Models.Charting;

namespace NetworkMonitor.Tests
{
    public class LiveRateBufferTests
    {
        private static readonly DateTime Origin = new DateTime(2026, 8, 2, 12, 0, 0, DateTimeKind.Utc);

        [Fact]
        public void SnapshotIsEmptyBeforeAnythingIsAdded()
        {
            LiveRateBuffer buffer = new LiveRateBuffer(5);

            IReadOnlyList<ChartPoint> points = buffer.Snapshot(Origin);

            Assert.Empty(points);
        }

        [Fact]
        public void SamplesInTheSameSecondAccumulateIntoOneBucket()
        {
            LiveRateBuffer buffer = new LiveRateBuffer(5);

            buffer.Add(Origin, 100, 10);
            buffer.Add(Origin.AddMilliseconds(400), 50, 5);

            IReadOnlyList<ChartPoint> points = buffer.Snapshot(Origin);
            ChartPoint newest = points[points.Count - 1];

            Assert.Equal(150, newest.BytesDownloaded);
            Assert.Equal(15, newest.BytesUploaded);
        }

        [Fact]
        public void SnapshotIsOldestFirstAndExactlyCapacityLong()
        {
            LiveRateBuffer buffer = new LiveRateBuffer(4);

            buffer.Add(Origin, 1, 0);

            IReadOnlyList<ChartPoint> points = buffer.Snapshot(Origin);

            Assert.Equal(4, points.Count);
            Assert.Equal(Origin.AddSeconds(-3), points[0].BucketStart);
            Assert.Equal(Origin, points[3].BucketStart);
        }

        [Fact]
        public void AnIdleGapReadsAsZeroesRatherThanStaleBytes()
        {
            LiveRateBuffer buffer = new LiveRateBuffer(10);

            buffer.Add(Origin, 900, 90);
            buffer.Add(Origin.AddSeconds(4), 100, 10);

            IReadOnlyList<ChartPoint> points = buffer.Snapshot(Origin.AddSeconds(4));

            Assert.Equal(900, points[points.Count - 5].BytesDownloaded);
            Assert.Equal(0, points[points.Count - 4].BytesDownloaded);
            Assert.Equal(0, points[points.Count - 3].BytesDownloaded);
            Assert.Equal(0, points[points.Count - 2].BytesDownloaded);
            Assert.Equal(100, points[points.Count - 1].BytesDownloaded);
        }

        [Fact]
        public void SnapshotZeroFillsForwardToNowWhenNothingHasArrivedSince()
        {
            LiveRateBuffer buffer = new LiveRateBuffer(6);

            buffer.Add(Origin, 500, 50);

            IReadOnlyList<ChartPoint> points = buffer.Snapshot(Origin.AddSeconds(3));

            Assert.Equal(6, points.Count);
            Assert.Equal(Origin.AddSeconds(3), points[5].BucketStart);
            Assert.Equal(0, points[5].BytesDownloaded);
            Assert.Equal(500, points[2].BytesDownloaded);
        }

        [Fact]
        public void WritingPastCapacityEvictsTheOldestBucket()
        {
            LiveRateBuffer buffer = new LiveRateBuffer(3);

            buffer.Add(Origin, 111, 0);
            buffer.Add(Origin.AddSeconds(1), 222, 0);
            buffer.Add(Origin.AddSeconds(2), 333, 0);
            buffer.Add(Origin.AddSeconds(3), 444, 0);

            IReadOnlyList<ChartPoint> points = buffer.Snapshot(Origin.AddSeconds(3));

            Assert.Equal(3, points.Count);
            Assert.Equal(Origin.AddSeconds(1), points[0].BucketStart);
            Assert.Equal(222, points[0].BytesDownloaded);
            Assert.Equal(444, points[2].BytesDownloaded);
        }

        [Fact]
        public void AWholeWindowOfSilenceLeavesNothingBehind()
        {
            LiveRateBuffer buffer = new LiveRateBuffer(3);

            buffer.Add(Origin, 999, 99);
            buffer.Add(Origin.AddSeconds(30), 0, 0);

            IReadOnlyList<ChartPoint> points = buffer.Snapshot(Origin.AddSeconds(30));

            Assert.Equal(3, points.Count);

            foreach (ChartPoint point in points)
            {
                Assert.Equal(0, point.BytesDownloaded);
                Assert.Equal(0, point.BytesUploaded);
            }

        }

        [Fact]
        public void SamplesOlderThanTheWindowAreDropped()
        {
            LiveRateBuffer buffer = new LiveRateBuffer(3);

            buffer.Add(Origin.AddSeconds(10), 100, 0);
            buffer.Add(Origin, 7777, 0);

            IReadOnlyList<ChartPoint> points = buffer.Snapshot(Origin.AddSeconds(10));
            long total = 0;

            foreach (ChartPoint point in points)
            {
                total += point.BytesDownloaded;
            }

            Assert.Equal(100, total);
        }

        [Fact]
        public void AnIntervalIsSpreadAcrossEverySecondItCovers()
        {
            LiveRateBuffer buffer = new LiveRateBuffer(10);

            buffer.AddInterval(Origin, Origin.AddSeconds(4), 400, 40);

            IReadOnlyList<ChartPoint> points = buffer.Snapshot(Origin.AddSeconds(3));
            long total = 0;

            foreach (ChartPoint point in points)
            {
                total += point.BytesDownloaded;
                Assert.True(point.BytesDownloaded <= 100);
            }

            Assert.Equal(400, total);
        }

        [Fact]
        public void ClearDiscardsEverythingHeld()
        {
            LiveRateBuffer buffer = new LiveRateBuffer(4);

            buffer.Add(Origin, 100, 10);
            buffer.Clear();

            IReadOnlyList<ChartPoint> points = buffer.Snapshot(Origin);

            Assert.Empty(points);
        }
    }
}
