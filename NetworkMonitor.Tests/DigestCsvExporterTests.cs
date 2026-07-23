using NetworkMonitor.Models.Digest;
using NetworkMonitor.Services.Digest;
using Xunit;

namespace NetworkMonitor.Tests
{
    public class DigestCsvExporterTests
    {
        [Fact]
        public void BuildAllCsvWritesHeaderRowThenOneRowPerReport()
        {
            DigestSummary first = new DigestSummary { TotalBytesUploaded = 100 };
            DigestSummary second = new DigestSummary { TotalBytesUploaded = 200 };

            List<(DateTime, DateTime, DateTime, DigestSummary)> reports = new()
            {
                (DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow, first),
                (DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow, second)
            };

            string csv = DigestCsvExporter.BuildAllCsv(reports);
            string[] lines = csv.Split(new string[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

            Assert.StartsWith("Period Start,Period End,Generated,Total Uploaded (Raw),Total Uploaded (Friendly)", lines[0]);
            Assert.Contains("100", lines[1]);
            Assert.Contains("200", lines[2]);
        }

        [Fact]
        public void BuildAllCsvPutsScalarMetricsInColumnsOnOneRow()
        {
            DigestSummary summary = new DigestSummary
            {
                TotalBytesUploaded = 100,
                TotalBytesDownloaded = 200,
                AppearedCount = 3,
                DisappearedCount = 4,
                OnlineCount = 5,
                OfflineCount = 6
            };

            List<(DateTime, DateTime, DateTime, DigestSummary)> reports = new()
            {
                (DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow, summary)
            };

            string csv = DigestCsvExporter.BuildAllCsv(reports);
            string[] lines = csv.Split(new string[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            string[] columns = lines[1].Split(',');

            Assert.Equal(13, columns.Length);
            Assert.Equal("100", columns[3]);
            Assert.Equal("100 B", columns[4]);
            Assert.Equal("200", columns[5]);
            Assert.Equal("200 B", columns[6]);
            Assert.Equal("0", columns[7]);
            Assert.Equal("0", columns[8]);
            Assert.Equal("3", columns[9]);
            Assert.Equal("4", columns[10]);
            Assert.Equal("5", columns[11]);
            Assert.Equal("6", columns[12]);
        }
    }
}
