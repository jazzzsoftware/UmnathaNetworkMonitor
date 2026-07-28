using NetworkMonitor.Models.Update;

namespace NetworkMonitor.Core.Update
{
    // Cancelled is carried separately from the result so a check abandoned by host shutdown
    // can be dropped instead of surfacing as a failure the user never asked about.
    public record UpdateCheckOutcome(UpdateCheckResult Result, bool Cancelled);
}
