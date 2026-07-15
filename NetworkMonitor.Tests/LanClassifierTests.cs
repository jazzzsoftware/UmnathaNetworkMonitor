using System.Net;
using NetworkMonitor.Services.Traffic;
using Xunit;

namespace NetworkMonitor.Tests
{
    public class LanClassifierTests
    {
        [Theory]
        [InlineData("10.0.0.1")]
        [InlineData("10.255.255.254")]
        [InlineData("172.16.0.1")]
        [InlineData("172.31.255.254")]
        [InlineData("192.168.0.1")]
        [InlineData("192.168.1.50")]
        [InlineData("169.254.10.20")]
        public void ClassifiesRfc1918AndLinkLocalAsLocal(string address)
        {
            LanClassifier classifier = new LanClassifier();

            bool isLocal = classifier.TryClassifyLocal(IPAddress.Parse(address), out uint packed);

            Assert.True(isLocal);
            Assert.NotEqual(0u, packed);
        }

        [Theory]
        [InlineData("8.8.8.8")]
        [InlineData("1.1.1.1")]
        [InlineData("172.32.0.1")]
        [InlineData("11.0.0.1")]
        [InlineData("192.169.0.1")]
        public void ClassifiesPublicAddressesAsNotLocal(string address)
        {
            LanClassifier classifier = new LanClassifier();

            bool isLocal = classifier.TryClassifyLocal(IPAddress.Parse(address), out uint packed);

            Assert.False(isLocal);
        }

        [Fact]
        public void RejectsIpv6Addresses()
        {
            LanClassifier classifier = new LanClassifier();

            bool isLocal = classifier.TryClassifyLocal(IPAddress.Parse("fe80::1"), out uint packed);

            Assert.False(isLocal);
            Assert.Equal(0u, packed);
        }

        [Fact]
        public void PacksIpv4ToBigEndianUint()
        {
            bool packed = LanClassifier.TryPackIpv4(IPAddress.Parse("192.168.1.50"), out uint value);

            Assert.True(packed);
            Assert.Equal(0xC0A80132u, value);
        }

        [Fact]
        public void FormatRoundTripsPackedValue()
        {
            LanClassifier.TryPackIpv4(IPAddress.Parse("192.168.1.50"), out uint value);

            string formatted = LanClassifier.Format(value);

            Assert.Equal("192.168.1.50", formatted);
        }

        [Fact]
        public void TryPackIpv4RejectsIpv6()
        {
            bool packed = LanClassifier.TryPackIpv4(IPAddress.Parse("fe80::1"), out uint value);

            Assert.False(packed);
            Assert.Equal(0u, value);
        }
    }
}
