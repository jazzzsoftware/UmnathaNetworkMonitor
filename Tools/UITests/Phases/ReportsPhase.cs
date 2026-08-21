using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Patterns;
using NetworkMonitor.UITests.Driving;
using NetworkMonitor.UITests.Runner;

namespace NetworkMonitor.UITests.Phases
{
    // Phase 05: the Reports page against the three seeded digests — the Daily Digest tab actually
    // rendering one, the PDF export writing a real PDF to disk, generating a digest on demand, the
    // History tab listing what was seeded, the export-everything CSV, and deleting a report.
    // Not abortsRun.
    //
    // The 24-hour digest schedule is not tested. It is bound to wall-clock time (DigestSchedule
    // against Settings.DigestGenerationHour), so the only part reachable from the UI is its
    // output, which is what "Generate now" produces here.
    //
    // Both exports go through ReportsPage.SaveBytesAsync, which does three things in a row: it
    // opens the Windows common save dialog, writes the file, and then hands it to whatever program
    // the operator has registered for that extension. That last step puts an uncontrolled external
    // window on screen with focus, so both export steps run behind ShellFileHandler's
    // already-running precondition and close what they opened before reading the file back — the
    // ordering DevicesPhase's CSV step arrived at the hard way.
    public static class ReportsPhase
    {
        private const string DigestTabAutomationId = "DigestTab";
        private const string HistoryTabAutomationId = "ReportsHistoryTab";
        private const string GenerateButtonAutomationId = "GenerateDigestButton";
        private const string ExportPdfButtonAutomationId = "ExportDigestPdfButton";
        private const string HistoryListAutomationId = "DigestHistoryList";
        private const string ExportAllCsvButtonAutomationId = "ExportAllReportsCsvButton";
        private const string DeleteButtonAutomationId = "DeleteDigestButton";

        private const string PeriodSubtitleAutomationId = "PeriodSubtitle";
        private const string GeneratedTextAutomationId = "GeneratedText";

        private const string PeriodSubtitlePrefix = "Report:";
        private const string GeneratedTextPrefix = "Generated:";

        private const string ExportedPdfFileName = "digest-export.pdf";
        private const string ExportedCsvFileName = "digest-reports-export.csv";
        private const string PdfExtension = ".pdf";
        private const string CsvExtension = ".csv";

        // Every PDF starts with this signature; the point of the assertion is that the export
        // wrote a real document rather than an empty or truncated file.
        private const string PdfSignature = "%PDF";

        // ReportsPage.xaml.cs's DeleteHistoryClick sets this exact Title, so the dialog is found by
        // the string the app itself sets rather than by a guessed class name — ByClassName
        // ("ContentDialog") failed repeatedly in real runs during Task 8.
        private const string DeleteDialogTitle = "Delete report?";
        private const string DeleteDialogConfirmButtonText = "Delete";

        private static readonly TimeSpan ControlTimeout = TimeSpan.FromSeconds(10);

        // Generating a digest queries the whole seeded database and renders its charts through
        // Win2D before the new report lands in the list; the render is the slow part.
        private static readonly TimeSpan GenerateTimeout = TimeSpan.FromSeconds(60);

        // A save dialog's file appearing on disk, or a list reloading after a delete.
        private static readonly TimeSpan DataChangeTimeout = TimeSpan.FromSeconds(15);

        public static Task<IReadOnlyList<StepResult>> RunAsync(PhaseContext context)
        {
            StepLog steps = new StepLog(context);
            AppSession session = context.Session
                ?? throw new InvalidOperationException(
                    "ReportsPhase requires LaunchPhase to have run first and set PhaseContext.Session.");

            Navigator navigator = new Navigator(session);

            navigator.GoTo(NavRoute.Reports);
            navigator.SelectTab(DigestTabAutomationId);

            RunDigestTab(session, context, steps);

            navigator.SelectTab(HistoryTabAutomationId);

            RunHistoryTab(session, context, steps);

            IReadOnlyList<StepResult> result = steps.Steps;
            Task<IReadOnlyList<StepResult>> completed = Task.FromResult(result);

            return completed;
        }

        private static void RunDigestTab(AppSession session, PhaseContext context, StepLog steps)
        {
            steps.Add(AssertTextStartsWith(session, "The Daily Digest tab renders the latest report", PeriodSubtitleAutomationId, PeriodSubtitlePrefix));
            steps.Add(AssertTextStartsWith(session, "The rendered report says when it was generated", GeneratedTextAutomationId, GeneratedTextPrefix));
            steps.AddRange(RunPdfExport(session, context.ArtifactFolder));
            steps.AddRange(RunGenerateNow(session, context));
        }

