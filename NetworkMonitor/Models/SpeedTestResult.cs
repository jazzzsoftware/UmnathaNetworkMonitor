using System.ComponentModel.DataAnnotations.Schema;

namespace NetworkMonitor.Models
{
    public class SpeedTestResult
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

        public double DownloadMbps
        {
            get;
            set;
        }

        public double UploadMbps
        {
            get;
            set;
        }

        public double LatencyMs
        {
            get;
            set;
        }

        public double JitterMs
        {
            get;
            set;
        }

        public string Server
        {
            get;
            set;
        } = string.Empty;

        public bool Success
        {
            get;
            set;
        }

        public string? Error
        {
            get;
            set;
        }

        [NotMapped]
        public DateTime LocalTimestamp => Timestamp.ToLocalTime();

        [NotMapped]
        public double DownloadMBps => DownloadMbps / 8.0;

        [NotMapped]
        public double UploadMBps => UploadMbps / 8.0;
    }
}
