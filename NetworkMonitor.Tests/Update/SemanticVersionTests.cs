using Xunit;
using NetworkMonitor.Core.Update;

namespace NetworkMonitor.Tests.Update
{
    public class SemanticVersionTests
    {
        [Theory]
        [InlineData("0.0.9", 0, 0, 9)]
        [InlineData("v0.0.9", 0, 0, 9)]
        [InlineData("V1.2.3", 1, 2, 3)]
        [InlineData("1.2", 1, 2, 0)]
        [InlineData("2", 2, 0, 0)]
        [InlineData("1.2.3-beta.1", 1, 2, 3)]
        public void TryParseParsesValidVersions(string text, int major, int minor, int patch)
        {
            bool parsed = SemanticVersion.TryParse(text, out SemanticVersion version);

            Assert.True(parsed);
            Assert.Equal(major, version.Major);
            Assert.Equal(minor, version.Minor);
            Assert.Equal(patch, version.Patch);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("abc")]
        [InlineData("1.x.3")]
        [InlineData("v")]
        public void TryParseRejectsInvalidVersions(string text)
        {
            bool parsed = SemanticVersion.TryParse(text, out SemanticVersion version);

            Assert.False(parsed);
        }

        [Fact]
        public void CompareToOrdersByPrecedence()
        {
            SemanticVersion.TryParse("0.0.9", out SemanticVersion newer);
            SemanticVersion.TryParse("0.0.8", out SemanticVersion older);

            Assert.True(newer.CompareTo(older) > 0);
            Assert.True(older.CompareTo(newer) < 0);
            Assert.Equal(0, newer.CompareTo(newer));
        }

        [Fact]
        public void CompareToRanksMinorAbovePatch()
        {
            SemanticVersion.TryParse("0.1.0", out SemanticVersion minorBump);
            SemanticVersion.TryParse("0.0.99", out SemanticVersion patchOnly);

            Assert.True(minorBump.CompareTo(patchOnly) > 0);
        }
    }
}
