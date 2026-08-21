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
        private const string LocalDataProcess = "System";
        private const string LocalFileProcess = "explorer.exe";

        private const int TcpProtocol = 6;
        private const int UdpProtocol = 17;
        private const int SmbPort = 445;
        private const int MediaPort = 8009;
        private const int MdnsPort = 5353;

        private const int NasDeviceIndex = 10;
        private const int SecondPeerDeviceIndex = 7;
        private const int RouterDeviceIndex = 0;

        private const long ChromeRollupDownloadBase = 700000L;
        private const long ChromeRollupDownloadStep = 12000L;
        private const long ChromeRollupUploadBase = 90000L;
        private const long ChromeRollupUploadStep = 1500L;
        private const long OneDriveRollupDownloadBase = 22000L;
        private const long OneDriveRollupDownloadStep = 300L;
        private const long OneDriveRollupUploadBase = 30000L;
        private const long OneDriveRollupUploadStep = 400L;

        private const long NasSmbRollupDownloadBase = 200000L;
        private const long NasSmbRollupDownloadStep = 7000L;
        private const long NasSmbRollupUploadBase = 25000L;
        private const long NasSmbRollupUploadStep = 900L;
        private const long NasFileRollupDownloadBase = 60000L;
        private const long NasFileRollupDownloadStep = 2000L;
        private const long NasFileRollupUploadBase = 8000L;
        private const long NasFileRollupUploadStep = 300L;
        private const long SecondPeerRollupDownloadBase = 15000L;
        private const long SecondPeerRollupDownloadStep = 500L;
        private const long SecondPeerRollupUploadBase = 3000L;
        private const long SecondPeerRollupUploadStep = 100L;
        private const long DiscoveryRollupUploadBase = 1000L;
        private const long DiscoveryRollupUploadStep = 30L;

        // The floor TrafficPhase asserts the Internet chart's reported peak against, for any
        // window whose buckets are a minute or wider (1h and 6h — both read TrafficRollups). It is
        // the newest seeded rollup minute's total download across both WAN processes, which is the
        // one seeded bucket guaranteed to still be inside the window however long the run takes to
        // reach the Traffic page. Download, not upload, because every seeded stream downloads more
        // than it uploads, and the chart's peak is the larger of the two.
        //
        // A floor rather than an equality: the app under test is capturing real traffic into the
        // same fixture database while the suite drives it, so the drawn peak can only be this or
        // higher — never lower. See TrafficPhase's header for the rest of that reasoning.
        public const long WanNewestRollupBucketDownloadBytes = ChromeRollupDownloadBase + OneDriveRollupDownloadBase;

        // The same floor for the Local chart. Only the data streams count: LocalViewModel's chart
        // SQL excludes discovery ports outright (`NOT LocalFlowClassifier.DiscoverySqlPredicate`),
        // so the two seeded mDNS streams contribute nothing to what the chart draws.
        public const long LocalNewestRollupBucketDownloadBytes =
            NasSmbRollupDownloadBase + NasFileRollupDownloadBase + SecondPeerRollupDownloadBase;

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
                    BytesUploaded = ChromeRollupUploadBase + (pointIndex * ChromeRollupUploadStep),
                    BytesDownloaded = ChromeRollupDownloadBase + (pointIndex * ChromeRollupDownloadStep)
                });

                trafficRollups.Add(new TrafficRollup
                {
                    MinuteEpoch = minuteEpoch,
                    ProcessName = WanProcessTwo,
                    ProcessPath = @"C:\Program Files\Microsoft OneDrive\OneDrive.exe",
                    BytesUploaded = OneDriveRollupUploadBase + (pointIndex * OneDriveRollupUploadStep),
                    BytesDownloaded = OneDriveRollupDownloadBase + (pointIndex * OneDriveRollupDownloadStep)
                });
            }

            return trafficRollups;
        }

        // Local (LAN) raw entries for the trailing ten minutes, across five streams. Task 9
        // widened this from the original two, because two could not exercise what the Local page
        // actually does — every addition below buys a specific assertion in TrafficPhase:
        //
        // 1. System → NAS over SMB (Data, tagged "SMB") — the service-tag chip.
        // 2. explorer.exe → NAS over SMB — a second app on one device, so the By-device lens has a
        //    group with two children. LocalTrafficGroupRow.HasChildren is `Children.Count > 1`, so
        //    with only stream 1 no row on either lens could ever expand and the drill-down was
        //    untestable.
        // 3. System → echo-lounge on 8009 (Data, untagged) — a second peer for one app, so the
        //    By-app lens has a group with two children too, and the same drill-down assertion
        //    holds on both lenses. The peer device matters: this was originally printer-office,
        //    which DevicesPhase deletes two phases before TrafficPhase reads it, leaving the peer
        //    unnameable and rendered as a bare IP. Point these streams only at devices no earlier
        //    phase edits or deletes.
        // 4. System → the router over mDNS (Discovery, to an IP that IS a seeded device) — folds
        //    into LocalTrafficGrouper's background row and produces the "discovery only" chip.
        //    The original seed's only discovery stream was #5, which the grouper drops outright,
        //    so the fold and the chip could not appear at all.
        // 5. System → the mDNS multicast group (Discovery, to an IP that is NOT a device) — kept
        //    exactly as it was, and now load-bearing: the background row's own label counts its
        //    children, so asserting it reads "1 device — discovery only" proves both that #4 was
        //    folded and that #5 was dropped.
        private static List<LocalTrafficEntry> BuildLocalTrafficEntries(DateTime nowUtc, List<Device> devices)
        {
            string nasIpAddress = devices[NasDeviceIndex].IpAddress;
            string secondPeerIpAddress = devices[SecondPeerDeviceIndex].IpAddress;
            string routerIpAddress = devices[RouterDeviceIndex].IpAddress;
            List<LocalTrafficEntry> localTrafficEntries = new List<LocalTrafficEntry>();

            for (int pointIndex = 0; pointIndex < 20; pointIndex++)
            {
                DateTime timestamp = nowUtc.AddSeconds(-30 * pointIndex);

                localTrafficEntries.Add(new LocalTrafficEntry
                {
                    Timestamp = timestamp,
                    ProcessName = LocalDataProcess,
                    RemoteIp = nasIpAddress,
                    Protocol = TcpProtocol,
                    RemotePort = SmbPort,
                    BytesUploaded = 5000 + (pointIndex * 300),
                    BytesDownloaded = 40000 + (pointIndex * 2500)
                });

                localTrafficEntries.Add(new LocalTrafficEntry
                {
                    Timestamp = timestamp,
                    ProcessName = LocalFileProcess,
                    RemoteIp = nasIpAddress,
                    Protocol = TcpProtocol,
                    RemotePort = SmbPort,
                    BytesUploaded = 1600 + (pointIndex * 90),
                    BytesDownloaded = 12000 + (pointIndex * 700)
                });

                localTrafficEntries.Add(new LocalTrafficEntry
                {
                    Timestamp = timestamp,
                    ProcessName = LocalDataProcess,
                    RemoteIp = secondPeerIpAddress,
                    Protocol = TcpProtocol,
                    RemotePort = MediaPort,
                    BytesUploaded = 600 + (pointIndex * 30),
                    BytesDownloaded = 3000 + (pointIndex * 150)
                });

                localTrafficEntries.Add(new LocalTrafficEntry
                {
                    Timestamp = timestamp,
                    ProcessName = LocalDataProcess,
                    RemoteIp = routerIpAddress,
                    Protocol = UdpProtocol,
                    RemotePort = MdnsPort,
                    BytesUploaded = 200 + (pointIndex * 10),
                    BytesDownloaded = 0
                });

                localTrafficEntries.Add(new LocalTrafficEntry
                {
                    Timestamp = timestamp,
                    ProcessName = LocalDataProcess,
                    RemoteIp = LocalDiscoveryMulticastIp,
                    Protocol = UdpProtocol,
                    RemotePort = MdnsPort,
                    BytesUploaded = 200 + (pointIndex * 10),
                    BytesDownloaded = 0
                });
            }

            return localTrafficEntries;
        }

        // Local (LAN) rollups at 5-minute cadence for the trailing six hours — the same five
        // streams as the raw entries above, so the 1-hour and 6-hour windows show the same shape
        // the 5-minute window does, and LocalNewestRollupBucketDownloadBytes is the newest
        // minute's total across the three that the chart actually draws.
        private static List<LocalTrafficRollup> BuildLocalTrafficRollups(DateTime nowUtc, List<Device> devices)
        {
            string nasIpAddress = devices[NasDeviceIndex].IpAddress;
            string secondPeerIpAddress = devices[SecondPeerDeviceIndex].IpAddress;
            string routerIpAddress = devices[RouterDeviceIndex].IpAddress;
            List<LocalTrafficRollup> localTrafficRollups = new List<LocalTrafficRollup>();

            for (int pointIndex = 0; pointIndex < 72; pointIndex++)
            {
                DateTime timestamp = nowUtc.AddMinutes(-5 * pointIndex);
                long minuteEpoch = MinuteEpoch(timestamp);

                localTrafficRollups.Add(new LocalTrafficRollup
                {
                    MinuteEpoch = minuteEpoch,
                    ProcessName = LocalDataProcess,
                    RemoteIp = nasIpAddress,
                    Protocol = TcpProtocol,
                    RemotePort = SmbPort,
                    BytesUploaded = NasSmbRollupUploadBase + (pointIndex * NasSmbRollupUploadStep),
                    BytesDownloaded = NasSmbRollupDownloadBase + (pointIndex * NasSmbRollupDownloadStep)
                });

                localTrafficRollups.Add(new LocalTrafficRollup
                {
                    MinuteEpoch = minuteEpoch,
                    ProcessName = LocalFileProcess,
                    RemoteIp = nasIpAddress,
                    Protocol = TcpProtocol,
                    RemotePort = SmbPort,
                    BytesUploaded = NasFileRollupUploadBase + (pointIndex * NasFileRollupUploadStep),
                    BytesDownloaded = NasFileRollupDownloadBase + (pointIndex * NasFileRollupDownloadStep)
                });

                localTrafficRollups.Add(new LocalTrafficRollup
                {
                    MinuteEpoch = minuteEpoch,
                    ProcessName = LocalDataProcess,
                    RemoteIp = secondPeerIpAddress,
                    Protocol = TcpProtocol,
                    RemotePort = MediaPort,
                    BytesUploaded = SecondPeerRollupUploadBase + (pointIndex * SecondPeerRollupUploadStep),
                    BytesDownloaded = SecondPeerRollupDownloadBase + (pointIndex * SecondPeerRollupDownloadStep)
                });

                localTrafficRollups.Add(new LocalTrafficRollup
                {
                    MinuteEpoch = minuteEpoch,
                    ProcessName = LocalDataProcess,
                    RemoteIp = routerIpAddress,
                    Protocol = UdpProtocol,
                    RemotePort = MdnsPort,
                    BytesUploaded = DiscoveryRollupUploadBase + (pointIndex * DiscoveryRollupUploadStep),
                    BytesDownloaded = 0
                });

                localTrafficRollups.Add(new LocalTrafficRollup
                {
                    MinuteEpoch = minuteEpoch,
                    ProcessName = LocalDataProcess,
                    RemoteIp = LocalDiscoveryMulticastIp,
                    Protocol = UdpProtocol,
                    RemotePort = MdnsPort,
                    BytesUploaded = DiscoveryRollupUploadBase + (pointIndex * DiscoveryRollupUploadStep),
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

        // Seconds since the Unix epoch truncated to the minute — NOT minutes since the epoch.
        // Task 9: this used to return TotalMinutes, which is 60x too small, so every seeded rollup
        // (WAN and LAN alike) carried a MinuteEpoch somewhere in January 1970. Both traffic pages
        // read rollups for every window a minute or wider (InternetViewModel/LocalViewModel:
        // `WHERE MinuteEpoch >= $cutoffEpoch`, with a cutoff in seconds), so the 1h, 6h, 24h and 7d
        // views showed "0 apps · 0 B total" against a fixture that believed it had seeded six hours
        // of traffic. Nothing asserted the traffic pages until this task, so the fixture had been
        // silently empty there since Task 5. Mirrors TrafficTracker.MinuteEpochFor exactly, which
        // is what actually writes these rows in the running app.
        private static long MinuteEpoch(DateTime timestampUtc)
        {
            long secondsEpoch = (long)(timestampUtc - DateTime.UnixEpoch).TotalSeconds;
            long minuteEpoch = (secondsEpoch / 60) * 60;

            return minuteEpoch;
        }
    }
}
