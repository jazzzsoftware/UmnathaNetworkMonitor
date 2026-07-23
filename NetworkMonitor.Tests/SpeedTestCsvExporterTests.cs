using System;
using System.Collections.Generic;
using NetworkMonitor.Models;
using NetworkMonitor.Services.Csv;
using Xunit;

namespace NetworkMonitor.Tests
{
    public class SpeedTestCsvExporterTests
    {
        [Fact]
        public void ToCsvWritesHeaderRow()
        {
            List<SpeedTestResult> results = [];

            string csv = SpeedTestCsvExporter.ToCsv(results);

            string firstLine = csv.Split('\n')[0].TrimEnd('\r');

            Assert.Equal("Timestamp,Download (Mb/s),Upload (Mb/s),Download (MB/s),Upload (MB/s),Latency (ms),Jitter (ms),Server,Success,Error", firstLine);
        }

        [Fact]
        public void ToCsvWritesResultRow()
        {
            List<SpeedTestResult> results =
            [
                new SpeedTestResult
                {
                    Timestamp = new DateTime(2026, 6, 28, 10, 0, 0, DateTimeKind.Utc),
                    DownloadMbps = 240.0,
                    UploadMbps = 16.0,
                    LatencyMs = 12.0,
                    JitterMs = 3.0,
                    Server = "JNB",
                    Success = true
                }
            ];

            string csv = SpeedTestCsvExporter.ToCsv(results);

            Assert.Contains("240.0,16.0,30.0,2.0,12,3,JNB,Yes", csv);
        }
    }
}
