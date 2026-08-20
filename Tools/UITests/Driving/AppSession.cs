using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;

namespace NetworkMonitor.UITests.Driving
{
    // Wraps the FlaUI Application (from InstalledApp.Launch) and owns the UIA3Automation used to
    // read it, so every phase shares one automation session instead of opening its own.
    public sealed class AppSession : IDisposable
    {
        private const string MiniGraphWindowTitle = "Umnatha mini graph";

        // Covers a cold WinUI 3 launch that also runs DatabaseInitializer.InitializeAsync
        // (baseline-then-migrate) before the window appears; generous because a false timeout
        // here fails every phase that follows it.
        private static readonly TimeSpan MainWindowTimeout = TimeSpan.FromSeconds(30);

        private readonly UIA3Automation _automation;

        public AppSession(Application application)
        {
            Application = application;
            _automation = new UIA3Automation();
        }

        public Application Application
        {
            get;
        }

        public Window MainWindow
        {
            get
            {
                Window? mainWindow = Application.GetMainWindow(_automation, MainWindowTimeout);

                if (mainWindow is null)
                {
                    throw new TimeoutException(
                        $"Waited {MainWindowTimeout.TotalSeconds:0.#}s for the app's main window to appear and it never did.");
                }

                return mainWindow;
            }
        }

        public Window? MiniGraphWindow
        {
            get
            {
                Window[] topLevelWindows = Application.GetAllTopLevelWindows(_automation);
                Window? miniGraphWindow = topLevelWindows.FirstOrDefault(window => window.Title == MiniGraphWindowTitle);

                return miniGraphWindow;
            }
        }

        public AutomationElement? ByAutomationId(string automationId)
        {
            Window[] topLevelWindows = Application.GetAllTopLevelWindows(_automation);
            AutomationElement? found = null;

            foreach (Window window in topLevelWindows)
            {
                found = window.FindFirstDescendant(automationId);

                if (found is not null)
                {
                    break;
                }

            }

            return found;
        }

        public void Dispose()
        {
            _automation.Dispose();
        }
    }
}
