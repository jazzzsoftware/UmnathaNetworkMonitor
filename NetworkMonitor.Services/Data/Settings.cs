using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using NetworkMonitor.Models.Formatting;
using NetworkMonitor.Core.Charting;
using NetworkMonitor.Core.Traffic;

namespace NetworkMonitor.Services.Data
{
    public class Settings
    {
        private static readonly object _saveLock = new object();

        // Held rather than built per save. Every JsonSerializerOptions instance owns its own converter
        // and type-metadata cache, so constructing one inside Save redid the reflection warm-up for the
        // whole of this type on every write and threw the cache away again. Saves are not rare: the
        // widget's placement debounce alone writes every 400 ms for the length of a drag.
        private static readonly JsonSerializerOptions SaveOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        // The one setting that has a live watcher. SettingsPage writes straight through to this
        // object, and the mini graph window is created once and then hidden and shown for the life of
        // the session — so without a signal a toggle could not reach it at all, and re-reading on show
        // only meant the user had to hide and show the widget to make the switch take. InternetPage
        // and LocalPage are reconstructed on navigation and re-read for free, so they do not subscribe.
        public event EventHandler? ChartSmoothScrollingChanged;

        public static string SettingsFilePath =>
            Path.Combine(
                AppPaths.AppDataFolder,
                "settings.json");

        public string SubnetBase
        {
            get;
            set;
        } = "192.168.1";

        public bool AutoDetectSubnet
        {
            get;
            set;
        } = true;

        public int StartHost
        {
            get;
            set;
        } = 1;

        public int EndHost
        {
            get;
            set;
        } = 254;

        public int IntervalMinutes
        {
            get;
            set;
        } = 5;

        public int PingTimeoutMs
        {
            get;
            set;
        } = 150;

        public int MaxParallelPings
        {
            get;
            set;
        } = 50;

        public bool ShowToasts
        {
            get;
            set;
        } = true;

        public bool UnapprovedOnlyToasts
        {
            get;
            set;
        } = false;

        public int HistoryPurgeDays
        {
            get;
            set;
        } = 30;

        public double InternetTimeRangeHours
        {
            get;
            set;
        } = 5.0 / 60.0;

        public double LocalTimeRangeHours
        {
            get;
            set;
        } = 5.0 / 60.0;

        public LocalLens LocalLens
        {
            get;
            set;
        } = LocalLens.ByApp;

        public bool DevicesOnlineOnly
        {
            get;
            set;
        } = false;

        public int TrafficIntervalSeconds
        {
            get;
            set;
        } = 1;

        public int TrafficPurgeDays
        {
            get;
            set;
        } = 7;

        private bool _chartSmoothScrolling = true;

        public bool ChartSmoothScrolling
        {
            get => _chartSmoothScrolling;
            set
            {

                if (_chartSmoothScrolling != value)
                {
                    _chartSmoothScrolling = value;

                    ChartSmoothScrollingChanged?.Invoke(this, EventArgs.Empty);
                }

            }
        }

        public string ChartSchemeId
        {
            get;
            set;
        } = ChartSchemeCatalog.DefaultSchemeId;

        public string ChartCustomDownload
        {
            get;
            set;
        } = "#1976D2";

        public string ChartCustomUpload
        {
            get;
            set;
        } = "#AB47BC";

        public string ChartCustomLatency
        {
            get;
            set;
        } = "#F57C00";

        public string ChartCustomJitter
        {
            get;
            set;
        } = "#2E7D32";

        public string ChartCustomSelection
        {
            get;
            set;
        } = "#F57C00";

        public int WindowX
        {
            get;
            set;
        } = -1;

        public int WindowY
        {
            get;
            set;
        } = -1;

        public int WindowWidth
        {
            get;
            set;
        } = -1;

        public int WindowHeight
        {
            get;
            set;
        } = -1;

        public bool WindowMaximized
        {
            get;
            set;
        } = false;

        public int DigestPurgeDays
        {
            get;
            set;
        } = 30;

        public int DigestGenerationHour
        {
            get;
            set;
        } = 6;

        // How far catch-up has looked, as opposed to how far it has generated. A window with no data
        // produces no report, so the report table alone never advances past it and it was re-examined
        // — three queries — on every cycle for as long as retention kept it in range. Absent from an
        // older settings.json, which deserialises to null and simply falls back to the report table.
        public DateTime? DigestCatchUpHighWaterUtc
        {
            get;
            set;
        }

