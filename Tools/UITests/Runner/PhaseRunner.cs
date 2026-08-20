using System.Diagnostics;

namespace NetworkMonitor.UITests.Runner
{
    public static class PhaseRunner
    {
        public static async Task<RunOutcome> RunAsync(IReadOnlyList<Phase> phases, PhaseContext context)
        {
            List<PhaseResult> results = new List<PhaseResult>();
            Stopwatch totalStopwatch = Stopwatch.StartNew();

            foreach (Phase phase in phases)
            {
                PhaseResult result = await RunPhaseAsync(phase, context);

                results.Add(result);

                if (result.Aborted && phase.AbortsRun)
                {
                    break;
                }

            }

            totalStopwatch.Stop();

            RunOutcome outcome = new RunOutcome(results, totalStopwatch.Elapsed);

            return outcome;
        }

        private static async Task<PhaseResult> RunPhaseAsync(Phase phase, PhaseContext context)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();

            IReadOnlyList<StepResult> steps;
            bool aborted;

            try
            {
                steps = await phase.Run(context);
                aborted = false;
            }
            catch (Exception exception)
            {
                List<StepResult> failure = new List<StepResult>
                {
                    StepResult.Fail(phase.Name, "phase completed without throwing", exception.Message)
                };

                steps = failure;
                aborted = true;
            }

            stopwatch.Stop();

            PhaseResult result = new PhaseResult(phase.Name, stopwatch.Elapsed, aborted, steps);

            return result;
        }
    }
}
