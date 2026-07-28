using NetworkMonitor.Core.Charting;
using Xunit;

namespace NetworkMonitor.Tests
{
    public class AxisScaleTests
    {
        [Fact]
        public void PeakJustOverAUnitBoundaryDoesNotJumpToTheNextDecade()
        {
            // The regression: a 2 Gb/s LAN transfer used to produce a 10 Gb/s axis, squashing the
            // trace into the bottom fifth of the chart.
            double niceMax = AxisScale.NiceMax(2_000_000_000.0);

            Assert.Equal(2_000_000_000.0, niceMax);
        }

        [Theory]
        [InlineData(1_200_000_000.0, 2_000_000_000.0)]
        [InlineData(4_500_000_000.0, 5_000_000_000.0)]
        [InlineData(5_000_000_000.0, 5_000_000_000.0)]
        [InlineData(5_100_000_000.0, 10_000_000_000.0)]
        [InlineData(958_000_000.0, 1_000_000_000.0)]
        [InlineData(45_000_000.0, 50_000_000.0)]
        [InlineData(2_000_000.0, 2_000_000.0)]
        [InlineData(1500.0, 2000.0)]
        [InlineData(1.0, 1.0)]
        public void RoundsUpTheOneTwoFiveTenLadder(double value, double expected)
        {
            double niceMax = AxisScale.NiceMax(value);

            Assert.Equal(expected, niceMax);
        }

        [Fact]
        public void NiceMaxIsNeverBelowTheValueItMustContain()
        {
            double value = 1.0;

            while (value < 1e12)
            {
                double niceMax = AxisScale.NiceMax(value);

                Assert.True(niceMax >= value, $"NiceMax({value}) returned {niceMax}, which clips the peak.");

                value *= 1.37;
            }

        }

        [Fact]
        public void NiceMaxHalvesCleanlyForTheMidGridline()
        {
            double niceMax = AxisScale.NiceMax(1_800_000_000.0);
            double half = niceMax / 2.0;

            Assert.Equal(2_000_000_000.0, niceMax);
            Assert.Equal(1_000_000_000.0, half);
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(-1.0)]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        public void NonPositiveOrNonFiniteValuesCollapseToZero(double value)
        {
            double niceMax = AxisScale.NiceMax(value);

            Assert.Equal(0.0, niceMax);
        }
    }
}
