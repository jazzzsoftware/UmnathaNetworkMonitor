namespace NetworkMonitor.Core.Csv
{
    public static class CsvField
    {
        public static string Escape(string value)
        {
            string sanitized = SanitizeFormula(value);
            string escaped = sanitized;

            if (sanitized.Contains(',') || sanitized.Contains('"') || sanitized.Contains('\n') || sanitized.Contains('\r'))
            {
                escaped = $"\"{sanitized.Replace("\"", "\"\"")}\"";
            }

            return escaped;
        }

        private static string SanitizeFormula(string value)
        {
            string sanitized = value;

            if (value.Length > 0 && (value[0] == '=' || value[0] == '+' || value[0] == '-' || value[0] == '@'))
            {
                sanitized = $"'{value}";
            }

            return sanitized;
        }
    }
}
