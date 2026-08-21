using System.Diagnostics;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using NetworkMonitor.UITests.Driving;
using NetworkMonitor.UITests.Evidence;

namespace NetworkMonitor.UITests.Runner
{
    public static class PhaseRunner
    {
        public static async Task<RunOutcome> RunAsync(IReadOnlyList<Phase> phases, PhaseContext context)
        {
            List<PhaseResult> results = new List<PhaseResult>();
            DateTime runStartedAt = DateTime.Now;
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

            RunOutcome outcome = new RunOutcome(results, runStartedAt, totalStopwatch.Elapsed);

            return outcome;
        }

        private static async Task<PhaseResult> RunPhaseAsync(Phase phase, PhaseContext context)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();

            DateTime startedAt = DateTime.Now;

            context.RecordedSteps = null;

            // Every phase starts with the front of the app clear. A ContentDialog is modal to the
            // window and blocks UIA calls to it, so one left behind by a failed step does not fail
            // the next step — it fails every step in every phase that follows, all with timeouts
            // that name the wrong thing. A run on 2026-08-21 lost three phases that way to a
            // single import dialog. The phase that leaves one open still owns that failure; this
            // only stops it spreading, and says so on the console when it finds something.
            if (context.Session is not null)
            {
                AppDialogs.DismissIfOpen(context.Session);
            }

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

                // Task 10: the steps recorded before the throw are kept and the abort is appended
                // to them. This catch used to replace the lot, so an abort erased everything the
                // phase had already proved — and, in the run that prompted this, erased the two
                // failed steps that actually explained why it went on to abort.
                List<StepResult> recorded = new List<StepResult>();

                if (context.RecordedSteps is not null)
                {
                    recorded.AddRange(context.RecordedSteps.Steps);
                }

                failedStep.Duration = stopwatch.Elapsed;
                failedStep.CompletedAt = DateTime.Now;

                recorded.Add(failedStep);

                steps = recorded;
                aborted = true;
            }

            stopwatch.Stop();

            PhaseResult result = new PhaseResult(phase.Name, startedAt, stopwatch.Elapsed, aborted, steps);

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
