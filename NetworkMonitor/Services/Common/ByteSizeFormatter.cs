namespace NetworkMonitor.Services.Common
{
    public static class ByteSizeFormatter
    {
        public static string Format(long bytes)
        {
            string result;

            if (bytes >= 1_000_000_000L)
            {
                result = $"{bytes / 1_000_000_000.0:F1} GB";
            }
            else if (bytes >= 1_000_000L)
            {
                result = $"{bytes / 1_000_000.0:F1} MB";
            }
            else if (bytes >= 1_000L)
            {
                result = $"{bytes / 1_000.0:F1} KB";
            }
            else
            {
                result = $"{bytes} B";
            }

            return result;
        }
    }
}
