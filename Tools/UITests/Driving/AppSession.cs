using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;

namespace NetworkMonitor.UITests.Driving
{
    // Wraps the FlaUI Application (from AppUnderTest.LaunchLocalBuild/LaunchInstalledBuild) and
    // owns the UIA3Automation used to read it, so every phase shares one automation session
    // instead of opening its own.
    public sealed class AppSession : IDisposable
    {
        private const string MainWindowTitle = "Umnatha Network Monitor";
        private const string MiniGraphWindowTitle = "Umnatha mini graph";

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

        // Fix round 1 (2026-08-20): a real run's abort evidence caught Application.GetMainWindow
        // resolving to the mini graph widget — titled "Umnatha mini graph", captured mid-run in
        // its tree dump — instead of the real shell, because GetMainWindow's Win32
        // MainWindowHandle heuristic returns whichever top-level window is topmost at query time,
        // and the widget is always-on-top by design. The same heuristic previously (Task 7) was
        // already known to resolve to the Splash window early in a cold start. Both failure modes
        // share one cause: MainWindowHandle answers "what's on top right now", not "which window
        // is actually the app's shell". Resolved by title instead — MainWindow.xaml:7 sets it
        // literally and it never changes — which also means this getter no longer polls
        // internally; a window either exists with this exact title right now or it does not, and
        // every caller that needs to wait for it already does so through Waits (ShellIsReady in
        // LaunchPhase.cs catches the not-found case and returns false so Waits.Until keeps
        // polling, rather than the exception aborting the wait on its first try).
        public Window MainWindow
        {
            get
            {
                Window[] topLevelWindows = Application.GetAllTopLevelWindows(_automation);
                Window? shellWindow = topLevelWindows.FirstOrDefault(window => window.Title == MainWindowTitle);

                if (shellWindow is null)
                {
                    throw new TimeoutException($"No top-level window titled '{MainWindowTitle}' exists right now.");
                }

                return shellWindow;
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

        public Window[] TopLevelWindows()
        {
            Window[] topLevelWindows = Application.GetAllTopLevelWindows(_automation);

            return topLevelWindows;
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
