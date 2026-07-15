namespace NetworkMonitor.Models
{
    public class LocalTrafficRollup
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
