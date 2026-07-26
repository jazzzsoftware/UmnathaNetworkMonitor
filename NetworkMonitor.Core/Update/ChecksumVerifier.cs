using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace NetworkMonitor.Core.Update
{
    public static class ChecksumVerifier
    {
        public static string ParseHashFromChecksumFile(string content)
        {
            string hash = string.Empty;

            if (!string.IsNullOrWhiteSpace(content))
            {
                string[] tokens = content.Trim().Split(
                    new[] { ' ', '\t', '\r', '\n', '*' },
                    StringSplitOptions.RemoveEmptyEntries);

                if (tokens.Length > 0)
                {
                    hash = tokens[0].ToLowerInvariant();
                }

            }

            return hash;
        }

        public static bool Verify(string expectedHashHex, string actualHashHex)
        {
            bool verified = false;

            if (!string.IsNullOrWhiteSpace(expectedHashHex) && !string.IsNullOrWhiteSpace(actualHashHex))
            {
                verified = string.Equals(
                    expectedHashHex.Trim(),
                    actualHashHex.Trim(),
                    StringComparison.OrdinalIgnoreCase);
            }

            return verified;
        }

        public static async Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken)
        {
            await using FileStream stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);

            using SHA256 sha256 = SHA256.Create();
            byte[] hashBytes = await sha256.ComputeHashAsync(stream, cancellationToken);
            string hash = Convert.ToHexString(hashBytes).ToLowerInvariant();

            return hash;
        }
    }
}
