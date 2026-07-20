namespace NetworkMonitor.Services.Traffic
{
    public static class LocalFlowClassifier
    {
        private const int Tcp = 6;
        private const int Udp = 17;

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
            bool discovery = remotePort switch
            {
                5353 => true,
                5355 => true,
                1900 => true,
                3702 => true,
                137 => true,
                138 => true,
                67 => true,
                68 => true,
                5350 => true,
                5351 => true,
                _ => false
            };

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
