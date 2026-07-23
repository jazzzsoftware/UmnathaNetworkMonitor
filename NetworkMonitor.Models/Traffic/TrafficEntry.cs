namespace NetworkMonitor.Models.Traffic
{
    public class TrafficEntry
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

        public string? ProcessPath
        {
            get;
            set;
        }
    }
}
