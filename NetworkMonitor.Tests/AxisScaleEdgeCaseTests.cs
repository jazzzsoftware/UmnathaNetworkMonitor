using NetworkMonitor.Core.Charting;
using Xunit;

namespace NetworkMonitor.Tests
{
    // The review inventory noted every existing AxisScale InlineData is >= 1.0, while sub-unit values
    // are reachable whenever a large bucket holds only a few bytes, and that the exact decade
    // boundary and negative infinity were untested.
    public class AxisScaleEdgeCaseTests
    {
        [Theory]
        [InlineData(0.3)]
        [InlineData(0.05)]
        [InlineData(0.9)]
        [InlineData(0.0001)]
        public void SubUnitValuesStillGetAnAxisAtOrAboveThem(double value)
        {
            double niceMax = AxisScale.NiceMax(value);

            Assert.True(niceMax >= value, $"NiceMax({value}) returned {niceMax}, which is below the value it must contain");
            Assert.True(niceMax > 0.0, $"NiceMax({value}) returned {niceMax}, which cannot scale a chart");
        }

        [Fact]
        public void TheExactDecadeBoundaryDoesNotJumpADecade()
        {
            double niceMax = AxisScale.NiceMax(10.0);

            Assert.Equal(10.0, niceMax);
        }

        // Degenerate input returns 0.0 rather than inventing an axis, and the caller applies the
        // floor — TrafficAreaChart divides by a safeMax, not by this value directly. Pinning that
        // contract here so a future "helpful" change to return 1.0 has to argue with a test.
        [Theory]
        [InlineData(0.0)]
        [InlineData(-5.0)]
        [InlineData(double.NegativeInfinity)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NaN)]
        public void DegenerateInputReturnsZeroAndLeavesTheFloorToTheCaller(double value)
        {
            double niceMax = AxisScale.NiceMax(value);

            Assert.Equal(0.0, niceMax);
        }
    }
}
