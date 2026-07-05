using System.Text.Json.Serialization;

namespace NetworkMonitor.Models
{
    public class DigestSummary
    {
        public string Headline
        {
            get;
            set;
        } = string.Empty;

        public long TotalBytesUploaded
        {
            get;
            set;
        }

        public long TotalBytesDownloaded
        {
            get;
            set;
        }

        public List<TrafficAppSummary> TopApps
        {
            get;
            set;
        } = new();

        public List<NewDeviceSummary> NewDevices
        {
            get;
            set;
        } = new();

        public int AppearedCount
        {
            get;
            set;
        }

        public int DisappearedCount
        {
            get;
            set;
        }

        public int OnlineCount
        {
            get;
            set;
        }

        public int OfflineCount
        {
            get;
            set;
        }

        public List<HourlyActivitySummary> HourlyActivity
        {
            get;
            set;
        } = new();

        public List<UnapprovedDeviceSummary> UnapprovedDevices
        {
            get;
            set;
        } = new();

        public List<UnapprovedDeviceSummary> AllDevices
        {
            get;
            set;
        } = new();

        public List<SpeedTestRowSummary> SpeedTests
        {
            get;
            set;
        } = new();
    }

    public class SpeedTestRowSummary
    {
        public DateTime Timestamp
        {
            get;
            set;
        }

        public double DownloadMbps
        {
            get;
            set;
        }

        public double UploadMbps
        {
            get;
            set;
        }

        public double LatencyMs
        {
            get;
            set;
        }

        public double JitterMs
        {
            get;
            set;
        }

        public string Server
        {
            get;
            set;
        } = string.Empty;

        [JsonIgnore]
        public string TimeDisplay => Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

        [JsonIgnore]
        public string DownloadDisplay => DownloadMbps.ToString("0.0");

        [JsonIgnore]
        public string UploadDisplay => UploadMbps.ToString("0.0");

        [JsonIgnore]
        public string DownloadMBpsDisplay => (DownloadMbps / 8.0).ToString("0.0");

        [JsonIgnore]
        public string UploadMBpsDisplay => (UploadMbps / 8.0).ToString("0.0");

        [JsonIgnore]
        public string LatencyDisplay => LatencyMs.ToString("0");

        [JsonIgnore]
        public string JitterDisplay => JitterMs.ToString("0");
    }

    public class TrafficAppSummary
    {
        public string ProcessName
        {
            get;
            set;
        } = string.Empty;

        public long BytesUploaded
        {
            get;
            set;
        }

        public long BytesDownloaded
        {
            get;
            set;
        }
    }

    public class NewDeviceSummary
    {
        public string DisplayName
        {
            get;
            set;
        } = string.Empty;

        public string MacAddress
        {
            get;
            set;
        } = string.Empty;

        public string IpAddress
        {
            get;
            set;
        } = string.Empty;

        public string Vendor
        {
            get;
            set;
        } = string.Empty;

        public DeviceType Type
        {
            get;
            set;
        }

        public bool IsApproved
        {
            get;
            set;
        }

        public DateTime FirstSeen
        {
            get;
            set;
        }
    }

    public class UnapprovedDeviceSummary
    {
        public string DisplayName
        {
            get;
            set;
        } = string.Empty;

        public string MacAddress
        {
            get;
            set;
        } = string.Empty;

        public string IpAddress
        {
            get;
            set;
        } = string.Empty;

        public string Vendor
        {
            get;
            set;
        } = string.Empty;

        public DeviceType Type
        {
            get;
            set;
        }

        public DateTime LastSeen
        {
            get;
            set;
        }

        public bool IsApproved
        {
            get;
            set;
        }

        public int AppearedCount
        {
            get;
            set;
        }

        public int DisappearedCount
        {
            get;
            set;
        }

        public bool Highlight
        {
            get;
            set;
        }

        [JsonIgnore]
        public string LastSeenDisplay => LastSeen.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

        [JsonIgnore]
        public string ConnectActivity => $"{AppearedCount} / {DisappearedCount}";

        [JsonIgnore]
        public string TypeIcon => Type switch
        {
            DeviceType.Router => "🌐",
            DeviceType.Switch => "🔀",
            DeviceType.WiFi => "📶",
            DeviceType.PC => "💻",
            DeviceType.Server => "🖥️",
            DeviceType.Mobile => "📱",
            DeviceType.Camera => "📷",
            DeviceType.SmartDevice => "💡",
            DeviceType.Energy => "⚡",
            _ => "❓"
        };
    }

    public class HourlyActivitySummary
    {
        public int Hour
        {
            get;
            set;
        }

        public int Appeared
        {
            get;
            set;
        }

        public int Disappeared
        {
            get;
            set;
        }
    }
}
