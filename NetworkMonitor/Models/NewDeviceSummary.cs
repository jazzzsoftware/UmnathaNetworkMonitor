namespace NetworkMonitor.Models
{
    public class NewDeviceSummary
    {
        public string DisplayName
        {
            get;
            set;
        } = string.Empty;

        public string MacAddress
        {
            get;
            set;
        } = string.Empty;

        public string IpAddress
        {
            get;
            set;
        } = string.Empty;

        public string Vendor
        {
            get;
            set;
        } = string.Empty;

        public DeviceType Type
        {
            get;
            set;
        }

        public bool IsApproved
        {
            get;
            set;
        }

        public DateTime FirstSeen
        {
            get;
            set;
        }
    }
}
