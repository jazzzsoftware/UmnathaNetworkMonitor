using NetworkMonitor.Models;

namespace NetworkMonitor.Services.Traffic
{
    public class TrafficFlushedEventArgs(IReadOnlyList<TrafficEntry> entries) : EventArgs
    {
        public IReadOnlyList<TrafficEntry> Entries
        {
            get;
        } = entries;
    }
}
