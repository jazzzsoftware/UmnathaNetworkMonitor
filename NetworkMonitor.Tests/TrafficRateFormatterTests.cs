using System;
using System.Collections.Generic;
using NetworkMonitor.Models;
using NetworkMonitor.Services.Traffic;
using Xunit;

namespace NetworkMonitor.Tests
{
    public class TrafficRateFormatterTests
    {
        [Theory]
        [InlineData(10, 1.0, "80 b/s")]
        [InlineData(128, 1.0, "1 Kb/s")]
        [InlineData(131_072, 1.0, "1 Mb/s")]
        [InlineData(134_217_728, 1.0, "1 Gb/s")]
        public void BitsPerSecondFormatsByMagnitude(long bytes, double seconds, string expected)
        {
            string result = TrafficRateFormatter.BitsPerSecond(bytes, seconds);

            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(500, 1.0, "500 B/s")]
        [InlineData(1024, 1.0, "1 KB/s")]
        [InlineData(1_048_576, 1.0, "1 MB/s")]
        [InlineData(2_147_483_648, 1.0, "2 GB/s")]
        public void BytesPerSecondFormatsByMagnitude(long bytes, double seconds, string expected)
        {
            string result = TrafficRateFormatter.BytesPerSecond(bytes, seconds);

            Assert.Equal(expected, result);
        }

        [Fact]
        public void BucketSecondsUsesGapBetweenFirstTwoPoints()
        {
            DateTime start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            List<ChartPoint> points =
            [
                new ChartPoint(start, 0, 0),
                new ChartPoint(start.AddSeconds(5), 0, 0),
                new ChartPoint(start.AddSeconds(10), 0, 0)
            ];

            double result = TrafficRateFormatter.BucketSeconds(points);

            Assert.Equal(5.0, result);
        }

        [Fact]
        public void BucketSecondsDefaultsToFiveWhenSinglePoint()
        {
            DateTime start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            List<ChartPoint> points = [new ChartPoint(start, 0, 0)];

            double result = TrafficRateFormatter.BucketSeconds(points);

            Assert.Equal(5.0, result);
        }

        [Fact]
        public void BucketSecondsDefaultsToFiveWhenGapIsZero()
        {
            DateTime start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            List<ChartPoint> points =
            [
                new ChartPoint(start, 0, 0),
                new ChartPoint(start, 0, 0)
            ];

            double result = TrafficRateFormatter.BucketSeconds(points);

            Assert.Equal(5.0, result);
        }
    }
}
