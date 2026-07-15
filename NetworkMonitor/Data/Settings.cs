using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;

namespace NetworkMonitor.Data
{
    public class Settings
    {
        public static string SettingsFilePath =>
            Path.Combine(
                AppPaths.AppDataFolder,
                "settings.json");

        public string SubnetBase
        {
            get;
            set;
        } = "192.168.1";

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
        } = 500;

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

        public double TrafficTimeRangeHours
        {
            get;
            set;
        } = 5.0 / 60.0;

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

        public bool ChartSmoothScrolling
        {
            get;
            set;
        } = true;

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

        public void Save()
        {
            string json = JsonSerializer.Serialize(this, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            AtomicFile.WriteAllText(SettingsFilePath, json);
        }

        public static string DetectSubnetBase()
        {
            string? address = GetPrimaryIPv4Address();

            if (address is null)
            {
                address = GetGatewayIPv4Address();
            }

            string subnetBase = "192.168.1";

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