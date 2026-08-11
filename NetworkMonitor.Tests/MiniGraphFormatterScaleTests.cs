using NetworkMonitor.Models.Formatting;
using NetworkMonitor.Models.SpeedTest;
using Xunit;

namespace NetworkMonitor.Tests
{
    // Scaled is private, so these go through SpeedTestShort — which is what the strip actually shows.
    public class MiniGraphFormatterScaleTests
    {
        private static SpeedTestResult Result(double downloadMbps, double uploadMbps)
        {
            SpeedTestResult result = new SpeedTestResult
            {
                Success = true,
                DownloadMbps = downloadMbps,
                UploadMbps = uploadMbps,
                LatencyMs = 12,
                JitterMs = 3,
                Timestamp = new DateTime(2026, 8, 11, 12, 0, 0, DateTimeKind.Utc)
            };

            return result;
        }

        [Fact]
        public void ALinkTooSlowToRoundAboveZeroDoesNotReadAsZero()
        {
            string text = MiniGraphFormatter.SpeedTestShort(Result(0.03, 0.02), RateUnitMode.Bits);

            Assert.Contains("<0.1", text);
            Assert.DoesNotContain("↓0 ", text);
        }

        [Fact]
        public void ATrulyIdleLinkStillReadsAsZero()
        {
            // Zero is zero. The "<0.1" form means "slow", and using it for nothing at all would be a
            // different kind of lie.
            string text = MiniGraphFormatter.SpeedTestShort(Result(0.0, 0.0), RateUnitMode.Bits);

            Assert.DoesNotContain("<0.1", text);
            Assert.Contains("0", text);
        }

        [Theory]
        [InlineData(0.05, "0.1")]
        [InlineData(0.4, "0.4")]
        [InlineData(3.0, "3")]
        [InlineData(5.6, "5.6")]
        public void ValuesThatRoundAboveZeroAreUnchanged(double downloadMbps, string expected)
        {
            string text = MiniGraphFormatter.SpeedTestShort(Result(downloadMbps, downloadMbps), RateUnitMode.Bits);

            Assert.Contains($"↓{expected} ", text);
            Assert.DoesNotContain("<0.1", text);
        }

        [Fact]
        public void TenAndAboveDropsTheDecimalAsBefore()
        {
            string text = MiniGraphFormatter.SpeedTestShort(Result(94.3, 21.7), RateUnitMode.Bits);

            Assert.Contains("↓94 ", text);
            Assert.Contains("↑22 ", text);
        }
    }
}
