using System.Globalization;
using NetworkMonitor.Core.Charting;
using Xunit;

namespace NetworkMonitor.Tests.Charting
{
    public class ChartDrawSummaryTests
    {
        [Fact]
        public void TheSummaryHasTheExactShapeTheSuiteParses()
        {
            string summary = ChartDrawSummary.Format(300, "down,up", 2411520L, 4194304L, "5m");

            Assert.Equal("buckets=300 series=down,up peak=2411520 scale=4194304 range=5m", summary);
        }

        [Fact]
        public void LargeNumbersCarryNoThousandsSeparatorInAnyCulture()
        {
            CultureInfo previous = CultureInfo.CurrentCulture;

            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");

                string summary = ChartDrawSummary.Format(1, "down", 1234567L, 2000000L, "6h");

                Assert.Contains("peak=1234567", summary);
                Assert.Contains("scale=2000000", summary);
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }

        }

        [Fact]
        public void AnEmptyChartIsStillReportedRatherThanLeftBlank()
        {
            string summary = ChartDrawSummary.Format(0, "down,up", 0L, 0L, "5m");

            Assert.Equal("buckets=0 series=down,up peak=0 scale=0 range=5m", summary);
        }

        [Fact]
        public void ASummaryRoundTripsThroughTryParse()
        {
            string summary = ChartDrawSummary.Format(300, "down,up", 2411520L, 4194304L, "5m");

            bool parsed = ChartDrawSummary.TryParse(summary, out ChartDrawValues values);

            Assert.True(parsed);
            Assert.Equal(300, values.Buckets);
            Assert.Equal("down,up", values.Series);
            Assert.Equal(2411520L, values.Peak);
            Assert.Equal(4194304L, values.Scale);
            Assert.Equal("5m", values.Range);
        }

        [Theory]
        [InlineData("")]
        [InlineData("buckets=300")]
        [InlineData("buckets=abc series=down peak=1 scale=1 range=5m")]
        public void TextThatIsNotASummaryFailsToParseRatherThanThrowing(string candidate)
        {
            bool parsed = ChartDrawSummary.TryParse(candidate, out ChartDrawValues values);

            Assert.False(parsed);
            Assert.Equal(0, values.Buckets);
        }

        [Theory]
        [InlineData(1.0, "5m")]
        [InlineData(60.0, "1h")]
        [InlineData(300.0, "6h")]
        public void TheRangeMapperPinsItsBoundaries(double bucketSeconds, string expectedRange)
        {
            string range = ChartDrawRange.FromBucketSeconds(bucketSeconds);

            Assert.Equal(expectedRange, range);
        }
    }
}
