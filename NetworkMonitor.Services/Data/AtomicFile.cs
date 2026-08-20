using NetworkMonitor.Services.Platform;

namespace NetworkMonitor.Services.Data
{
    public static class AtomicFile
    {
        private const int MaxMoveAttempts = 5;
        private const int FirstRetryDelayMilliseconds = 20;

        // File.Move needs DELETE access on the temp file, and an on-access antivirus scanner
        // (Kaspersky and Defender both do this) opens a file the moment it is closed to scan it.
        // A write landing in that window is refused with ERROR_ACCESS_DENIED for a few milliseconds
        // and then succeeds, so a single attempt threw the whole save away. Backing off 20/40/80/160ms
        // covers the scan, and the caller is told whether the file was actually published.
        public static bool WriteAllText(string path, string contents)
        {
            string tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            bool written = false;
            int refusedMoves = 0;
            Exception? failure = null;

            try
            {
                string directory = Path.GetDirectoryName(path)!;

                Directory.CreateDirectory(directory);

                File.WriteAllText(tempPath, contents);

                int retryDelayMilliseconds = FirstRetryDelayMilliseconds;

                for (int attempt = 1; attempt <= MaxMoveAttempts && !written; attempt++)
                {

                    try
                    {
                        File.Move(tempPath, path, true);
                        written = true;
                        failure = null;
                    }
                    catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
                    {
                        failure = exception;
                        refusedMoves = attempt;

                        if (attempt < MaxMoveAttempts)
                        {
                            Thread.Sleep(retryDelayMilliseconds);
                            retryDelayMilliseconds *= 2;
                        }

                    }

                }

            }
            catch (Exception exception)
            {
                failure = exception;
            }

            if (failure is not null)
            {
                string context = $"AtomicFile.WriteAllText ({path} not written)";

                if (refusedMoves > 0)
                {
                    context = $"AtomicFile.WriteAllText ({path} not written; the move was refused {refusedMoves} times)";
                }

                AppLog.Error(context, failure);
            }

            if (!written)
            {

                try
                {
                    File.Delete(tempPath);
                }
                catch (Exception cleanupException)
                {
                    AppLog.Error("AtomicFile.WriteAllText cleanup", cleanupException);
                }

            }

            return written;
        }
    }
}
