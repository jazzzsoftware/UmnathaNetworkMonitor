namespace NetworkMonitor.UITests.Runner
{
    public sealed class StepResult
    {
        private StepResult(StepOutcome outcome, string name, string message)
        {
            Outcome = outcome;
            Name = name;
            Message = message;
        }

        public StepOutcome Outcome
        {
            get;
        }

        public string Name
        {
            get;
        }

        public string Message
        {
            get;
        }

        // When this step was recorded, and how long the work behind it took — measured from the
        // moment the previous step in the same phase was recorded, so it covers the driving as
        // well as the assertion. Every step is recorded as it happens, so this timing is the real
        // cost of that step rather than a share of a batch total.
        public DateTime CompletedAt
        {
            get;
            set;
        }

        public TimeSpan Duration
        {
            get;
            set;
        }

        public string ScreenshotPath
        {
            get;
            set;
        } = string.Empty;

        public string TreeDumpPath
        {
            get;
            set;
        } = string.Empty;

        public static StepResult Pass(string name)
        {
            StepResult result = new StepResult(StepOutcome.Passed, name, string.Empty);

            return result;
        }

        public static StepResult Fail(string name, string expected, string actual)
        {
            string message = $"Expected: {expected}\nActual:   {actual}";

            StepResult result = new StepResult(StepOutcome.Failed, name, message);

            return result;
        }

        public static StepResult Skip(string name, string why)
        {
            StepResult result = new StepResult(StepOutcome.Skipped, name, why);

            return result;
        }
    }
}
