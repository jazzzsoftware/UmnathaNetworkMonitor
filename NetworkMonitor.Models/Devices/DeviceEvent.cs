using System.ComponentModel.DataAnnotations.Schema;

namespace NetworkMonitor.Models.Devices
{
    public class DeviceEvent
    {
        public int Id
        {
            get;
            set;
        }

        public int DeviceId
        {
            get;
            set;
        }

        public Device Device
        {
            get;
            set;
        } = null!;

        public DeviceEventType EventType
        {
            get;
            set;
        }

        public DateTime Timestamp
        {
            get;
            set;
        }

        [NotMapped]
        public string TimestampLabel => Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

        [NotMapped]
        public string EventLabel => EventType == DeviceEventType.Appeared ? "Appeared" : "Disappeared";
    }
}