namespace NetworkMonitor.Core.Update
{
    public static class UpdateDecision
    {
        public static bool IsNewer(string currentVersion, string candidateVersion)
        {
            bool isNewer = false;

            if (SemanticVersion.TryParse(currentVersion, out SemanticVersion current)
                && SemanticVersion.TryParse(candidateVersion, out SemanticVersion candidate))
            {
                isNewer = candidate.CompareTo(current) > 0;
            }

            return isNewer;
        }
    }
}
