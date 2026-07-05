using System;
using System.Collections.Generic;

namespace NetworkMonitor.Models
{
    public record ChartValue(DateTime Timestamp, double Value);

    public record ChartSeries(string Name, string ColorHex, IReadOnlyList<ChartValue> Points);
}
