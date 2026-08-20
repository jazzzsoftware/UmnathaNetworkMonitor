using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NetworkMonitor.Models.Devices;
using NetworkMonitor.Models.Digest;
using NetworkMonitor.Models.Scanning;
using NetworkMonitor.Models.SpeedTest;
using NetworkMonitor.Models.Traffic;
using NetworkMonitor.Services.Data;

namespace NetworkMonitor.UITests.Fixtures
{
    // Builds a fixture database through the app's own migration path (DatabaseInitializer),
    // never a checked-in .db, so the fixture cannot drift from the current schema. Every
    // timestamp is computed from the caller-supplied nowUtc, never DateTime.UtcNow inline, so
    // the same fixture and the same 5-minute/1-hour/6-hour window assertions are reproducible
    // on every run.
    public static class SeedDatabase
    {
        private const string RouterVendor = "Netgear";
        private const string WanProcessOne = "chrome.exe";
        private const string WanProcessTwo = "OneDrive.exe";
        private const string LocalDiscoveryMulticastIp = "224.0.0.251";

        public static async Task<SeedCounts> BuildAsync(string dbPath, DateTime nowUtc)
        {
            DbContextOptionsBuilder<AppDbContext> optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
            optionsBuilder.UseSqlite($"Data Source={dbPath};Pooling=False");

            SeedCounts counts;

            await using (AppDbContext db = new AppDbContext(optionsBuilder.Options))
            {
                await DatabaseInitializer.InitializeAsync(db);

                List<Device> devices = BuildDevices(nowUtc);
                db.Devices.AddRange(devices);
                await db.SaveChangesAsync();

                List<DeviceEvent> deviceEvents = BuildDeviceEvents(devices, nowUtc);
                db.DeviceEvents.AddRange(deviceEvents);

                List<ScanSession> scanSessions = BuildScanSessions(nowUtc);
                db.ScanSessions.AddRange(scanSessions);

                List<TrafficEntry> trafficEntries = BuildTrafficEntries(nowUtc);
                db.TrafficEntries.AddRange(trafficEntries);

                List<TrafficRollup> trafficRollups = BuildTrafficRollups(nowUtc);
                db.TrafficRollups.AddRange(trafficRollups);

                List<LocalTrafficEntry> localTrafficEntries = BuildLocalTrafficEntries(nowUtc, devices);
                db.LocalTrafficEntries.AddRange(localTrafficEntries);

                List<LocalTrafficRollup> localTrafficRollups = BuildLocalTrafficRollups(nowUtc, devices);
                db.LocalTrafficRollups.AddRange(localTrafficRollups);

                List<SpeedTestResult> speedTestResults = BuildSpeedTestResults(nowUtc);
                db.SpeedTestResults.AddRange(speedTestResults);

                List<DigestReport> digestReports = BuildDigestReports(nowUtc, devices, trafficEntries, localTrafficEntries, speedTestResults);
                db.DigestReports.AddRange(digestReports);

                await db.SaveChangesAsync();

                int approvedDeviceCount = devices.Count(device => device.IsApproved);
                int unapprovedDeviceCount = devices.Count(device => !device.IsApproved);

                counts = new SeedCounts(
                    devices.Count,
                    approvedDeviceCount,
                    unapprovedDeviceCount,
                    deviceEvents.Count,
                    speedTestResults.Count,
                    digestReports.Count);
            }

            return counts;
        }

