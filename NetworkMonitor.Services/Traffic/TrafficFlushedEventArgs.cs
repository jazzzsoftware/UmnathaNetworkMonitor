using NetworkMonitor.Models.Traffic;

namespace NetworkMonitor.Services.Traffic
{
    public class TrafficFlushedEventArgs(IReadOnlyList<TrafficEntry> entries, IReadOnlyList<LocalTrafficDelta> localDeltas) : EventArgs
    {
        public IReadOnlyList<TrafficEntry> Entries
        {
            get;
        } = entries;

        public IReadOnlyList<LocalTrafficDelta> LocalDeltas
        {
            get;
        } = localDeltas;
    }
}
