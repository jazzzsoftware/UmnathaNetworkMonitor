using Xunit;
using NetworkMonitor.Core.Update;
using NetworkMonitor.Models.Update;

namespace NetworkMonitor.Tests.Update
{
    public class ReleaseInfoParserTests
    {
        private const string ValidJson = """
        {
          "tag_name": "v0.0.9",
          "assets": [
            { "name": "Umnatha.Network.Monitor.v0.0.9.exe", "browser_download_url": "https://example/app.exe", "size": 15000000 },
            { "name": "Umnatha.Network.Monitor.v0.0.9.exe.sha256", "browser_download_url": "https://example/app.exe.sha256", "size": 64 }
          ]
        }
        """;

        [Fact]
        public void ParseReadsTagVersionAndAssetUrls()
        {
            AvailableUpdate? update = ReleaseInfoParser.Parse(ValidJson);

            Assert.NotNull(update);
            Assert.Equal("v0.0.9", update!.VersionTag);
            Assert.Equal("0.0.9", update.NormalizedVersion);
            Assert.Equal("https://example/app.exe", update.InstallerUrl);
            Assert.Equal("https://example/app.exe.sha256", update.ChecksumUrl);
            Assert.Equal(15000000, update.SizeBytes);
        }

        [Fact]
        public void ParseReturnsNullWhenExeAssetMissing()
        {
            string json = """
            { "tag_name": "v0.0.9", "assets": [ { "name": "notes.txt", "browser_download_url": "https://x/notes.txt", "size": 1 } ] }
            """;

            AvailableUpdate? update = ReleaseInfoParser.Parse(json);

            Assert.Null(update);
        }

        [Fact]
        public void ParseReturnsNullWhenChecksumAssetMissing()
        {
            string json = """
            { "tag_name": "v0.0.9", "assets": [ { "name": "app.exe", "browser_download_url": "https://x/app.exe", "size": 10 } ] }
            """;

            AvailableUpdate? update = ReleaseInfoParser.Parse(json);

            Assert.Null(update);
        }

        [Theory]
        [InlineData("")]
        [InlineData("not json")]
        [InlineData("{ \"assets\": [] }")]
        public void ParseReturnsNullForBadInput(string json)
        {
            AvailableUpdate? update = ReleaseInfoParser.Parse(json);

            Assert.Null(update);
        }

        [Fact]
        public void TryParseVersionTagReadsTagFromCompleteRelease()
        {
            bool parsed = ReleaseInfoParser.TryParseVersionTag(ValidJson, out string versionTag);

            Assert.True(parsed);
            Assert.Equal("v0.0.9", versionTag);
        }

        [Fact]
        public void TryParseVersionTagSucceedsWhenChecksumAssetMissing()
        {
            string json = """
            { "tag_name": "v0.0.9", "assets": [ { "name": "app.exe", "browser_download_url": "https://x/app.exe", "size": 10 } ] }
            """;

            bool parsed = ReleaseInfoParser.TryParseVersionTag(json, out string versionTag);

            Assert.True(parsed);
            Assert.Equal("v0.0.9", versionTag);
        }

        [Fact]
        public void TryParseVersionTagSucceedsWhenAssetsAbsentEntirely()
        {
            bool parsed = ReleaseInfoParser.TryParseVersionTag("{ \"tag_name\": \"v0.0.9\" }", out string versionTag);

            Assert.True(parsed);
            Assert.Equal("v0.0.9", versionTag);
        }

        [Theory]
        [InlineData("")]
        [InlineData("not json")]
        [InlineData("[]")]
        [InlineData("{ \"assets\": [] }")]
        [InlineData("{ \"tag_name\": \"\" }")]
        [InlineData("{ \"tag_name\": \"garbage\" }")]
        public void TryParseVersionTagFailsForUnusableInput(string json)
        {
            bool parsed = ReleaseInfoParser.TryParseVersionTag(json, out string versionTag);

            Assert.False(parsed);
            Assert.Equal(string.Empty, versionTag);
        }
    }
}
