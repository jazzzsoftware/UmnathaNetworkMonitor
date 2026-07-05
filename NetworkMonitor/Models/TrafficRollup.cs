namespace NetworkMonitor.Models
{
    public class TrafficRollup
    {
        public int Id
        {
            get;
            set;
        }

        public long MinuteEpoch
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