        public bool DigestNotify
        {
            get;
            set;
        } = true;

        public bool EnableLogging
        {
            get;
            set;
        } = false;

        public bool SpeedTestEnabled
        {
            get;
            set;
        } = true;

        public RateUnitMode RateUnitMode
        {
            get;
            set;
        } = RateUnitMode.Both;

        public bool AutoCheckForUpdates
        {
            get;
            set;
        } = true;

        public bool ShowMiniGraph
        {
            get;
            set;
        } = false;

        public bool MiniGraphShowInternet
        {
            get;
            set;
        } = true;

        public bool MiniGraphShowLocal
        {
            get;
            set;
        } = true;

        public bool MiniGraphShowSpeedTest
        {
            get;
            set;
        } = true;

        public bool MiniGraphShowUnknownDevices
        {
            get;
            set;
        } = true;

        public int MiniGraphX
        {
            get;
            set;
        } = int.MinValue;

        public int MiniGraphY
        {
            get;
            set;
        } = int.MinValue;

        public int MiniGraphWidth
        {
            get;
            set;
        } = 320;

        public int MiniGraphHeight
        {
            get;
            set;
        } = 230;

        public int MiniGraphOpacity
        {
            get;
            set;
        } = 100;

        public bool MiniGraphHorizontal
        {
            get;
            set;
        } = false;

        public int MiniGraphStripX
        {
            get;
            set;
        } = int.MinValue;

        public int MiniGraphStripY
        {
            get;
            set;
        } = int.MinValue;

        public int MiniGraphStripHeight
        {
            get;
            set;
        } = 40;

        public bool MiniGraphShowBorder
        {
            get;
            set;
        } = true;

        public bool Save()
        {
            bool saved;

            lock (_saveLock)
            {
                string json = JsonSerializer.Serialize(this, SaveOptions);

                saved = AtomicFile.WriteAllText(SettingsFilePath, json);
            }

            return saved;
        }

        public static string DetectSubnetBase()
        {
            string? detected = TryDetectSubnetBase();
            string subnetBase = detected ?? "192.168.1";

            return subnetBase;
        }

        public static string? TryDetectSubnetBase()
        {
            string? address = GetPrimaryIPv4Address();

            if (address is null)
            {
                address = GetGatewayIPv4Address();
            }

            string? subnetBase = null;

            if (address is not null)
            {
                string[] parts = address.Split('.');

                if (parts.Length == 4)
                {
                    subnetBase = $"{parts[0]}.{parts[1]}.{parts[2]}";
                }

            }

            return subnetBase;
        }

        private static string? GetPrimaryIPv4Address()
        {
            string? address = null;

            try
            {
                using Socket socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                socket.Connect("8.8.8.8", 65530);

                if (socket.LocalEndPoint is IPEndPoint endPoint
                    && !IPAddress.IsLoopback(endPoint.Address)
                    && !endPoint.Address.ToString().StartsWith("169.254.", StringComparison.Ordinal))
                {
                    address = endPoint.Address.ToString();
                }

            }
            catch
            {
            }

            return address;
        }

        private static string? GetGatewayIPv4Address()
        {
            string? address = NetworkInterface.GetAllNetworkInterfaces()
                .Where(networkInterface => networkInterface.OperationalStatus == OperationalStatus.Up
                             && networkInterface.NetworkInterfaceType != NetworkInterfaceType.Loopback
                             && networkInterface.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                .Where(networkInterface => networkInterface.GetIPProperties().GatewayAddresses
                    .Any(gateway => gateway.Address.AddressFamily == AddressFamily.InterNetwork
                                    && !gateway.Address.Equals(IPAddress.Any)))
                .SelectMany(networkInterface => networkInterface.GetIPProperties().UnicastAddresses)
                .Where(unicast => unicast.Address.AddressFamily == AddressFamily.InterNetwork
                               && !IPAddress.IsLoopback(unicast.Address)
                               && !unicast.Address.ToString().StartsWith("169.254.", StringComparison.Ordinal))
                .Select(unicast => unicast.Address.ToString())
                .FirstOrDefault();

            return address;
        }
    }
}