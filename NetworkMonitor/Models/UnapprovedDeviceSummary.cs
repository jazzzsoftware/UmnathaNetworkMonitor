using System.Text.Json.Serialization;

namespace NetworkMonitor.Models
{
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
}
