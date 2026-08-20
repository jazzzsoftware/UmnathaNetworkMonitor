using System.Diagnostics;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using Microsoft.Data.Sqlite;
using NetworkMonitor.Core.Common;
using NetworkMonitor.UITests.Evidence;
using NetworkMonitor.UITests.Fixtures;
using NetworkMonitor.UITests.Runner;

if (args.Contains("--selftest"))
{
    int selfTestExitCode = await RunSelfTest();

    return selfTestExitCode;
}

if (args.Contains("--guard-selftest"))
{
    bool guardSelfTestPassed = await RunGuardSelfTest();
    int guardSelfTestExitCode = guardSelfTestPassed ? 0 : 1;

    return guardSelfTestExitCode;
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

// Exercises RealDataGuard.CopyAside/Restore end to end — the highest-consequence code in the
// whole suite — against a throwaway folder this function builds and seeds itself. Never the
// real %LOCALAPPDATA%\UmnathaNetworkMonitor: it uses the internal (string realFolder) overloads
// for exactly that reason. Every row count used for comparison is read independently of
// RealDataGuard's own counting code, so a shared bug in that code can't hide from this test.
static async Task<bool> RunGuardSelfTest()
{
    string rootFolder = Path.Combine(
        Path.GetTempPath(),
        "umnatha-uitests-guard-selftest",
        DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));

    string fakeRealFolder = Path.Combine(rootFolder, "fake-real");
    string fakeRealDatabasePath = Path.Combine(fakeRealFolder, "networkmonitor.db");

    Directory.CreateDirectory(fakeRealFolder);

    await SeedDatabase.BuildAsync(fakeRealDatabasePath, DateTime.UtcNow);

    Console.WriteLine();
    Console.WriteLine("=== --guard-selftest: RealDataGuard against a throwaway folder (never the real one) ===");
    Console.WriteLine($"Throwaway 'real' folder: {fakeRealFolder}");
    Console.WriteLine();

    Dictionary<string, long> originalCounts = CountRowsIndependently(fakeRealDatabasePath);
    bool allPassed = true;

    string firstBackupPath = RealDataGuard.CopyAside(fakeRealFolder);
    Dictionary<string, long> manifestCounts = ReadManifestIndependently(Path.Combine(firstBackupPath, "uitest-row-counts.txt"));

    allPassed = Check(
        "CopyAside's manifest matches the live database's row counts, captured before copying",
        DictionariesEqual(originalCounts, manifestCounts)) && allPassed;

    File.Delete(Path.Combine(firstBackupPath, "uitest-row-counts.txt"));

    bool corruptRestoreResult = RealDataGuard.Restore(firstBackupPath, fakeRealFolder);
    Dictionary<string, long> countsAfterCorruptRestore = CountRowsIndependently(fakeRealDatabasePath);

    allPassed = Check("Restore refuses a backup with a missing manifest", !corruptRestoreResult) && allPassed;
    allPassed = Check(
        "A refused restore left the target's row counts unchanged",
        DictionariesEqual(originalCounts, countsAfterCorruptRestore)) && allPassed;
    allPassed = Check("A refused restore left the target folder in place", Directory.Exists(fakeRealFolder)) && allPassed;

    string secondBackupPath = RealDataGuard.CopyAside(fakeRealFolder);

    Directory.Delete(fakeRealFolder, true);

    bool cleanRestoreResult = RealDataGuard.Restore(secondBackupPath, fakeRealFolder);
    Dictionary<string, long> countsAfterCleanRestore = CountRowsIndependently(fakeRealDatabasePath);

    allPassed = Check("Restore succeeds against a valid backup after the target was lost entirely", cleanRestoreResult) && allPassed;
    allPassed = Check("Restored row counts match the original", DictionariesEqual(originalCounts, countsAfterCleanRestore)) && allPassed;
    allPassed = Check("Restore deleted the backup only after a successful, verified restore", !Directory.Exists(secondBackupPath)) && allPassed;

    Dictionary<string, long> countsBeforeEmptyPathRestore = CountRowsIndependently(fakeRealDatabasePath);
    bool emptyPathRestoreResult = RealDataGuard.Restore(string.Empty, fakeRealFolder);
    Dictionary<string, long> countsAfterEmptyPathRestore = CountRowsIndependently(fakeRealDatabasePath);

    allPassed = Check("Restore(string.Empty) refuses", !emptyPathRestoreResult) && allPassed;
    allPassed = Check(
        "Restore(string.Empty) did not touch the target",
        DictionariesEqual(countsBeforeEmptyPathRestore, countsAfterEmptyPathRestore)) && allPassed;

    string nonExistentBackupPath = Path.Combine(rootFolder, "does-not-exist");
    Dictionary<string, long> countsBeforeMissingPathRestore = CountRowsIndependently(fakeRealDatabasePath);
    bool missingPathRestoreResult = RealDataGuard.Restore(nonExistentBackupPath, fakeRealFolder);
    Dictionary<string, long> countsAfterMissingPathRestore = CountRowsIndependently(fakeRealDatabasePath);

    allPassed = Check("Restore(<non-existent path>) refuses", !missingPathRestoreResult) && allPassed;
    allPassed = Check(
        "Restore(<non-existent path>) did not touch the target",
        DictionariesEqual(countsBeforeMissingPathRestore, countsAfterMissingPathRestore)) && allPassed;

    // Coverage gap 1: a backup whose manifest is present and parses, but whose recorded counts
    // disagree with what is actually in it — the exact case the manifest exists to catch. This
    // never happened above: every earlier refusal was a missing/empty/non-existent path, not a
    // tampered one.
    string tamperedBackupPath = RealDataGuard.CopyAside(fakeRealFolder);
    string tamperedManifestPath = Path.Combine(tamperedBackupPath, "uitest-row-counts.txt");
    List<string> tamperedManifestLines = File.ReadAllLines(tamperedManifestPath).ToList();

    for (int lineIndex = 0; lineIndex < tamperedManifestLines.Count; lineIndex++)
    {

        if (tamperedManifestLines[lineIndex].StartsWith("Devices=", StringComparison.Ordinal))
        {
            tamperedManifestLines[lineIndex] = "Devices=999999";
        }

    }

    File.WriteAllLines(tamperedManifestPath, tamperedManifestLines);

    Dictionary<string, long> countsBeforeTamperedRestore = CountRowsIndependently(fakeRealDatabasePath);
    bool tamperedRestoreResult = RealDataGuard.Restore(tamperedBackupPath, fakeRealFolder);
    Dictionary<string, long> countsAfterTamperedRestore = CountRowsIndependently(fakeRealDatabasePath);

    allPassed = Check(
        "Restore refuses when the manifest disagrees with the backup's actual contents",
        !tamperedRestoreResult) && allPassed;
    allPassed = Check(
        "A count-mismatch refusal left the target's row counts unchanged",
        DictionariesEqual(countsBeforeTamperedRestore, countsAfterTamperedRestore)) && allPassed;
    allPassed = Check("A count-mismatch refusal left the target folder in place", Directory.Exists(fakeRealFolder)) && allPassed;

    // Coverage gap 2: force the second Directory.Move inside SwapInStagedFolder to fail after the
    // first one (real -> displaced) has already happened, and confirm the rollback puts the
    // original back intact. Calls SwapInStagedFolder directly — a seam that exists for exactly
    // this — rather than the full Restore pipeline, which is already covered above.
    string swapTestFolder = Path.Combine(rootFolder, "swap-test");
    string swapTestDatabasePath = Path.Combine(swapTestFolder, "networkmonitor.db");

    Directory.CreateDirectory(swapTestFolder);
    await SeedDatabase.BuildAsync(swapTestDatabasePath, DateTime.UtcNow);

    Dictionary<string, long> swapOriginalCounts = CountRowsIndependently(swapTestDatabasePath);
    string swapStagingFolder = swapTestFolder + ".uitest-restore-staging-selftest";

    Directory.CreateDirectory(swapStagingFolder);
    File.WriteAllText(Path.Combine(swapStagingFolder, "marker.txt"), "this staged copy must never end up in place");

    bool swapThrew = false;

    try
    {
        RealDataGuard.SwapInStagedFolder(swapTestFolder, swapStagingFolder, () => Directory.Delete(swapStagingFolder, true));
    }
    catch (Exception)
    {
        swapThrew = true;
    }

    Dictionary<string, long> swapCountsAfterRollback = CountRowsIndependently(swapTestDatabasePath);

    allPassed = Check("SwapInStagedFolder throws when the second move fails partway through", swapThrew) && allPassed;
    allPassed = Check(
        "Rollback restored the original folder's contents after the swap failed",
        DictionariesEqual(swapOriginalCounts, swapCountsAfterRollback)) && allPassed;
    allPassed = Check(
        "Rollback left no marker file from the staged copy that never got swapped in",
        !File.Exists(Path.Combine(swapTestFolder, "marker.txt"))) && allPassed;

    Console.WriteLine();
    Console.WriteLine(allPassed ? "guard-selftest: ALL CHECKS PASSED" : "guard-selftest: SOME CHECKS FAILED");
    Console.WriteLine();

    TryCleanUpGuardSelfTestRoot(rootFolder);

    return allPassed;
}

