using NetworkMonitor.Models.SpeedTest;

namespace NetworkMonitor.Models.Formatting
{
    public static class MiniGraphFormatter
    {
        // 0.5 Mb/s, the same floor InternetTrafficAppRow.HasRate uses for the live rate badge.
        private const double RateThresholdBytesPerSecond = 62_500.0;

        public static string Rate(double downloadBytesPerSecond, double uploadBytesPerSecond, RateUnitMode mode)
        {
            string text = "—";

            if (downloadBytesPerSecond + uploadBytesPerSecond >= RateThresholdBytesPerSecond)
            {
                bool inBytes = mode == RateUnitMode.Bytes;
                double divisor = inBytes ? 1_000_000.0 : 125_000.0;
                string unit = inBytes ? "MB/s" : "Mb/s";
                string download = Scaled(downloadBytesPerSecond / divisor);
                string upload = Scaled(uploadBytesPerSecond / divisor);

                text = $"↓{download} ↑{upload} {unit}";
            }

            return text;
        }

        public static string SpeedTest(SpeedTestResult? latest, RateUnitMode mode)
        {
            string text = "No speed test yet";

            if (latest is not null && latest.Success)
            {
                bool inBytes = mode == RateUnitMode.Bytes;
                string unit = inBytes ? "MB/s" : "Mb/s";
                double download = inBytes ? latest.DownloadMBps : latest.DownloadMbps;
                double upload = inBytes ? latest.UploadMBps : latest.UploadMbps;
                string time = latest.LocalTimestamp.ToString("HH:mm");

                text = $"Speed {time} ↓{Scaled(download)} ↑{Scaled(upload)} {unit} · {latest.LatencyMs:F0} ms";
            }

            return text;
        }

        public static string UnknownDevices(int count)
        {
            string text = count switch
            {
                <= 0 => "✓ no unknown devices",
                1 => "⚠ 1 unknown device",
                _ => $"⚠ {count} unknown devices"
            };

            return text;
        }

        // "0.#" rather than "F1" below ten: 5.6 has to keep its decimal or a slow link reads as
        // zero, but 3.0 must render as "3" — there is no room in this window for a decimal that
        // carries no information.
        private static string Scaled(double value)
        {
            string text = value >= 10.0 ? value.ToString("F0") : value.ToString("0.#");

            return text;
        }
    }
}
