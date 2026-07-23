using System.Net;
using Xunit;
using NetworkMonitor.Core.Traffic;

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

        [Theory]
        [InlineData("127.0.0.1")]
        [InlineData("127.0.0.53")]
        [InlineData("127.255.255.255")]
        public void TreatsLoopbackAsSelfOrLoopback(string address)
        {
            LanClassifier classifier = new LanClassifier();

            bool isSelfOrLoopback = classifier.IsSelfOrLoopback(IPAddress.Parse(address));

            Assert.True(isSelfOrLoopback);
        }

        [Theory]
        [InlineData("8.8.8.8")]
        [InlineData("203.0.113.7")]
        public void TreatsRemoteAddressesAsNotSelfOrLoopback(string address)
        {
            LanClassifier classifier = new LanClassifier();

            bool isSelfOrLoopback = classifier.IsSelfOrLoopback(IPAddress.Parse(address));

            Assert.False(isSelfOrLoopback);
        }

        [Theory]
        [InlineData("192.168.1.255")]
        [InlineData("192.168.100.255")]
        [InlineData("255.255.255.255")]
        [InlineData("224.0.0.251")]
        [InlineData("239.255.255.250")]
        public void TreatsBroadcastAndMulticastAsNonDevice(string address)
        {
            LanClassifier classifier = new LanClassifier();

            bool result = classifier.IsBroadcastOrMulticast(IPAddress.Parse(address));

            Assert.True(result);
        }

        [Theory]
        [InlineData("192.168.1.50")]
        [InlineData("192.168.1.126")]
        [InlineData("8.8.8.8")]
        public void TreatsRegularAddressesAsDevices(string address)
        {
            LanClassifier classifier = new LanClassifier();

            bool result = classifier.IsBroadcastOrMulticast(IPAddress.Parse(address));

            Assert.False(result);
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
