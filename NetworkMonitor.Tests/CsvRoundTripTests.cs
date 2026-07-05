using System.Collections.Generic;
using NetworkMonitor.Models;
using NetworkMonitor.Services.Csv;
using Xunit;

namespace NetworkMonitor.Tests
{
    public class CsvRoundTripTests
    {
        [Fact]
        public void ExportThenImportPreservesCoreFields()
        {
            List<Device> originals =
            [
                new Device
                {
                    FriendlyName = "Living Room TV",
                    MacAddress = "00:1B:44:11:22:33",
                    IpAddress = "192.168.1.50",
                    Hostname = "tv.local",
                    Vendor = "Samsung",
                    Type = DeviceType.SmartDevice,
                    Notes = "Behind the couch, HDMI 2"
                },
                new Device
                {
                    FriendlyName = "Gateway",
                    MacAddress = "00:1B:44:99:88:77",
                    IpAddress = "192.168.1.1",
                    Hostname = "router.local",
                    Vendor = "Netgear",
                    Type = DeviceType.Router,
                    Notes = null
                }
            ];

            string csv = DeviceCsvExporter.ToCsv(originals);
            IReadOnlyList<Device> imported = DeviceCsvImporter.Parse(csv);

            Assert.Equal(originals.Count, imported.Count);

            for (int index = 0; index < originals.Count; index++)
            {
                Device original = originals[index];
                Device roundTripped = imported[index];

                Assert.Equal(original.MacAddress, roundTripped.MacAddress);
                Assert.Equal(original.IpAddress, roundTripped.IpAddress);
                Assert.Equal(original.Hostname, roundTripped.Hostname);
                Assert.Equal(original.Vendor, roundTripped.Vendor);
                Assert.Equal(original.Type, roundTripped.Type);
                Assert.Equal(original.FriendlyName, roundTripped.FriendlyName);
                Assert.Equal(original.Notes, roundTripped.Notes);
            }

        }
    }
}
