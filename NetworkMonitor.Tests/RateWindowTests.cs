using Xunit;
using NetworkMonitor.Core.Traffic;

namespace NetworkMonitor.Tests
{
    public class RateWindowTests
    {
        [Fact]
        public void AverageIsZeroWhileTheWindowIsEmpty()
        {
            RateWindow window = new RateWindow();

            Assert.Equal(0, window.Count);
            Assert.Equal(0, window.Total);
            Assert.Equal(0.0, window.Average);
        }

        [Fact]
        public void AverageIsTheMeanOfTheSamplesHeld()
        {
            RateWindow window = new RateWindow();

            window.Add(100, 5);
            window.Add(200, 5);
            window.Add(300, 5);

            Assert.Equal(3, window.Count);
            Assert.Equal(600, window.Total);
            Assert.Equal(200.0, window.Average);
        }

        [Fact]
        public void OldestSamplesLeaveTheWindowOnceItIsFull()
        {
            RateWindow window = new RateWindow();

            window.Add(1000, 3);
            window.Add(10, 3);
            window.Add(20, 3);
            window.Add(30, 3);

            Assert.Equal(3, window.Count);
            Assert.Equal(60, window.Total);
            Assert.Equal(20.0, window.Average);
        }

        [Fact]
        public void TotalReachesZeroOnceAllRetainedSamplesAreIdle()
        {
            RateWindow window = new RateWindow();

            window.Add(500, 2);
            window.Add(0, 2);
            window.Add(0, 2);

            Assert.Equal(2, window.Count);
            Assert.Equal(0, window.Total);
        }

        [Fact]
        public void ShrinkingTheWindowDropsTheOldestSamplesImmediately()
        {
            RateWindow window = new RateWindow();

            window.Add(10, 5);
            window.Add(20, 5);
            window.Add(30, 1);

            Assert.Equal(1, window.Count);
            Assert.Equal(30, window.Total);
        }
    }
}
