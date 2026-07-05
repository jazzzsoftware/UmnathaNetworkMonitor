namespace NetworkMonitor.Services.Scanning
{
    public static class MacNormalizer
    {
        public static string Normalize(string mac)
        {
            string normalized = mac.Trim().Replace("-", ":").ToUpperInvariant();

            return normalized;
        }

        public static bool IsRandomized(string mac)
        {
            bool randomized = false;
            string normalized = Normalize(mac);

            if (normalized.Length >= 2
                && byte.TryParse(normalized.AsSpan(0, 2), System.Globalization.NumberStyles.HexNumber, null, out byte firstOctet))
            {
                randomized = (firstOctet & 0x02) != 0 && (firstOctet & 0x01) == 0;
            }

            return randomized;
        }
    }
}
