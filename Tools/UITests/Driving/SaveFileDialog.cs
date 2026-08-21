using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.Patterns;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;
using NetworkMonitor.UITests.Evidence;

namespace NetworkMonitor.UITests.Driving
{
    // Drives the Windows common item dialog — the one `Win32FileSaveDialog.PickSavePath` and
    // `OpenFileDialog.Show` both put on screen — to a given path and confirms it closed.
    //
    // Task 10 moved this out of DevicesPhase, unchanged in behaviour, because the Reports page's
    // PDF and CSV exports reach the same dialog through the same helper in the app
    // (`ReportsPage.SaveBytesAsync`). Every comment below records something a real run taught the
    // Task 8 fix rounds; none of it is theory, and it should exist once rather than per phase.
    public static class SaveFileDialog
    {
        // "1148" (the file name combo's edit box) and "1" (its accept button) are the well-known,
        // stable AutomationIds of that system dialog — not ids this codebase defines. A Windows
        // message box also renders as class "#32770": a second, separate top-level window of the
        // same class, not a descendant of the file dialog.
        private const string DialogClassName = "#32770";
        private const string NameBoxAutomationId = "1148";
        private const string AcceptButtonAutomationId = "1";

        // The dialog's actual input control carries this exact accessible name (its label),
        // whatever AutomationId the current Windows version gives it — fix round 2 (2026-08-20):
        // the previous fallback ("the first Edit anywhere in the dialog") found the dialog's
        // search box instead, a real export path was typed into it, and Windows rejected the
        // `\`/`:` characters that box does not accept.
        private const string FileNameLabelText = "File name:";

        private static readonly TimeSpan ControlTimeout = TimeSpan.FromSeconds(10);

        // The dialog either closes promptly after its accept button is clicked or something is
        // wrong with the click; five seconds is a bound on "did that take", not a wait expected to
        // be spent.
        private static readonly TimeSpan AcceptConfirmTimeout = TimeSpan.FromSeconds(5);

        // How long Enter alone is given to close the dialog before the accept button is tried as a
        // fallback. Short on purpose: this is the common path, and every second spent here is
        // added to a step that has already succeeded.
        private static readonly TimeSpan EnterCommitTimeout = TimeSpan.FromSeconds(3);

        public static void SaveTo(string filePath, string artifactFolder)
        {

            using (UIA3Automation automation = new UIA3Automation())
            {
                AutomationElement desktop = automation.GetDesktop();
                AutomationElement dialogWindow = Waits.UntilFound(
                    () => desktop.FindFirstDescendant(conditionFactory => conditionFactory.ByClassName(DialogClassName)),
                    ControlTimeout,
                    "the native file dialog to appear");

                AutomationElement fileNameBox = FindFileNameBoxWithDiagnostics(dialogWindow, artifactFolder);
                IValuePattern fileNameValuePattern = fileNameBox.Patterns.Value.Pattern;

                fileNameValuePattern.SetValue(filePath);

                // Fix round 2 (2026-08-20): reading the value back and comparing it is the guard
                // against having driven the wrong control. Whatever this is, if it did not accept
                // what was just written, stop here and name it rather than clicking accept.
                string actualValue = fileNameValuePattern.Value.ValueOrDefault ?? string.Empty;

                if (!string.Equals(actualValue, filePath, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Set the file dialog's file name box to '" + filePath + "', but reading it back gave '"
                        + actualValue + "'. The control actually found was " + UiaText.Describe(fileNameBox)
                        + " -- this is a wrong control, not a slow one.");
                }

                // ValuePattern.SetValue updates the control's UIA-visible text (the read-back above
                // confirms that much), but the shell's ComboBoxEx does not always treat that as a
                // real keystroke for its own "what did the user type" state — clicking Save right
                // after SetValue alone left the dialog sitting open, unmoved, in a live run.
                fileNameBox.Focus();
                Keyboard.Type(VirtualKeyShort.RETURN);

                // Task 10: Enter is what commits, and it usually closes the dialog outright — so
                // the accept button is a fallback for when it did not, not a second confirmation
                // to send regardless. Reaching into a window that has already been destroyed threw
                // "Operation timed out (0x80131505)" out of a real run, *after* the save had
                // succeeded: the export was on disk, the step was reported as failed, and the
                // app's own result dialog was left on screen to block the three steps that
                // followed. Waiting for the close first, and treating any lookup failure as "it
                // closed while I was asking", removes both.
                bool closedByEnter = WaitForDialogToClose(desktop, EnterCommitTimeout);

                if (!closedByEnter)
                {
                    ClickAcceptButton(dialogWindow);
                }

                // If a message box appeared after accept, read its text into the failure now
                // rather than letting the caller wait out a timeout looking for something else
                // while the real answer sits on screen.
                string? errorDialogText = TryReadErrorDialogText(desktop);

                if (errorDialogText is not null)
                {
                    throw new InvalidOperationException("The file dialog reported an error after accepting: " + errorDialogText);
                }

                bool dialogClosed = closedByEnter || WaitForDialogToClose(desktop, AcceptConfirmTimeout);

                if (!dialogClosed)
                {
                    string dumpPath = UiaTreeDumper.Dump(dialogWindow, artifactFolder, "file-dialog-did-not-close");

                    throw new InvalidOperationException(
                        "The file dialog was still open " + AcceptConfirmTimeout.TotalSeconds
                        + "s after clicking its accept button (AutomationId '" + AcceptButtonAutomationId
                        + "'); its structure is dumped at " + dumpPath + ".");
                }

            }

        }

