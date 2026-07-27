using System.Collections.Generic;
using NetworkMonitor.Models.Traffic;
using Xunit;
using NetworkMonitor.Core.Traffic;

namespace NetworkMonitor.Tests
{
    public class LocalTrafficGrouperTests
    {
        private static readonly Dictionary<string, string> Names = new Dictionary<string, string>
        {
            ["192.168.1.50"] = "Surfrat NAS",
            ["192.168.1.126"] = "Geyser IOT"
        };

        [Fact]
        public void ByAppFoldsDiscoveryIntoBackgroundAndKeepsDataUpFront()
        {
            List<LocalFlowMinute> minutes = new List<LocalFlowMinute>
            {
                new LocalFlowMinute("System", "192.168.1.50", 6, 445, 10, 4000),
                new LocalFlowMinute("chrome", "192.168.1.126", 17, 5353, 0, 200)
            };

            IReadOnlyList<LocalTrafficGroupRow> groups = LocalTrafficGrouper.Build(minutes, Names, LocalLens.ByApp);

            Assert.Equal(GroupKind.All, groups[0].Kind);
            Assert.Equal(4010, groups[0].TotalBytes);
            Assert.Equal("System", groups[1].DisplayName);
            Assert.Equal("SMB", groups[1].ServiceTag);
            Assert.True(groups[^1].IsBackground);
            Assert.Equal(200, groups[^1].TotalBytes);
        }

        [Fact]
        public void GroupTagComesFromTheChildThatMovedTheMostBytes()
        {
            List<LocalFlowMinute> minutes = new List<LocalFlowMinute>
            {
                new LocalFlowMinute("explorer", "192.168.1.126", 6, 80, 100, 400),
                new LocalFlowMinute("explorer", "192.168.1.50", 6, 445, 1000, 4_000_000)
            };

            IReadOnlyList<LocalTrafficGroupRow> groups = LocalTrafficGrouper.Build(minutes, Names, LocalLens.ByApp);

            Assert.Equal("explorer", groups[1].DisplayName);
            Assert.Equal("SMB", groups[1].ServiceTag);
        }

        [Fact]
        public void ByDeviceGroupsOnRemoteIpWithFriendlyName()
        {
            List<LocalFlowMinute> minutes = new List<LocalFlowMinute>
            {
                new LocalFlowMinute("System", "192.168.1.50", 6, 445, 10, 4000)
            };

            IReadOnlyList<LocalTrafficGroupRow> groups = LocalTrafficGrouper.Build(minutes, Names, LocalLens.ByDevice);

            Assert.Equal("Surfrat NAS", groups[1].DisplayName);
            Assert.Equal("192.168.1.50", groups[1].SubLabel);
            Assert.Equal("System", groups[1].Children[0].DisplayName);
        }

        [Fact]
        public void DropsDiscoveryToAddressesThatAreNotKnownDevices()
        {
            List<LocalFlowMinute> minutes = new List<LocalFlowMinute>
            {
                new LocalFlowMinute("avp", "192.168.1.126", 17, 5353, 0, 100),
                new LocalFlowMinute("avp", "192.168.1.200", 17, 137, 0, 100)
            };

            IReadOnlyList<LocalTrafficGroupRow> groups = LocalTrafficGrouper.Build(minutes, Names, LocalLens.ByApp);
            LocalTrafficGroupRow background = groups[^1];

            Assert.True(background.IsBackground);
            Assert.Single(background.Children);
            Assert.Equal("Geyser IOT", background.Children[0].DisplayName);
        }

        [Fact]
        public void ByDeviceAllRowIsLabelledAllDevices()
        {
            List<LocalFlowMinute> minutes = new List<LocalFlowMinute>
            {
                new LocalFlowMinute("System", "192.168.1.50", 6, 445, 10, 4000)
            };

            IReadOnlyList<LocalTrafficGroupRow> groups = LocalTrafficGrouper.Build(minutes, Names, LocalLens.ByDevice);

            Assert.Equal("All Devices", groups[0].DisplayName);
        }
    }
}
