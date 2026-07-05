using NetworkMonitor.Models;

namespace NetworkMonitor.Services.SpeedTest
{
    public static class SpeedTestMessage
    {
        public static string Format(SpeedTestResult result)
        {
            string message;

            if (result.Success)
            {
                message = $"Speed test: {result.DownloadMbps:0.0} ↓ / {result.UploadMbps:0.0} ↑ Mbps · {result.DownloadMBps:0.0} ↓ / {result.UploadMBps:0.0} ↑ MBps · {result.LatencyMs:0} ms";
            }
            else
            {
                message = $"Speed test failed: {result.Error}";
            }

            return message;
        }
    }
}
