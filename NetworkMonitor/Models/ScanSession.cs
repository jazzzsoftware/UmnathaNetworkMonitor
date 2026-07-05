namespace NetworkMonitor.Models
{
    public class ScanSession
    {
        public int Id
        {
            get;
            set;
        }

        public DateTime StartedAt
        {
            get;
            set;
        }

        public DateTime? CompletedAt
        {
            get;
            set;
        }

        public int DevicesFound
        {
            get;
            set;
        }

        public int NewDevices
        {
            get;
            set;
        }

        public int DevicesGone
        {
            get;
            set;
        }
    }
}