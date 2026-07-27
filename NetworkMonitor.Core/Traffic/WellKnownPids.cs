namespace NetworkMonitor.Core.Traffic
{
    public static class WellKnownPids
    {
        // ETW reports -1 when it cannot attribute a packet to a process; those bytes are
        // folded into the System process rather than discarded, so totals stay complete.
        public const int System = 4;

        public const string SystemName = "System";
    }
}