        private static bool WaitForDialogToClose(AutomationElement desktop, TimeSpan timeout)
        {
            bool closed;

            try
            {
                Waits.Until(
                    () => !DialogIsOpen(desktop),
                    timeout,
                    "the file dialog to close");

                closed = true;
            }
            catch (TimeoutException)
            {
                closed = false;
            }

            return closed;
        }

        // A dialog that is mid-close can throw out of the search rather than returning null, which
        // is the same answer for this purpose: it is on its way out.
        private static bool DialogIsOpen(AutomationElement desktop)
        {
            bool open;

            try
            {
                open = desktop.FindFirstDescendant(conditionFactory => conditionFactory.ByClassName(DialogClassName)) is not null;
            }
            catch (Exception)
            {
                open = false;
            }

            return open;
        }

        private static void ClickAcceptButton(AutomationElement dialogWindow)
        {

            try
            {
                AutomationElement? acceptButton = dialogWindow.FindFirstDescendant(AcceptButtonAutomationId);

                if (acceptButton is not null)
                {
                    acceptButton.Click();
                }

            }
            catch (Exception exception)
            {
                Console.WriteLine($"SaveFileDialog: the accept button could not be reached (the dialog was probably already closing): {exception.Message}");
            }

        }

        // Cancels a save dialog that is still on screen. Called from a failed export's catch: the
        // dialog is modal to the app under test, so leaving it open turns one failed step into
        // every later step failing for a reason that has nothing to do with what they assert —
        // which is exactly what a Task 10 run did, taking two phases down with it.
        public static void DismissIfOpen()
        {

            try
            {

                using (UIA3Automation automation = new UIA3Automation())
                {
                    AutomationElement desktop = automation.GetDesktop();
                    AutomationElement? strayDialog = desktop.FindFirstDescendant(
                        conditionFactory => conditionFactory.ByClassName(DialogClassName));

                    if (strayDialog is not null)
                    {
                        AutomationElement? cancelButton = strayDialog.FindFirstDescendant(
                            conditionFactory => conditionFactory.ByName("Cancel"));

                        if (cancelButton is not null)
                        {
                            cancelButton.Click();
                        }

                    }

                }

            }
            catch (Exception exception)
            {
                Console.WriteLine($"SaveFileDialog: could not dismiss a stray file dialog: {exception.Message}");
            }

        }

