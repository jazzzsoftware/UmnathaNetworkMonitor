using NetworkMonitor.Models.Charting;
using NetworkMonitor.Models.Traffic;

namespace NetworkMonitor.ViewModels
{
    internal record InternetLoadResult(
        List<ChartPoint> ChartPoints,
        List<InternetTrafficAppRow> DisplayRows,
        string StatusText,
        long CutoffEpoch,
        long BucketSeconds);
}
