using Xunit;
using NetworkMonitor.Core.Update;

namespace NetworkMonitor.Tests.Update
{
    public class UpdateDecisionTests
    {
        [Theory]
        [InlineData("0.0.8", "0.0.9", true)]
        [InlineData("0.0.8", "v0.0.9", true)]
        [InlineData("0.0.9", "0.0.9", false)]
        [InlineData("0.0.9", "0.0.8", false)]
        [InlineData("1.0.0", "0.9.9", false)]
        public void IsNewerComparesVersions(string current, string candidate, bool expected)
        {
            bool actual = UpdateDecision.IsNewer(current, candidate);

            Assert.Equal(expected, actual);
        }

        [Theory]
        [InlineData("garbage", "0.0.9")]
        [InlineData("0.0.8", "garbage")]
        public void IsNewerReturnsFalseWhenEitherVersionUnparseable(string current, string candidate)
        {
            bool actual = UpdateDecision.IsNewer(current, candidate);

            Assert.False(actual);
        }
    }
}
