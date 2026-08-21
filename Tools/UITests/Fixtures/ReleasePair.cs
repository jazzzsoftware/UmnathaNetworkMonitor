using NetworkMonitor.Models.Update;

namespace NetworkMonitor.UITests.Fixtures
{
    // The two releases the update lifecycle drives between: install Baseline, let the app find
    // Target, and prove it got there.
    public sealed record ReleasePair(AvailableUpdate Target, AvailableUpdate Baseline);
}
