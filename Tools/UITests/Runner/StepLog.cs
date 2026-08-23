using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using NetworkMonitor.UITests.Evidence;

namespace NetworkMonitor.UITests.Runner
{
    // A phase's list of step results that captures evidence the moment a failure is recorded.
    //
    // Task 10 (2026-08-21): before this, README.md and the report both promised screenshots and
    // tree dumps "on step failure", and neither existed. The only code that filled those fields
    // was PhaseRunner.CaptureAbortEvidence — a phase that *threw* — and the self-test's fabricated
    // failure. A step that failed an assertion inside a phase that ran to completion produced
    // nothing, which is the ordinary case: Task 9's one real failure had to be diagnosed by
    // driving the app by hand afterwards.
    //
    // There is deliberately no AddRange. It existed until a phase helper collected its steps into
    // a plain List and handed the whole batch over afterwards: capture then ran at log time rather
    // than failure time, so the By-device lens failures were photographed after the phase had
    // already switched the lens back - evidence that looked authoritative and pointed the wrong
    // way. Helpers take a StepLog and Add as they go, which keeps Add and the assertion the same
    // moment.
    //
    // Capture happens in Add, not at the end of the phase, deliberately. A screenshot taken after
    // the phase finished would show a screen the failure did not happen on — worse than no
    // evidence, because it looks like evidence. Everything here is best-effort and never throws:
    // a failed capture must not turn a recorded assertion failure into a phase abort.
    public sealed class StepLog
    {
        private readonly List<StepResult> _steps = new List<StepResult>();
        private readonly PhaseContext _context;

        // Restarted on every Add, so each step's Duration covers the driving and waiting that
        // produced it rather than only the assertion at the end of that work.
        private readonly System.Diagnostics.Stopwatch _sinceLastStep = System.Diagnostics.Stopwatch.StartNew();

        public StepLog(PhaseContext context)
        {
            _context = context;

            // Registered on the context so PhaseRunner can still report the steps a phase recorded
            // if that phase later throws. Before Task 10 those were lost: the catch replaced them
            // with a single "phase completed without throwing" failure, so a run that aborted
            // late showed nothing about the twenty assertions that had already passed — or, worse,
            // about the two that had just failed and explained the abort.
            context.RecordedSteps = this;
        }

        public IReadOnlyList<StepResult> Steps => _steps;

        public void Add(StepResult step)
        {
            step.Duration = _sinceLastStep.Elapsed;
            step.CompletedAt = DateTime.Now;

            _sinceLastStep.Restart();
            _steps.Add(step);

            if (step.Outcome == StepOutcome.Failed)
            {
                CaptureEvidence(step);
            }

        }

        private void CaptureEvidence(StepResult step)
        {

            try
            {
                AutomationElement? window = TryGetSessionWindow();

                if (window is not null)
                {
                    Write(window, step);
                }
                else
                {

                    using (UIA3Automation automation = new UIA3Automation())
                    {
                        AutomationElement desktop = automation.GetDesktop();

                        Write(desktop, step);
                    }

                }

            }
            catch (Exception captureFailure)
            {
                Console.WriteLine($"StepLog: could not capture evidence for '{step.Name}': {captureFailure.Message}");
            }

        }

        private void Write(AutomationElement root, StepResult step)
        {
            step.ScreenshotPath = ScreenshotWriter.Write(root, _context.ArtifactFolder, step.Name);
            step.TreeDumpPath = UiaTreeDumper.Dump(root, _context.ArtifactFolder, step.Name);
        }

        // The shell window is the useful root when it exists, but a step can fail precisely
        // because the app is gone or unreachable — in which case the desktop still shows whatever
        // replaced it (a dialog, a handler that stole focus, an empty screen), which is the thing
        // worth looking at.
        private AutomationElement? TryGetSessionWindow()
        {
            AutomationElement? window = null;

            if (_context.Session is not null)
            {

                try
                {
                    window = _context.Session.MainWindow;
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
