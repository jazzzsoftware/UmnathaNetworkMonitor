namespace NetworkMonitor.UITests.Runner
{
    public sealed class RunOutcome
    {
        public RunOutcome(IReadOnlyList<PhaseResult> phases, TimeSpan totalDuration)
        {
            Phases = phases;
            TotalDuration = totalDuration;
        }

        public IReadOnlyList<PhaseResult> Phases
        {
            get;
        }

        public TimeSpan TotalDuration
        {
            get;
        }

        public int PassedCount => CountSteps(StepOutcome.Passed);

        public int FailedCount => CountSteps(StepOutcome.Failed);

        public int SkippedCount => CountSteps(StepOutcome.Skipped);

        public int ExitCode
        {
            get
            {
                bool anyAborted = Phases.Any(phase => phase.Aborted);

                int exitCode;

                if (FailedCount == 0 && !anyAborted)
                {
                    exitCode = 0;
                }
                else
                {
                    exitCode = 1;
                }

                return exitCode;
            }
        }

        private int CountSteps(StepOutcome outcome)
        {
            int count = Phases.SelectMany(phase => phase.Steps).Count(step => step.Outcome == outcome);

            return count;
        }
    }
}
