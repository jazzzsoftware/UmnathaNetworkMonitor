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
        private static readonly TimeSpan SessionSetupTimeout = TimeSpan.FromSeconds(5);
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
                TraceEventSession? startedSession = StartSessionWithBoundedWait();

                if (startedSession is not null)
                {
                    _session = startedSession;
                    _stopRegistration = ct.Register(() => startedSession.Stop());
                    AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
                    AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

                    await Task.Run(() => startedSession.Source.Process(), CancellationToken.None);
                }

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
            AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
            AppDomain.CurrentDomain.UnhandledException -= OnDomainUnhandledException;
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

        // A leftover "NetworkMonitorTraffic" ETW session from a process that was killed rather
        // than exited through the tray has, in practice, left the native Stop()/StartTrace() calls
        // in RunSessionSetup hanging with no way to cancel them — the only known recovery before
        // this fix was running `logman stop NetworkMonitorTraffic -ets` from an elevated prompt.
        // Running the setup on its own throwaway thread means a stuck call strands that thread
        // instead of a shared pool worker, and the bounded Wait below is what stops it from ever
        // blocking app startup: on timeout, capture is simply unavailable for this session.
        private TraceEventSession? StartSessionWithBoundedWait()
        {
            SessionSetupOutcome outcome = new SessionSetupOutcome();
            Thread setupThread = new Thread(() => RunSessionSetup(outcome))
            {
                IsBackground = true,
                Name = "TrafficCollector-Setup"
            };

            setupThread.Start();

            bool completedInTime = outcome.Ready.Wait(SessionSetupTimeout);
            TraceEventSession? result = null;

            if (!completedInTime)
            {
                AppLog.Error(
                    "TrafficCollector.StartSessionWithBoundedWait",
                    new TimeoutException($"ETW session setup did not return within {SessionSetupTimeout.TotalSeconds:0}s. Traffic capture is unavailable this session. The known cause is a leftover '{SessionName}' session orphaned by a previously killed process, whose native Stop() call can hang indefinitely."));
            }
            else if (outcome.Failure is not null)
            {
                AppLog.Error("TrafficCollector.StartSessionWithBoundedWait", outcome.Failure);
            }
            else
            {
                result = outcome.Session;
            }

            return result;
        }

        private void RunSessionSetup(SessionSetupOutcome outcome)
        {

            try
            {
                StopOrphanedSession();

                TraceEventSession session = new TraceEventSession(SessionName);
                session.EnableKernelProvider(KernelTraceEventParser.Keywords.NetworkTCPIP);

                session.Source.Kernel.TcpIpSend += args => AddBytes(args.ProcessID, args.daddr, args.size, upload: true, protocol: 6, remotePort: (ushort)args.dport);
                session.Source.Kernel.TcpIpRecv += args => AddBytes(args.ProcessID, args.daddr, args.size, upload: false, protocol: 6, remotePort: (ushort)args.dport);
                session.Source.Kernel.UdpIpSend += args => AddBytes(args.ProcessID, args.daddr, args.size, upload: true, protocol: 17, remotePort: (ushort)args.dport);
                session.Source.Kernel.UdpIpRecv += args => AddBytes(args.ProcessID, args.saddr, args.size, upload: false, protocol: 17, remotePort: (ushort)args.sport);

                outcome.Session = session;
            }
            catch (Exception exception)
            {
                outcome.Failure = exception;
            }
            finally
            {
                outcome.Ready.Set();
            }

        }

        // Not every process death goes through the tray Exit item — an unhandled exception on a
        // background thread (never seen by App.OnUnhandledException, which only covers the XAML
        // dispatch pipeline) crashes the process without the BackgroundService ever observing
        // cancellation. Catching it process-wide here is a best-effort reduction in how often a
        // session gets orphaned in the first place; it does not replace the bounded-wait recovery
        // above, which is what makes a hard kill (Task Manager, a debugger, a bad update) survivable.
        private void OnProcessExit(object? sender, EventArgs eventArgs)
        {
            StopSessionBestEffort();
        }

        private void OnDomainUnhandledException(object? sender, UnhandledExceptionEventArgs eventArgs)
        {
            StopSessionBestEffort();
        }

        private void StopSessionBestEffort()
        {

            try
            {
                _session?.Stop(noThrow: true);
            }
            catch (Exception exception)
            {
                AppLog.Error("TrafficCollector.StopSessionBestEffort", exception);
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

        private sealed class SessionSetupOutcome
        {
            public readonly ManualResetEventSlim Ready = new ManualResetEventSlim(false);
            public TraceEventSession? Session;
            public Exception? Failure;
        }
    }
}
