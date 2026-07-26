using Xunit;
using NetworkMonitor.Models.Update;

namespace NetworkMonitor.Tests.Update
{
    public class UpdateCheckResultTests
    {
        [Fact]
        public void AvailableCarriesUpdateAndSetsAvailability()
        {
            AvailableUpdate update = new AvailableUpdate("v0.0.9", "0.0.9", "https://x/app.exe", "https://x/app.exe.sha256", 123);

            UpdateCheckResult result = UpdateCheckResult.Available(update);

            Assert.Equal(UpdateAvailability.UpdateAvailable, result.Availability);
            Assert.Same(update, result.Update);
            Assert.Null(result.ErrorMessage);
        }

        [Fact]
        public void UpToDateHasNoUpdateOrError()
        {
            UpdateCheckResult result = UpdateCheckResult.UpToDate();

            Assert.Equal(UpdateAvailability.UpToDate, result.Availability);
            Assert.Null(result.Update);
            Assert.Null(result.ErrorMessage);
        }

        [Fact]
        public void FailedCarriesMessage()
        {
            UpdateCheckResult result = UpdateCheckResult.Failed("no network");

            Assert.Equal(UpdateAvailability.CheckFailed, result.Availability);
            Assert.Equal("no network", result.ErrorMessage);
        }
    }
}
