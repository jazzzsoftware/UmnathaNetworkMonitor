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
        private const int MaxSetupAttempts = 2;
        private static readonly TimeSpan SessionSetupTimeout = TimeSpan.FromSeconds(25);
        private readonly ConcurrentDictionary<int, long[]> _counters = new();
        private readonly ConcurrentDictionary<LocalFlowKey, long[]> _localCounters = new();
        private readonly LanClassifier _lanClassifier;
        private readonly InAppNotificationService _notificationService;
        private TraceEventSession? _session;
        private CancellationTokenRegistration _stopRegistration;

        public TrafficCollector(LanClassifier lanClassifier, InAppNotificationService notificationService)
        {
            _lanClassifier = lanClassifier;
            _notificationService = notificationService;
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

        // StopOrphanedSession()'s native Stop() call (and the StartTrace() calls in
        // RunSessionSetup) have no timeout anywhere in the underlying TraceEvent library —
        // confirmed by reading TraceEventSession.cs from microsoft/perfview. What actually makes
        // them slow or stuck on a real machine was not caught directly here: repeated kill/relaunch
        // cycles against the unbounded original code always completed in well under a second, and
        // AppHost.StartAsync() already returns before this method ever runs, so a stall here cannot
        // block the splash screen by itself — the only remaining path from "Stop() is stuck" to
        // "shell never appears" is indirect (for example a stuck ControlTrace call holding an
        // OS-wide ETW lock that something else on the UI thread later waits on), and that indirect
        // path was not tested. Tools/UITests independently carries its own `logman stop` workaround
        // for this same session name, which is suggestive but not proof of the mechanism.
        //
        // Regardless of the exact trigger, a native call with no bound of its own should not be
        // able to hold the app up indefinitely. This runs the cleanup-and-create sequence on its
        // own dedicated thread — so a permanently stuck call strands one throwaway thread rather
        // than a shared pool worker — behind a generous, defence-in-depth timeout: long enough
        // that a cold boot or an antivirus-loaded machine legitimately taking a while won't trip
        // it, short enough to still bound a genuine hang, and retried once before finally giving up
        // and telling the user capture is unavailable rather than only writing it to the log.
        private TraceEventSession? StartSessionWithBoundedWait()
        {
            TraceEventSession? result = null;
            bool succeeded = false;

            for (int attempt = 1; attempt <= MaxSetupAttempts && !succeeded; attempt++)
            {
                (TraceEventSession? Session, bool TimedOut, Exception? Failure) attemptResult = TryStartSessionOnce();

                if (attemptResult.TimedOut)
                {
                    AppLog.Error(
                        "TrafficCollector.StartSessionWithBoundedWait",
                        new TimeoutException($"ETW session setup did not return within {SessionSetupTimeout.TotalSeconds:0}s (attempt {attempt} of {MaxSetupAttempts})."));
                }
                else if (attemptResult.Failure is not null)
                {
                    AppLog.Error("TrafficCollector.StartSessionWithBoundedWait", attemptResult.Failure);
                }
                else
                {
                    result = attemptResult.Session;
                    succeeded = true;
                }

            }

            if (!succeeded)
            {
                _notificationService.Show("Traffic capture is unavailable this session. Restart the app to try again.");
            }

            return result;
        }

        private (TraceEventSession? Session, bool TimedOut, Exception? Failure) TryStartSessionOnce()
        {
            SessionSetupOutcome outcome = new SessionSetupOutcome();
            Thread setupThread = new Thread(() => RunSessionSetup(outcome))
            {
                IsBackground = true,
                Name = "TrafficCollector-Setup"
            };

            setupThread.Start();

            bool signaled = outcome.Ready.Wait(SessionSetupTimeout);

            if (!signaled)
            {
                int previousState = Interlocked.CompareExchange(ref outcome.State, SessionSetupOutcome.WaiterGaveUpFirst, SessionSetupOutcome.InProgress);

                if (previousState != SessionSetupOutcome.InProgress)
                {
                    // The setup thread actually finished in the sliver of time between our Wait()
                    // elapsing and this CompareExchange — it already holds SetupFinishedFirst, and
                    // Ready is already set (or is being set this instant), so this cannot hang.
                    signaled = outcome.Ready.Wait(SessionSetupTimeout);
                }

            }

            (TraceEventSession? Session, bool TimedOut, Exception? Failure) result;

            if (signaled)
            {
                result = (outcome.Session, false, outcome.Failure);
            }
            else
            {
                result = (null, true, null);
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
                int previousState = Interlocked.CompareExchange(ref outcome.State, SessionSetupOutcome.SetupFinishedFirst, SessionSetupOutcome.InProgress);

                if (previousState == SessionSetupOutcome.InProgress)
                {
                    outcome.Ready.Set();
                }
                else
                {
                    // The waiter already gave up on this attempt (its bound elapsed first), so
                    // nothing will ever read outcome.Session — stop and dispose it ourselves rather
                    // than leaving a live, running session that nothing owns. Without this, a
                    // timeout would manufacture exactly the kind of orphan this class exists to
                    // clean up in the first place.
                    outcome.Session?.Stop(noThrow: true);
                    outcome.Session?.Dispose();
                }

            }

        }

        // Not every process death goes through the tray Exit item. An unhandled exception on a
        // background thread never reaches App.OnUnhandledException (which only covers the XAML
        // dispatch pipeline) and crashes the process without the BackgroundService ever observing
        // cancellation — AppDomain.UnhandledException is the one place that still runs first in
        // that case. This is a best-effort reduction in how often a session gets orphaned by that
        // specific case only; it does nothing for Task Manager, a debugger kill, or a forced update
        // install (none of those run any process code at all), which is exactly why the
        // bounded-wait recovery in StartSessionWithBoundedWait is the change that actually matters.
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
            public const int InProgress = 0;
            public const int SetupFinishedFirst = 1;
            public const int WaiterGaveUpFirst = 2;

            public readonly ManualResetEventSlim Ready = new ManualResetEventSlim(false);
            public TraceEventSession? Session;
            public Exception? Failure;
            public int State;
        }
    }
}
