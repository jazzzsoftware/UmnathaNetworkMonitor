namespace NetworkMonitor.UITests.Runner
{
    public sealed class Phase
    {
        public Phase(string name, bool abortsRun, Func<PhaseContext, Task<IReadOnlyList<StepResult>>> run)
        {
            Name = name;
            AbortsRun = abortsRun;
            Run = run;
        }

        public string Name
        {
            get;
        }

        public bool AbortsRun
        {
            get;
        }

        public Func<PhaseContext, Task<IReadOnlyList<StepResult>>> Run
        {
            get;
        }
    }
}
