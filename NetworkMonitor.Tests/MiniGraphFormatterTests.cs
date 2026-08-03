using Xunit;
using NetworkMonitor.Models.Formatting;
using NetworkMonitor.Models.SpeedTest;

namespace NetworkMonitor.Tests
{
    public class MiniGraphFormatterTests
    {
        // Two em spaces, spelled out here because the separation between the readings is the point of
        // the format and plain spaces in a literal would not show it.
        private const string Gap = "\u2003\u2003";

        // The widget draws a bold "Speed Test" label and this string follows it, so every case has to
        // start with the same gap or the readings would butt up against the label.
        [Fact]
        public void SpeedTestReadsAsAPromptWhenTheDatabaseIsEmpty()
        {
            string text = MiniGraphFormatter.SpeedTest(null, RateUnitMode.Bits);

            Assert.Equal($"{Gap}not run yet", text);
        }

        [Fact]
        public void SpeedTestShowsRatesPingJitterThenTheTime()
        {
            SpeedTestResult result = new SpeedTestResult
            {
                Timestamp = new DateTime(2026, 8, 2, 6, 0, 0, DateTimeKind.Utc).ToUniversalTime(),
                DownloadMbps = 512.0,
                UploadMbps = 48.0,
                LatencyMs = 9.0,
                JitterMs = 3.0,
                Success = true
            };

            string text = MiniGraphFormatter.SpeedTest(result, RateUnitMode.Bits);

            string time = result.LocalTimestamp.ToString("HH:mm");

            Assert.StartsWith($"{Gap}↓512 ↑48 Mb/s{Gap}", text);
            Assert.Contains($"9 ms Ping{Gap}3 ms Jitter", text);
            Assert.EndsWith($"3 ms Jitter{Gap}{time}", text);
        }

        [Fact]
        public void ByteModeRendersTheSpeedTestInBytesPerSecond()
        {
            SpeedTestResult result = new SpeedTestResult
            {
                Timestamp = new DateTime(2026, 8, 2, 6, 0, 0, DateTimeKind.Utc).ToUniversalTime(),
                DownloadMbps = 512.0,
                UploadMbps = 48.0,
                LatencyMs = 9.0,
                JitterMs = 3.0,
                Success = true
            };

            string text = MiniGraphFormatter.SpeedTest(result, RateUnitMode.Bytes);

            Assert.Contains("MB/s", text);
            Assert.DoesNotContain("Mb/s", text);
        }

        [Fact]
        public void AFailedSpeedTestIsTreatedAsNoResult()
        {
            SpeedTestResult result = new SpeedTestResult
            {
                Timestamp = DateTime.UtcNow,
                Success = false,
                Error = "No internet"
            };

            string text = MiniGraphFormatter.SpeedTest(result, RateUnitMode.Bits);

            Assert.Equal($"{Gap}not run yet", text);
        }

        [Fact]
        public void UnknownDevicesReadsAsATickAtZero()
        {
            string text = MiniGraphFormatter.UnknownDevices(0);

            Assert.Equal("✓ no unknown devices", text);
        }

        [Fact]
        public void UnknownDevicesWarnsAndAgreesInNumber()
        {
            string one = MiniGraphFormatter.UnknownDevices(1);
            string many = MiniGraphFormatter.UnknownDevices(2);

            Assert.Equal("⚠ 1 unknown device", one);
            Assert.Equal("⚠ 2 unknown devices", many);
        }
    }
}
