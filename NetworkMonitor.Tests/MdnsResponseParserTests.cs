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
        public void FallsBackToTypeKeyWhenNoStandardModelKey()
        {
            List<MdnsAddressRecord> addresses = new()
            {
                new MdnsAddressRecord("eWeLink_1000beb2e9.local", "192.168.1.126")
            };
            List<MdnsPointerRecord> pointers = new();
            List<MdnsServiceRecord> services = new()
            {
                new MdnsServiceRecord("eWeLink_1000beb2e9._ewelink._tcp.local", "eWeLink_1000beb2e9.local")
            };
            List<MdnsTextRecord> texts = new()
            {
                new MdnsTextRecord("eWeLink_1000beb2e9._ewelink._tcp.local", new List<string> { "txtvers=1", "id=1000beb2e9", "type=th_plug", "apivers=1" })
            };

            IReadOnlyDictionary<string, MdnsInfo> result = MdnsResponseParser.Parse(addresses, pointers, services, texts);

            Assert.Equal("th_plug", result["192.168.1.126"].Model);
        }

        [Fact]
        public void PrefersStandardModelKeyOverTypeKey()
        {
            List<MdnsAddressRecord> addresses = new()
            {
                new MdnsAddressRecord("device.local", "192.168.1.44")
            };
            List<MdnsPointerRecord> pointers = new();
            List<MdnsServiceRecord> services = new()
            {
                new MdnsServiceRecord("Widget._http._tcp.local", "device.local")
            };
            List<MdnsTextRecord> texts = new()
            {
                new MdnsTextRecord("Widget._http._tcp.local", new List<string> { "type=generic", "md=Widget-Pro-2" })
            };

            IReadOnlyDictionary<string, MdnsInfo> result = MdnsResponseParser.Parse(addresses, pointers, services, texts);

            Assert.Equal("Widget-Pro-2", result["192.168.1.44"].Model);
        }

        [Fact]
        public void SkipsOpaqueGuidInstanceNameFromPairingService()
        {
            List<MdnsAddressRecord> addresses = new()
            {
                new MdnsAddressRecord("Marks-Dev-iPhone.local", "192.168.1.131")
            };
            List<MdnsPointerRecord> pointers = new()
            {
                new MdnsPointerRecord("_remotepairing._tcp.local", "E75EB158-8418-439A-9D09-1A5BEAFF973E._remotepairing._tcp.local")
            };
            List<MdnsServiceRecord> services = new()
            {
                new MdnsServiceRecord("E75EB158-8418-439A-9D09-1A5BEAFF973E._remotepairing._tcp.local", "Marks-Dev-iPhone.local")
            };
            List<MdnsTextRecord> texts = new();

            IReadOnlyDictionary<string, MdnsInfo> result = MdnsResponseParser.Parse(addresses, pointers, services, texts);

            Assert.Empty(result);
        }

        [Fact]
        public void PrefersFriendlyServiceOverOpaquePairingService()
        {
            List<MdnsAddressRecord> addresses = new()
            {
                new MdnsAddressRecord("Marks-Dev-iPhone.local", "192.168.1.131")
            };
            List<MdnsPointerRecord> pointers = new()
            {
                new MdnsPointerRecord("_remotepairing._tcp.local", "E75EB158-8418-439A-9D09-1A5BEAFF973E._remotepairing._tcp.local"),
                new MdnsPointerRecord("_airplay._tcp.local", "Mark's iPhone._airplay._tcp.local")
            };
            List<MdnsServiceRecord> services = new()
            {
                new MdnsServiceRecord("E75EB158-8418-439A-9D09-1A5BEAFF973E._remotepairing._tcp.local", "Marks-Dev-iPhone.local"),
                new MdnsServiceRecord("Mark's iPhone._airplay._tcp.local", "Marks-Dev-iPhone.local")
            };
            List<MdnsTextRecord> texts = new();

            IReadOnlyDictionary<string, MdnsInfo> result = MdnsResponseParser.Parse(addresses, pointers, services, texts);

            Assert.Equal("Mark's iPhone", result["192.168.1.131"].Name);
        }

        [Fact]
        public void DecodesDnsEscapedSpacesInFriendlyName()
        {
            List<MdnsAddressRecord> addresses = new()
            {
                new MdnsAddressRecord("appletv.local", "192.168.1.20")
            };
            List<MdnsPointerRecord> pointers = new()
            {
                new MdnsPointerRecord("_airplay._tcp.local", "Living\\032Room._airplay._tcp.local")
            };
            List<MdnsServiceRecord> services = new()
            {
                new MdnsServiceRecord("Living\\032Room._airplay._tcp.local", "appletv.local")
            };
            List<MdnsTextRecord> texts = new();

            IReadOnlyDictionary<string, MdnsInfo> result = MdnsResponseParser.Parse(addresses, pointers, services, texts);

            Assert.Equal("Living Room", result["192.168.1.20"].Name);
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
