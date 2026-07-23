namespace NetworkMonitor.Models.Traffic
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

        public int Protocol
        {
            get;
            set;
        }

        public int RemotePort
        {
            get;
            set;
        }

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
