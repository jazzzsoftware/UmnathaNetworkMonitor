using System.Collections.Generic;
using NetworkMonitor.Models;
using NetworkMonitor.Services.Traffic;
using Xunit;

namespace NetworkMonitor.Tests
{
    public class LocalTrafficAggregatorTests
    {
        [Fact]
        public void GroupsByAppWithPerDeviceChildrenSortedByTotal()
        {
            List<LocalTrafficMinute> minutes = new()
            {
                new LocalTrafficMinute(60, "System", "192.168.1.50", 100, 4000),
                new LocalTrafficMinute(120, "System", "192.168.1.50", 0, 1000),
                new LocalTrafficMinute(60, "System", "192.168.1.99", 10, 20),
                new LocalTrafficMinute(60, "chrome", "192.168.1.10", 5, 5)
            };
            Dictionary<string, string> namesByIp = new()
            {
                { "192.168.1.50", "SurfratNas" }
            };

            IReadOnlyList<LocalTrafficAppRow> rows = LocalTrafficAggregator.Build(minutes, namesByIp);

            Assert.Equal(2, rows.Count);
            Assert.Equal("System", rows[0].ProcessName);
            Assert.Equal(5130, rows[0].TotalBytes);
            Assert.Equal(2, rows[0].Peers.Count);
            Assert.Equal("SurfratNas", rows[0].Peers[0].DisplayName);
            Assert.Equal(5100, rows[0].Peers[0].TotalBytes);
            Assert.Equal("192.168.1.99", rows[0].Peers[1].DisplayName);
            Assert.Equal("SurfratNas +1", rows[0].PeerSummary);
            Assert.Equal("chrome", rows[1].ProcessName);
            Assert.Equal("192.168.1.10", rows[1].PeerSummary);
        }
    }
}
