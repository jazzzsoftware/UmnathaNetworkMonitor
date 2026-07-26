namespace NetworkMonitor.Models.Update
{
    public record AvailableUpdate(
        string VersionTag,
        string NormalizedVersion,
        string InstallerUrl,
        string ChecksumUrl,
        long SizeBytes);
}
