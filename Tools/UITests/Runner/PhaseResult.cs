namespace NetworkMonitor.UITests.Runner
{
    public sealed class PhaseResult
    {
        public PhaseResult(string name, TimeSpan duration, bool aborted, IReadOnlyList<StepResult> steps)
        {
            Name = name;
            Duration = duration;
            Aborted = aborted;
            Steps = steps;
        }

        public string Name
        {
            get;
        }

        public TimeSpan Duration
        {
            get;
        }

        public bool Aborted
        {
            get;
        }

        public IReadOnlyList<StepResult> Steps
        {
            get;
        }
    }
}
