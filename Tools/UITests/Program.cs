using System.Diagnostics;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using NetworkMonitor.UITests.Evidence;
using NetworkMonitor.UITests.Runner;

if (args.Contains("--selftest"))
{
    int selfTestExitCode = RunSelfTest();

    return selfTestExitCode;
}

PreflightResult preflight = Preflight.Check();

if (!preflight.Ready)
{
    Console.WriteLine("Preflight failed. The suite did not start:");
    Console.WriteLine();

    foreach (string blocker in preflight.Blockers)
    {
        Console.WriteLine($"  - {blocker}");
    }

    Console.WriteLine();

    return 2;
}

Console.WriteLine($"Preflight passed. Installed version: {Preflight.ReadInstalledVersion()}");

return 0;

// Fabricates one passed, one failed (with a real screenshot and tree dump of the desktop) and
// one skipped StepResult, then renders them through the real HtmlReport. This is how the report
// gets changed and re-checked later without paying for a full 15-minute driven run — kept as a
// standing diagnostic, not deleted once the real phases exist.
static int RunSelfTest()
{
    string artifactFolder = Path.Combine(
        Path.GetTempPath(),
        "umnatha-uitests-selftest",
        DateTime.Now.ToString("yyyyMMdd-HHmmss"));

    Directory.CreateDirectory(artifactFolder);

    StepResult passedStep = StepResult.Pass("Launches the main window");
    StepResult failedStep = BuildFailedSelfTestStep(artifactFolder);
    StepResult skippedStep = StepResult.Skip(
        "Drives the update banner",
        "Self-test does not touch the installed app.");

    List<StepResult> steps = new List<StepResult> { passedStep, failedStep, skippedStep };
    PhaseResult phase = new PhaseResult("Self-test phase", TimeSpan.FromSeconds(3), false, steps);
    List<PhaseResult> phases = new List<PhaseResult> { phase };
    RunOutcome outcome = new RunOutcome(phases, TimeSpan.FromSeconds(3));
    RunEnvironment environment = RunEnvironment.Read();

    environment.AppVersionAfter = environment.AppVersionBefore;

    string reportPath = HtmlReport.Write(outcome, environment, artifactFolder);

    Console.WriteLine($"Self-test report written to: {reportPath}");

    OpenInBrowser(reportPath);

    int exitCode = reportPath.Length > 0 ? 0 : 1;

    return exitCode;
}

static StepResult BuildFailedSelfTestStep(string artifactFolder)
{
    StepResult failedStep = StepResult.Fail(
        "Finds the Scan Now button",
        "an enabled button named 'Scan Now'",
        "no matching element");

    using (UIA3Automation automation = new UIA3Automation())
    {
        AutomationElement desktop = automation.GetDesktop();

        failedStep.ScreenshotPath = ScreenshotWriter.Write(desktop, artifactFolder, failedStep.Name);
        failedStep.TreeDumpPath = UiaTreeDumper.Dump(desktop, artifactFolder, failedStep.Name);
    }

    return failedStep;
}

static void OpenInBrowser(string reportPath)
{

    if (reportPath.Length > 0)
    {

        try
        {
            ProcessStartInfo startInfo = new ProcessStartInfo(reportPath)
            {
                UseShellExecute = true
            };

            Process.Start(startInfo);
        }
        catch (Exception failure)
        {
            Console.WriteLine($"Could not open the report automatically: {failure.Message}");
        }

    }

}
