namespace NetworkMonitor.UITests.Fixtures
{
    public sealed record SeedCounts(
        int KnownDevices,
        int ApprovedDevices,
        int UnapprovedDevices,
        int DeviceEvents,
        int SpeedTestResults,
        int DigestReports);
}
