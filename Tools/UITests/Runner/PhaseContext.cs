using NetworkMonitor.UITests.Driving;
using NetworkMonitor.UITests.Fixtures;

namespace NetworkMonitor.UITests.Runner
{
    public sealed class PhaseContext
    {
        public PhaseContext(string dataFolder, string artifactFolder, SeedCounts seed)
        {
            DataFolder = dataFolder;
            ArtifactFolder = artifactFolder;
            Seed = seed;
        }

        public AppSession? Session
        {
            get;
            set;
        }

        // Set by StepLog's constructor, so PhaseRunner can recover what a phase had already
        // recorded when that phase throws rather than returns.
        public StepLog? RecordedSteps
        {
            get;
            set;
        }

        public string DataFolder
        {
            get;
        }

        public string ArtifactFolder
        {
            get;
        }

        public SeedCounts Seed
        {
            get;
        }
    }
}
