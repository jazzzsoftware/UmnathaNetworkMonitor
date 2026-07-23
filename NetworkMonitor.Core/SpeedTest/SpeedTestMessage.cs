using NetworkMonitor.Models.Formatting;
using NetworkMonitor.Models.SpeedTest;

namespace NetworkMonitor.Core.SpeedTest
{
    public static class SpeedTestMessage
    {
        public static string Format(SpeedTestResult result)
        {
            string message;

            if (result.Success)
            {
                string throughput = TrafficRateFormatter.Mode switch
                {
                    RateUnitMode.Bits => $"{result.DownloadMbps:0.0} ↓ / {result.UploadMbps:0.0} ↑ Mb/s",
                    RateUnitMode.Bytes => $"{result.DownloadMBps:0.0} ↓ / {result.UploadMBps:0.0} ↑ MB/s",
                    _ => $"{result.DownloadMbps:0.0} ↓ / {result.UploadMbps:0.0} ↑ Mb/s · {result.DownloadMBps:0.0} ↓ / {result.UploadMBps:0.0} ↑ MB/s"
                };

                message = $"Speed test: {throughput} · {result.LatencyMs:0} ms";
            }
            else
            {
                message = $"Speed test failed: {result.Error}";
            }

            return message;
        }
    }
}
