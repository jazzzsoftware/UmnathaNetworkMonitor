using NetworkMonitor.Core.Traffic;
using NetworkMonitor.Models.Charting;
using Xunit;

namespace NetworkMonitor.Tests
{
    // Every FlushSpread test hardcodes bucketSeconds = 1.0, but the Internet and Local view models
    // pass _windowBucketSeconds, which is 60+ on wide ranges. That path had no coverage at all, and
    // both C3-4 and C5-2 were defects in it.
    public class ChartPointSpreaderTests
    {
        private static List<ChartPoint> Buckets(DateTime startUtc, double bucketSeconds, int count)
        {
            List<ChartPoint> points = new List<ChartPoint>(count);

            for (int index = 0; index < count; index++)
            {
                points.Add(new ChartPoint(startUtc.AddSeconds(bucketSeconds * index), 0, 0));
            }

            return points;
        }

        [Fact]
        public void ApplyPreservesTheTotalAcrossSixtySecondBuckets()
        {
            DateTime start = new DateTime(2026, 8, 11, 12, 0, 0, DateTimeKind.Utc);
            List<ChartPoint> points = Buckets(start, 60.0, 10);

            ChartPointSpreader.Apply(points, 7_777, 3_333, 60.0, start.AddSeconds(120), start.AddSeconds(300));

            long upload = points.Sum(point => point.BytesUploaded);
            long download = points.Sum(point => point.BytesDownloaded);

            Assert.Equal(7_777, upload);
            Assert.Equal(3_333, download);
        }

        [Fact]
        public void ApplyOnlyTouchesTheBucketsTheIntervalOverlaps()
        {
            DateTime start = new DateTime(2026, 8, 11, 12, 0, 0, DateTimeKind.Utc);
            List<ChartPoint> points = Buckets(start, 60.0, 10);

            ChartPointSpreader.Apply(points, 6_000, 0, 60.0, start.AddSeconds(180), start.AddSeconds(300));

            Assert.Equal(0, points[0].BytesUploaded);
            Assert.Equal(0, points[1].BytesUploaded);
            Assert.Equal(0, points[2].BytesUploaded);
            Assert.True(points[3].BytesUploaded > 0);
            Assert.True(points[4].BytesUploaded > 0);
            Assert.Equal(0, points[5].BytesUploaded);
        }

        [Fact]
        public void ApplySplitsAnIntervalEvenlyBetweenTwoWideBuckets()
        {
            DateTime start = new DateTime(2026, 8, 11, 12, 0, 0, DateTimeKind.Utc);
            List<ChartPoint> points = Buckets(start, 60.0, 4);

            ChartPointSpreader.Apply(points, 1_000, 0, 60.0, start.AddSeconds(30), start.AddSeconds(90));

            Assert.Equal(500, points[0].BytesUploaded);
            Assert.Equal(500, points[1].BytesUploaded);
        }

        [Fact]
        public void ApplyAddsToWhateverTheBucketAlreadyHeld()
        {
            DateTime start = new DateTime(2026, 8, 11, 12, 0, 0, DateTimeKind.Utc);
            List<ChartPoint> points = new List<ChartPoint>
            {
                new ChartPoint(start, 100, 200)
            };

            ChartPointSpreader.Apply(points, 50, 60, 60.0, start, start.AddSeconds(60));

            Assert.Equal(150, points[0].BytesUploaded);
            Assert.Equal(260, points[0].BytesDownloaded);
        }

        [Fact]
        public void ApplyOnAnEmptyWindowDoesNotThrow()
        {
            List<ChartPoint> points = new List<ChartPoint>();
            DateTime start = new DateTime(2026, 8, 11, 12, 0, 0, DateTimeKind.Utc);

            ChartPointSpreader.Apply(points, 1_000, 1_000, 60.0, start, start.AddSeconds(60));

            Assert.Empty(points);
        }
    }
}
