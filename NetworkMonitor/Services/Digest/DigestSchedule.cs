namespace NetworkMonitor.Services.Digest
{
    public static class DigestSchedule
    {
        public static DateTime NextRunLocal(DateTime nowLocal, int generationHour)
        {
            DateTime todayRun = new DateTime(nowLocal.Year, nowLocal.Month, nowLocal.Day, generationHour, 0, 0, DateTimeKind.Local);
            DateTime next = nowLocal < todayRun ? todayRun : todayRun.AddDays(1);

            return next;
        }

        public static List<(DateTime StartUtc, DateTime EndUtc)> MissedWindows(
            DateTime? lastPeriodEndUtc, DateTime nowLocal, int generationHour, int retentionDays)
        {
            List<(DateTime StartUtc, DateTime EndUtc)> windows = new();
            DateTime todayRun = new DateTime(nowLocal.Year, nowLocal.Month, nowLocal.Day, generationHour, 0, 0, DateTimeKind.Local);
            DateTime mostRecentBoundaryLocal = nowLocal >= todayRun ? todayRun : todayRun.AddDays(-1);
            DateTime earliestBoundaryLocal = mostRecentBoundaryLocal.AddDays(-(retentionDays - 1));
            DateTime cursorEndLocal;

            if (lastPeriodEndUtc is null)
            {
                cursorEndLocal = mostRecentBoundaryLocal;
            }
            else
            {
                DateTime lastEndLocal = lastPeriodEndUtc.Value.ToLocalTime();
                cursorEndLocal = lastEndLocal.AddDays(1);
            }

            if (cursorEndLocal < earliestBoundaryLocal)
            {
                cursorEndLocal = earliestBoundaryLocal;
            }

            while (cursorEndLocal <= mostRecentBoundaryLocal)
            {
                DateTime startUtc = cursorEndLocal.AddDays(-1).ToUniversalTime();
                DateTime endUtc = cursorEndLocal.ToUniversalTime();
                windows.Add((startUtc, endUtc));
                cursorEndLocal = cursorEndLocal.AddDays(1);
            }

            return windows;
        }
    }
}