        // Twelve devices spanning approved and unapproved, one renamed via FriendlyName (device
        // index 2), one carrying operator Notes (device index 5) — the exact fixture the spec's
        // device-page phase asserts against. Eight are approved, four are not.
        private static List<Device> BuildDevices(DateTime nowUtc)
        {
            List<Device> devices = new List<Device>
            {
                new Device
                {
                    MacAddress = "02:00:00:00:00:01",
                    IpAddress = "192.168.50.1",
                    Hostname = "router.local",
                    Vendor = RouterVendor,
                    Type = DeviceType.Router,
                    IsApproved = true,
                    IsHost = false,
                    IsOnline = true,
                    FirstSeen = nowUtc.AddDays(-14),
                    LastSeen = nowUtc
                },
                new Device
                {
                    MacAddress = "02:00:00:00:00:02",
                    IpAddress = "192.168.50.10",
                    Hostname = "DESKTOP-UITEST",
                    Vendor = "Dell Inc.",
                    Type = DeviceType.PC,
                    IsApproved = true,
                    IsHost = true,
                    IsOnline = true,
                    FirstSeen = nowUtc.AddDays(-14),
                    LastSeen = nowUtc
                },
                new Device
                {
                    MacAddress = "02:00:00:00:00:03",
                    IpAddress = "192.168.50.11",
                    Hostname = "DESKTOP-8X2KQ1",
                    FriendlyName = "Mark's Laptop",
                    Vendor = "Lenovo",
                    Type = DeviceType.PC,
                    IsApproved = true,
                    IsHost = false,
                    IsOnline = true,
                    FirstSeen = nowUtc.AddDays(-13),
                    LastSeen = nowUtc
                },
                new Device
                {
                    MacAddress = "02:00:00:00:00:04",
                    IpAddress = "192.168.50.12",
                    Hostname = "iphone-mark",
                    Vendor = "Apple, Inc.",
                    Type = DeviceType.Mobile,
                    IsApproved = true,
                    IsHost = false,
                    IsOnline = false,
                    FirstSeen = nowUtc.AddDays(-13),
                    LastSeen = nowUtc.AddHours(-2)
                },
                new Device
                {
                    MacAddress = "02:00:00:00:00:05",
                    IpAddress = "192.168.50.13",
                    Hostname = "android-guest",
                    Vendor = "Samsung Electronics",
                    Type = DeviceType.Mobile,
                    IsApproved = false,
                    IsHost = false,
                    IsOnline = false,
                    FirstSeen = nowUtc.AddHours(-40),
                    LastSeen = nowUtc.AddHours(-5)
                },
                new Device
                {
                    MacAddress = "02:00:00:00:00:06",
                    IpAddress = "192.168.50.14",
                    Hostname = "smart-tv",
                    Vendor = "LG Electronics",
                    Type = DeviceType.SmartDevice,
                    Notes = "Guest device - ask before connecting.",
                    IsApproved = false,
                    IsHost = false,
                    IsOnline = true,
                    FirstSeen = nowUtc.AddDays(-10),
                    LastSeen = nowUtc
                },
                new Device
                {
                    MacAddress = "02:00:00:00:00:07",
                    IpAddress = "192.168.50.15",
                    Hostname = "ipad-kitchen",
                    Vendor = "Apple, Inc.",
                    Type = DeviceType.Mobile,
                    IsApproved = true,
                    IsHost = false,
                    IsOnline = true,
                    FirstSeen = nowUtc.AddDays(-12),
                    LastSeen = nowUtc
                },
                new Device
                {
                    MacAddress = "02:00:00:00:00:08",
                    IpAddress = "192.168.50.16",
                    Hostname = "echo-lounge",
                    Vendor = "Amazon Technologies Inc.",
                    Type = DeviceType.SmartDevice,
                    IsApproved = true,
                    IsHost = false,
                    IsOnline = true,
                    FirstSeen = nowUtc.AddDays(-12),
                    LastSeen = nowUtc
                },
                new Device
                {
                    MacAddress = "02:00:00:00:00:09",
                    IpAddress = "192.168.50.17",
                    Hostname = "cam-frontdoor",
                    Vendor = "Hikvision",
                    Type = DeviceType.Camera,
                    IsApproved = true,
                    IsHost = false,
                    IsOnline = true,
                    FirstSeen = nowUtc.AddDays(-11),
                    LastSeen = nowUtc
                },
                new Device
                {
                    MacAddress = "02:00:00:00:00:0A",
                    IpAddress = "192.168.50.18",
                    Hostname = "printer-office",
                    Vendor = "HP Inc.",
                    Type = DeviceType.SmartDevice,
                    IsApproved = true,
                    IsHost = false,
                    IsOnline = false,
                    FirstSeen = nowUtc.AddDays(-11),
                    LastSeen = nowUtc.AddHours(-20)
                },
                new Device
                {
                    MacAddress = "02:00:00:00:00:0B",
                    IpAddress = "192.168.50.19",
                    Hostname = "nas-media",
                    Vendor = "Synology",
                    Type = DeviceType.Server,
                    IsApproved = false,
                    IsHost = false,
                    IsOnline = true,
                    FirstSeen = nowUtc.AddDays(-9),
                    LastSeen = nowUtc
                },
                new Device
                {
                    MacAddress = "02:00:00:00:00:0C",
                    IpAddress = "192.168.50.20",
                    Type = DeviceType.Unknown,
                    IsApproved = false,
                    IsHost = false,
                    IsOnline = false,
                    FirstSeen = nowUtc.AddHours(-48),
                    LastSeen = nowUtc.AddHours(-30)
                }
            };

            return devices;
        }

