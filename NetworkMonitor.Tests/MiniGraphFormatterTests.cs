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

        // Two en spaces, spelled out for the same reason Gap is: the separation is the point of the
        // format and plain spaces in a literal would not show it.
        private const string ShortGap = "\u2002\u2002";

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

        // The horizontal cell draws its own bold "Speed" label with a margin, so unlike the vertical
        // widget's line this string must not carry a leading gap of its own.
        [Fact]
        public void ShortSpeedTestOpensWithTheDownloadRateAndNoLeadingGap()
        {
            SpeedTestResult result = new SpeedTestResult
            {
                Timestamp = new DateTime(2026, 8, 2, 6, 0, 0, DateTimeKind.Utc),
                DownloadMbps = 94.0,
                UploadMbps = 12.0,
                LatencyMs = 18.0,
                JitterMs = 4.0,
                Success = true
            };

            string text = MiniGraphFormatter.SpeedTestShort(result, RateUnitMode.Bits);

            Assert.Equal($"↓94 ↑12 Mb/s{ShortGap}18 ms", text);
        }

        [Fact]
        public void ShortSpeedTestDropsJitterAndTheTimestamp()
        {
            SpeedTestResult result = new SpeedTestResult
            {
                Timestamp = new DateTime(2026, 8, 2, 6, 0, 0, DateTimeKind.Utc),
                DownloadMbps = 94.0,
                UploadMbps = 12.0,
                LatencyMs = 18.0,
                JitterMs = 4.0,
                Success = true
            };

            string text = MiniGraphFormatter.SpeedTestShort(result, RateUnitMode.Bits);

            Assert.DoesNotContain("Jitter", text);
            Assert.DoesNotContain(result.LocalTimestamp.ToString("HH:mm"), text);
        }

        [Fact]
        public void ShortSpeedTestHonoursByteMode()
        {
            SpeedTestResult result = new SpeedTestResult
            {
                Timestamp = new DateTime(2026, 8, 2, 6, 0, 0, DateTimeKind.Utc),
                DownloadMbps = 512.0,
                UploadMbps = 48.0,
                LatencyMs = 9.0,
                JitterMs = 3.0,
                Success = true
            };

            string text = MiniGraphFormatter.SpeedTestShort(result, RateUnitMode.Bytes);

            Assert.Contains("MB/s", text);
            Assert.DoesNotContain("Mb/s", text);
        }

        // Below ten a rate must keep its decimal or a slow link reads as zero, and at or above ten the
        // decimal carries nothing and costs width the cell does not have.
        [Fact]
        public void ShortSpeedTestKeepsADecimalOnlyBelowTen()
        {
            SpeedTestResult slow = new SpeedTestResult
            {
                Timestamp = new DateTime(2026, 8, 2, 6, 0, 0, DateTimeKind.Utc),
                DownloadMbps = 5.6,
                UploadMbps = 3.0,
                LatencyMs = 40.0,
                Success = true
            };

            string text = MiniGraphFormatter.SpeedTestShort(slow, RateUnitMode.Bits);

            Assert.Equal($"↓5.6 ↑3 Mb/s{ShortGap}40 ms", text);
        }

        [Fact]
        public void ShortSpeedTestReadsAsAPromptWhenNothingHasRun()
        {
            string missing = MiniGraphFormatter.SpeedTestShort(null, RateUnitMode.Bits);

            Assert.Equal("not run yet", missing);
        }

        [Fact]
        public void ShortSpeedTestTreatsAFailedRunAsNoResult()
        {
            SpeedTestResult failed = new SpeedTestResult
            {
                Timestamp = DateTime.UtcNow,
                Success = false,
                Error = "No internet"
            };

            string text = MiniGraphFormatter.SpeedTestShort(failed, RateUnitMode.Bits);

            Assert.Equal("not run yet", text);
        }
    }
}
