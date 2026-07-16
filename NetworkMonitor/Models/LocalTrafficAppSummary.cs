using System.Text.Json.Serialization;

namespace NetworkMonitor.Models
{
    public class LocalTrafficAppSummary
    {
        public string ProcessName
        {
            get;
            set;
        } = string.Empty;

        public string Peer
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
