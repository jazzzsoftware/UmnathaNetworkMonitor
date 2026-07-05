using System.Collections.Generic;
using System.Text;
using NetworkMonitor.Models;

namespace NetworkMonitor.Services.Csv
{
    public static class SpeedTestCsvExporter
    {
        public static string ToCsv(IEnumerable<SpeedTestResult> results)
        {
            StringBuilder builder = new();
            builder.AppendLine("Timestamp,Download (Mbps),Upload (Mbps),Download (MBps),Upload (MBps),Latency (ms),Jitter (ms),Server,Success,Error");

            foreach (SpeedTestResult result in results)
            {
                string timestamp = result.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
                string successLabel = result.Success ? "Yes" : "No";

                string line = string.Join(",",
                    CsvField.Escape(timestamp),
                    CsvField.Escape(result.DownloadMbps.ToString("0.0")),
                    CsvField.Escape(result.UploadMbps.ToString("0.0")),
                    CsvField.Escape(result.DownloadMBps.ToString("0.0")),
                    CsvField.Escape(result.UploadMBps.ToString("0.0")),
                    CsvField.Escape(result.LatencyMs.ToString("0")),
                    CsvField.Escape(result.JitterMs.ToString("0")),
                    CsvField.Escape(result.Server),
                    CsvField.Escape(successLabel),
                    CsvField.Escape(result.Error ?? string.Empty));

                builder.AppendLine(line);
            }

            string csv = builder.ToString();

            return csv;
        }
    }
}
