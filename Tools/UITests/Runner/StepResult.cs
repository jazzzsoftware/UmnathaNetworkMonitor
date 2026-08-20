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
