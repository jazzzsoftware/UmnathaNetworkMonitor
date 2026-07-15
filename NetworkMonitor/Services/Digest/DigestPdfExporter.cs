using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using NetworkMonitor.Models;
using NetworkMonitor.Services.Common;

namespace NetworkMonitor.Services.Digest
{
    public class DigestPdfExporter(DigestChartRenderer chartRenderer)
    {
        private const string MbpsColor = "#00897B";
        private const string MBpsColor = "#607D8B";

        public byte[] BuildPdf(DigestSummary summary, DateTime periodStartUtc, DateTime periodEndUtc, DateTime generatedAtUtc)
        {
            byte[] trafficChart = chartRenderer.RenderInternetTrafficChart(summary, true);
            byte[] trafficSplitChart = chartRenderer.RenderInternetTrafficSplitChart(summary, true);
            byte[] localSplitChart = chartRenderer.RenderLocalTrafficSplitChart(summary, true);
            byte[] throughputChart = chartRenderer.RenderSpeedThroughputChart(summary, true);
            byte[] latencyChart = chartRenderer.RenderSpeedLatencyChart(summary, true);

            string periodText = $"{periodStartUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss} – {periodEndUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}";
            string generatedText = generatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

            byte[] pdf = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Margin(36);
                    page.Size(PageSizes.A4);
                    page.DefaultTextStyle(style => style.FontSize(9));

                    page.Header().Column(headerColumn =>
                    {
                        headerColumn.Item().Text("Umnatha Network Monitor — Daily Digest").FontSize(18).Bold();
                        headerColumn.Item().PaddingTop(4).Text($"Report period: {periodText}").FontSize(14).SemiBold();
                        headerColumn.Item().Text($"Generated: {generatedText}").FontSize(10).FontColor(Colors.Grey.Medium);
                    });

                    page.Content().PaddingTop(12).Column(column =>
                    {
                        column.Spacing(12);

                        column.Item().Background(Colors.Grey.Lighten3).Padding(12).Column(section =>
                        {
                            section.Spacing(8);

                            section.Item().Text("Internet — Download vs Upload").FontSize(11).SemiBold();
                            section.Item().Image(trafficSplitChart);

                            section.Item().Text("Top apps by internet traffic").FontSize(11).SemiBold();
                            section.Item().Image(trafficChart);

                            section.Item().Table(table =>
                            {
                                table.ColumnsDefinition(columns =>
                                {
                                    columns.RelativeColumn(3);
                                    columns.RelativeColumn();
                                    columns.RelativeColumn();
                                });

                                table.Header(header =>
                                {
                                    header.Cell().Text("Process").SemiBold();
                                    header.Cell().AlignRight().Text("Download").SemiBold();
                                    header.Cell().AlignRight().Text("Upload").SemiBold();
                                });

                                foreach (InternetTrafficAppSummary app in summary.InternetTopApps)
                                {
                                    table.Cell().Text(app.ProcessName);
                                    table.Cell().AlignRight().Text(ByteSizeFormatter.Format(app.BytesDownloaded));
                                    table.Cell().AlignRight().Text(ByteSizeFormatter.Format(app.BytesUploaded));
                                }

                            });
                        });

                        column.Item().Background(Colors.Grey.Lighten3).Padding(12).Column(section =>
                        {
                            section.Spacing(8);

                            section.Item().Text("Local — Download vs Upload").FontSize(11).SemiBold();
                            section.Item().Image(localSplitChart);

                            section.Item().Text("Top devices by local traffic").FontSize(11).SemiBold();

                            section.Item().Table(table =>
                            {
                                table.ColumnsDefinition(columns =>
                                {
                                    columns.RelativeColumn(3);
                                    columns.RelativeColumn();
                                    columns.RelativeColumn();
                                    columns.RelativeColumn();
                                });

                                table.Header(header =>
                                {
                                    header.Cell().Text("Device").SemiBold();
                                    header.Cell().AlignRight().Text("Download").SemiBold();
                                    header.Cell().AlignRight().Text("Upload").SemiBold();
                                    header.Cell().AlignRight().Text("Total").SemiBold();
                                });

                                foreach (LocalTrafficDeviceSummary localDevice in summary.TopLocalDevices)
                                {
                                    table.Cell().Text(localDevice.DeviceName);
                                    table.Cell().AlignRight().Text(ByteSizeFormatter.Format(localDevice.BytesDownloaded));
                                    table.Cell().AlignRight().Text(ByteSizeFormatter.Format(localDevice.BytesUploaded));
                                    table.Cell().AlignRight().Text(ByteSizeFormatter.Format(localDevice.TotalBytes));
                                }

                            });
                        });

                        column.Item().Background(Colors.Grey.Lighten3).Padding(12).Column(section =>
                        {
                            section.Spacing(8);

                            section.Item().Text("Speed tests (last 24 hours)").FontSize(11).SemiBold();
                            section.Item().Image(throughputChart);
                            section.Item().Image(latencyChart);

                            AddSpeedTestTable(section, summary.SpeedTests);
                        });

                        column.Item().Background(Colors.Grey.Lighten3).Padding(12).Column(section =>
                        {
                            section.Spacing(8);

                            AddDeviceTable(section, "All devices", summary.AllDevices);
                            AddDeviceTable(section, "Unapproved devices", summary.UnapprovedDevices);
                        });
                    });

                    page.Footer().AlignCenter().Text(text =>
                    {
                        text.Span("Umnatha Network Monitor — exported ");
                        text.Span(DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
                    });
                });
            }).GeneratePdf();

            return pdf;
        }

        private static void AddSpeedTestTable(ColumnDescriptor column, IReadOnlyList<SpeedTestRowSummary> speedTests)
        {
            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(1.6f);
                    columns.RelativeColumn(1.2f);
                    columns.RelativeColumn(1.2f);
                    columns.RelativeColumn(1.2f);
                    columns.RelativeColumn(1.2f);
                    columns.RelativeColumn();
                    columns.RelativeColumn();
                    columns.RelativeColumn(1.2f);
                });

                table.Header(header =>
                {
                    header.Cell().Text("Time").SemiBold();
                    header.Cell().AlignRight().Text("Download (Mbps)").SemiBold().FontColor(MbpsColor);
                    header.Cell().AlignRight().Text("Upload (Mbps)").SemiBold().FontColor(MbpsColor);
                    header.Cell().AlignRight().Text("Download (MBps)").SemiBold().FontColor(MBpsColor);
                    header.Cell().AlignRight().Text("Upload (MBps)").SemiBold().FontColor(MBpsColor);
                    header.Cell().AlignRight().Text("Latency (ms)").SemiBold();
                    header.Cell().AlignRight().Text("Jitter (ms)").SemiBold();
                    header.Cell().Text("Server").SemiBold();
                });

                foreach (SpeedTestRowSummary test in speedTests.OrderByDescending(row => row.Timestamp))
                {
                    table.Cell().Text(test.TimeDisplay);
                    table.Cell().AlignRight().Text(test.DownloadDisplay).FontColor(MbpsColor);
                    table.Cell().AlignRight().Text(test.UploadDisplay).FontColor(MbpsColor);
                    table.Cell().AlignRight().Text(test.DownloadMBpsDisplay).FontColor(MBpsColor);
                    table.Cell().AlignRight().Text(test.UploadMBpsDisplay).FontColor(MBpsColor);
                    table.Cell().AlignRight().Text(test.LatencyDisplay);
                    table.Cell().AlignRight().Text(test.JitterDisplay);
                    table.Cell().Text(test.Server);
                }

            });
        }

        private static void AddDeviceTable(ColumnDescriptor column, string caption, IReadOnlyList<UnapprovedDeviceSummary> devices)
        {
            column.Item().Text(caption).FontSize(11).SemiBold();

            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(1.4f);
                    columns.RelativeColumn(1.1f);
                    columns.RelativeColumn(2);
                    columns.RelativeColumn(1.3f);
                    columns.RelativeColumn(1.6f);
                    columns.RelativeColumn(1.8f);
                    columns.RelativeColumn(1.2f);
                });

                table.Header(header =>
                {
                    header.Cell().Text("Last seen").SemiBold();
                    header.Cell().Text("Type").SemiBold();
                    header.Cell().Text("Name").SemiBold();
                    header.Cell().Text("IP Address").SemiBold();
                    header.Cell().Text("MAC Address").SemiBold();
                    header.Cell().Text("Vendor").SemiBold();
                    header.Cell().Text("Conn / Disc").SemiBold();
                });

                foreach (UnapprovedDeviceSummary device in devices)
                {
                    Color rowColour = device.Highlight ? Colors.Red.Lighten4 : Colors.Transparent;
                    table.Cell().Background(rowColour).Text(device.LastSeenDisplay);
                    table.Cell().Background(rowColour).Text(device.Type.ToString());
                    table.Cell().Background(rowColour).Text(device.DisplayName);
                    table.Cell().Background(rowColour).Text(device.IpAddress);
                    table.Cell().Background(rowColour).Text(device.MacAddress);
                    table.Cell().Background(rowColour).Text(device.Vendor);
                    table.Cell().Background(rowColour).Text(device.ConnectActivity);
                }

            });
        }
    }
}
