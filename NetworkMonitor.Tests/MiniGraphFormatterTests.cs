using Xunit;
using NetworkMonitor.Models.Formatting;
using NetworkMonitor.Models.SpeedTest;

namespace NetworkMonitor.Tests
{
    public class MiniGraphFormatterTests
    {
        [Fact]
        public void RateReadsAsADashBelowTheHalfMegabitThreshold()
        {
            string text = MiniGraphFormatter.Rate(30_000.0, 20_000.0, RateUnitMode.Bits);

            Assert.Equal("—", text);
        }

        [Fact]
        public void RateShowsBothArrowsWithASingleSharedUnit()
        {
            string text = MiniGraphFormatter.Rate(14_750_000.0, 375_000.0, RateUnitMode.Bits);

            Assert.Equal("↓118 ↑3 Mb/s", text);
        }

        [Fact]
        public void BothModeRendersAsMegabitsOnlyBecauseThereIsNoRoomForTwoUnits()
        {
            string bits = MiniGraphFormatter.Rate(14_750_000.0, 375_000.0, RateUnitMode.Bits);
            string both = MiniGraphFormatter.Rate(14_750_000.0, 375_000.0, RateUnitMode.Both);

            Assert.Equal(bits, both);
        }

        [Fact]
        public void ByteModeRendersMegabytesPerSecond()
        {
            string text = MiniGraphFormatter.Rate(14_000_000.0, 2_000_000.0, RateUnitMode.Bytes);

            Assert.Equal("↓14 ↑2 MB/s", text);
        }

        [Fact]
        public void SmallRatesKeepOneDecimalSoTheyDoNotCollapseToZero()
        {
            string text = MiniGraphFormatter.Rate(700_000.0, 100_000.0, RateUnitMode.Bits);

            Assert.Equal("↓5.6 ↑0.8 Mb/s", text);
        }

        [Fact]
        public void SpeedTestReadsAsAPromptWhenTheDatabaseIsEmpty()
        {
            string text = MiniGraphFormatter.SpeedTest(null, RateUnitMode.Bits);

            Assert.Equal("No speed test yet", text);
        }

        [Fact]
        public void SpeedTestShowsTimeRatesAndPing()
        {
            SpeedTestResult result = new SpeedTestResult
            {
                Timestamp = new DateTime(2026, 8, 2, 6, 0, 0, DateTimeKind.Utc).ToUniversalTime(),
                DownloadMbps = 512.0,
                UploadMbps = 48.0,
                LatencyMs = 9.0,
                Success = true
            };

            string text = MiniGraphFormatter.SpeedTest(result, RateUnitMode.Bits);

            Assert.StartsWith("Speed ", text);
            Assert.Contains("↓512 ↑48 Mb/s", text);
            Assert.EndsWith("· 9 ms", text);
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

            Assert.Equal("No speed test yet", text);
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
