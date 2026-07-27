using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace NetworkMonitor.Core.Traffic
{
    public sealed class LanClassifier : IDisposable
    {
        private const uint LastOctetMask = 0x000000FFu;
        private const uint LimitedBroadcast = 0xFFFFFFFFu;
        private static readonly TimeSpan RefreshDebounce = TimeSpan.FromSeconds(2);
        private static readonly (uint Start, uint End)[] FixedRanges = BuildFixedRanges();

        private volatile (uint Start, uint End)[] _ranges;

        private volatile (uint Start, uint End)[] _adapterRanges = Array.Empty<(uint Start, uint End)>();

        private volatile HashSet<uint> _selfAddresses = new HashSet<uint>();

        private readonly Timer _refreshTimer;

        public LanClassifier()
        {
            _ranges = FixedRanges;
            _refreshTimer = new Timer(OnRefreshDue, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

            Refresh();

            NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;
        }

        public static bool TryPackIpv4(IPAddress address, out uint packed)
        {
            packed = 0;

            bool success = false;

            if (address.AddressFamily == AddressFamily.InterNetwork)
            {
                byte[] bytes = address.GetAddressBytes();

                packed = ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
                success = true;
            }

            return success;
        }

        public static string Format(uint packed)
        {
            byte first = (byte)((packed >> 24) & 0xFF);
            byte second = (byte)((packed >> 16) & 0xFF);
            byte third = (byte)((packed >> 8) & 0xFF);
            byte fourth = (byte)(packed & 0xFF);

            string formatted = $"{first}.{second}.{third}.{fourth}";

            return formatted;
        }

        public bool TryClassifyLocal(IPAddress address, out uint packed)
        {
            bool isLocal = false;

            if (TryPackIpv4(address, out packed))
            {
                (uint Start, uint End)[] ranges = _ranges;

                foreach ((uint Start, uint End) range in ranges)
                {

                    if (packed >= range.Start && packed <= range.End)
                    {
                        isLocal = true;

                        break;
                    }

                }

            }

            return isLocal;
        }

        public bool IsSelfOrLoopback(IPAddress address)
        {
            bool result = false;

            if (TryPackIpv4(address, out uint packed))
            {

                if ((packed & 0xFF000000u) == 0x7F000000u)
                {
                    result = true;
                }
                else
                {
                    HashSet<uint> selfAddresses = _selfAddresses;

                    result = selfAddresses.Contains(packed);
                }

            }

            return result;
        }

        public bool IsBroadcastOrMulticast(IPAddress address)
        {
            bool result = false;

            if (TryPackIpv4(address, out uint packed))
            {
                bool multicast = (packed & 0xF0000000u) == 0xE0000000u;

                if (multicast || packed == LimitedBroadcast)
                {
                    result = true;
                }
                else
                {
                    result = IsSubnetBroadcast(packed);
                }

            }

            return result;
        }

        public void Dispose()
        {
            NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;
            _refreshTimer.Dispose();
        }

        public void Refresh()
        {
            List<(uint Start, uint End)> ranges = new List<(uint Start, uint End)>(FixedRanges);
            List<(uint Start, uint End)> adapterRanges = new List<(uint Start, uint End)>();
            HashSet<uint> selfAddresses = new HashSet<uint>();

            foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
            {

                if (adapter.OperationalStatus != OperationalStatus.Up)
                {
                    continue;
                }

                IPInterfaceProperties properties = adapter.GetIPProperties();

                foreach (UnicastIPAddressInformation unicast in properties.UnicastAddresses)
                {

                    if (unicast.Address.AddressFamily != AddressFamily.InterNetwork)
                    {
                        continue;
                    }

                    if (!TryPackIpv4(unicast.Address, out uint ip))
                    {
                        continue;
                    }

                    selfAddresses.Add(ip);

                    if (!TryPackIpv4(unicast.IPv4Mask, out uint mask))
                    {
                        continue;
                    }

                    if (mask == 0)
                    {
                        continue;
                    }

                    uint start = ip & mask;
                    uint end = start | ~mask;

                    ranges.Add((start, end));
                    adapterRanges.Add((start, end));
                }

            }

            _ranges = ranges.ToArray();
            _adapterRanges = adapterRanges.ToArray();
            _selfAddresses = selfAddresses;
        }

        private bool IsSubnetBroadcast(uint packed)
        {
            bool result = false;
            bool covered = false;
            (uint Start, uint End)[] adapterRanges = _adapterRanges;

            foreach ((uint Start, uint End) range in adapterRanges)
            {

                if (packed >= range.Start && packed <= range.End)
                {
                    covered = true;
                    result = packed == range.End;

                    break;
                }

            }

            // No adapter owns this address, so its prefix length is unknown. Assume /24 inside
            // private space (near-universal there) and leave everything else alone: a public
            // host whose address ends in .255 is an ordinary unicast address, and on a /23 or
            // wider LAN so is x.y.even.255 — dropping either would silently lose real traffic.
            if (!covered && (packed & LastOctetMask) == LastOctetMask && IsPrivate(packed))
            {
                result = true;
            }

            return result;
        }

        private static bool IsPrivate(uint packed)
        {
            bool result = false;

            foreach ((uint Start, uint End) range in FixedRanges)
            {

                if (packed >= range.Start && packed <= range.End)
                {
                    result = true;

                    break;
                }

            }

            return result;
        }

        private static (uint Start, uint End)[] BuildFixedRanges()
        {
            (uint Start, uint End)[] ranges = new (uint Start, uint End)[]
            {
                (0x0A000000u, 0x0AFFFFFFu),
                (0xAC100000u, 0xAC1FFFFFu),
                (0xC0A80000u, 0xC0A8FFFFu),
                (0xA9FE0000u, 0xA9FEFFFFu)
            };

            return ranges;
        }

        private void OnNetworkAddressChanged(object? sender, EventArgs eventArgs)
        {
            // Adapter up/down, DHCP renew and VPN connect all fire several of these in a burst;
            // rebuilding the range table once after the burst is enough.
            _refreshTimer.Change(RefreshDebounce, Timeout.InfiniteTimeSpan);
        }

        private void OnRefreshDue(object? state)
        {

            try
            {
                Refresh();
            }
            catch (NetworkInformationException)
            {
            }

        }
    }
}
