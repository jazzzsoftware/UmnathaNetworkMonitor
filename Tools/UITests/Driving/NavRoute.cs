namespace NetworkMonitor.UITests.Driving
{
    // Not in Task 6's file list, but Navigator.GoTo(NavRoute route) needs a concrete type to
    // compile against and "one type per file" rules out folding it into Navigator.cs. Same
    // situation Task 3 hit with AppSession/SeedCounts against PhaseContext.
    public enum NavRoute
    {
        Traffic,
        Devices,
        Reports,
        Settings
    }
}
