namespace NetworkMonitor.UITests.Runner
{
    // Empty until the seed-database work defines the known row counts every assertion checks
    // against; it exists now so PhaseContext.Seed has a concrete type. Kept in Runner rather
    // than an "Environment" folder/namespace: that name collides with System.Environment,
    // which Preflight.cs (also in this project) calls unqualified.
    public sealed class SeedCounts
    {
    }
}
