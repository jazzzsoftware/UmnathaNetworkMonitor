using NetworkMonitor.Services.Scanning;
using Xunit;

namespace NetworkMonitor.Tests
{
    public class MacNormalizerTests
    {
        [Theory]
        [InlineData("02:11:22:33:44:55", true)]
        [InlineData("06:AA:BB:CC:DD:EE", true)]
        [InlineData("DA:A1:19:9F:00:01", true)]
        [InlineData("3e-1a-2b-3c-4d-5e", true)]
        [InlineData("b8:27:eb:00:11:22", false)]
        [InlineData("F0:18:98:AA:BB:CC", false)]
        [InlineData("DC:A6:32:00:11:22", false)]
        [InlineData("01:00:5E:00:00:01", false)]
        [InlineData("FF:FF:FF:FF:FF:FF", false)]
        public void DetectsRandomizedMacByLocallyAdministeredBit(string mac, bool expected)
        {
            bool randomized = MacNormalizer.IsRandomized(mac);

            Assert.Equal(expected, randomized);
        }
    }
}