        private static void RunHistoryTab(AppSession session, PhaseContext context, StepLog steps)
        {
            AutomationElement historyList = WaitForElement(session, HistoryListAutomationId, "the digest history list");

            // One more than was seeded: RunGenerateNow has just added a report, and asserted that
            // it did. Comparing against the seed count alone here would fail for the right reason
            // in the wrong place.
            int expectedCount = context.Seed.DigestReports + 1;

            steps.Add(AssertListCount("The History tab lists every report the database holds", historyList, expectedCount));

            bool selected = TrySelectFirstItem(historyList);

            if (!selected)
            {
                steps.Add(StepResult.Fail("Selecting a report from the history renders it", "the first report in the list to be selectable", "no selectable list item was found"));
                steps.Add(StepResult.Skip("Exporting every report to CSV writes a file", "No report was selected (see the previous step)."));
                steps.Add(StepResult.Skip("Deleting a report removes it from the history", "No report was selected (see the previous step)."));
            }
            else
            {
                steps.Add(AssertTextStartsWith(session, "Selecting a report from the history renders it", PeriodSubtitleAutomationId, PeriodSubtitlePrefix));
                steps.AddRange(RunCsvExport(session, context.ArtifactFolder));
                steps.Add(RunDelete(session, historyList, expectedCount));
            }

        }

        // Wrapped whole: a native dialog plus an external handler process is the least predictable
        // thing this phase does, and a failure in it should cost this step rather than the rest of
        // the phase.
        private static List<StepResult> RunPdfExport(AppSession session, string artifactFolder)
        {
            const string stepName = "Exporting the digest writes a real PDF to the fixture folder";
            List<StepResult> steps = new List<StepResult>();
            string exportPath = Path.Combine(artifactFolder, ExportedPdfFileName);
            string handlerProcessName = ShellFileHandler.ResolveHandlerProcessName(PdfExtension);
            string preExistingHandlerBlocker = ShellFileHandler.FindPreExistingHandlerBlocker(handlerProcessName, PdfExtension);

            if (preExistingHandlerBlocker.Length > 0)
            {
                steps.Add(StepResult.Fail(stepName, "no instance of the .pdf file handler already running before Export is clicked", preExistingHandlerBlocker));
            }
            else
            {

                try
                {
                    ClickButton(session, ExportPdfButtonAutomationId, "the Export PDF button");
                    SaveFileDialog.SaveTo(exportPath, artifactFolder);

                    Waits.Until(() => File.Exists(exportPath), DataChangeTimeout, "the exported PDF to appear on disk");

                    // Closed before the file is read back, for the two reasons DevicesPhase's CSV
                    // step documents: the handler races the read for the file lock, and a step that
                    // throws during the read would otherwise skip this cleanup and leave the
                    // handler's window holding focus into the next step.
                    ShellFileHandler.CloseOpenedFile(handlerProcessName, exportPath);

                    byte[] exported = File.ReadAllBytes(exportPath);
                    string signature = exported.Length >= PdfSignature.Length
                        ? System.Text.Encoding.ASCII.GetString(exported, 0, PdfSignature.Length)
                        : string.Empty;

                    if (exported.Length > 0 && string.Equals(signature, PdfSignature, StringComparison.Ordinal))
                    {
                        steps.Add(StepResult.Pass(stepName));
                    }
                    else
                    {
                        steps.Add(StepResult.Fail(
                            stepName,
                            $"a non-empty file at {exportPath} starting '{PdfSignature}'",
                            $"{exported.Length} byte(s) written, starting '{signature}'"));
                    }

                }
                catch (Exception failure)
                {
                    // The dialog is modal to the app under test, so a failure that leaves it open
                    // takes every later step down with it — which is how a Task 10 run lost two
                    // whole phases to one failed export.
                    SaveFileDialog.DismissIfOpen();

                    AppDialogs.DismissIfOpen(session);

                    steps.Add(StepResult.Fail(stepName, $"a PDF written to {exportPath}", failure.Message));
                }

            }

            return steps;
        }

