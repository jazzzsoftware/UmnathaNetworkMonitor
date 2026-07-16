using NetworkMonitor.Models;

namespace NetworkMonitor.ViewModels
{
    internal record LocalLoadResult(
        List<ChartPoint> ChartPoints,
        List<LocalTrafficAppRow> DisplayRows,
        string StatusText,
        long CutoffEpoch,
        long BucketSeconds);
}
