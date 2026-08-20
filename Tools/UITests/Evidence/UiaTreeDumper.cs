using System.Text;
using FlaUI.Core.AutomationElements;

namespace NetworkMonitor.UITests.Evidence
{
    public static class UiaTreeDumper
    {
        private const int MaxDepth = 12;

        public static string Dump(AutomationElement root, string artifactFolder, string stepName)
        {
            string path = string.Empty;

            try
            {
                Directory.CreateDirectory(artifactFolder);

                string fileName = $"{Sanitise(stepName)}.tree.txt";
                string fullPath = Path.Combine(artifactFolder, fileName);
                StringBuilder builder = new StringBuilder();

                AppendElement(builder, root, 0);

                File.WriteAllText(fullPath, builder.ToString());

                path = fullPath;
            }
            catch (Exception failure)
            {
                Console.WriteLine($"Could not dump the automation tree for '{stepName}': {failure.Message}");
            }

            return path;
        }

        private static void AppendElement(StringBuilder builder, AutomationElement element, int depth)
        {
            string indent = new string(' ', depth * 2);
            string line = FormatLine(element);

            builder.AppendLine($"{indent}{line}");

            if (depth < MaxDepth)
            {
                AutomationElement[] children = GetChildren(element);

                foreach (AutomationElement child in children)
                {
                    AppendElement(builder, child, depth + 1);
                }

            }

        }

        private static string FormatLine(AutomationElement element)
        {
            string controlType = ReadProperty(() => element.ControlType.ToString());
            string automationId = ReadProperty(() => element.AutomationId);
            string name = ReadProperty(() => element.Name);
            string isEnabled = ReadProperty(() => element.IsEnabled.ToString());
            string isOffscreen = ReadProperty(() => element.IsOffscreen.ToString());
            string formatted = $"{controlType} | {automationId} | {name} | {isEnabled} | {isOffscreen}";

            return formatted;
        }

        // Each of the five properties above is read independently: on a real desktop, one
        // property (AutomationId is the common offender on plain Win32 windows) throwing
        // "not supported" must not blank the whole row when the other four read just fine.
        private static string ReadProperty(Func<string> read)
        {
            string value;

            try
            {
                value = read();
            }
            catch (Exception)
            {
                value = "?";
            }

            return value;
        }

        private static AutomationElement[] GetChildren(AutomationElement element)
        {
            AutomationElement[] children;

            try
            {
                children = element.FindAllChildren();
            }
            catch (Exception)
            {
                children = Array.Empty<AutomationElement>();
            }

            return children;
        }

        private static string Sanitise(string stepName)
        {
            string cleaned = string.Join("-", stepName.Split(Path.GetInvalidFileNameChars()));

            return cleaned;
        }
    }
}