        private static List<StepResult> RunCsvExport(AppSession session, string artifactFolder)
        {
            const string stepName = "Exporting every report to CSV writes a file";
            List<StepResult> steps = new List<StepResult>();
            string exportPath = Path.Combine(artifactFolder, ExportedCsvFileName);
            string handlerProcessName = ShellFileHandler.ResolveHandlerProcessName(CsvExtension);
            string preExistingHandlerBlocker = ShellFileHandler.FindPreExistingHandlerBlocker(handlerProcessName, CsvExtension);

            if (preExistingHandlerBlocker.Length > 0)
            {
                steps.Add(StepResult.Fail(stepName, "no instance of the .csv file handler already running before Export is clicked", preExistingHandlerBlocker));
            }
            else
            {

                try
                {
                    ClickButton(session, ExportAllCsvButtonAutomationId, "the Export All Reports To CSV button");
                    SaveFileDialog.SaveTo(exportPath, artifactFolder);

                    Waits.Until(() => File.Exists(exportPath), DataChangeTimeout, "the exported reports CSV to appear on disk");

                    ShellFileHandler.CloseOpenedFile(handlerProcessName, exportPath);

                    string exported = File.ReadAllText(exportPath);
                    bool looksReal = exported.Length > 0 && exported.Contains(",", StringComparison.Ordinal);

                    if (looksReal)
                    {
                        steps.Add(StepResult.Pass(stepName));
                    }
                    else
                    {
                        steps.Add(StepResult.Fail(stepName, $"a non-empty CSV at {exportPath}", $"{exported.Length} byte(s) written"));
                    }

                }
                catch (Exception failure)
                {
                    SaveFileDialog.DismissIfOpen();

                    AppDialogs.DismissIfOpen(session);

                    steps.Add(StepResult.Fail(stepName, $"a CSV written to {exportPath}", failure.Message));
                }

            }

            return steps;
        }

        // "Generate now" builds a digest for the trailing period and stores it, so the proof it
        // worked is a new row in the history rather than anything on the digest view itself, whose
        // text is dominated by the period it covers.
        private static List<StepResult> RunGenerateNow(AppSession session, PhaseContext context)
        {
            const string stepName = "Generating a digest on demand adds one to the history";
            List<StepResult> steps = new List<StepResult>();
            int expectedCount = context.Seed.DigestReports + 1;

            try
            {
                InvokeButton(session, GenerateButtonAutomationId, "the Generate now button");

                Navigator navigator = new Navigator(session);

                navigator.SelectTab(HistoryTabAutomationId);

                AutomationElement historyList = WaitForElement(session, HistoryListAutomationId, "the digest history list");

                Waits.Until(
                    () => CountListItems(historyList) == expectedCount,
                    GenerateTimeout,
                    $"the digest history to hold {expectedCount} reports after generating one");

                steps.Add(StepResult.Pass(stepName));

                navigator.SelectTab(DigestTabAutomationId);
            }
            catch (Exception failure)
            {
                steps.Add(StepResult.Fail(stepName, $"{expectedCount} reports in the history", failure.Message));
            }

            return steps;
        }

        private static StepResult RunDelete(AppSession session, AutomationElement historyList, int countBeforeDelete)
        {
            const string stepName = "Deleting a report removes it from the history";
            int expectedCount = countBeforeDelete - 1;
            StepResult result;

            try
            {
                InvokeButton(session, DeleteButtonAutomationId, "the Delete button");

                AutomationElement confirmDialog = Waits.UntilFound(
                    () => session.MainWindow.FindFirstDescendant(conditionFactory => conditionFactory.ByName(DeleteDialogTitle)),
                    ControlTimeout,
                    $"the delete confirmation dialog titled '{DeleteDialogTitle}'");

                AutomationElement confirmButton = Waits.UntilFound(
                    () => confirmDialog.FindFirstDescendant(conditionFactory => conditionFactory.ByName(DeleteDialogConfirmButtonText)),
                    ControlTimeout,
                    "the delete confirmation dialog's Delete button");

                confirmButton.Click();

                Waits.Until(
                    () => CountListItems(historyList) == expectedCount,
                    DataChangeTimeout,
                    $"the digest history to drop to {expectedCount} reports after deleting one");

                result = StepResult.Pass(stepName);
            }
            catch (Exception failure)
            {
                result = StepResult.Fail(stepName, $"{expectedCount} reports left in the history", failure.Message);
            }

            return result;
        }

        private static StepResult AssertListCount(string stepName, AutomationElement list, int expectedCount)
        {
            int actualCount = CountListItems(list);
            StepResult result;

            if (actualCount == expectedCount)
            {
                result = StepResult.Pass(stepName);
            }
            else
            {
                result = StepResult.Fail(stepName, $"{expectedCount} report(s)", $"{actualCount} report(s)");
            }

            return result;
        }

