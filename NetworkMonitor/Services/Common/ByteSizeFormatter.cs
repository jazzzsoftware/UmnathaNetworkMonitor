namespace NetworkMonitor.Services.Common
{
    public static class ByteSizeFormatter
    {
        public static string Format(long bytes)
        {
            string result;

            if (bytes >= 1_073_741_824L)
            {
                result = $"{bytes / 1_073_741_824.0:F1} GB";
            }
            else if (bytes >= 1_048_576L)
            {
                result = $"{bytes / 1_048_576.0:F1} MB";
            }
            else if (bytes >= 1_024L)
            {
                result = $"{bytes / 1_024.0:F1} KB";
            }
            else
            {
                result = $"{bytes} B";
            }

            return result;
        }
    }
}
