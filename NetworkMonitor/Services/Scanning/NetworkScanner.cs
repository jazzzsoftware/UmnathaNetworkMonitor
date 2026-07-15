using System.Net;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using NetworkMonitor.Data;

namespace NetworkMonitor.Services.Scanning
{
    public partial class NetworkScanner(OuiDatabase oui, MdnsProbe mdnsProbe)
    {
        private const int MaxParallelDnsLookups = 20;

        private const int PingCancelBufferMs = 2000;

        private const int ArpTimeoutSeconds = 10;

        private const int MdnsListenMs = 4000;

        public async Task<IReadOnlyList<ScannedDevice>> ScanAsync(
            Settings settings, CancellationToken ct = default)
        {
            using SemaphoreSlim semaphore = new(settings.MaxParallelPings);
            using SemaphoreSlim dnsSemaphore = new(MaxParallelDnsLookups);

            Task<IReadOnlyDictionary<string, MdnsInfo>> mdnsTask =
                mdnsProbe.DiscoverAsync(TimeSpan.FromMilliseconds(MdnsListenMs), ct);

            IEnumerable<Task<string?>> pingTasks = Enumerable
                .Range(settings.StartHost, settings.EndHost - settings.StartHost + 1)
                .Select(host => PingHostAsync($"{settings.SubnetBase}.{host}", settings.PingTimeoutMs, semaphore, ct));

            IEnumerable<string> respondingIps = (await Task.WhenAll(pingTasks))
                .Where(ip => ip is not null)
                .Select(ip => ip!);

            Dictionary<string, string> arpTable = await GetArpTableAsync(ct);

            IReadOnlyDictionary<string, MdnsInfo> mdnsMap = await mdnsTask;

            List<Task<ScannedDevice>> deviceTasks = respondingIps
                .Where(ip => arpTable.ContainsKey(ip))
                .Select(async ip =>
                {
                    string mac = arpTable[ip];
                    string? hostname = await ResolveHostnameAsync(ip, dnsSemaphore, ct);
                    string? vendor = oui.Lookup(mac);
                    mdnsMap.TryGetValue(ip, out MdnsInfo? mdnsInfo);
                    ScannedDevice scannedDevice = new(ip, mac, hostname, vendor, mdnsInfo?.Name, mdnsInfo?.Model);

                    return scannedDevice;
                })
                .ToList();

            ScannedDevice[] devices = await Task.WhenAll(deviceTasks);

            return devices;
        }

        private static async Task<string?> PingHostAsync(
            string ip, int timeoutMs, SemaphoreSlim sem, CancellationToken ct)
        {
            string? result = null;
            bool acquired = false;

            try
            {
                await sem.WaitAsync(ct);
                acquired = true;

                using Ping ping = new();
                using CancellationTokenSource pingTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                pingTimeout.CancelAfter(TimeSpan.FromMilliseconds(timeoutMs + PingCancelBufferMs));

                PingReply reply = await ping.SendPingAsync(ip, TimeSpan.FromMilliseconds(timeoutMs), cancellationToken: pingTimeout.Token);
                result = reply.Status == IPStatus.Success ? ip : null;
            }
            catch
            {
            }
            finally
            {

                if (acquired)
                {
                    sem.Release();
                }

            }

            return result;
        }

        private static async Task<string?> ResolveHostnameAsync(string ip, SemaphoreSlim sem, CancellationToken ct)
        {
            string? hostname = null;
            bool acquired = false;

            try
            {
                await sem.WaitAsync(ct);
                acquired = true;

                using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(2));

                hostname = (await Dns.GetHostEntryAsync(ip, cts.Token)).HostName;
            }
            catch
            {
            }
            finally
            {

                if (acquired)
                {
                    sem.Release();
                }

            }

            return hostname;
        }

        private static async Task<Dictionary<string, string>> GetArpTableAsync(CancellationToken ct)
        {
            Dictionary<string, string> result = new(StringComparer.OrdinalIgnoreCase);

            try
            {
                using CancellationTokenSource arpTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                arpTimeout.CancelAfter(TimeSpan.FromSeconds(ArpTimeoutSeconds));

                System.Diagnostics.ProcessStartInfo psi = new("arp", "-a")
                {
                    RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true
                };

                using System.Diagnostics.Process proc = System.Diagnostics.Process.Start(psi)!;

                try
                {
                    string output = await proc.StandardOutput.ReadToEndAsync(arpTimeout.Token);
                    await proc.WaitForExitAsync(arpTimeout.Token);

                    foreach (Match match in ArpLineRegex().Matches(output))
                    {
                        result[match.Groups["ip"].Value] = NormaliseMac(match.Groups["mac"].Value);
                    }

                }
                catch (OperationCanceledException)
                {

                    if (!proc.HasExited)
                    {
                        proc.Kill(true);
                    }

                }

            }
            catch
            {
            }

            return result;
        }

        private static string NormaliseMac(string raw)
        {
            string normalised = MacNormalizer.Normalize(raw);

            return normalised;
        }

        [GeneratedRegex(
            @"(?<ip>\d{1,3}(?:\.\d{1,3}){3})\s+" +
            @"(?<mac>[0-9a-fA-F]{2}[:\-][0-9a-fA-F]{2}[:\-][0-9a-fA-F]{2}[:\-][0-9a-fA-F]{2}[:\-][0-9a-fA-F]{2}[:\-][0-9a-fA-F]{2})")]
        private static partial Regex ArpLineRegex();

    }
}