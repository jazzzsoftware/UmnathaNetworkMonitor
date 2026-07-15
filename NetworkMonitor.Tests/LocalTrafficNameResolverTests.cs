using NetworkMonitor.Services.Traffic;
using Xunit;

namespace NetworkMonitor.Tests
{
    public class LocalTrafficNameResolverTests
    {
        [Fact]
        public void ResolvesKnownIpToDeviceName()
        {
            Dictionary<string, string> namesByIp = new()
            {
                ["192.168.1.50"] = "Synology NAS"
            };

            string resolved = LocalTrafficNameResolver.Resolve("192.168.1.50", namesByIp);

            Assert.Equal("Synology NAS", resolved);
        }

        [Fact]
        public void FallsBackToBareIpWhenUnknown()
        {
            Dictionary<string, string> namesByIp = new();

            string resolved = LocalTrafficNameResolver.Resolve("192.168.1.99", namesByIp);

            Assert.Equal("192.168.1.99", resolved);
        }

        [Fact]
        public void FallsBackToBareIpWhenNameIsWhitespace()
        {
            Dictionary<string, string> namesByIp = new()
            {
                ["192.168.1.50"] = "   "
            };

            string resolved = LocalTrafficNameResolver.Resolve("192.168.1.50", namesByIp);

            Assert.Equal("192.168.1.50", resolved);
        }
    }
}
