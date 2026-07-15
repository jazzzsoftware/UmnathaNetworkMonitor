using System.Text.Json.Serialization;

namespace NetworkMonitor.Models
{
    public class LocalTrafficDeviceSummary
    {
        public string DeviceName
        {
            get;
            set;
        } = string.Empty;

        public string RemoteIp
        {
            get;
            set;
        } = string.Empty;

        public long BytesDownloaded
        {
            get;
            set;
        }

        public long BytesUploaded
        {
            get;
            set;
        }

        [JsonIgnore]
        public long TotalBytes => BytesDownloaded + BytesUploaded;
    }
}
