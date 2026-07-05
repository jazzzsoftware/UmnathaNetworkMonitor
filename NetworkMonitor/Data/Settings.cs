using System;
using System.IO;
using System.Linq;
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
            string? subnet = NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => ni.OperationalStatus == OperationalStatus.Up
                             && ni.NetworkInterfaceType != NetworkInterfaceType.Loopback
                             && ni.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                .SelectMany(ni => ni.GetIPProperties().UnicastAddresses)
                .Where(addr => addr.Address.AddressFamily == AddressFamily.InterNetwork)
                .Select(addr => addr.Address.ToString().Split('.'))
                .Where(parts => parts.Length == 4)
                .Select(parts => $"{parts[0]}.{parts[1]}.{parts[2]}")
                .FirstOrDefault();

            string subnetBase = subnet ?? "192.168.1";

            return subnetBase;
        }
    }
}