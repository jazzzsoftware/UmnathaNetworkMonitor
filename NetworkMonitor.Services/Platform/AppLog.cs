using System;
using System.IO;
using NetworkMonitor.Services.Data;

namespace NetworkMonitor.Services.Platform
{
    public static class AppLog
    {
        private const int RetentionDays = 7;

        private static readonly object _gate = new object();

        public static string LogDirectory => Path.Combine(AppPaths.AppDataFolder, "Logs");

        public static bool IsEnabled
        {
            get;
            set;
        }

        public static void Initialize(bool enabled)
        {
            IsEnabled = enabled;

            if (enabled)
            {
                PurgeOldLogs();
            }

        }

        public static void Info(string message)
        {
            Write($"[INFO]  {message}");
        }

        public static void Error(string context, Exception exception)
        {

            if (exception is not null)
            {
                string detail = $"[ERROR] [{context}] {exception.GetType().FullName}: {exception.Message}{Environment.NewLine}{exception.StackTrace}";

                Write(detail);
            }

        }

        private static void Write(string body)
        {

            if (IsEnabled)
            {

                try
                {
                    Directory.CreateDirectory(LogDirectory);
                    string path = Path.Combine(LogDirectory, $"Log-{DateTime.Now:yyyyMMdd}.txt");
                    string entry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {body}{Environment.NewLine}";

                    lock (_gate)
                    {
                        File.AppendAllText(path, entry);
                    }

                }
                catch (Exception)
                {
                }

            }

        }

        private static void PurgeOldLogs()
        {

            try
            {

                if (Directory.Exists(LogDirectory))
                {
                    DateTime cutoff = DateTime.Now.AddDays(-RetentionDays);

                    foreach (string file in Directory.GetFiles(LogDirectory, "Log-*.txt"))
                    {

                        if (File.GetLastWriteTime(file) < cutoff)
                        {
                            File.Delete(file);
                        }

                    }

                }

            }
            catch (Exception)
            {
            }

        }
    }
}
