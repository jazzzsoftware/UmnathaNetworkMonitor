namespace NetworkMonitor.Models.Update
{
    public record UpdateCheckResult(
        UpdateAvailability Availability,
        AvailableUpdate? Update,
        string? ErrorMessage)
    {
        public static UpdateCheckResult UpToDate()
        {
            UpdateCheckResult result = new UpdateCheckResult(UpdateAvailability.UpToDate, null, null);

            return result;
        }

        public static UpdateCheckResult Available(AvailableUpdate update)
        {
            UpdateCheckResult result = new UpdateCheckResult(UpdateAvailability.UpdateAvailable, update, null);

            return result;
        }

        public static UpdateCheckResult Failed(string errorMessage)
        {
            UpdateCheckResult result = new UpdateCheckResult(UpdateAvailability.CheckFailed, null, errorMessage);

            return result;
        }
    }
}
