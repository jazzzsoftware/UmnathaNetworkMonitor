using System;
using System.Collections.Generic;
using NetworkMonitor.Models.Charting;
using NetworkMonitor.Models.Formatting;
using Xunit;

namespace NetworkMonitor.Tests
{
    [Collection("RateUnitMode")]
    public class TrafficRateFormatterTests
    {
        [Theory]
        [InlineData(10, 1.0, "80 b/s")]
        [InlineData(125, 1.0, "1 Kb/s")]
        [InlineData(125_000, 1.0, "1 Mb/s")]
        [InlineData(125_000_000, 1.0, "1 Gb/s")]
        public void BitsPerSecondFormatsByMagnitude(long bytes, double seconds, string expected)
        {
            string result = TrafficRateFormatter.BitsPerSecond(bytes, seconds);

            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(500, 1.0, "500 B/s")]
        [InlineData(1000, 1.0, "1 KB/s")]
        [InlineData(1_000_000, 1.0, "1 MB/s")]
        [InlineData(2_000_000_000, 1.0, "2 GB/s")]
        public void BytesPerSecondFormatsByMagnitude(long bytes, double seconds, string expected)
        {
            string result = TrafficRateFormatter.BytesPerSecond(bytes, seconds);

            Assert.Equal(expected, result);
        }

        // The mini graph has room for one unit per line, so Both has to resolve to something. Bits wins
        // because that is what line speeds are quoted in; an explicit choice is left alone.
        [Fact]
        public void SingleUnitResolvesBothToBitsAndLeavesAnExplicitChoiceAlone()
        {
            Assert.Equal(RateUnitMode.Bits, TrafficRateFormatter.SingleUnit(RateUnitMode.Both));
            Assert.Equal(RateUnitMode.Bits, TrafficRateFormatter.SingleUnit(RateUnitMode.Bits));
            Assert.Equal(RateUnitMode.Bytes, TrafficRateFormatter.SingleUnit(RateUnitMode.Bytes));
        }

        [Fact]
        public void CompositeShowsBothUnitsByDefault()
        {
            string result = TrafficRateFormatter.Composite(1_000_000, 1.0);

            Assert.Equal("8 Mb/s · 1 MB/s", result);
        }

        [Fact]
        public void CompositeShowsOnlyBitsInBitsMode()
        {

            try
            {
                TrafficRateFormatter.Mode = RateUnitMode.Bits;
                string result = TrafficRateFormatter.Composite(1_000_000, 1.0);

                Assert.Equal("8 Mb/s", result);
            }
            finally
            {
                TrafficRateFormatter.Mode = RateUnitMode.Both;
            }

        }

        [Fact]
        public void CompositeShowsOnlyBytesInBytesMode()
        {

            try
            {
                TrafficRateFormatter.Mode = RateUnitMode.Bytes;
                string result = TrafficRateFormatter.Composite(1_000_000, 1.0);

                Assert.Equal("1 MB/s", result);
            }
            finally
            {
                TrafficRateFormatter.Mode = RateUnitMode.Both;
            }

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
