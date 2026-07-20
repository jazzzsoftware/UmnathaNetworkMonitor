using System.Collections.Generic;
using NetworkMonitor.Models;
using NetworkMonitor.Services.Traffic;
using Xunit;

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
        public void ByApp_FoldsDiscoveryIntoBackgroundAndKeepsDataUpFront()
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
        public void ByDevice_GroupsOnRemoteIpWithFriendlyName()
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
        public void ByDevice_AllRowIsLabelledAllDevices()
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
