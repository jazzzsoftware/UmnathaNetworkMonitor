using NetworkMonitor.Models;
using NetworkMonitor.Services.Digest;
using Xunit;

namespace NetworkMonitor.Tests
{
    public class DigestSummaryBuilderTests
    {
        private static readonly DateTime WindowStart = new DateTime(2026, 6, 18, 6, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime WindowEnd = new DateTime(2026, 6, 19, 6, 0, 0, DateTimeKind.Utc);

        [Fact]
        public void BuildTopAppsAreOrderedByTotalBytesAndCappedAtTen()
        {
            List<AppTrafficTotal> traffic = new();

            for (int appIndex = 0; appIndex < 12; appIndex++)
            {
                traffic.Add(new AppTrafficTotal
                {
                    ProcessName = $"app{appIndex}",
                    BytesUploaded = appIndex * 100,
                    BytesDownloaded = appIndex * 100
                });
            }

            DigestSummary summary = DigestSummaryBuilder.Build(
                new List<DeviceEvent>(), new List<Device>(), traffic, new List<LocalTrafficDeviceSummary>(), WindowStart, WindowEnd);

            Assert.Equal(10, summary.InternetTopApps.Count);
            Assert.Equal("app11", summary.InternetTopApps[0].ProcessName);
            Assert.Equal("app2", summary.InternetTopApps[9].ProcessName);
        }

        [Fact]
        public void BuildExcludesSystemFromInternetTopAppsAndTotals()
        {
            List<AppTrafficTotal> traffic = new()
            {
                new AppTrafficTotal { ProcessName = "System", BytesUploaded = 1000, BytesDownloaded = 1000 },
                new AppTrafficTotal { ProcessName = "chrome", BytesUploaded = 10, BytesDownloaded = 20 }
            };

            DigestSummary summary = DigestSummaryBuilder.Build(
                new List<DeviceEvent>(), new List<Device>(), traffic, new List<LocalTrafficDeviceSummary>(), WindowStart, WindowEnd);

            Assert.Single(summary.InternetTopApps);
            Assert.Equal("chrome", summary.InternetTopApps[0].ProcessName);
            Assert.Equal(10, summary.TotalBytesUploaded);
            Assert.Equal(20, summary.TotalBytesDownloaded);
        }

        [Fact]
        public void BuildTopLocalDevicesResolveNamesSortAndCapAtTen()
        {
            List<LocalTrafficDeviceSummary> localTraffic = new();

            for (int deviceIndex = 0; deviceIndex < 12; deviceIndex++)
            {
                localTraffic.Add(new LocalTrafficDeviceSummary
                {
                    RemoteIp = $"192.168.1.{deviceIndex}",
                    BytesUploaded = deviceIndex * 100,
                    BytesDownloaded = deviceIndex * 100
                });
            }

            List<Device> devices = new()
            {
                new Device { MacAddress = "AA", IpAddress = "192.168.1.11", FriendlyName = "Synology NAS" }
            };

            DigestSummary summary = DigestSummaryBuilder.Build(
                new List<DeviceEvent>(), devices, new List<AppTrafficTotal>(), localTraffic, WindowStart, WindowEnd);

            Assert.Equal(10, summary.TopLocalDevices.Count);
            Assert.Equal("Synology NAS", summary.TopLocalDevices[0].DeviceName);
            Assert.Equal("192.168.1.2", summary.TopLocalDevices[9].DeviceName);
        }

        [Fact]
        public void BuildTotalsSumAllTraffic()
        {
            List<AppTrafficTotal> traffic = new()
            {
                new AppTrafficTotal { ProcessName = "a", BytesUploaded = 10, BytesDownloaded = 5 },
                new AppTrafficTotal { ProcessName = "b", BytesUploaded = 20, BytesDownloaded = 7 }
            };

            DigestSummary summary = DigestSummaryBuilder.Build(
                new List<DeviceEvent>(), new List<Device>(), traffic, new List<LocalTrafficDeviceSummary>(), WindowStart, WindowEnd);

            Assert.Equal(30, summary.TotalBytesUploaded);
            Assert.Equal(12, summary.TotalBytesDownloaded);
        }

        [Fact]
        public void BuildNewDevicesAreThoseFirstSeenInWindow()
        {
            List<Device> devices = new()
            {
                new Device { MacAddress = "AA", FirstSeen = WindowStart.AddHours(1), IsApproved = false },
                new Device { MacAddress = "BB", FirstSeen = WindowStart.AddDays(-5), IsApproved = true }
            };

            DigestSummary summary = DigestSummaryBuilder.Build(
                new List<DeviceEvent>(), devices, new List<AppTrafficTotal>(), new List<LocalTrafficDeviceSummary>(), WindowStart, WindowEnd);

            Assert.Single(summary.NewDevices);
            Assert.Equal("AA", summary.NewDevices[0].MacAddress);
        }

        [Fact]
        public void BuildUnapprovedDevicesAreUnapprovedAndSeenInWindow()
        {
            List<Device> devices = new()
            {
                new Device { MacAddress = "AA", IsApproved = false, FirstSeen = WindowStart.AddDays(-1), LastSeen = WindowStart.AddHours(2) },
                new Device { MacAddress = "BB", IsApproved = true, FirstSeen = WindowStart.AddDays(-1), LastSeen = WindowStart.AddHours(2) },
                new Device { MacAddress = "CC", IsApproved = false, FirstSeen = WindowStart.AddDays(-10), LastSeen = WindowStart.AddDays(-5) }
            };

            DigestSummary summary = DigestSummaryBuilder.Build(
                new List<DeviceEvent>(), devices, new List<AppTrafficTotal>(), new List<LocalTrafficDeviceSummary>(), WindowStart, WindowEnd);

            Assert.Single(summary.UnapprovedDevices);
            Assert.Equal("AA", summary.UnapprovedDevices[0].MacAddress);
        }

        [Fact]
        public void BuildActivityCountsMatchEventTypes()
        {
            List<DeviceEvent> events = new()
            {
                new DeviceEvent { EventType = DeviceEventType.Appeared, Timestamp = WindowStart.AddHours(2) },
                new DeviceEvent { EventType = DeviceEventType.Appeared, Timestamp = WindowStart.AddHours(2) },
                new DeviceEvent { EventType = DeviceEventType.Disappeared, Timestamp = WindowStart.AddHours(3) }
            };

            DigestSummary summary = DigestSummaryBuilder.Build(
                events, new List<Device>(), new List<AppTrafficTotal>(), new List<LocalTrafficDeviceSummary>(), WindowStart, WindowEnd);

            Assert.Equal(2, summary.AppearedCount);
            Assert.Equal(1, summary.DisappearedCount);
        }

        [Fact]
        public void BuildOnlineOfflineCountsComeFromDevices()
        {
            List<Device> devices = new()
            {
                new Device { MacAddress = "AA", IsOnline = true, FirstSeen = WindowStart.AddDays(-1) },
                new Device { MacAddress = "BB", IsOnline = false, FirstSeen = WindowStart.AddDays(-1) },
                new Device { MacAddress = "CC", IsOnline = true, FirstSeen = WindowStart.AddDays(-1) }
            };

            DigestSummary summary = DigestSummaryBuilder.Build(
                new List<DeviceEvent>(), devices, new List<AppTrafficTotal>(), new List<LocalTrafficDeviceSummary>(), WindowStart, WindowEnd);

            Assert.Equal(2, summary.OnlineCount);
            Assert.Equal(1, summary.OfflineCount);
        }

        [Fact]
        public void BuildHeadlineCallsOutNewUnapprovedDevices()
        {
            List<Device> devices = new()
            {
                new Device { MacAddress = "AA", FirstSeen = WindowStart.AddHours(1), IsApproved = false }
            };

            DigestSummary summary = DigestSummaryBuilder.Build(
                new List<DeviceEvent>(), devices, new List<AppTrafficTotal>(), new List<LocalTrafficDeviceSummary>(), WindowStart, WindowEnd);

            Assert.Contains("1 new unapproved device", summary.Headline);
        }

        [Fact]
        public void BuildHourlyActivityBucketsEventsByLocalHour()
        {
            DateTime appearedUtc = WindowStart.AddHours(2);
            DateTime disappearedUtc = WindowStart.AddHours(14);

            List<DeviceEvent> events = new()
            {
                new DeviceEvent { EventType = DeviceEventType.Appeared, Timestamp = appearedUtc },
                new DeviceEvent { EventType = DeviceEventType.Disappeared, Timestamp = disappearedUtc }
            };

            DigestSummary summary = DigestSummaryBuilder.Build(
                events, new List<Device>(), new List<AppTrafficTotal>(), new List<LocalTrafficDeviceSummary>(), WindowStart, WindowEnd);

            int appearedHour = appearedUtc.ToLocalTime().Hour;
            int disappearedHour = disappearedUtc.ToLocalTime().Hour;
            int emptyHour = Enumerable.Range(0, 24).First(hour => hour != appearedHour && hour != disappearedHour);

            Assert.Equal(24, summary.HourlyActivity.Count);
            Assert.Equal(1, summary.HourlyActivity[appearedHour].Appeared);
            Assert.Equal(1, summary.HourlyActivity[disappearedHour].Disappeared);
            Assert.Equal(0, summary.HourlyActivity[emptyHour].Appeared);
            Assert.Equal(0, summary.HourlyActivity[emptyHour].Disappeared);
        }

        [Fact]
        public void BuildHeadlineNonWarningUsesPluralForMultipleNewApprovedDevices()
        {
            List<Device> devices = new()
            {
                new Device { MacAddress = "AA", FirstSeen = WindowStart.AddHours(1), IsApproved = true },
                new Device { MacAddress = "BB", FirstSeen = WindowStart.AddHours(2), IsApproved = true }
            };

            List<AppTrafficTotal> traffic = new()
            {
                new AppTrafficTotal { ProcessName = "app", BytesUploaded = 1_073_741_824, BytesDownloaded = 0 }
            };

            DigestSummary summary = DigestSummaryBuilder.Build(
                new List<DeviceEvent>(), devices, traffic, new List<LocalTrafficDeviceSummary>(), WindowStart, WindowEnd);

            Assert.Contains("2 new devices", summary.Headline);
            Assert.Contains("GB traffic", summary.Headline);
            Assert.DoesNotContain("⚠️", summary.Headline);
        }

        [Fact]
        public void BuildHeadlineNonWarningUsesSingularForOneNewApprovedDevice()
        {
            List<Device> devices = new()
            {
                new Device { MacAddress = "AA", FirstSeen = WindowStart.AddHours(1), IsApproved = true }
            };

            List<AppTrafficTotal> traffic = new()
            {
                new AppTrafficTotal { ProcessName = "app", BytesUploaded = 1_073_741_824, BytesDownloaded = 0 }
            };

            DigestSummary summary = DigestSummaryBuilder.Build(
                new List<DeviceEvent>(), devices, traffic, new List<LocalTrafficDeviceSummary>(), WindowStart, WindowEnd);

            Assert.Contains("1 new device", summary.Headline);
            Assert.DoesNotContain("1 new devices", summary.Headline);
            Assert.DoesNotContain("⚠️", summary.Headline);
        }

        [Fact]
        public void BuildNewDevicesExcludeDeviceFirstSeenExactlyAtWindowEnd()
        {
            List<Device> devices = new()
            {
                new Device { MacAddress = "CC", FirstSeen = WindowEnd, IsApproved = false },
                new Device { MacAddress = "DD", FirstSeen = WindowStart.AddHours(1), IsApproved = false }
            };

            DigestSummary summary = DigestSummaryBuilder.Build(
                new List<DeviceEvent>(), devices, new List<AppTrafficTotal>(), new List<LocalTrafficDeviceSummary>(), WindowStart, WindowEnd);

            Assert.DoesNotContain(summary.NewDevices, newDevice => newDevice.MacAddress == "CC");
            Assert.Single(summary.NewDevices);
            Assert.Equal("DD", summary.NewDevices[0].MacAddress);
        }
    }
}
