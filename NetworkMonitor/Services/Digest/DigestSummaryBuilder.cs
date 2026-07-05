using NetworkMonitor.Models;

namespace NetworkMonitor.Services.Digest
{
    public static class DigestSummaryBuilder
    {
        public static DigestSummary Build(
            IReadOnlyList<DeviceEvent> events,
            IReadOnlyList<Device> devices,
            IReadOnlyList<AppTrafficTotal> traffic,
            DateTime startUtc,
            DateTime endUtc)
        {
            DigestSummary summary = new DigestSummary();

            summary.TopApps = traffic
                .OrderByDescending(appTotal => appTotal.BytesUploaded + appTotal.BytesDownloaded)
                .Take(10)
                .Select(appTotal => new TrafficAppSummary
                {
                    ProcessName = appTotal.ProcessName,
                    BytesUploaded = appTotal.BytesUploaded,
                    BytesDownloaded = appTotal.BytesDownloaded
                })
                .ToList();

            summary.TotalBytesUploaded = traffic.Sum(appTotal => appTotal.BytesUploaded);
            summary.TotalBytesDownloaded = traffic.Sum(appTotal => appTotal.BytesDownloaded);

            summary.NewDevices = devices
                .Where(device => device.FirstSeen >= startUtc && device.FirstSeen < endUtc)
                .OrderBy(device => device.FirstSeen)
                .Select(device => new NewDeviceSummary
                {
                    DisplayName = device.DisplayName,
                    MacAddress = device.MacAddress,
                    IpAddress = device.IpAddress,
                    Vendor = device.Vendor ?? string.Empty,
                    Type = device.Type,
                    IsApproved = device.IsApproved,
                    FirstSeen = device.FirstSeen
                })
                .ToList();

            Dictionary<int, int> appearedByDevice = events
                .Where(deviceEvent => deviceEvent.EventType == DeviceEventType.Appeared)
                .GroupBy(deviceEvent => deviceEvent.DeviceId)
                .ToDictionary(group => group.Key, group => group.Count());

            Dictionary<int, int> disappearedByDevice = events
                .Where(deviceEvent => deviceEvent.EventType == DeviceEventType.Disappeared)
                .GroupBy(deviceEvent => deviceEvent.DeviceId)
                .ToDictionary(group => group.Key, group => group.Count());

            summary.AllDevices = devices
                .Where(device => device.FirstSeen < endUtc && device.LastSeen >= startUtc)
                .OrderBy(device => IpSortKey(device.IpAddress))
                .Select(device => new UnapprovedDeviceSummary
                {
                    DisplayName = device.DisplayName,
                    MacAddress = device.MacAddress,
                    IpAddress = device.IpAddress,
                    Vendor = device.Vendor ?? string.Empty,
                    Type = device.Type,
                    LastSeen = device.LastSeen,
                    IsApproved = device.IsApproved,
                    Highlight = !device.IsApproved,
                    AppearedCount = appearedByDevice.GetValueOrDefault(device.Id),
                    DisappearedCount = disappearedByDevice.GetValueOrDefault(device.Id)
                })
                .ToList();

            summary.UnapprovedDevices = devices
                .Where(device => !device.IsApproved && device.FirstSeen < endUtc && device.LastSeen >= startUtc)
                .OrderByDescending(device => device.LastSeen)
                .Select(device => new UnapprovedDeviceSummary
                {
                    DisplayName = device.DisplayName,
                    MacAddress = device.MacAddress,
                    IpAddress = device.IpAddress,
                    Vendor = device.Vendor ?? string.Empty,
                    Type = device.Type,
                    LastSeen = device.LastSeen,
                    IsApproved = device.IsApproved,
                    AppearedCount = appearedByDevice.GetValueOrDefault(device.Id),
                    DisappearedCount = disappearedByDevice.GetValueOrDefault(device.Id)
                })
                .ToList();

            summary.AppearedCount = events.Count(deviceEvent => deviceEvent.EventType == DeviceEventType.Appeared);
            summary.DisappearedCount = events.Count(deviceEvent => deviceEvent.EventType == DeviceEventType.Disappeared);
            summary.OnlineCount = devices.Count(device => device.IsOnline);
            summary.OfflineCount = devices.Count(device => !device.IsOnline);
            summary.HourlyActivity = BuildHourlyActivity(events);
            summary.Headline = BuildHeadline(summary);

            return summary;
        }

        private static long IpSortKey(string ipAddress)
        {
            long key = 0;

            if (System.Net.IPAddress.TryParse(ipAddress, out System.Net.IPAddress? parsed))
            {
                byte[] bytes = parsed.GetAddressBytes();

                if (bytes.Length == 4)
                {
                    key = ((long)bytes[0] << 24) | ((long)bytes[1] << 16) | ((long)bytes[2] << 8) | bytes[3];
                }

            }

            return key;
        }

        private static List<HourlyActivitySummary> BuildHourlyActivity(IReadOnlyList<DeviceEvent> events)
        {
            List<HourlyActivitySummary> hourly = new();

            for (int hour = 0; hour < 24; hour++)
            {
                hourly.Add(new HourlyActivitySummary { Hour = hour, Appeared = 0, Disappeared = 0 });
            }

            foreach (DeviceEvent deviceEvent in events)
            {
                int localHour = deviceEvent.Timestamp.ToLocalTime().Hour;

                if (deviceEvent.EventType == DeviceEventType.Appeared)
                {
                    hourly[localHour].Appeared++;
                }
                else
                {
                    hourly[localHour].Disappeared++;
                }

            }

            return hourly;
        }

        private static string BuildHeadline(DigestSummary summary)
        {
            int newUnapproved = summary.NewDevices.Count(device => !device.IsApproved);
            double totalGb = (summary.TotalBytesUploaded + summary.TotalBytesDownloaded) / 1_073_741_824.0;
            string trafficPart = $"{totalGb:0.0} GB traffic";
            string headline;

            if (newUnapproved > 0)
            {
                string plural = newUnapproved == 1 ? "device" : "devices";
                headline = $"⚠️ {newUnapproved} new unapproved {plural} · {trafficPart}";
            }
            else
            {
                string plural = summary.NewDevices.Count == 1 ? "device" : "devices";
                headline = $"{summary.NewDevices.Count} new {plural} · {trafficPart}";
            }

            return headline;
        }
    }
}
