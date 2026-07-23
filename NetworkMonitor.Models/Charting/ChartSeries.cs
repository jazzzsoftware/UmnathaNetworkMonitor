using System.Collections.Generic;

namespace NetworkMonitor.Models.Charting
{
    public record ChartSeries(string Name, string ColorHex, IReadOnlyList<ChartValue> Points);
}
