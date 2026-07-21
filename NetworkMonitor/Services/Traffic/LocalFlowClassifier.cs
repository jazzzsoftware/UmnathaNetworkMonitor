using System.Collections.Generic;

namespace NetworkMonitor.Services.Traffic
{
    public static class LocalFlowClassifier
    {
        private const int Tcp = 6;
        private const int Udp = 17;

        private static readonly int[] DiscoveryPorts = { 5353, 5355, 1900, 3702, 137, 138, 67, 68, 5350, 5351 };
        private static readonly HashSet<int> DiscoveryPortSet = new HashSet<int>(DiscoveryPorts);

        public static string DiscoverySqlPredicate => $"(Protocol = {Udp} AND RemotePort IN ({string.Join(",", DiscoveryPorts)}))";

        public static FlowClassification Classify(int protocol, int remotePort)
        {
            FlowClassification result;

            if (protocol == Udp && IsDiscoveryPort(remotePort))
            {
                result = new FlowClassification(FlowCategory.Discovery, null);
            }
            else
            {
                string? tag = ServiceTagFor(protocol, remotePort);
                result = new FlowClassification(FlowCategory.Data, tag);
            }

            return result;
        }

        private static bool IsDiscoveryPort(int remotePort)
        {
            bool discovery = DiscoveryPortSet.Contains(remotePort);

            return discovery;
        }

        private static string? ServiceTagFor(int protocol, int remotePort)
        {
            string? tag = null;

            if (protocol == Tcp)
            {
                tag = remotePort switch
                {
                    445 => "SMB",
                    139 => "SMB",
                    2049 => "NFS",
                    548 => "AFP",
                    80 => "HTTP",
                    8080 => "HTTP",
                    443 => "HTTPS",
                    8443 => "HTTPS",
                    22 => "SSH",
                    3389 => "RDP",
                    _ => null
                };
            }

            return tag;
        }
    }
}
