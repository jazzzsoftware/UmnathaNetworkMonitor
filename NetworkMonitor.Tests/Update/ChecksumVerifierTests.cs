using System.IO;
using System.Threading.Tasks;
using Xunit;
using NetworkMonitor.Core.Update;

namespace NetworkMonitor.Tests.Update
{
    public class ChecksumVerifierTests
    {
        [Theory]
        [InlineData("ABC123", "abc123")]
        [InlineData("abc123  *Umnatha.Network.Monitor.v0.0.9.exe", "abc123")]
        [InlineData("  deadBEEF \n", "deadbeef")]
        [InlineData("", "")]
        public void ParseHashExtractsLeadingHexTokenLowerCased(string content, string expected)
        {
            string actual = ChecksumVerifier.ParseHashFromChecksumFile(content);

            Assert.Equal(expected, actual);
        }

        [Fact]
        public void VerifyIsCaseInsensitive()
        {
            Assert.True(ChecksumVerifier.Verify("ABC123", "abc123"));
            Assert.False(ChecksumVerifier.Verify("abc123", "abc124"));
            Assert.False(ChecksumVerifier.Verify("", "abc123"));
        }

        [Fact]
        public async Task ComputeSha256MatchesKnownHashOfEmptyFile()
        {
            string path = Path.Combine(Path.GetTempPath(), $"nm-update-test-{System.Guid.NewGuid():N}.bin");
            await File.WriteAllBytesAsync(path, System.Array.Empty<byte>(), TestContext.Current.CancellationToken);

            try
            {
                string hash = await ChecksumVerifier.ComputeSha256Async(path, TestContext.Current.CancellationToken);

                Assert.Equal("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", hash);
            }
            finally
            {
                File.Delete(path);
            }

        }
    }
}
