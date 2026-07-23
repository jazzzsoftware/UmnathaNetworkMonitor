using NetworkMonitor.Models.Formatting;
using NetworkMonitor.Models.SpeedTest;
using Xunit;
using NetworkMonitor.Core.SpeedTest;

namespace NetworkMonitor.Tests
{
    [Collection("RateUnitMode")]
    public class SpeedTestMessageTests
    {
        [Fact]
        public void FormatProducesSuccessSummary()
        {
            SpeedTestResult result = BuildSuccessResult();

            string message = SpeedTestMessage.Format(result);

            Assert.Equal("Speed test: 240.0 ↓ / 16.0 ↑ Mb/s · 30.0 ↓ / 2.0 ↑ MB/s · 12 ms", message);
        }

        [Fact]
        public void FormatShowsOnlyBitsInBitsMode()
        {
            SpeedTestResult result = BuildSuccessResult();

            try
            {
                TrafficRateFormatter.Mode = RateUnitMode.Bits;
                string message = SpeedTestMessage.Format(result);

                Assert.Equal("Speed test: 240.0 ↓ / 16.0 ↑ Mb/s · 12 ms", message);
            }
            finally
            {
                TrafficRateFormatter.Mode = RateUnitMode.Both;
            }

        }

        [Fact]
        public void FormatShowsOnlyBytesInBytesMode()
        {
            SpeedTestResult result = BuildSuccessResult();

            try
            {
                TrafficRateFormatter.Mode = RateUnitMode.Bytes;
                string message = SpeedTestMessage.Format(result);

                Assert.Equal("Speed test: 30.0 ↓ / 2.0 ↑ MB/s · 12 ms", message);
            }
            finally
            {
                TrafficRateFormatter.Mode = RateUnitMode.Both;
            }

        }

        [Fact]
        public void FormatProducesFailureSummary()
        {
            SpeedTestResult result = new SpeedTestResult
            {
                Success = false,
                Error = "Name resolution failed"
            };

            string message = SpeedTestMessage.Format(result);

            Assert.Equal("Speed test failed: Name resolution failed", message);
        }

        private static SpeedTestResult BuildSuccessResult()
        {
            SpeedTestResult result = new SpeedTestResult
            {
                DownloadMbps = 240.0,
                UploadMbps = 16.0,
                LatencyMs = 12.4,
                Success = true
            };

            return result;
        }
    }
}
