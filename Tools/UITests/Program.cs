using System.Diagnostics;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using NetworkMonitor.Core.Common;
using NetworkMonitor.UITests.Evidence;
using NetworkMonitor.UITests.Fixtures;
using NetworkMonitor.UITests.Runner;

if (args.Contains("--selftest"))
{
    int selfTestExitCode = await RunSelfTest();

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
// standing diagnostic, not deleted once the real phases exist. It also builds a fixture data
// folder and proves the one claim the rest of the suite depends on: seeding it never touches the
// operator's real database.
static async Task<int> RunSelfTest()
{
    string artifactFolder = Path.Combine(
        Path.GetTempPath(),
        "umnatha-uitests-selftest",
        DateTime.Now.ToString("yyyyMMdd-HHmmss"));

    Directory.CreateDirectory(artifactFolder);

    bool realDatabaseUntouched = await ProveFixtureBuildsAndRealDatabaseIsUntouched();

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

    int exitCode = reportPath.Length > 0 && realDatabaseUntouched ? 0 : 1;

    return exitCode;
}

// Reads the real database's LastWriteTimeUtc before and after seeding a throwaway fixture, and
// prints both. This is the claim RealDataGuard and every later phase rest on, so it is checked
// explicitly rather than assumed from "SeedDatabase only ever opens dbPath".
static async Task<bool> ProveFixtureBuildsAndRealDatabaseIsUntouched()
{
    string realDatabasePath = RealDatabasePath();
    DateTime? beforeTimestamp = ReadLastWriteTimeUtcIfExists(realDatabasePath);

    Console.WriteLine();
    Console.WriteLine($"Real database path: {realDatabasePath}");
    Console.WriteLine($"Real database LastWriteTimeUtc before: {DescribeTimestamp(beforeTimestamp)}");

    DataFolderFixture fixture = await DataFolderFixture.CreateAsync();

    Console.WriteLine();
    Console.WriteLine($"Fixture data folder: {fixture.FolderPath}");
    Console.WriteLine("Seeded counts:");
    Console.WriteLine($"  KnownDevices:      {fixture.Counts.KnownDevices}");
    Console.WriteLine($"  ApprovedDevices:   {fixture.Counts.ApprovedDevices}");
    Console.WriteLine($"  UnapprovedDevices: {fixture.Counts.UnapprovedDevices}");
    Console.WriteLine($"  DeviceEvents:      {fixture.Counts.DeviceEvents}");
    Console.WriteLine($"  SpeedTestResults:  {fixture.Counts.SpeedTestResults}");
    Console.WriteLine($"  DigestReports:     {fixture.Counts.DigestReports}");

    DateTime? afterTimestamp = ReadLastWriteTimeUtcIfExists(realDatabasePath);
    bool unchanged = beforeTimestamp == afterTimestamp;

    Console.WriteLine();
    Console.WriteLine($"Real database LastWriteTimeUtc after:  {DescribeTimestamp(afterTimestamp)}");
    Console.WriteLine(unchanged ? "Real database untouched: PASS" : "Real database untouched: FAIL");
    Console.WriteLine();

    return unchanged;
}

static string RealDatabasePath()
{
    string localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    string realFolder = AppDataFolderResolver.Resolve(null, localApplicationData);
    string databasePath = Path.Combine(realFolder, "networkmonitor.db");

    return databasePath;
}

static DateTime? ReadLastWriteTimeUtcIfExists(string path)
{
    DateTime? timestamp = null;

    if (File.Exists(path))
    {
        timestamp = File.GetLastWriteTimeUtc(path);
    }

    return timestamp;
}

static string DescribeTimestamp(DateTime? timestamp)
{
    string description = timestamp.HasValue
        ? timestamp.Value.ToString("O")
        : "(file does not exist — expected if the app has never run on this machine)";

    return description;
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
