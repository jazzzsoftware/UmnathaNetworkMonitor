using System.Collections.Generic;
using NetworkMonitor.Services.Scanning;
using Xunit;

namespace NetworkMonitor.Tests
{
    public class MdnsResponseParserTests
    {
        [Fact]
        public void CorrelatesInstanceNameToIp()
        {
            List<MdnsAddressRecord> addresses = new()
            {
                new MdnsAddressRecord("appletv.local", "192.168.1.20")
            };
            List<MdnsPointerRecord> pointers = new()
            {
                new MdnsPointerRecord("_airplay._tcp.local", "Living Room._airplay._tcp.local")
            };
            List<MdnsServiceRecord> services = new()
            {
                new MdnsServiceRecord("Living Room._airplay._tcp.local", "appletv.local")
            };
            List<MdnsTextRecord> texts = new();

            IReadOnlyDictionary<string, MdnsInfo> result = MdnsResponseParser.Parse(addresses, pointers, services, texts);

            Assert.True(result.ContainsKey("192.168.1.20"));
            Assert.Equal("Living Room", result["192.168.1.20"].Name);
        }

        [Fact]
        public void ExtractsModelFromTextRecord()
        {
            List<MdnsAddressRecord> addresses = new()
            {
                new MdnsAddressRecord("appletv.local", "192.168.1.20")
            };
            List<MdnsPointerRecord> pointers = new();
            List<MdnsServiceRecord> services = new()
            {
                new MdnsServiceRecord("Living Room._airplay._tcp.local", "appletv.local")
            };
            List<MdnsTextRecord> texts = new()
            {
                new MdnsTextRecord("Living Room._airplay._tcp.local", new List<string> { "model=AppleTV5,3" })
            };

            IReadOnlyDictionary<string, MdnsInfo> result = MdnsResponseParser.Parse(addresses, pointers, services, texts);

            Assert.Equal("AppleTV5,3", result["192.168.1.20"].Model);
        }

        [Fact]
        public void UncorrelatedRecordsProduceNoEntries()
        {
            List<MdnsAddressRecord> addresses = new();
            List<MdnsPointerRecord> pointers = new()
            {
                new MdnsPointerRecord("_airplay._tcp.local", "Living Room._airplay._tcp.local")
            };
            List<MdnsServiceRecord> services = new()
            {
                new MdnsServiceRecord("Living Room._airplay._tcp.local", "appletv.local")
            };
            List<MdnsTextRecord> texts = new();

            IReadOnlyDictionary<string, MdnsInfo> result = MdnsResponseParser.Parse(addresses, pointers, services, texts);

            Assert.Empty(result);
        }
    }
}
