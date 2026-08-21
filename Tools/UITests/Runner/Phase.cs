namespace NetworkMonitor.UITests.Runner
{
    public sealed class Phase
    {
        public Phase(string name, bool abortsRun, Func<PhaseContext, Task<IReadOnlyList<StepResult>>> run, TimeSpan expectedDuration)
        {
            Name = name;
            AbortsRun = abortsRun;
            Run = run;
            ExpectedDuration = expectedDuration;
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

        // A deliberately generous, hand-set estimate of how long this phase takes for a real
        // run -- not a hard timeout (those live in the phase's own file) and not a measured
        // average. Preflight.CheckAsync sums these across every phase actually registered for
        // the run to judge whether the operator's screen-saver timeout will survive it; see the
        // registration site in Program.cs and Preflight's own margin comment for how it is used.
        public TimeSpan ExpectedDuration
        {
            get;
        }
    }
}
