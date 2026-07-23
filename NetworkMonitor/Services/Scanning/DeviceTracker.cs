using Microsoft.EntityFrameworkCore;
using NetworkMonitor.Data;
using NetworkMonitor.Models.Devices;
using NetworkMonitor.Models.Scanning;

namespace NetworkMonitor.Services.Scanning
{
    public class DeviceTracker(IDbContextFactory<AppDbContext> dbFactory)
    {
        public async Task<(ScanSession Session, List<DeviceNotification> Notifications)> MergeAsync(
            IReadOnlyList<ScannedDevice> scanned, CancellationToken ct = default)
        {
            await using AppDbContext db = await dbFactory.CreateDbContextAsync(ct);

            ScanSession session = new()
            {
                StartedAt = DateTime.UtcNow
            };
            List<DeviceNotification> notifications = [];

            HashSet<string> scannedMacs = scanned
                .Select(scannedDevice => MacNormalizer.Normalize(scannedDevice.Mac))
                .ToHashSet();

            List<Device> candidates = await db.Devices
                .Where(device => scannedMacs.Contains(device.MacAddress)
                    || device.IsOnline
                    || (device.IsApproved && device.Hostname != null))
                .ToListAsync(ct);
            Dictionary<string, Device> devicesByMac = new();

            foreach (Device existingDevice in candidates
                         .OrderByDescending(device => device.IsApproved)
                         .ThenBy(device => device.Id))
            {
                string existingKey = existingDevice.MacAddress;

                if (!devicesByMac.ContainsKey(existingKey))
                {
                    devicesByMac[existingKey] = existingDevice;
                }

            }

            List<Device> gone = candidates
                .Where(device => device.IsOnline)
                .Where(device => !scannedMacs.Contains(device.MacAddress))
                .ToList();

            foreach (Device device in gone)
            {
                device.IsOnline = false;
                db.DeviceEvents.Add(new DeviceEvent
                {
                    Device = device, EventType = DeviceEventType.Disappeared, Timestamp = DateTime.UtcNow
                });
                notifications.Add(new DeviceNotification(
                    device.DisplayName, device.MacAddress, device.IpAddress, device.Vendor, device.Type,
                    false, false, device.IsApproved));
            }

            session.DevicesGone = gone.Count;

            foreach (ScannedDevice scannedDevice in scanned)
            {
                string macKey = MacNormalizer.Normalize(scannedDevice.Mac);

                devicesByMac.TryGetValue(macKey, out Device? device);

                bool isNew = device is null;

                if (isNew)
                {
                    device = new Device
                    {
                        MacAddress = macKey, FirstSeen = DateTime.UtcNow, IsApproved = false
                    };

                    if (scannedDevice.IsHost)
                    {
                        device.Type = DeviceType.PC;
                    }

                    db.Devices.Add(device);
                    devicesByMac[macKey] = device;
                    session.NewDevices++;
                }

                bool wasOffline = !device!.IsOnline;
                device.IpAddress = scannedDevice.Ip;

                if (scannedDevice.Hostname is not null)
                {
                    device.Hostname = scannedDevice.Hostname;
                }

                if (isNew && MacNormalizer.IsRandomized(macKey) && !string.IsNullOrEmpty(device.Hostname))
                {
                    Device? hostnameMatch = candidates
                        .Where(existing => existing.IsApproved
                            && existing.Hostname is not null
                            && string.Equals(existing.Hostname, device.Hostname, StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(existing => existing.LastSeen)
                        .FirstOrDefault();

                    if (hostnameMatch is not null)
                    {
                        device.FriendlyName = hostnameMatch.FriendlyName;
                        device.Type = hostnameMatch.Type;
                        device.IsApproved = true;
                    }

                }

                device.Vendor ??= scannedDevice.Vendor;
                MdnsEnrichment.Apply(device, new MdnsInfo(scannedDevice.MdnsName, scannedDevice.Model));
                device.IsHost = scannedDevice.IsHost;
                device.IsOnline = true;
                device.LastSeen = DateTime.UtcNow;

                if (isNew || wasOffline)
                {
                    db.DeviceEvents.Add(new DeviceEvent
                    {
                        Device = device, EventType = DeviceEventType.Appeared, Timestamp = DateTime.UtcNow
                    });
                    notifications.Add(new DeviceNotification(
                        device.FriendlyName ?? device.Hostname ?? scannedDevice.Mac,
                        scannedDevice.Mac, scannedDevice.Ip, device.Vendor ?? scannedDevice.Vendor, device.Type,
                        true, isNew, device.IsApproved));
                }

            }

            session.DevicesFound = scanned.Count;
            session.CompletedAt = DateTime.UtcNow;
            db.ScanSessions.Add(session);
            await db.SaveChangesAsync(ct);

            return (session, notifications);
        }
    }
}