namespace NetworkMonitor.Services.Scanning
{
    public record NetworkChangedEventArgs(string OldSubnetBase, string NewSubnetBase);
}