        // The digest view and the history detail are two instances of the same DigestReportView, so
        // both carry the same automation identifiers; only one panel is visible at a time
        // (ReportsPage's TabBarSelectionChanged collapses the other), which is why this reads the
        // first element that is not reported offscreen rather than simply the first match.
        private static StepResult AssertTextStartsWith(AppSession session, string stepName, string automationId, string expectedPrefix)
        {
            string text = ReadVisibleText(session, automationId);
            StepResult result;

            if (text.StartsWith(expectedPrefix, StringComparison.Ordinal))
            {
                result = StepResult.Pass(stepName);
            }
            else
            {
                string actual = text.Length > 0 ? $"'{text}'" : $"no visible element with AutomationId '{automationId}'";

                result = StepResult.Fail(stepName, $"text starting '{expectedPrefix}'", actual);
            }

            return result;
        }

        private static string ReadVisibleText(AppSession session, string automationId)
        {
            string text = string.Empty;

            try
            {
                AutomationElement[] candidates = session.MainWindow.FindAllDescendants(
                    conditionFactory => conditionFactory.ByAutomationId(automationId));

                foreach (AutomationElement candidate in candidates)
                {

                    if (!candidate.Properties.IsOffscreen.ValueOrDefault)
                    {
                        text = UiaText.NameOrEmpty(candidate);

                        break;
                    }

                }

            }
            catch (Exception)
            {
                text = string.Empty;
            }

            return text;
        }

        private static int CountListItems(AutomationElement list)
        {
            int count = 0;

            try
            {
                AutomationElement[] items = list.FindAllDescendants(
                    conditionFactory => conditionFactory.ByControlType(ControlType.ListItem));

                count = items.Length;
            }
            catch (Exception)
            {
                count = -1;
            }

            return count;
        }

        private static bool TrySelectFirstItem(AutomationElement list)
        {
            bool selected = false;

            try
            {
                AutomationElement? firstItem = list.FindFirstDescendant(
                    conditionFactory => conditionFactory.ByControlType(ControlType.ListItem));

                if (firstItem is not null)
                {
                    ISelectionItemPattern selectionItemPattern = firstItem.Patterns.SelectionItem.Pattern;

                    selectionItemPattern.Select();

                    Waits.Until(
                        () => selectionItemPattern.IsSelected.Value,
                        ControlTimeout,
                        "the first history report to report itself selected after Select()");

                    selected = true;
                }

            }
            catch (Exception)
            {
                selected = false;
            }

            return selected;
        }

        private static AutomationElement WaitForElement(AppSession session, string automationId, string description)
        {
            AutomationElement element = Waits.UntilFound(
                () => session.MainWindow.FindFirstDescendant(automationId),
                ControlTimeout,
                description);

            return element;
        }

        // Invoked through the pattern, which is right for a button whose handler returns promptly.
        private static void InvokeButton(AppSession session, string buttonAutomationId, string description)
        {
            AutomationElement button = FindButton(session, buttonAutomationId, description);
            IInvokePattern invokePattern = button.Patterns.Invoke.Pattern;

            invokePattern.Invoke();
        }

        // Clicked with the mouse, NOT invoked — and the distinction is not cosmetic.
        //
        // InvokePattern.Invoke() runs the handler and does not return until that handler does. Both
        // export handlers reach Win32FileSaveDialog.PickSavePath, which is a blocking modal call on
        // the app's UI thread, so the handler does not return until the dialog is dismissed — and
        // the only thing that would dismiss it is the code waiting inside Invoke(). Two runs
        // deadlocked exactly there: ~25 seconds inside Invoke until FlaUI's own UIA timeout fired,
        // with the dialog still open (its screenshot showed the suggested file name untouched,
        // because the line that types the path had never been reached), which then took the rest
        // of the phase down with it. A real click posts input and returns immediately, which is
        // why DevicesPhase's CSV export never hit this.
        private static void ClickButton(AppSession session, string buttonAutomationId, string description)
        {
            AutomationElement button = FindButton(session, buttonAutomationId, description);

            button.Click();
        }

        private static AutomationElement FindButton(AppSession session, string buttonAutomationId, string description)
        {
            AutomationElement button = Waits.UntilFound(
                () => session.MainWindow.FindFirstDescendant(buttonAutomationId),
                ControlTimeout,
                description);

            return button;
        }
    }
}
