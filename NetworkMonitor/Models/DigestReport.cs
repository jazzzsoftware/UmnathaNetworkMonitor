using System.ComponentModel.DataAnnotations.Schema;

namespace NetworkMonitor.Models
{
    public class DigestReport
    {
        public int Id
        {
            get;
            set;
        }

        public DateTime PeriodStart
        {
            get;
            set;
        }

        public DateTime PeriodEnd
        {
            get;
            set;
        }

        public DateTime GeneratedAt
        {
            get;
            set;
        }

        public string Headline
        {
            get;
            set;
        } = string.Empty;

        public string SummaryJson
        {
            get;
            set;
        } = string.Empty;

        public bool IsScheduled
        {
            get;
            set;
        }

        [NotMapped]
        public string PeriodEndDisplay => PeriodEnd.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    }
}