        // Twelve arrivals spread evenly across the trailing 48 hours (nowUtc-47h .. nowUtc-3h)
        // plus six departures for the first six devices, two hours after each arrival — eighteen
        // events in total, all inside the 48-hour window the History tab phase asserts against.
        private static List<DeviceEvent> BuildDeviceEvents(List<Device> devices, DateTime nowUtc)
        {
            List<DeviceEvent> deviceEvents = new List<DeviceEvent>();

            for (int deviceIndex = 0; deviceIndex < devices.Count; deviceIndex++)
            {
                DateTime appearedAt = nowUtc.AddHours(-47 + (deviceIndex * 4));

                deviceEvents.Add(new DeviceEvent
                {
                    DeviceId = devices[deviceIndex].Id,
                    EventType = DeviceEventType.Appeared,
                    Timestamp = appearedAt
                });

                if (deviceIndex < 6)
                {
                    deviceEvents.Add(new DeviceEvent
                    {
                        DeviceId = devices[deviceIndex].Id,
                        EventType = DeviceEventType.Disappeared,
                        Timestamp = appearedAt.AddHours(2)
                    });
                }

            }

            return deviceEvents;
        }

        private static List<ScanSession> BuildScanSessions(DateTime nowUtc)
        {
            List<ScanSession> scanSessions = new List<ScanSession>();

            for (int sessionIndex = 0; sessionIndex < 4; sessionIndex++)
            {
                DateTime startedAt = nowUtc.AddHours(-sessionIndex * 12);

                scanSessions.Add(new ScanSession
                {
                    StartedAt = startedAt,
                    CompletedAt = startedAt.AddSeconds(8),
                    DevicesFound = 12,
                    NewDevices = sessionIndex == 3 ? 1 : 0,
                    DevicesGone = 0
                });
            }

            return scanSessions;
        }

        // Raw per-flush entries at 30-second cadence for the trailing five minutes, for two WAN
        // processes — the resolution the 5-minute chart window reads.
        private static List<TrafficEntry> BuildTrafficEntries(DateTime nowUtc)
        {
            List<TrafficEntry> trafficEntries = new List<TrafficEntry>();

            for (int pointIndex = 0; pointIndex < 10; pointIndex++)
            {
                DateTime timestamp = nowUtc.AddSeconds(-30 * pointIndex);

                trafficEntries.Add(new TrafficEntry
                {
                    Timestamp = timestamp,
                    ProcessName = WanProcessOne,
                    ProcessPath = @"C:\Program Files\Google\Chrome\Application\chrome.exe",
                    BytesUploaded = 20000 + (pointIndex * 500),
                    BytesDownloaded = 150000 + (pointIndex * 4000)
                });

                trafficEntries.Add(new TrafficEntry
                {
                    Timestamp = timestamp,
                    ProcessName = WanProcessTwo,
                    ProcessPath = @"C:\Program Files\Microsoft OneDrive\OneDrive.exe",
                    BytesUploaded = 8000 + (pointIndex * 200),
                    BytesDownloaded = 6000 + (pointIndex * 150)
                });
            }

            return trafficEntries;
        }

        // Per-minute rollups at 5-minute cadence for the trailing six hours, for the same two
        // WAN processes — the resolution the 1-hour and 6-hour chart windows read.
        private static List<TrafficRollup> BuildTrafficRollups(DateTime nowUtc)
        {
            List<TrafficRollup> trafficRollups = new List<TrafficRollup>();

            for (int pointIndex = 0; pointIndex < 72; pointIndex++)
            {
                DateTime timestamp = nowUtc.AddMinutes(-5 * pointIndex);
                long minuteEpoch = MinuteEpoch(timestamp);

                trafficRollups.Add(new TrafficRollup
                {
                    MinuteEpoch = minuteEpoch,
                    ProcessName = WanProcessOne,
                    ProcessPath = @"C:\Program Files\Google\Chrome\Application\chrome.exe",
                    BytesUploaded = 90000 + (pointIndex * 1500),
                    BytesDownloaded = 700000 + (pointIndex * 12000)
                });

                trafficRollups.Add(new TrafficRollup
                {
                    MinuteEpoch = minuteEpoch,
                    ProcessName = WanProcessTwo,
                    ProcessPath = @"C:\Program Files\Microsoft OneDrive\OneDrive.exe",
                    BytesUploaded = 30000 + (pointIndex * 400),
                    BytesDownloaded = 22000 + (pointIndex * 300)
                });
            }

            return trafficRollups;
        }

