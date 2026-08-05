using NetworkMonitor.Models.SpeedTest;

namespace NetworkMonitor.Models.Formatting
{
    public static class MiniGraphFormatter
    {
        // Two em spaces. The speed line carries four separate readings and ran together as one string
        // at normal word spacing; this is what separates them without a row of punctuation.
        private const string ElementGap = "\u2003\u2003";

        // Two en spaces. The short line carries two readings rather than four, so it needs clear
        // separation between the rates and the ping without the wider gap the full line uses.
        private const string ShortGap = "\u2002\u2002";

        // Everything after the widget's bold "Speed Test" label, which is why the string opens with the
        // same gap that separates the readings from one another. The time comes last: it is the least
        // urgent part of the line and reads better at the end than wedged between the label and the
        // rates it has nothing to do with.
        public static string SpeedTest(SpeedTestResult? latest, RateUnitMode mode)
        {
            string text = $"{ElementGap}not run yet";

            if (latest is not null && latest.Success)
            {
                bool inBytes = TrafficRateFormatter.SingleUnit(mode) == RateUnitMode.Bytes;
                string unit = inBytes ? "MB/s" : "Mb/s";
                double download = inBytes ? latest.DownloadMBps : latest.DownloadMbps;
                double upload = inBytes ? latest.UploadMBps : latest.UploadMbps;
                string time = latest.LocalTimestamp.ToString("HH:mm");

                text = $"{ElementGap}↓{Scaled(download)} ↑{Scaled(upload)} {unit}{ElementGap}{latest.LatencyMs:F0} ms Ping{ElementGap}{latest.JitterMs:F0} ms Jitter{ElementGap}{time}";
            }

            return text;
        }

        // The horizontal strip has room for the rates and the ping and nothing else. Jitter and the
        // timestamp are dropped rather than shrunk: a cell wide enough for all four readings would be
        // twice the width of a traffic section and would dominate the strip.
        public static string SpeedTestShort(SpeedTestResult? latest, RateUnitMode mode)
        {
            string text = "not run yet";

            if (latest is not null && latest.Success)
            {
                bool inBytes = TrafficRateFormatter.SingleUnit(mode) == RateUnitMode.Bytes;
                string unit = inBytes ? "MB/s" : "Mb/s";
                double download = inBytes ? latest.DownloadMBps : latest.DownloadMbps;
                double upload = inBytes ? latest.UploadMBps : latest.UploadMbps;

                text = $"↓{Scaled(download)} ↑{Scaled(upload)} {unit}{ShortGap}{latest.LatencyMs:F0} ms";
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
