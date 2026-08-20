using System.Drawing.Imaging;
using FlaUI.Core.Capturing;
using FlaUI.Core.AutomationElements;

namespace NetworkMonitor.UITests.Evidence
{
    public static class ScreenshotWriter
    {
        public static string Write(AutomationElement element, string artifactFolder, string stepName)
        {
            string path = string.Empty;

            try
            {
                Directory.CreateDirectory(artifactFolder);

                string fileName = $"{Sanitise(stepName)}.png";
                string fullPath = Path.Combine(artifactFolder, fileName);

                using (CaptureImage image = Capture.Element(element))
                {
                    image.Bitmap.Save(fullPath, ImageFormat.Png);
                }

                path = fullPath;
            }
            catch (Exception failure)
            {
                Console.WriteLine($"Could not capture a screenshot for '{stepName}': {failure.Message}");
            }

            return path;
        }

        private static string Sanitise(string stepName)
        {
            string cleaned = string.Join("-", stepName.Split(Path.GetInvalidFileNameChars()));

            return cleaned;
        }
    }
}
