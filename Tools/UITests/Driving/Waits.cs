namespace NetworkMonitor.UITests.Driving
{
    public static class Waits
    {
        // The one delay in the whole suite: the interval between condition polls inside Until.
        // No other file may call Thread.Sleep as a synchronisation device — every wait routes
        // through here so the flake policy has exactly one place to audit.
        private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

        public static void Until(Func<bool> condition, TimeSpan timeout, string whatWeWereWaitingFor)
        {
            DateTime deadline = DateTime.UtcNow + timeout;

            bool satisfied = false;

            while (DateTime.UtcNow < deadline)
            {

                if (condition())
                {
                    satisfied = true;

                    break;
                }

                Thread.Sleep(PollInterval);
            }

            if (!satisfied)
            {
                throw new TimeoutException(
                    $"Waited {timeout.TotalSeconds:0.#}s for {whatWeWereWaitingFor} and it never happened.");
            }

        }

        public static TFound UntilFound<TFound>(Func<TFound?> find, TimeSpan timeout, string whatWeWereLookingFor)
            where TFound : class
        {
            TFound? found = null;

            Until(
                () =>
                {
                    found = find();

                    bool present = found is not null;

                    return present;
                },
                timeout,
                whatWeWereLookingFor);

            return found!;
        }
    }
}
