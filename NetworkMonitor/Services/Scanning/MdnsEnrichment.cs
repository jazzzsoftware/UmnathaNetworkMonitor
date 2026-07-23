using NetworkMonitor.Models.Devices;

namespace NetworkMonitor.Services.Scanning
{
    public static class MdnsEnrichment
    {
        public static void Apply(Device device, MdnsInfo? info)
        {

            if (info is not null)
            {

                if (!string.IsNullOrWhiteSpace(info.Name))
                {
                    device.MdnsName = info.Name;
                }

                if (!string.IsNullOrWhiteSpace(info.Model))
                {
                    device.Model = info.Model;
                }

            }

        }
    }
}
