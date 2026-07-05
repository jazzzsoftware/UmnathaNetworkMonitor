using NetworkMonitor.Models;
using Xunit;

namespace NetworkMonitor.Tests
{
    public class SpeedTestResultTests
    {
        [Fact]
        public void DownloadMBpsConvertsMegabitsToMegabytes()
        {
            SpeedTestResult result = new SpeedTestResult
            {
                DownloadMbps = 80.0
            };

            Assert.Equal(10.0, result.DownloadMBps, 3);
        }

        [Fact]
        public void UploadMBpsConvertsMegabitsToMegabytes()
        {
            SpeedTestResult result = new SpeedTestResult
            {
                UploadMbps = 18.0
            };

            Assert.Equal(2.25, result.UploadMBps, 3);
        }
    }
}
