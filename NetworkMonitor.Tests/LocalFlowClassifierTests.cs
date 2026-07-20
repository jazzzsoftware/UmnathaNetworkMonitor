using NetworkMonitor.Services.Traffic;
using Xunit;

namespace NetworkMonitor.Tests
{
    public class LocalFlowClassifierTests
    {
        [Theory]
        [InlineData(17, 5353)]
        [InlineData(17, 1900)]
        [InlineData(17, 5355)]
        [InlineData(17, 137)]
        [InlineData(17, 3702)]
        public void ClassifiesKnownDiscoveryPortsAsDiscovery(int protocol, int remotePort)
        {
            FlowClassification classification = LocalFlowClassifier.Classify(protocol, remotePort);

            Assert.Equal(FlowCategory.Discovery, classification.Category);
        }

        [Theory]
        [InlineData(6, 445, "SMB")]
        [InlineData(6, 139, "SMB")]
        [InlineData(6, 80, "HTTP")]
        [InlineData(6, 443, "HTTPS")]
        [InlineData(6, 2049, "NFS")]
        [InlineData(6, 3389, "RDP")]
        public void TagsKnownDataServices(int protocol, int remotePort, string expectedTag)
        {
            FlowClassification classification = LocalFlowClassifier.Classify(protocol, remotePort);

            Assert.Equal(FlowCategory.Data, classification.Category);
            Assert.Equal(expectedTag, classification.ServiceTag);
        }

        [Fact]
        public void TreatsUnknownPortAsUntaggedData()
        {
            FlowClassification classification = LocalFlowClassifier.Classify(6, 51413);

            Assert.Equal(FlowCategory.Data, classification.Category);
            Assert.Null(classification.ServiceTag);
        }

        [Fact]
        public void TreatsDiscoveryPortOnTcpAsData()
        {
            FlowClassification classification = LocalFlowClassifier.Classify(6, 5353);

            Assert.Equal(FlowCategory.Data, classification.Category);
            Assert.Null(classification.ServiceTag);
        }

        [Fact]
        public void TreatsUnknownUdpPortAsUntaggedData()
        {
            FlowClassification classification = LocalFlowClassifier.Classify(17, 51413);

            Assert.Equal(FlowCategory.Data, classification.Category);
            Assert.Null(classification.ServiceTag);
        }
    }
}
