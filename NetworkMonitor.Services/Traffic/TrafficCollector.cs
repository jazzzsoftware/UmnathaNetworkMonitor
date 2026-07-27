using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;
using Microsoft.Extensions.Hosting;
using NetworkMonitor.Services.Platform;
using System.Collections.Concurrent;
using System.Net;
using NetworkMonitor.Core.Traffic;

namespace NetworkMonitor.Services.Traffic
{
    public sealed class TrafficCollector : BackgroundService
    {
        private const string SessionName = "NetworkMonitorTraffic";
        private const int UploadSlot = 0;
        private const int DownloadSlot = 1;
        private const int IdleSlot = 2;
        private const int CounterSlots = 3;
        private const long MaxIdleDrains = 60;
        private readonly ConcurrentDictionary<int, long[]> _counters = new();
        private readonly ConcurrentDictionary<LocalFlowKey, long[]> _localCounters = new();
        private readonly LanClassifier _lanClassifier;
        private TraceEventSession? _session;
        private CancellationTokenRegistration _stopRegistration;

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

                long upload = Interlocked.Exchange(ref counter[UploadSlot], 0);
                long download = Interlocked.Exchange(ref counter[DownloadSlot], 0);

                if (upload > 0 || download > 0)
                {
                    counter[IdleSlot] = 0;
                    snapshot[entry.Key] = (upload, download);
                }
                else
                {
                    counter[IdleSlot]++;

                    if (counter[IdleSlot] > MaxIdleDrains)
                    {
                        DropIdleCounter(_counters, entry.Key, counter, snapshot);
                    }

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

                long upload = Interlocked.Exchange(ref counter[UploadSlot], 0);
                long download = Interlocked.Exchange(ref counter[DownloadSlot], 0);

                if (upload > 0 || download > 0)
                {
                    counter[IdleSlot] = 0;
                    snapshot[entry.Key] = (upload, download);
                }
                else
                {
                    counter[IdleSlot]++;

                    if (counter[IdleSlot] > MaxIdleDrains)
                    {
                        DropIdleCounter(_localCounters, entry.Key, counter, snapshot);
                    }

                }

            }

            return snapshot;
        }

        protected override async Task ExecuteAsync(CancellationToken ct)
        {
            // Everything below runs before the first real await, and StartAsync only returns
            // when a hosted service reaches one — without this the ETW setup would hold up
            // AppHost.StartAsync, which OnLaunched awaits behind the splash screen.
            await Task.Yield();

            try
            {
                StopOrphanedSession();

                _session = new TraceEventSession(SessionName);
                _session.EnableKernelProvider(KernelTraceEventParser.Keywords.NetworkTCPIP);

                _session.Source.Kernel.TcpIpSend += args => AddBytes(args.ProcessID, args.daddr, args.size, upload: true, protocol: 6, remotePort: (ushort)args.dport);
                _session.Source.Kernel.TcpIpRecv += args => AddBytes(args.ProcessID, args.daddr, args.size, upload: false, protocol: 6, remotePort: (ushort)args.dport);
                _session.Source.Kernel.UdpIpSend += args => AddBytes(args.ProcessID, args.daddr, args.size, upload: true, protocol: 17, remotePort: (ushort)args.dport);
                _session.Source.Kernel.UdpIpRecv += args => AddBytes(args.ProcessID, args.saddr, args.size, upload: false, protocol: 17, remotePort: (ushort)args.sport);

                TraceEventSession startedSession = _session;
                _stopRegistration = ct.Register(() => startedSession.Stop());

                await Task.Run(() => startedSession.Source.Process(), CancellationToken.None);
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
            _stopRegistration.Dispose();
            _session?.Dispose();
            base.Dispose();
        }

        private static void DropIdleCounter<TKey>(
            ConcurrentDictionary<TKey, long[]> counters,
            TKey key,
            long[] counter,
            Dictionary<TKey, (long Upload, long Download)> snapshot)
            where TKey : notnull
        {
            // Remove only if the dictionary still holds the array we just drained, then drain it
            // once more: a collector thread can have added bytes between the exchange above and
            // this removal, and those would otherwise be stranded on an orphaned array.
            bool removed = counters.TryRemove(new KeyValuePair<TKey, long[]>(key, counter));

            if (removed)
            {
                long strayUpload = Interlocked.Exchange(ref counter[UploadSlot], 0);
                long strayDownload = Interlocked.Exchange(ref counter[DownloadSlot], 0);

                if (strayUpload > 0 || strayDownload > 0)
                {
                    snapshot[key] = (strayUpload, strayDownload);
                }

            }

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

                if (!_lanClassifier.IsSelfOrLoopback(remote) && !_lanClassifier.IsBroadcastOrMulticast(remote))
                {
                    int slot = upload ? UploadSlot : DownloadSlot;
                    int keyPid = pid < 0 ? WellKnownPids.System : pid;

                    if (_lanClassifier.TryClassifyLocal(remote, out uint packed))
                    {
                        LocalFlowKey key = new LocalFlowKey(keyPid, packed, protocol, remotePort);
                        long[] localCounter = _localCounters.GetOrAdd(key, static missingKey => new long[CounterSlots]);

                        Interlocked.Add(ref localCounter[slot], bytes);
                    }
                    else
                    {
                        long[] counter = _counters.GetOrAdd(keyPid, static missingPid => new long[CounterSlots]);

                        Interlocked.Add(ref counter[slot], bytes);
                    }

                }

            }

        }
    }
}
