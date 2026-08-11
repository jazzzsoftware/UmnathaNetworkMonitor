using NetworkMonitor.Core.Traffic;
using NetworkMonitor.Models.Traffic;
using Xunit;

namespace NetworkMonitor.Tests
{
    // These two filters are the entire reason the widget's numbers agree with the Internet and Local
    // tabs. They had no coverage at all while they sat in Services.
    public class WidgetTrafficTotalsTests
    {
        [Fact]
        public void WanExcludesSystemBecauseTheInternetTabDoes()
        {
            List<TrafficEntry> entries = new()
            {
                new TrafficEntry { ProcessName = "chrome", BytesDownloaded = 1_000, BytesUploaded = 100 },
                new TrafficEntry { ProcessName = "System", BytesDownloaded = 9_000, BytesUploaded = 900 },
                new TrafficEntry { ProcessName = "steam", BytesDownloaded = 500, BytesUploaded = 50 }
            };

            TrafficTotals totals = WidgetTrafficTotals.Wan(entries);

            Assert.Equal(1_500, totals.BytesDownloaded);
            Assert.Equal(150, totals.BytesUploaded);
        }

        [Fact]
        public void WanIsCaseSensitiveOnTheProcessNameJustAsTheTabQueryIs()
        {
            List<TrafficEntry> entries = new()
            {
                new TrafficEntry { ProcessName = "system", BytesDownloaded = 100, BytesUploaded = 10 }
            };

            TrafficTotals totals = WidgetTrafficTotals.Wan(entries);

            Assert.Equal(100, totals.BytesDownloaded);
        }

        [Fact]
        public void WanOfNothingIsZeroRatherThanAThrow()
        {
            TrafficTotals totals = WidgetTrafficTotals.Wan(new List<TrafficEntry>());

            Assert.Equal(0, totals.BytesDownloaded);
            Assert.Equal(0, totals.BytesUploaded);
        }

        [Fact]
        public void LanKeepsDataAndDropsDiscovery()
        {
            // 445 is SMB — data. 5353 is mDNS — discovery, which ticks over on every device on the
            // segment and drew a dense sawtooth in the widget beside a flat line on the tab.
            List<LocalTrafficDelta> deltas = new()
            {
                new LocalTrafficDelta("explorer", null, "192.168.1.10", 6, 445, 200, 2_000),
                new LocalTrafficDelta("mdnsresponder", null, "192.168.1.11", 17, 5353, 900, 9_000)
            };

            TrafficTotals totals = WidgetTrafficTotals.Lan(deltas);

            Assert.Equal(2_000, totals.BytesDownloaded);
            Assert.Equal(200, totals.BytesUploaded);
        }

        [Fact]
        public void LanOfDiscoveryOnlyIsZero()
        {
            List<LocalTrafficDelta> deltas = new()
            {
                new LocalTrafficDelta("svchost", null, "192.168.1.12", 17, 5353, 500, 5_000)
            };

            TrafficTotals totals = WidgetTrafficTotals.Lan(deltas);

            Assert.Equal(0, totals.BytesDownloaded);
            Assert.Equal(0, totals.BytesUploaded);
        }
    }
}
