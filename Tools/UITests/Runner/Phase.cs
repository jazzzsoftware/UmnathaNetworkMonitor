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

        // How long this phase takes for a real run: roughly three times the worst of four
        // measured runs, so a considerably slower machine still fits. Not a hard timeout - those
        // live in the phase's own file, and are far longer. Preflight.CheckAsync sums these
        // across every phase actually registered for the run to judge whether the operator's
        // screen-saver timeout will survive it. Keep them honest: they were hand-set guesses
        // until 2026-08-23, summed to nine times the real runtime, and refused runs on machines
        // with an ordinary 15-minute saver. The registration site in Program.cs records the
        // measurement behind each one.
        public TimeSpan ExpectedDuration
        {
            get;
        }
    }
}
