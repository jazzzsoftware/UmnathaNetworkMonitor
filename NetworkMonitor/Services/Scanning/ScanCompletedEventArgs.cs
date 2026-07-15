using NetworkMonitor.Models;

namespace NetworkMonitor.Services.Scanning
{
    public record ScanCompletedEventArgs(ScanSession Session, bool IsManual);
}
