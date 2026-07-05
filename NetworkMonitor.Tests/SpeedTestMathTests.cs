using System;
using System.Collections.Generic;
using NetworkMonitor.Services.SpeedTest;
using Xunit;

namespace NetworkMonitor.Tests
{
    public class SpeedTestMathTests
    {
        [Fact]
        public void ToMbpsConvertsBytesPerSecondToMegabitsPerSecond()
        {
            double mbps = SpeedTestMath.ToMbps(1_000_000, TimeSpan.FromSeconds(1));

            Assert.Equal(8.0, mbps, 3);
        }

        [Fact]
        public void ToMbpsReturnsZeroForZeroElapsed()
        {
            double mbps = SpeedTestMath.ToMbps(1_000_000, TimeSpan.Zero);

            Assert.Equal(0.0, mbps);
        }

        [Fact]
        public void MeanAveragesSamples()
        {
            List<double> samples = [10.0, 20.0, 30.0];

            double mean = SpeedTestMath.Mean(samples);

            Assert.Equal(20.0, mean, 3);
        }

        [Fact]
        public void MeanReturnsZeroForEmpty()
        {
            List<double> samples = [];

            double mean = SpeedTestMath.Mean(samples);

            Assert.Equal(0.0, mean);
        }

        [Fact]
        public void MinReturnsSmallestSample()
        {
            List<double> samples = [30.0, 12.0, 20.0];

            double min = SpeedTestMath.Min(samples);

            Assert.Equal(12.0, min, 3);
        }

        [Fact]
        public void MinReturnsZeroForEmpty()
        {
            List<double> samples = [];

            double min = SpeedTestMath.Min(samples);

            Assert.Equal(0.0, min);
        }

        [Fact]
        public void JitterAveragesConsecutiveDifferences()
        {
            List<double> samples = [10.0, 14.0, 12.0];

            double jitter = SpeedTestMath.Jitter(samples);

            Assert.Equal(3.0, jitter, 3);
        }

        [Fact]
        public void JitterReturnsZeroForSingleSample()
        {
            List<double> samples = [10.0];

            double jitter = SpeedTestMath.Jitter(samples);

            Assert.Equal(0.0, jitter);
        }

        [Fact]
        public void ColoFromCfRayReturnsSuffixAfterDash()
        {
            string colo = SpeedTestMath.ColoFromCfRay("8a1b2c3d4e5f6789-JNB");

            Assert.Equal("JNB", colo);
        }

        [Fact]
        public void ColoFromCfRayReturnsEmptyWhenNoDash()
        {
            string colo = SpeedTestMath.ColoFromCfRay("8a1b2c3d4e5f6789");

            Assert.Equal(string.Empty, colo);
        }

        [Fact]
        public void ColoFromCfRayReturnsEmptyForTrailingDash()
        {
            string colo = SpeedTestMath.ColoFromCfRay("8a1b2c3d4e5f6789-");

            Assert.Equal(string.Empty, colo);
        }

        [Fact]
        public void ColoFromCfRayReturnsEmptyForEmptyInput()
        {
            string colo = SpeedTestMath.ColoFromCfRay(string.Empty);

            Assert.Equal(string.Empty, colo);
        }
    }
}
