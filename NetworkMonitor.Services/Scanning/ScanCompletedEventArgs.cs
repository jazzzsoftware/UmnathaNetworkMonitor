using NetworkMonitor.Models.Scanning;

namespace NetworkMonitor.Services.Scanning
{
    public record ScanCompletedEventArgs(ScanSession Session, bool IsManual);
}
