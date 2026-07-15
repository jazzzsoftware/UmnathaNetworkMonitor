using System.Text.Json.Serialization;

namespace NetworkMonitor.Models
{
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
}
