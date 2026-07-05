using NetworkMonitor.Models;
using NetworkMonitor.Services.SpeedTest;
using Xunit;

namespace NetworkMonitor.Tests
{
    public class SpeedTestMessageTests
    {
        [Fact]
        public void FormatProducesSuccessSummary()
        {
            SpeedTestResult result = new SpeedTestResult
            {
                DownloadMbps = 240.0,
                UploadMbps = 16.0,
                LatencyMs = 12.4,
                Success = true
            };

            string message = SpeedTestMessage.Format(result);

            Assert.Equal("Speed test: 240.0 ↓ / 16.0 ↑ Mbps · 30.0 ↓ / 2.0 ↑ MBps · 12 ms", message);
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
    }
}
