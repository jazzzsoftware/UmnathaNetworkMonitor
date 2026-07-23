using System.Text;
using NetworkMonitor.Models.Digest;
using NetworkMonitor.Models.Formatting;
using NetworkMonitor.Models.SpeedTest;
using NetworkMonitor.Models.Traffic;
using NetworkMonitor.Core.Csv;

namespace NetworkMonitor.Core.Digest
{
    public static class DigestCsvExporter
    {
        private const string HeaderRow = "Period Start,Period End,Generated,Total Uploaded (Raw),Total Uploaded (Friendly),Total Downloaded (Raw),Total Downloaded (Friendly),All Devices,Unapproved Devices,Appeared,Disappeared,Online,Offline";

        public static string BuildAllCsv(IReadOnlyList<(DateTime PeriodStartUtc, DateTime PeriodEndUtc, DateTime GeneratedAtUtc, DigestSummary Summary)> reports)
        {
            StringBuilder builder = new StringBuilder();

            builder.AppendLine(HeaderRow);

            foreach ((DateTime PeriodStartUtc, DateTime PeriodEndUtc, DateTime GeneratedAtUtc, DigestSummary Summary) entry in reports)
            {
                string row = BuildSummaryRow(entry.PeriodStartUtc, entry.PeriodEndUtc, entry.GeneratedAtUtc, entry.Summary);
                builder.AppendLine(row);
            }

            foreach ((DateTime PeriodStartUtc, DateTime PeriodEndUtc, DateTime GeneratedAtUtc, DigestSummary Summary) entry in reports)
            {
                string periodEnd = entry.PeriodEndUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

                builder.AppendLine();
                builder.AppendLine();
                builder.AppendLine($"Report,{periodEnd}");
                AppendTrafficTable(builder, entry.Summary);
                AppendLocalTrafficTable(builder, entry.Summary);
                AppendSpeedTestTable(builder, entry.Summary.SpeedTests);
                AppendDeviceTable(builder, "All devices", entry.Summary.AllDevices);
                AppendDeviceTable(builder, "Unapproved devices", entry.Summary.UnapprovedDevices);
            }

            string csv = builder.ToString();

            return csv;
        }

        private static string BuildSummaryRow(DateTime periodStartUtc, DateTime periodEndUtc, DateTime generatedAtUtc, DigestSummary summary)
        {
            string[] columns = new string[]
            {
                CsvField.Escape(periodStartUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")),
                CsvField.Escape(periodEndUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")),
                CsvField.Escape(generatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")),
                CsvField.Escape(summary.TotalBytesUploaded.ToString()),
                CsvField.Escape(ByteSizeFormatter.Format(summary.TotalBytesUploaded)),
                CsvField.Escape(summary.TotalBytesDownloaded.ToString()),
                CsvField.Escape(ByteSizeFormatter.Format(summary.TotalBytesDownloaded)),
                CsvField.Escape(summary.AllDevices.Count.ToString()),
                CsvField.Escape(summary.UnapprovedDevices.Count.ToString()),
                CsvField.Escape(summary.AppearedCount.ToString()),
                CsvField.Escape(summary.DisappearedCount.ToString()),
                CsvField.Escape(summary.OnlineCount.ToString()),
                CsvField.Escape(summary.OfflineCount.ToString())
            };
            string row = string.Join(",", columns);

            return row;
        }

        private static void AppendTrafficTable(StringBuilder builder, DigestSummary summary)
        {
            builder.AppendLine("Top apps by internet traffic");
            builder.AppendLine("Process,Download (Raw),Download (Friendly),Upload (Raw),Upload (Friendly)");

            foreach (InternetTrafficAppSummary app in summary.InternetTopApps)
            {
                string[] cells = new string[]
                {
                    CsvField.Escape(app.ProcessName),
                    CsvField.Escape(app.BytesDownloaded.ToString()),
                    CsvField.Escape(ByteSizeFormatter.Format(app.BytesDownloaded)),
                    CsvField.Escape(app.BytesUploaded.ToString()),
                    CsvField.Escape(ByteSizeFormatter.Format(app.BytesUploaded))
                };
                builder.AppendLine(string.Join(",", cells));
            }

            builder.AppendLine();
        }

        private static void AppendLocalTrafficTable(StringBuilder builder, DigestSummary summary)
        {
            builder.AppendLine("Top apps by local traffic");
            builder.AppendLine("App,Peer,Download (Raw),Download (Friendly),Upload (Raw),Upload (Friendly),Total (Raw),Total (Friendly)");

            foreach (LocalTrafficAppSummary localApp in summary.TopLocalApps)
            {
                string[] cells = new string[]
                {
                    CsvField.Escape(localApp.ProcessName),
                    CsvField.Escape(localApp.Peer),
                    CsvField.Escape(localApp.BytesDownloaded.ToString()),
                    CsvField.Escape(ByteSizeFormatter.Format(localApp.BytesDownloaded)),
                    CsvField.Escape(localApp.BytesUploaded.ToString()),
                    CsvField.Escape(ByteSizeFormatter.Format(localApp.BytesUploaded)),
                    CsvField.Escape(localApp.TotalBytes.ToString()),
                    CsvField.Escape(ByteSizeFormatter.Format(localApp.TotalBytes))
                };
                builder.AppendLine(string.Join(",", cells));
            }

            builder.AppendLine();
        }

        private static void AppendSpeedTestTable(StringBuilder builder, IReadOnlyList<SpeedTestRowSummary> speedTests)
        {
            builder.AppendLine("Speed tests (last 24 hours)");
            builder.AppendLine("Time,Download (Mb/s),Upload (Mb/s),Download (MB/s),Upload (MB/s),Latency (ms),Jitter (ms),Server");

            foreach (SpeedTestRowSummary test in speedTests.OrderByDescending(row => row.Timestamp))
            {
                string[] cells = new string[]
                {
                    CsvField.Escape(test.TimeDisplay),
                    CsvField.Escape(test.DownloadDisplay),
                    CsvField.Escape(test.UploadDisplay),
                    CsvField.Escape(test.DownloadMBpsDisplay),
                    CsvField.Escape(test.UploadMBpsDisplay),
                    CsvField.Escape(test.LatencyDisplay),
                    CsvField.Escape(test.JitterDisplay),
                    CsvField.Escape(test.Server)
                };
                builder.AppendLine(string.Join(",", cells));
            }

            builder.AppendLine();
        }

        private static void AppendDeviceTable(StringBuilder builder, string caption, IReadOnlyList<UnapprovedDeviceSummary> devices)
        {
            builder.AppendLine(caption);
            builder.AppendLine("Last seen,Type,Name,IP Address,MAC Address,Vendor,Conn / Disc");

            foreach (UnapprovedDeviceSummary device in devices)
            {
                string[] cells = new string[]
                {
                    CsvField.Escape(device.LastSeenDisplay),
                    CsvField.Escape(device.Type.ToString()),
                    CsvField.Escape(device.DisplayName),
                    CsvField.Escape(device.IpAddress),
                    CsvField.Escape(device.MacAddress),
                    CsvField.Escape(device.Vendor),
                    CsvField.Escape(device.ConnectActivity)
                };
                builder.AppendLine(string.Join(",", cells));
            }

            builder.AppendLine();
        }
    }
}
