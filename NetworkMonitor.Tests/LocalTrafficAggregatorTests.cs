using NetworkMonitor.Models;
using NetworkMonitor.Services.Traffic;
using Xunit;

namespace NetworkMonitor.Tests
{
    public class LocalTrafficAggregatorTests
    {
        [Fact]
        public void SumsBytesPerEndpointAndResolvesName()
        {
            List<LocalTrafficMinute> minutes = new()
            {
                new LocalTrafficMinute(60, "192.168.1.50", 100, 200),
                new LocalTrafficMinute(120, "192.168.1.50", 10, 20)
            };

            Dictionary<string, string> namesByIp = new()
            {
                ["192.168.1.50"] = "Synology NAS"
            };

            IReadOnlyList<LocalTrafficDeviceRow> rows = LocalTrafficAggregator.Build(minutes, namesByIp);

            Assert.Single(rows);
            Assert.Equal("Synology NAS", rows[0].DisplayName);
            Assert.Equal(110, rows[0].BytesUploaded);
            Assert.Equal(220, rows[0].BytesDownloaded);
            Assert.Equal(330, rows[0].TotalBytes);
        }

        [Fact]
        public void SortsByTotalBytesDescending()
        {
            List<LocalTrafficMinute> minutes = new()
            {
                new LocalTrafficMinute(60, "192.168.1.10", 1, 1),
                new LocalTrafficMinute(60, "192.168.1.20", 500, 500),
                new LocalTrafficMinute(60, "192.168.1.30", 50, 50)
            };

            Dictionary<string, string> namesByIp = new();

            IReadOnlyList<LocalTrafficDeviceRow> rows = LocalTrafficAggregator.Build(minutes, namesByIp);

            Assert.Equal("192.168.1.20", rows[0].RemoteIp);
            Assert.Equal("192.168.1.30", rows[1].RemoteIp);
            Assert.Equal("192.168.1.10", rows[2].RemoteIp);
        }

        [Fact]
        public void FallsBackToBareIpWhenNameUnknown()
        {
            List<LocalTrafficMinute> minutes = new()
            {
                new LocalTrafficMinute(60, "192.168.1.77", 5, 5)
            };

            Dictionary<string, string> namesByIp = new();

            IReadOnlyList<LocalTrafficDeviceRow> rows = LocalTrafficAggregator.Build(minutes, namesByIp);

            Assert.Equal("192.168.1.77", rows[0].DisplayName);
        }
    }
}
