namespace NetworkMonitor.Models
{
    public class DigestSummary
    {
        public string Headline
        {
            get;
            set;
        } = string.Empty;

        public long TotalBytesUploaded
        {
            get;
            set;
        }

        public long TotalBytesDownloaded
        {
            get;
            set;
        }

        public List<InternetTrafficAppSummary> InternetTopApps
        {
            get;
            set;
        } = new();

        public List<LocalTrafficAppSummary> TopLocalApps
        {
            get;
            set;
        } = new();

        public List<NewDeviceSummary> NewDevices
        {
            get;
            set;
        } = new();

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

        public int OnlineCount
        {
            get;
            set;
        }

        public int OfflineCount
        {
            get;
            set;
        }

        public List<HourlyActivitySummary> HourlyActivity
        {
            get;
            set;
        } = new();

        public List<UnapprovedDeviceSummary> UnapprovedDevices
        {
            get;
            set;
        } = new();

        public List<UnapprovedDeviceSummary> AllDevices
        {
            get;
            set;
        } = new();

        public List<SpeedTestRowSummary> SpeedTests
        {
            get;
            set;
        } = new();
    }
}
