using System.Globalization;

namespace NetworkMonitor.Core.Charting
{
    public static class ChartDrawSummary
    {
        public static string Format(int buckets, string series, long peak, long scale, string range)
        {
            string summary = string.Create(
                CultureInfo.InvariantCulture,
                $"buckets={buckets} series={series} peak={peak} scale={scale} range={range}");

            return summary;
        }

        public static bool TryParse(string candidate, out ChartDrawValues values)
        {
            values = default;

            bool parsed = false;

            if (!string.IsNullOrWhiteSpace(candidate))
            {
                Dictionary<string, string> fields = new Dictionary<string, string>(StringComparer.Ordinal);

                foreach (string pair in candidate.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    int separator = pair.IndexOf('=');

                    if (separator > 0)
                    {
                        fields[pair[..separator]] = pair[(separator + 1)..];
                    }

                }

                bool hasEvery =
                    fields.ContainsKey("buckets")
                    && fields.ContainsKey("series")
                    && fields.ContainsKey("peak")
                    && fields.ContainsKey("scale")
                    && fields.ContainsKey("range");

                if (hasEvery)
                {
                    int buckets = 0;
                    long peak = 0L;
                    long scale = 0L;

                    bool numbersRead =
                        int.TryParse(fields["buckets"], NumberStyles.Integer, CultureInfo.InvariantCulture, out buckets)
                        && long.TryParse(fields["peak"], NumberStyles.Integer, CultureInfo.InvariantCulture, out peak)
                        && long.TryParse(fields["scale"], NumberStyles.Integer, CultureInfo.InvariantCulture, out scale);

                    if (numbersRead)
                    {
                        values = new ChartDrawValues(buckets, fields["series"], peak, scale, fields["range"]);
                        parsed = true;
                    }

                }

            }

            return parsed;
        }
    }
}
