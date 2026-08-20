using System.Diagnostics;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using NetworkMonitor.UITests.Evidence;

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
                StepResult failedStep = StepResult.Fail(phase.Name, "phase completed without throwing", exception.Message);

                CaptureAbortEvidence(context, failedStep);

                steps = new List<StepResult> { failedStep };
                aborted = true;
            }

            stopwatch.Stop();

            PhaseResult result = new PhaseResult(phase.Name, stopwatch.Elapsed, aborted, steps);

            return result;
        }

        // Fix round 1 (2026-08-20): a real aborted run's report showed "No screenshot captured.
        // No tree dump captured." for the one phase that actually aborted — an abort is exactly
        // the case evidence exists for, and it was previously the one case that produced none.
        // Best-effort and never thrown: a failed capture must not mask the abort it was
        // documenting.
        private static void CaptureAbortEvidence(PhaseContext context, StepResult failedStep)
        {

            try
            {
                AutomationElement? sessionWindow = TryGetSessionWindow(context);

                if (sessionWindow is not null)
                {
                    WriteEvidence(sessionWindow, context.ArtifactFolder, failedStep);
                }
                else
                {

                    using (UIA3Automation automation = new UIA3Automation())
                    {
                        AutomationElement desktop = automation.GetDesktop();

                        WriteEvidence(desktop, context.ArtifactFolder, failedStep);
                    }

                }

            }
            catch (Exception evidenceFailure)
            {
                Console.WriteLine($"PhaseRunner: could not capture abort evidence: {evidenceFailure.Message}");
            }

        }

        private static void WriteEvidence(AutomationElement root, string artifactFolder, StepResult failedStep)
        {
            failedStep.ScreenshotPath = ScreenshotWriter.Write(root, artifactFolder, failedStep.Name);
            failedStep.TreeDumpPath = UiaTreeDumper.Dump(root, artifactFolder, failedStep.Name);
        }

        // No window can be found either when nothing was ever launched (a failure before
        // LaunchPhase assigns PhaseContext.Session) or when the session exists but its main
        // window cannot be resolved (AppSession.MainWindow itself throws TimeoutException) —
        // both fall back to the desktop root in CaptureAbortEvidence above.
        private static AutomationElement? TryGetSessionWindow(PhaseContext context)
        {
            AutomationElement? window = null;

            if (context.Session is not null)
            {

                try
                {
                    window = context.Session.MainWindow;
                }
                catch (Exception)
                {
                    window = null;
                }

            }

            return window;
        }
    }
}
