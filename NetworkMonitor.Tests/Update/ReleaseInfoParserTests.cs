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
        public void ParsePairsTheChecksumPublishedForTheChosenInstaller()
        {
            string json = """
            {
              "tag_name": "v1.2.3",
              "assets": [
                { "name": "Umnatha.Setup.v1.2.3.exe", "browser_download_url": "https://example/setup.exe", "size": 900 },
                { "name": "Umnatha.Portable.v1.2.3.exe.sha256", "browser_download_url": "https://example/portable.sha256", "size": 64 },
                { "name": "Umnatha.Setup.v1.2.3.exe.sha256", "browser_download_url": "https://example/setup.sha256", "size": 64 }
              ]
            }
            """;

            AvailableUpdate? update = ReleaseInfoParser.Parse(json);

            Assert.NotNull(update);
            Assert.Equal("https://example/setup.exe", update!.InstallerUrl);
            Assert.Equal("https://example/setup.sha256", update.ChecksumUrl);
        }

        [Fact]
        public void ParseFallsBackToTheOnlyChecksumWhenNamesDoNotMatch()
        {
            string json = """
            {
              "tag_name": "v1.2.3",
              "assets": [
                { "name": "Setup.exe", "browser_download_url": "https://example/setup.exe", "size": 900 },
                { "name": "checksums.sha256", "browser_download_url": "https://example/checksums.sha256", "size": 64 }
              ]
            }
            """;

            AvailableUpdate? update = ReleaseInfoParser.Parse(json);

            Assert.NotNull(update);
            Assert.Equal("https://example/checksums.sha256", update!.ChecksumUrl);
        }

        [Fact]
        public void ParseReturnsNullWhenNoChecksumMatchesAmongSeveral()
        {
            string json = """
            {
              "tag_name": "v1.2.3",
              "assets": [
                { "name": "Setup.exe", "browser_download_url": "https://example/setup.exe", "size": 900 },
                { "name": "Other.exe.sha256", "browser_download_url": "https://example/other.sha256", "size": 64 },
                { "name": "Third.exe.sha256", "browser_download_url": "https://example/third.sha256", "size": 64 }
              ]
            }
            """;

            AvailableUpdate? update = ReleaseInfoParser.Parse(json);

            Assert.Null(update);
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
