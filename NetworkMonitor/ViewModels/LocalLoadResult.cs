using System.Collections.Generic;
using NetworkMonitor.Models.Charting;
using NetworkMonitor.Models.Traffic;
using NetworkMonitor.Services.Traffic;

namespace NetworkMonitor.ViewModels
{
    internal record LocalLoadResult(
        List<ChartPoint> ChartPoints,
        List<LocalTrafficGroupRow> Groups,
        List<LocalFlowMinute> Minutes,
        string StatusText,
        long CutoffEpoch,
        long BucketSeconds);
}
