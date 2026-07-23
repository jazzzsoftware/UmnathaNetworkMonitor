using NetworkMonitor.Models.Devices;
using NetworkMonitor.Services.Scanning;
using Xunit;

namespace NetworkMonitor.Tests
{
    public class MdnsEnrichmentTests
    {
        [Fact]
        public void FillsNameAndModelOnEmptyDevice()
        {
            Device device = new();

            MdnsEnrichment.Apply(device, new MdnsInfo("Kitchen HomePod", "AudioAccessory5,1"));

            Assert.Equal("Kitchen HomePod", device.MdnsName);
            Assert.Equal("AudioAccessory5,1", device.Model);
        }

        [Fact]
        public void RefreshesStaleMdnsName()
        {
            Device device = new()
            {
                MdnsName = "Old Name"
            };

            MdnsEnrichment.Apply(device, new MdnsInfo("New Name", null));

            Assert.Equal("New Name", device.MdnsName);
        }

        [Fact]
        public void NullInfoLeavesDeviceUnchanged()
        {
            Device device = new()
            {
                MdnsName = "Existing",
                Model = "ExistingModel"
            };

            MdnsEnrichment.Apply(device, null);

            Assert.Equal("Existing", device.MdnsName);
            Assert.Equal("ExistingModel", device.Model);
        }

        [Fact]
        public void EmptyValuesDoNotClobberAndFriendlyNameUntouched()
        {
            Device device = new()
            {
                FriendlyName = "Curated",
                MdnsName = "Existing"
            };

            MdnsEnrichment.Apply(device, new MdnsInfo(string.Empty, null));

            Assert.Equal("Existing", device.MdnsName);
            Assert.Equal("Curated", device.FriendlyName);
        }
    }
}
