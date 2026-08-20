using NetworkMonitor.UITests.Runner;

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
