using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;
using Microsoft.Extensions.Hosting;
using NetworkMonitor.Services.Platform;
using System.Collections.Concurrent;
using System.Net;

namespace NetworkMonitor.Services.Traffic
{
    public class TrafficCollector : BackgroundService
    {
        private const string SessionName = "NetworkMonitorTraffic";
        private const int SystemPid = 4;
        private readonly ConcurrentDictionary<int, long[]> _counters = new();
        private readonly ConcurrentDictionary<LocalFlowKey, long[]> _localCounters = new();
        private readonly LanClassifier _lanClassifier;
        private TraceEventSession? _session;

        public TrafficCollector(LanClassifier lanClassifier)
        {
            _lanClassifier = lanClassifier;
        }

        public Dictionary<int, (long Upload, long Download)> DrainAndReset()
        {
            Dictionary<int, (long Upload, long Download)> snapshot = new();

            foreach (KeyValuePair<int, long[]> entry in _counters)
            {
                long[] counter = entry.Value;

                long upload = Interlocked.Exchange(ref counter[0], 0);
                long download = Interlocked.Exchange(ref counter[1], 0);

                if (upload > 0 || download > 0)
                {
                    snapshot[entry.Key] = (upload, download);
                }

            }

            return snapshot;
        }

        public Dictionary<LocalFlowKey, (long Upload, long Download)> DrainAndResetLocal()
        {
            Dictionary<LocalFlowKey, (long Upload, long Download)> snapshot = new();

            foreach (KeyValuePair<LocalFlowKey, long[]> entry in _localCounters)
            {
                long[] counter = entry.Value;

                long upload = Interlocked.Exchange(ref counter[0], 0);
                long download = Interlocked.Exchange(ref counter[1], 0);

                if (upload > 0 || download > 0)
                {
                    snapshot[entry.Key] = (upload, download);
                }

            }

            return snapshot;
        }

        protected override async Task ExecuteAsync(CancellationToken ct)
        {

            try
            {
                StopOrphanedSession();

                _session = new TraceEventSession(SessionName);
                _session.EnableKernelProvider(KernelTraceEventParser.Keywords.NetworkTCPIP);

                _session.Source.Kernel.TcpIpSend += args => AddBytes(args.ProcessID, args.daddr, args.size, upload: true, protocol: 6, remotePort: (ushort)args.dport);
                _session.Source.Kernel.TcpIpRecv += args => AddBytes(args.ProcessID, args.saddr, args.size, upload: false, protocol: 6, remotePort: (ushort)args.sport);
                _session.Source.Kernel.UdpIpSend += args => AddBytes(args.ProcessID, args.daddr, args.size, upload: true, protocol: 17, remotePort: (ushort)args.dport);
                _session.Source.Kernel.UdpIpRecv += args => AddBytes(args.ProcessID, args.saddr, args.size, upload: false, protocol: 17, remotePort: (ushort)args.sport);

                ct.Register(() => _session.Stop());

                await Task.Run(() => _session.Source.Process(), CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                AppLog.Error("TrafficCollector.ExecuteAsync", exception);
            }

        }

        public override void Dispose()
        {
            _session?.Dispose();
            base.Dispose();
        }

        private static void StopOrphanedSession()
        {

            foreach (string activeSession in TraceEventSession.GetActiveSessionNames())
            {

                if (string.Equals(activeSession, SessionName, StringComparison.OrdinalIgnoreCase))
                {
                    using TraceEventSession leftover = new TraceEventSession(SessionName, TraceEventSessionOptions.Attach);
                    leftover.Stop();
                }

            }

        }

        private void AddBytes(int pid, IPAddress remote, int bytes, bool upload, byte protocol, ushort remotePort)
        {

            if (bytes > 0)
            {

                if (!_lanClassifier.IsSelfOrLoopback(remote))
                {
                    int slot = upload ? 0 : 1;

                    if (_lanClassifier.TryClassifyLocal(remote, out uint packed))
                    {
                        int keyPid = pid < 0 ? SystemPid : pid;
                        LocalFlowKey key = new LocalFlowKey(keyPid, packed, protocol, remotePort);
                        long[] localCounter = _localCounters.GetOrAdd(key, static missingKey => new long[2]);

                        Interlocked.Add(ref localCounter[slot], bytes);
                    }
                    else if (pid >= 0)
                    {
                        long[] counter = _counters.GetOrAdd(pid, static missingPid => new long[2]);

                        Interlocked.Add(ref counter[slot], bytes);
                    }

                }

            }

        }
    }
}