static void TryCleanUpGuardSelfTestRoot(string rootFolder)
{

    try
    {

        if (Directory.Exists(rootFolder))
        {
            Directory.Delete(rootFolder, true);
        }

    }
    catch (Exception exception)
    {
        Console.WriteLine(
            $"guard-selftest: could not clean up its own throwaway folder at {rootFolder} ({exception.Message}). "
            + "It is safe to delete by hand — none of it is real data.");
    }

}

static bool Check(string label, bool condition)
{
    string status = condition ? "PASS" : "FAIL";

    Console.WriteLine($"  [{status}] {label}");

    return condition;
}

// Deliberately duplicates RealDataGuard's table list and counting shape rather than calling
// back into it, so this test doesn't just confirm RealDataGuard agrees with itself.
static Dictionary<string, long> CountRowsIndependently(string databasePath)
{
    string[] tables =
    {
        "Devices",
        "ScanSessions",
        "DeviceEvents",
        "TrafficEntries",
        "TrafficRollups",
        "LocalTrafficEntries",
        "LocalTrafficRollups",
        "DigestReports",
        "SpeedTestResults"
    };

    Dictionary<string, long> counts = new Dictionary<string, long>();

    if (File.Exists(databasePath))
    {

        using (SqliteConnection connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly;Pooling=False"))
        {
            connection.Open();

            foreach (string table in tables)
            {

                using (SqliteCommand command = connection.CreateCommand())
                {
                    command.CommandText = $"SELECT COUNT(*) FROM \"{table}\";";

                    object? result = command.ExecuteScalar();

                    counts[table] = result is null ? 0 : Convert.ToInt64(result);
                }

            }

        }

    }
    else
    {

        foreach (string table in tables)
        {
            counts[table] = -1;
        }

    }

    return counts;
}

static Dictionary<string, long> ReadManifestIndependently(string manifestPath)
{
    Dictionary<string, long> counts = new Dictionary<string, long>();

    if (File.Exists(manifestPath))
    {

        foreach (string line in File.ReadAllLines(manifestPath))
        {
            string[] parts = line.Split('=');

            if (parts.Length == 2 && long.TryParse(parts[1], out long count))
            {
                counts[parts[0]] = count;
            }

        }

    }

    return counts;
}

static bool DictionariesEqual(Dictionary<string, long> first, Dictionary<string, long> second)
{
    bool equal = first.Count == second.Count;

    if (equal)
    {

        foreach (KeyValuePair<string, long> entry in first)
        {
            bool matches = second.TryGetValue(entry.Key, out long otherValue) && otherValue == entry.Value;

            if (!matches)
            {
                equal = false;
            }

        }

    }

    return equal;
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
