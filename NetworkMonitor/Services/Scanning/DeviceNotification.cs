using NetworkMonitor.Models.Devices;

namespace NetworkMonitor.Services.Scanning
{
    public record DeviceNotification(
        string DisplayName,
        string MacAddress,
        string IpAddress,
        string? Vendor,
        DeviceType Type,
        bool Appeared,
        bool IsNew,
        bool IsApproved);
}