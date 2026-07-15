namespace NetworkMonitor.Models
{
    public class InternetTrafficAppSummary
    {
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
    }
}
