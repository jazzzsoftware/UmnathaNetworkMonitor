namespace NetworkMonitor.Models
{
    public class LocalTrafficEntry
    {
        public int Id
        {
            get;
            set;
        }

        public DateTime Timestamp
        {
            get;
            set;
        }

        public string ProcessName
        {
            get;
            set;
        } = string.Empty;

        public string? ProcessPath
        {
            get;
            set;
        }

        public string RemoteIp
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
}
