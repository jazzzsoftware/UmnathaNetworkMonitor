using NetworkMonitor.Services.Platform;

namespace NetworkMonitor.Services.Data
{
    public static class AtomicFile
    {
        public static void WriteAllText(string path, string contents)
        {
            string tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";

            try
            {
                string directory = Path.GetDirectoryName(path)!;

                Directory.CreateDirectory(directory);

                File.WriteAllText(tempPath, contents);
                File.Move(tempPath, path, true);
            }
            catch (Exception exception)
            {
                AppLog.Error("AtomicFile.WriteAllText", exception);

                try
                {
                    File.Delete(tempPath);
                }
                catch (Exception cleanupException)
                {
                    AppLog.Error("AtomicFile.WriteAllText cleanup", cleanupException);
                }

            }

        }
    }
}
