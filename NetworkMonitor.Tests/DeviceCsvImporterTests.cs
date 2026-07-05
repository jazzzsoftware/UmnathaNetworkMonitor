using System.Collections.Generic;
using NetworkMonitor.Models;
using NetworkMonitor.Services.Csv;
using Xunit;

namespace NetworkMonitor.Tests
{
    public class DeviceCsvImporterTests
    {
        [Fact]
        public void ParseReadsSingleRow()
        {
            string csv =
                "Name,Type,IP Address,MAC Address,Vendor,Hostname,Online,First Seen,Last Seen,Notes\n" +
                "Office PC,PC,192.168.1.10,00:1B:44:AA:BB:CC,Dell,office.local,No,2026-01-01 08:00:00,2026-01-02 09:00:00,Desk\n";

            IReadOnlyList<Device> devices = DeviceCsvImporter.Parse(csv);

            Assert.Single(devices);
            Device device = devices[0];
            Assert.Equal("00:1B:44:AA:BB:CC", device.MacAddress);
            Assert.Equal("192.168.1.10", device.IpAddress);
            Assert.Equal(DeviceType.PC, device.Type);
            Assert.Equal("Dell", device.Vendor);
            Assert.Equal("Desk", device.Notes);
            Assert.True(device.IsApproved);
        }

        [Fact]
        public void ParseSkipsRowsWithoutMacAddress()
        {
            string csv =
                "Name,Type,IP Address,MAC Address\n" +
                "No Mac,PC,192.168.1.11,\n" +
                "Has Mac,PC,192.168.1.12,00:1B:44:00:00:01\n";

            IReadOnlyList<Device> devices = DeviceCsvImporter.Parse(csv);

            Assert.Single(devices);
            Assert.Equal("00:1B:44:00:00:01", devices[0].MacAddress);
        }

        [Fact]
        public void ParseReturnsEmptyWhenNoMacColumn()
        {
            string csv =
                "Name,Type,IP Address\n" +
                "Office PC,PC,192.168.1.10\n";

            IReadOnlyList<Device> devices = DeviceCsvImporter.Parse(csv);

            Assert.Empty(devices);
        }

        [Fact]
        public void ParseRejectsDeviceHistoryExport()
        {
            string csv =
                "Time,Event,Name,IP Address,MAC Address,Vendor\n" +
                "2026-01-01 08:00:00,Appeared,Office PC,192.168.1.10,00:1B:44:AA:BB:CC,Dell\n";

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => DeviceCsvImporter.Parse(csv));

            Assert.Contains("device history", exception.Message);
        }

        [Fact]
        public void ParseHandlesQuotedFieldsWithCommasAndQuotes()
        {
            string csv =
                "Name,Type,IP Address,MAC Address,Notes\n" +
                "\"The \"\"Big\"\" Server\",Server,192.168.1.20,00:1B:44:00:00:02,\"Rack 4, slot 2\"\n";

            IReadOnlyList<Device> devices = DeviceCsvImporter.Parse(csv);

            Assert.Single(devices);
            Device device = devices[0];
            Assert.Equal("The \"Big\" Server", device.FriendlyName);
            Assert.Equal("Rack 4, slot 2", device.Notes);
            Assert.Equal(DeviceType.Server, device.Type);
        }

        [Fact]
        public void ParseUnknownTypeFallsBackToUnknown()
        {
            string csv =
                "Name,Type,IP Address,MAC Address\n" +
                "Gadget,Toaster,192.168.1.30,00:1B:44:00:00:03\n";

            IReadOnlyList<Device> devices = DeviceCsvImporter.Parse(csv);

            Assert.Single(devices);
            Assert.Equal(DeviceType.Unknown, devices[0].Type);
        }

        [Fact]
        public void ParseDoesNotSetFriendlyNameWhenNameEqualsHostname()
        {
            string csv =
                "Name,Type,IP Address,MAC Address,Hostname\n" +
                "office.local,PC,192.168.1.40,00:1B:44:00:00:04,office.local\n";

            IReadOnlyList<Device> devices = DeviceCsvImporter.Parse(csv);

            Assert.Single(devices);
            Assert.Null(devices[0].FriendlyName);
        }
    }
}