        // Local (LAN) raw entries for the trailing ten minutes, covering both flow
        // classifications LocalFlowClassifier recognises: SMB to the seeded NAS device (Data,
        // tagged) and mDNS to the multicast group (Discovery).
        private static List<LocalTrafficEntry> BuildLocalTrafficEntries(DateTime nowUtc, List<Device> devices)
        {
            string nasIpAddress = devices[10].IpAddress;
            List<LocalTrafficEntry> localTrafficEntries = new List<LocalTrafficEntry>();

            for (int pointIndex = 0; pointIndex < 20; pointIndex++)
            {
                DateTime timestamp = nowUtc.AddSeconds(-30 * pointIndex);

                localTrafficEntries.Add(new LocalTrafficEntry
                {
                    Timestamp = timestamp,
                    ProcessName = "System",
                    RemoteIp = nasIpAddress,
                    Protocol = 6,
                    RemotePort = 445,
                    BytesUploaded = 5000 + (pointIndex * 300),
                    BytesDownloaded = 40000 + (pointIndex * 2500)
                });

                localTrafficEntries.Add(new LocalTrafficEntry
                {
                    Timestamp = timestamp,
                    ProcessName = "System",
                    RemoteIp = LocalDiscoveryMulticastIp,
                    Protocol = 17,
                    RemotePort = 5353,
                    BytesUploaded = 200 + (pointIndex * 10),
                    BytesDownloaded = 0
                });
            }

            return localTrafficEntries;
        }

        // Local (LAN) rollups at 5-minute cadence for the trailing six hours, same two
        // classifications as the raw entries above.
        private static List<LocalTrafficRollup> BuildLocalTrafficRollups(DateTime nowUtc, List<Device> devices)
        {
            string nasIpAddress = devices[10].IpAddress;
            List<LocalTrafficRollup> localTrafficRollups = new List<LocalTrafficRollup>();

            for (int pointIndex = 0; pointIndex < 72; pointIndex++)
            {
                DateTime timestamp = nowUtc.AddMinutes(-5 * pointIndex);
                long minuteEpoch = MinuteEpoch(timestamp);

                localTrafficRollups.Add(new LocalTrafficRollup
                {
                    MinuteEpoch = minuteEpoch,
                    ProcessName = "System",
                    RemoteIp = nasIpAddress,
                    Protocol = 6,
                    RemotePort = 445,
                    BytesUploaded = 25000 + (pointIndex * 900),
                    BytesDownloaded = 200000 + (pointIndex * 7000)
                });

                localTrafficRollups.Add(new LocalTrafficRollup
                {
                    MinuteEpoch = minuteEpoch,
                    ProcessName = "System",
                    RemoteIp = LocalDiscoveryMulticastIp,
                    Protocol = 17,
                    RemotePort = 5353,
                    BytesUploaded = 1000 + (pointIndex * 30),
                    BytesDownloaded = 0
                });
            }

            return localTrafficRollups;
        }

        // Thirty results, one per hour for the trailing thirty hours, with download, upload and
        // latency all trending — the "visible trend" the spec's speed-test phase asserts against.
        private static List<SpeedTestResult> BuildSpeedTestResults(DateTime nowUtc)
        {
            List<SpeedTestResult> speedTestResults = new List<SpeedTestResult>();

            for (int resultIndex = 0; resultIndex < 30; resultIndex++)
            {
                int stepsFromOldest = 29 - resultIndex;

                speedTestResults.Add(new SpeedTestResult
                {
                    Timestamp = nowUtc.AddHours(-resultIndex),
                    DownloadMbps = 80.0 + (stepsFromOldest * 2.4),
                    UploadMbps = 10.0 + (stepsFromOldest * 0.5),
                    LatencyMs = 28.0 - (stepsFromOldest * 0.3),
                    JitterMs = 3.5 - (stepsFromOldest * 0.05),
                    Server = "Cloudflare",
                    Success = true
                });
            }

            return speedTestResults;
        }

