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
