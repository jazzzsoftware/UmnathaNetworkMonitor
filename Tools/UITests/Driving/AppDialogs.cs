using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

namespace NetworkMonitor.UITests.Driving
{
    // Closes one of the app's own ContentDialogs if a failed step left it on screen.
    //
    // Task 10: a real run threw partway through the CSV import, after the import itself had
    // succeeded, leaving "Import Approved Devices — Added 0 new devices" sitting there. A
    // ContentDialog is modal to the window, so the next three steps waited out their timeouts
    // looking for dialogs that could never open while that one held the front, and reported
    // failures that had nothing to do with what they were testing. One failed step should cost
    // one step.
    //
    // Only the dismissive buttons are ever pressed — never a "Delete", "Save" or "Update now",
    // which would commit something on the way out. Best-effort throughout: this runs on the way
    // out of a failure and must not manufacture a second one.
    public static class AppDialogs
    {
        // "Close" is deliberately NOT in this list. The window's own title bar carries a Button
        // named exactly that, so including it would close the app under test — to tray, silently,
        // taking the rest of the run with it — every time a phase asked for a stray dialog to be
        // dismissed and there wasn't one.
        private static readonly string[] DismissiveButtonNames = { "OK", "Cancel", "Later" };

        public static void DismissIfOpen(AppSession session)
        {

            try
            {
                Window mainWindow = session.MainWindow;

                foreach (string buttonName in DismissiveButtonNames)
                {
                    AutomationElement? button = mainWindow.FindFirstDescendant(
                        conditionFactory => conditionFactory.ByName(buttonName).And(conditionFactory.ByControlType(ControlType.Button)));

                    if (button is not null && IsOnScreen(button))
                    {
                        Console.WriteLine($"AppDialogs: dismissing a dialog left open by a failed step, via its '{buttonName}' button.");

                        button.Click();

                        break;
                    }

                }

            }
            catch (Exception exception)
            {
                Console.WriteLine($"AppDialogs: could not dismiss a stray dialog: {exception.Message}");
            }

        }

        private static bool IsOnScreen(AutomationElement element)
        {
            bool onScreen;

            try
            {
                System.Drawing.Rectangle bounds = element.BoundingRectangle;

                onScreen = !element.IsOffscreen && bounds.Width > 0 && bounds.Height > 0;
            }
            catch (Exception)
            {
                onScreen = false;
            }

            return onScreen;
        }
    }
}