        // Three generated digests: two scheduled daily reports and one manual, each covering a
        // 24-hour period ending at midnight-aligned points within the trailing three days, with
        // a real DigestSummary so the renderer sees exactly the shape DigestGenerator produces.
        private static List<DigestReport> BuildDigestReports(
            DateTime nowUtc,
            List<Device> devices,
            List<TrafficEntry> trafficEntries,
            List<LocalTrafficEntry> localTrafficEntries,
            List<SpeedTestResult> speedTestResults)
        {
            List<DigestReport> digestReports = new List<DigestReport>();

            for (int digestIndex = 0; digestIndex < 3; digestIndex++)
            {
                DateTime periodEnd = nowUtc.AddDays(-digestIndex);
                DateTime periodStart = periodEnd.AddDays(-1);
                DateTime generatedAt = periodEnd.AddMinutes(5);
                DigestSummary summary = BuildDigestSummary(devices, trafficEntries, localTrafficEntries, speedTestResults, digestIndex);

                // The most recent digest's period ends at nowUtc itself, so "5 minutes after
                // period end" would land in the future — clamp to nowUtc, never later than now.
                if (generatedAt > nowUtc)
                {
                    generatedAt = nowUtc;
                }

                digestReports.Add(new DigestReport
                {
                    PeriodStart = periodStart,
                    PeriodEnd = periodEnd,
                    GeneratedAt = generatedAt,
                    Headline = summary.Headline,
                    SummaryJson = JsonSerializer.Serialize(summary),
                    IsScheduled = digestIndex < 2
                });
            }

            return digestReports;
        }

        private static DigestSummary BuildDigestSummary(
            List<Device> devices,
            List<TrafficEntry> trafficEntries,
            List<LocalTrafficEntry> localTrafficEntries,
            List<SpeedTestResult> speedTestResults,
            int digestIndex)
        {
            TrafficEntry topWanEntry = trafficEntries[0];
            LocalTrafficEntry topLocalEntry = localTrafficEntries[0];
            SpeedTestResult latestSpeedTest = speedTestResults[digestIndex];
            Device newDevice = devices[2];
            Device unapprovedDevice = devices[5];

            DigestSummary summary = new DigestSummary
            {
                Headline = $"Fixture digest {digestIndex + 1} of 3",
                TotalBytesUploaded = topWanEntry.BytesUploaded + topLocalEntry.BytesUploaded,
                TotalBytesDownloaded = topWanEntry.BytesDownloaded + topLocalEntry.BytesDownloaded,
                AppearedCount = 12,
                DisappearedCount = 6,
                OnlineCount = 8,
                OfflineCount = 4
            };

            summary.InternetTopApps.Add(new InternetTrafficAppSummary
            {
                ProcessName = topWanEntry.ProcessName,
                BytesUploaded = topWanEntry.BytesUploaded,
                BytesDownloaded = topWanEntry.BytesDownloaded
            });

            summary.TopLocalApps.Add(new LocalTrafficAppSummary
            {
                ProcessName = topLocalEntry.ProcessName,
                Peer = topLocalEntry.RemoteIp,
                BytesUploaded = topLocalEntry.BytesUploaded,
                BytesDownloaded = topLocalEntry.BytesDownloaded
            });

            summary.NewDevices.Add(new NewDeviceSummary
            {
                DisplayName = newDevice.DisplayName,
                MacAddress = newDevice.MacAddress,
                IpAddress = newDevice.IpAddress,
                Vendor = newDevice.Vendor ?? string.Empty,
                Type = newDevice.Type,
                IsApproved = newDevice.IsApproved,
                FirstSeen = newDevice.FirstSeen
            });

            summary.UnapprovedDevices.Add(new UnapprovedDeviceSummary
            {
                DisplayName = unapprovedDevice.DisplayName,
                MacAddress = unapprovedDevice.MacAddress,
                IpAddress = unapprovedDevice.IpAddress,
                Vendor = unapprovedDevice.Vendor ?? string.Empty,
                Type = unapprovedDevice.Type,
                LastSeen = unapprovedDevice.LastSeen,
                IsApproved = unapprovedDevice.IsApproved,
                AppearedCount = 1,
                DisappearedCount = 0,
                Highlight = true
            });

            summary.HourlyActivity.Add(new HourlyActivitySummary
            {
                Hour = 0,
                Appeared = 2,
                Disappeared = 1
            });

            summary.SpeedTests.Add(new SpeedTestRowSummary
            {
                Timestamp = latestSpeedTest.Timestamp,
                DownloadMbps = latestSpeedTest.DownloadMbps,
                UploadMbps = latestSpeedTest.UploadMbps,
                LatencyMs = latestSpeedTest.LatencyMs,
                JitterMs = latestSpeedTest.JitterMs,
                Server = latestSpeedTest.Server
            });

            return summary;
        }

        private static long MinuteEpoch(DateTime timestampUtc)
        {
            long minuteEpoch = (long) (timestampUtc - DateTime.UnixEpoch).TotalMinutes;

            return minuteEpoch;
        }
    }
}
