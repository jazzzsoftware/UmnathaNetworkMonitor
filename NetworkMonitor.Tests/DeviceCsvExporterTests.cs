using System;
using System.Collections.Generic;
using NetworkMonitor.Models.Devices;
using NetworkMonitor.Services.Csv;
using Xunit;

namespace NetworkMonitor.Tests
{
    public class DeviceCsvExporterTests
    {
        [Fact]
        public void ToCsvWritesHeaderRow()
        {
            List<Device> devices = [];

            string csv = DeviceCsvExporter.ToCsv(devices);

            string firstLine = csv.Split('\n')[0].TrimEnd('\r');

            Assert.Equal("Name,Type,IP Address,MAC Address,Vendor,Hostname,Online,First Seen,Last Seen,Notes", firstLine);
        }

        [Fact]
        public void ToCsvWritesOnlineLabel()
        {
            List<Device> devices =
            [
                new Device
                {
                    FriendlyName = "Router",
                    MacAddress = "00:1B:44:11:22:33",
                    IpAddress = "192.168.1.1",
                    Type = DeviceType.Router,
                    IsOnline = true
                }
            ];

            string csv = DeviceCsvExporter.ToCsv(devices);

            Assert.Contains("Router,Router,192.168.1.1,00:1B:44:11:22:33", csv);
            Assert.Contains(",Yes,", csv);
        }

        [Fact]
        public void ToCsvQuotesFieldsContainingCommas()
        {
            List<Device> devices =
            [
                new Device
                {
                    FriendlyName = "Living Room",
                    MacAddress = "00:1B:44:11:22:33",
                    IpAddress = "192.168.1.2",
                    Notes = "Behind the TV, left side"
                }
            ];

            string csv = DeviceCsvExporter.ToCsv(devices);

            Assert.Contains("\"Behind the TV, left side\"", csv);
        }

        [Fact]
        public void ToCsvEscapesEmbeddedQuotes()
        {
            List<Device> devices =
            [
                new Device
                {
                    FriendlyName = "The \"Big\" Server",
                    MacAddress = "00:1B:44:11:22:33",
                    IpAddress = "192.168.1.3"
                }
            ];

            string csv = DeviceCsvExporter.ToCsv(devices);

            Assert.Contains("\"The \"\"Big\"\" Server\"", csv);
        }
    }
}
