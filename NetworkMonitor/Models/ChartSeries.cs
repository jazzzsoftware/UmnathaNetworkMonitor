using System.Collections.Generic;

namespace NetworkMonitor.Models
{
    public record ChartSeries(string Name, string ColorHex, IReadOnlyList<ChartValue> Points);
}