        // Dumps the dialog's real tree to the artifact folder before letting a lookup failure
        // propagate — a native OS dialog is exactly the part of a run with no other live evidence
        // to diagnose from, and guessing again without it is how fix round 2's original "first Edit
        // anywhere" mistake happened in the first place.
        private static AutomationElement FindFileNameBoxWithDiagnostics(AutomationElement dialogWindow, string artifactFolder)
        {
            AutomationElement resolved;

            try
            {
                resolved = FindFileNameBox(dialogWindow);
            }
            catch (TimeoutException)
            {
                string dumpPath = UiaTreeDumper.Dump(dialogWindow, artifactFolder, "file-dialog-structure");

                Console.WriteLine($"SaveFileDialog: could not locate the file dialog's name box; its real structure is dumped at {dumpPath}");

                throw;
            }

            return resolved;
        }

        // A real run showed AutomationId "1148" resolving to the ComboBox wrapper (not its Edit
        // child) for one dialog, where it had been absent entirely for another moments before — so
        // both the known-id path and the label fallback are normalised through the same
        // ResolveEditableControl rule, rather than only the fallback drilling into the Edit child.
        private static AutomationElement FindFileNameBox(AutomationElement dialogWindow)
        {
            AutomationElement? byKnownId = dialogWindow.FindFirstDescendant(NameBoxAutomationId);
            AutomationElement control = byKnownId ?? FindFileNameControlByLabel(dialogWindow);
            AutomationElement resolved = ResolveEditableControl(control);

            return resolved;
        }

        private static AutomationElement FindFileNameControlByLabel(AutomationElement dialogWindow)
        {
            AutomationElement labelledControl = Waits.UntilFound(
                () => dialogWindow.FindFirstDescendant(
                    conditionFactory => conditionFactory.ByName(FileNameLabelText)
                        .And(conditionFactory.ByControlType(ControlType.ComboBox).Or(conditionFactory.ByControlType(ControlType.Edit)))),
                ControlTimeout,
                "the file dialog's ComboBox or Edit control labelled '" + FileNameLabelText
                    + "' (AutomationId '" + NameBoxAutomationId + "' was not present)");

            return labelledControl;
        }

        // The control's own editable text lives on a nested Edit child when it is a ComboBox
        // wrapper; if it is already an Edit, it is used as-is.
        private static AutomationElement ResolveEditableControl(AutomationElement control)
        {
            AutomationElement? editChild = control.ControlType == ControlType.Edit
                ? null
                : control.FindFirstDescendant(conditionFactory => conditionFactory.ByControlType(ControlType.Edit));

            AutomationElement resolved = editChild ?? control;

            return resolved;
        }

        // A message box shares the file dialog's window class, so it shows up as a second,
        // separate top-level "#32770" window rather than a descendant of the first — this is a
        // sibling-window check, not a tree search within the dialog itself.
        private static string? TryReadErrorDialogText(AutomationElement desktop)
        {
            AutomationElement[] dialogClassWindows = desktop.FindAllDescendants(
                conditionFactory => conditionFactory.ByClassName(DialogClassName));
            string? errorText = null;

            if (dialogClassWindows.Length > 1)
            {
                AutomationElement errorWindow = dialogClassWindows[dialogClassWindows.Length - 1];
                string collectedText = CollectDialogText(errorWindow);

                errorText = collectedText.Length > 0 ? collectedText : UiaText.NameOrEmpty(errorWindow);
            }

            return errorText;
        }

        private static string CollectDialogText(AutomationElement dialogRoot)
        {
            AutomationElement[] textDescendants = dialogRoot.FindAllDescendants(
                conditionFactory => conditionFactory.ByControlType(ControlType.Text));
            List<string> fragments = new List<string>();

            foreach (AutomationElement textElement in textDescendants)
            {
                string fragmentText = UiaText.NameOrEmpty(textElement);

                if (fragmentText.Length > 0)
                {
                    fragments.Add(fragmentText);
                }

            }

            string combined = string.Join(" | ", fragments);

            return combined;
        }
    }
}
