using NetworkMonitor.Services.Platform;

namespace NetworkMonitor.Data
{
    public static class AtomicFile
    {
        public static void WriteAllText(string path, string contents)
        {

            try
            {
                string directory = Path.GetDirectoryName(path)!;

                Directory.CreateDirectory(directory);

                string tempPath = path + ".tmp";

                File.WriteAllText(tempPath, contents);
                File.Move(tempPath, path, true);
            }
            catch (Exception exception)
            {
                AppLog.Error("AtomicFile.WriteAllText", exception);
            }

        }
    }
}
