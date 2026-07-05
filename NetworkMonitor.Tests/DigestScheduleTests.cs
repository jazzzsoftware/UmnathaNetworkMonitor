using NetworkMonitor.Services.Digest;
using Xunit;

namespace NetworkMonitor.Tests
{
    public class DigestScheduleTests
    {
        [Fact]
        public void NextRunLocalIsTodayAtHourWhenBeforeIt()
        {
            DateTime now = new DateTime(2026, 6, 19, 5, 0, 0, DateTimeKind.Local);

            DateTime next = DigestSchedule.NextRunLocal(now, 6);

            Assert.Equal(new DateTime(2026, 6, 19, 6, 0, 0, DateTimeKind.Local), next);
        }

        [Fact]
        public void NextRunLocalIsTomorrowAtHourWhenAfterIt()
        {
            DateTime now = new DateTime(2026, 6, 19, 7, 0, 0, DateTimeKind.Local);

            DateTime next = DigestSchedule.NextRunLocal(now, 6);

            Assert.Equal(new DateTime(2026, 6, 20, 6, 0, 0, DateTimeKind.Local), next);
        }

        [Fact]
        public void NextRunLocalIsTomorrowAtHourWhenExactlyAtIt()
        {
            DateTime now = new DateTime(2026, 6, 19, 6, 0, 0, DateTimeKind.Local);

            DateTime next = DigestSchedule.NextRunLocal(now, 6);

            Assert.Equal(new DateTime(2026, 6, 20, 6, 0, 0, DateTimeKind.Local), next);
        }

        [Fact]
        public void MissedWindowsEmptyWhenNoneDue()
        {
            DateTime now = new DateTime(2026, 6, 19, 5, 0, 0, DateTimeKind.Local);
            DateTime lastEnd = new DateTime(2026, 6, 18, 6, 0, 0, DateTimeKind.Local).ToUniversalTime();

            List<(DateTime StartUtc, DateTime EndUtc)> windows = DigestSchedule.MissedWindows(lastEnd, now, 6, 90);

            Assert.Empty(windows);
        }

        [Fact]
        public void MissedWindowsReturnsEachMissedDayOrdered()
        {
            DateTime now = new DateTime(2026, 6, 19, 7, 0, 0, DateTimeKind.Local);
            DateTime lastEnd = new DateTime(2026, 6, 16, 6, 0, 0, DateTimeKind.Local).ToUniversalTime();

            List<(DateTime StartUtc, DateTime EndUtc)> windows = DigestSchedule.MissedWindows(lastEnd, now, 6, 90);

            Assert.Equal(3, windows.Count);
            Assert.True(windows[0].EndUtc < windows[1].EndUtc);
            Assert.True(windows[1].EndUtc < windows[2].EndUtc);
            Assert.Equal(new DateTime(2026, 6, 19, 6, 0, 0, DateTimeKind.Local).ToUniversalTime(), windows[2].EndUtc);
        }

        [Fact]
        public void MissedWindowsFirstRunGeneratesOnlyMostRecentDay()
        {
            DateTime now = new DateTime(2026, 6, 19, 7, 0, 0, DateTimeKind.Local);

            List<(DateTime StartUtc, DateTime EndUtc)> windows = DigestSchedule.MissedWindows(null, now, 6, 90);

            Assert.Single(windows);
            Assert.Equal(new DateTime(2026, 6, 19, 6, 0, 0, DateTimeKind.Local).ToUniversalTime(), windows[0].EndUtc);
        }

        [Fact]
        public void MissedWindowsBoundedByRetention()
        {
            DateTime now = new DateTime(2026, 6, 19, 7, 0, 0, DateTimeKind.Local);
            DateTime lastEnd = new DateTime(2025, 1, 1, 6, 0, 0, DateTimeKind.Local).ToUniversalTime();

            List<(DateTime StartUtc, DateTime EndUtc)> windows = DigestSchedule.MissedWindows(lastEnd, now, 6, 90);

            Assert.Equal(90, windows.Count);
            Assert.Equal(new DateTime(2026, 3, 21, 6, 0, 0, DateTimeKind.Local).ToUniversalTime(), windows[0].StartUtc);
            Assert.Equal(new DateTime(2026, 6, 19, 6, 0, 0, DateTimeKind.Local).ToUniversalTime(), windows[89].EndUtc);
        }
    }
}
