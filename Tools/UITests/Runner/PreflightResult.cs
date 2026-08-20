namespace NetworkMonitor.UITests.Runner
{
    public sealed class PreflightResult
    {
        public PreflightResult(IReadOnlyList<string> blockers)
        {
            Blockers = blockers;
        }

        public IReadOnlyList<string> Blockers
        {
            get;
        }

        public bool Ready => Blockers.Count == 0;
    }
}
