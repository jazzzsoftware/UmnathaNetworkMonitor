namespace NetworkMonitor.Services.Scanning
{
    public record ScannedDevice(string Ip, string Mac, string? Hostname, string? Vendor, string? MdnsName, string? Model, bool IsHost = false);
}
