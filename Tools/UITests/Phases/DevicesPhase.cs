using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Patterns;
using FlaUI.UIA3;
using NetworkMonitor.UITests.Driving;
using NetworkMonitor.UITests.Runner;

namespace NetworkMonitor.UITests.Phases
{
    // Phase 02: All / Approved / Unapproved / History against the fixture SeedDatabase.BuildAsync
    // seeds (SeedCounts), then CSV export/re-import, an edit and a delete on the Approved tab.
    // Not abortsRun — a failed assertion here is recorded and the run continues; only a missing
    // nav item, tab or grid (the environment, not a value) is allowed to throw.
    //
    // Two corrections to what a literal reading of SeedCounts would suggest, both load-bearing for
    // every row-count assertion below:
    //
    // 1. AllDevicesViewModel.LoadAsync (NetworkMonitor/ViewModels/AllDevicesViewModel.cs:130-135)
    //    loads only devices that are online OR were last seen within the trailing 24 hours — the
    //    same "Fing-style ... last 24h" filter AllDevicesPage.xaml:34 captions on screen. That
    //    query backs All, Approved and Unapproved alike (only the later in-memory filter differs
    //    between them), so every one of those three tabs drops SeedDatabase's twelfth device (MAC
    //    02:00:00:00:00:0C — offline, last seen 30h ago, the only seeded device that is both
    //    offline and outside the window). All and Unapproved are therefore one row short of what
    //    SeedCounts reports; Approved is unaffected because none of its eight devices falls
    //    outside the window.
    // 2. Device.IsRandomizedMac (NetworkMonitor.Models/Devices/Device.cs:237-251) flags the
    //    locally-administered bit, and every seeded MAC (SeedDatabase uses the 02:00:00:00:00:0x
    //    range) sets it — so every seeded device shows the "Private MAC" badge next to its name.
    //    Name-column assertions below match with Contains, never exact equality, in case that
    //    badge text ends up folded into the cell's UIA Name the same way GridReader.CellText reads
    //    it (see that file's own UNVERIFIED note — never run against the real app either).
    public static class DevicesPhase
    {
        private const int AllNameColumn = 2;
        private const int ApprovedNameColumn = 2;
        private const int ApprovedActionsColumn = 6;
        private const int HistoryEventColumn = 1;

        private const string RenamedDeviceName = "Mark's Laptop";
        private const string NotesDeviceHostname = "smart-tv";
        private const string NotesDeviceText = "Guest device - ask before connecting.";
        private const string EditTargetHostname = "ipad-kitchen";
        private const string EditTargetMac = "02:00:00:00:00:07";
        private const string EditedFriendlyName = "UI Test Renamed Device";
        private const string DeleteTargetHostname = "printer-office";
        private const string ExportedCsvFileName = "approved-devices-export.csv";

        // The Windows common item dialog (IFileDialog and GetOpenFileNameW both render the same
        // Vista+ dialog on this OS — Win32FileSaveDialog.PickSavePath and OpenFileDialog.Show use
        // one each). "1148" (the file name combo's edit box) and "1" (its accept button) are the
        // well-known, stable AutomationIds for that system dialog — not ids this codebase defines.
        private const string CommonFileDialogClassName = "#32770";
        private const string CommonFileDialogNameBoxId = "1148";
        private const string CommonFileDialogAcceptButtonId = "1";

        // The only seeded device that is both offline and outside AllDevicesViewModel's trailing
        // 24-hour window (see the class comment above) — dropped from All and Unapproved alike.
        private const int ExcludedByTwentyFourHourWindow = 1;

        // A tab, a grid or a row's action button either exists once the page's initial LoadAsync
        // (one EF query against the small seeded database) has finished, or it will not appear at
        // all; ten seconds covers a slow first layout pass without masking a genuinely missing
        // control.
        private static readonly TimeSpan ControlTimeout = TimeSpan.FromSeconds(10);

        // A Save/Delete/Import click triggers an async EF SaveChangesAsync plus a LoadAsync reload
        // before the grid's bound collection updates; generous because a false timeout here would
        // report a working feature as broken.
        private static readonly TimeSpan DataChangeTimeout = TimeSpan.FromSeconds(15);

        public static Task<IReadOnlyList<StepResult>> RunAsync(PhaseContext context)
        {
            List<StepResult> steps = new List<StepResult>();
            AppSession session = context.Session
                ?? throw new InvalidOperationException(
                    "DevicesPhase requires LaunchPhase to have run first and set PhaseContext.Session.");

            Navigator navigator = new Navigator(session);

            navigator.GoTo(NavRoute.Devices);

            AutomationElement allGrid = WaitForGrid(session, "AllDevicesGrid");
            int expectedAllCount = context.Seed.KnownDevices - ExcludedByTwentyFourHourWindow;

            steps.Add(AssertGridRowCount("The All grid shows the seeded devices inside the last 24 hours", allGrid, expectedAllCount));
            steps.Add(AssertRowFound("The renamed device shows its friendly name", allGrid, AllNameColumn, RenamedDeviceName));
            steps.Add(AssertNotesAttached(allGrid, AllNameColumn, NotesDeviceHostname, NotesDeviceText));

            SelectTab(session, "ApprovedDevicesTab");

            AutomationElement approvedGrid = WaitForGrid(session, "ApprovedDevicesGrid");

            steps.Add(AssertGridRowCount("The Approved grid matches the seeded approved count", approvedGrid, context.Seed.ApprovedDevices));

            SelectTab(session, "UnapprovedDevicesTab");

            AutomationElement unapprovedGrid = WaitForGrid(session, "UnapprovedDevicesGrid");
            int expectedUnapprovedCount = context.Seed.UnapprovedDevices - ExcludedByTwentyFourHourWindow;

            steps.Add(AssertGridRowCount(
                "The Unapproved grid matches the seeded unapproved count inside the last 24 hours",
                unapprovedGrid,
                expectedUnapprovedCount));

            SelectTab(session, "DeviceHistoryTab");

            AutomationElement historyGrid = WaitForGrid(session, "DeviceHistoryGrid");

            steps.Add(AssertGridRowCount("The History grid shows all 48 hours of seeded events", historyGrid, context.Seed.DeviceEvents));
            steps.Add(AssertHistoryHasArrivalsAndDepartures(historyGrid));

            SelectTab(session, "ApprovedDevicesTab");
            steps.AddRange(RunCsvExportImport(session, context.ArtifactFolder, context.Seed.ApprovedDevices));

            // Fix round 1 (2026-08-20): Edit and Delete each re-fetch the grid themselves, inside
            // their own try/catch, rather than sharing one reference resolved out here. A real run
            // had CSV's native file dialog hit "UIA Timeout", then a grid re-fetch made out here
            // (unwrapped) hit its own COM timeout and escaped this method entirely — PhaseRunner's
            // catch then discarded every step already recorded above, down to a single generic
            // "phase completed without throwing" failure. Fetching inside each function's own
            // try/catch means a COM hiccup after CSV costs only that one step's result, not every
            // result gathered before it.
            steps.AddRange(RunEditDevice(session));
            steps.Add(RunDeleteDevice(session));

            IReadOnlyList<StepResult> result = steps;
            Task<IReadOnlyList<StepResult>> completed = Task.FromResult(result);

            return completed;
        }

        private static AutomationElement WaitForGrid(AppSession session, string gridAutomationId)
        {
            AutomationElement grid = Waits.UntilFound(
                () => session.MainWindow.FindFirstDescendant(gridAutomationId),
                ControlTimeout,
                $"the '{gridAutomationId}' grid to appear");

            return grid;
        }

        private static void SelectTab(AppSession session, string tabAutomationId)
        {
            AutomationElement tabItem = Waits.UntilFound(
                () => session.MainWindow.FindFirstDescendant(tabAutomationId),
                ControlTimeout,
                $"the '{tabAutomationId}' tab to appear");

            ISelectionItemPattern selectionItemPattern = tabItem.Patterns.SelectionItem.Pattern;

            selectionItemPattern.Select();

            Waits.Until(
                () => selectionItemPattern.IsSelected.Value,
                ControlTimeout,
                $"the '{tabAutomationId}' tab to report itself selected after Select()");
        }

        private static StepResult AssertGridRowCount(string stepName, AutomationElement grid, int expectedCount)
        {
            int actualCount = GridReader.RowCount(grid);
            StepResult result;

            if (actualCount == expectedCount)
            {
                result = StepResult.Pass(stepName);
            }
            else
            {
                result = StepResult.Fail(stepName, $"{expectedCount} row(s)", $"{actualCount} row(s)");
            }

            return result;
        }

        private static StepResult AssertRowFound(string stepName, AutomationElement grid, int nameColumn, string expectedSubstring)
        {
            int rowIndex = FindRowIndexByNameText(grid, nameColumn, expectedSubstring);
            StepResult result;

            if (rowIndex >= 0)
            {
                result = StepResult.Pass(stepName);
            }
            else
            {
                result = StepResult.Fail(stepName, $"a row whose Name column contains '{expectedSubstring}'", "no matching row");
            }

            return result;
        }

        private static StepResult AssertNotesAttached(AutomationElement grid, int nameColumn, string hostname, string expectedNotes)
        {
            const string stepName = "The device with notes shows them";
            int rowIndex = FindRowIndexByNameText(grid, nameColumn, hostname);
            StepResult result;

            if (rowIndex < 0)
            {
                result = StepResult.Fail(stepName, $"a row whose Name column contains '{hostname}'", "no matching row");
            }
            else
            {
                string helpText = GridReader.CellHelpText(grid, rowIndex, nameColumn);

                if (helpText.Contains(expectedNotes, StringComparison.Ordinal))
                {
                    result = StepResult.Pass(stepName);
                }
                else
                {
                    string actual = helpText.Length > 0 ? helpText : "(empty HelpText)";

                    result = StepResult.Fail(stepName, $"the Name cell's UI Automation HelpText to contain '{expectedNotes}'", actual);
                }

            }

            return result;
        }

        private static StepResult AssertHistoryHasArrivalsAndDepartures(AutomationElement historyGrid)
        {
            const string stepName = "The History grid shows both arrivals and departures";
            int rowCount = GridReader.RowCount(historyGrid);
            bool sawAppeared = false;
            bool sawDisappeared = false;

            for (int row = 0; row < rowCount; row++)
            {
                string eventText = GridReader.CellText(historyGrid, row, HistoryEventColumn);

                if (eventText.Contains("Appeared", StringComparison.Ordinal))
                {
                    sawAppeared = true;
                }

                if (eventText.Contains("Disappeared", StringComparison.Ordinal))
                {
                    sawDisappeared = true;
                }

            }

            StepResult result;

            if (sawAppeared && sawDisappeared)
            {
                result = StepResult.Pass(stepName);
            }
            else
            {
                result = StepResult.Fail(
                    stepName,
                    "at least one 'Appeared' row and at least one 'Disappeared' row",
                    $"Appeared seen: {sawAppeared}, Disappeared seen: {sawDisappeared}");
            }

            return result;
        }

        private static int FindRowIndexByNameText(AutomationElement grid, int nameColumn, string expectedSubstring)
        {
            int rowCount = GridReader.RowCount(grid);
            int matchedRow = -1;

            for (int row = 0; row < rowCount && matchedRow < 0; row++)
            {
                string cellText = GridReader.CellText(grid, row, nameColumn);

                if (cellText.Contains(expectedSubstring, StringComparison.Ordinal))
                {
                    matchedRow = row;
                }

            }

            return matchedRow;
        }

        // ContentDialog.Content is a plain string in ImportButtonClick/ExportButtonClick's result
        // dialogs, which WinUI wraps in a TextBlock whose Name carries the message text — but that
        // TextBlock is one descendant among several (title bar, buttons), so this scans rather than
        // assumes a fixed position. Returns the first descendant whose Name contains the marker, or
        // an empty string if none does.
        // Fix round 1 (2026-08-20): GridReader.CellText's live-confirmed finding — some UIA
        // elements throw "not supported" reading .Name rather than returning empty — applies
        // here too, since this walks an arbitrary, unknown dialog subtree rather than a single
        // known cell shape. Each read is defensive for exactly that reason; one unreadable
        // element must not abort the whole scan.
        private static string DescendantTextContaining(AutomationElement root, string marker)
        {
            AutomationElement[] descendants = root.FindAllDescendants();
            string matchedText = string.Empty;

            for (int descendantIndex = 0; descendantIndex < descendants.Length && matchedText.Length == 0; descendantIndex++)
            {
                string candidateText = TryReadName(descendants[descendantIndex]);

                if (candidateText.Contains(marker, StringComparison.Ordinal))
                {
                    matchedText = candidateText;
                }

            }

            return matchedText;
        }

        private static string TryReadName(AutomationElement element)
        {
            string name;

            try
            {
                name = element.Name;
            }
            catch (Exception)
            {
                name = string.Empty;
            }

            return name;
        }

        // Fix round 1 (2026-08-20): a real run's Edit/Delete steps both failed waiting for their
        // ContentDialog — "the Edit dialog for 02:00:00:00:00:07" and "the delete confirmation
        // dialog" both timed out even though the row was correctly located (FindRowIndexByNameText
        // succeeded, so the failure was strictly after that) and the button itself was found (its
        // own distinct Waits.UntilFound message never appeared in the failure). The likeliest cause:
        // this method never scrolled the row into view before clicking, unlike GridReader.CellText —
        // GetItem still returns an element for an unrealised row, but Click() on it lands on a
        // stale or degenerate on-screen position, not the real button, so nothing ever opened.
        private static void ClickRowButton(AutomationElement grid, int row, int actionsColumn, string buttonAutomationId)
        {
            IGridPattern gridPattern = grid.Patterns.Grid.Pattern;

            GridReader.ScrollRowIntoView(grid, row);

            AutomationElement actionsCell = gridPattern.GetItem(row, actionsColumn);
            AutomationElement button = Waits.UntilFound(
                () => actionsCell.FindFirstDescendant(buttonAutomationId),
                ControlTimeout,
                $"the '{buttonAutomationId}' button in row {row}");

            button.Click();
        }

        private static void DriveCommonFileDialog(string filePath)
        {

            using (UIA3Automation automation = new UIA3Automation())
            {
                AutomationElement desktop = automation.GetDesktop();
                AutomationElement dialogWindow = Waits.UntilFound(
                    () => desktop.FindFirstDescendant(conditionFactory => conditionFactory.ByClassName(CommonFileDialogClassName)),
                    ControlTimeout,
                    "the native file dialog to appear");

                AutomationElement fileNameBox = FindFileNameBox(dialogWindow);
                IValuePattern fileNameValuePattern = fileNameBox.Patterns.Value.Pattern;

                fileNameValuePattern.SetValue(filePath);

                AutomationElement acceptButton = Waits.UntilFound(
                    () => dialogWindow.FindFirstDescendant(CommonFileDialogAcceptButtonId),
                    ControlTimeout,
                    "the file dialog's accept button to appear");

                acceptButton.Click();
            }

        }

        // Fix round 1 (2026-08-20): a real run's CSV export step got past finding the dialog
        // window itself (by class name — confirmed correct, since the failure was specifically
        // "the file dialog's file name box to appear", not the dialog window) but never found
        // AutomationId "1148" for the file name box. Falls back to the first Edit-type descendant
        // — whatever id the combo container carries, its editable text entry is a plain Edit
        // control — rather than depending on one specific, unconfirmed id.
        private static AutomationElement FindFileNameBox(AutomationElement dialogWindow)
        {
            AutomationElement? byKnownId = dialogWindow.FindFirstDescendant(CommonFileDialogNameBoxId);
            AutomationElement resolved;

            if (byKnownId is not null)
            {
                resolved = byKnownId;
            }
            else
            {
                resolved = Waits.UntilFound(
                    () => dialogWindow.FindFirstDescendant(conditionFactory => conditionFactory.ByControlType(ControlType.Edit)),
                    ControlTimeout,
                    "the file dialog's file name box (by control type — AutomationId '1148' was not present)");
            }

            return resolved;
        }

        // Wrapped in one try/catch rather than letting a dialog-automation failure throw out of
        // RunAsync: this is the least certain part of the phase (a native OS dialog, plus
        // ShellLauncher.Open launching an uncontrolled external process — see the comment inline
        // below), and a failure here should not cost the Edit/Delete steps that follow it.
        private static List<StepResult> RunCsvExportImport(AppSession session, string artifactFolder, int approvedDeviceCount)
        {
            List<StepResult> steps = new List<StepResult>();
            string exportPath = Path.Combine(artifactFolder, ExportedCsvFileName);

            try
            {
                AutomationElement exportButton = Waits.UntilFound(
                    () => session.MainWindow.FindFirstDescendant("ExportCsvButton"),
                    ControlTimeout,
                    "the Export CSV button");

                exportButton.Click();
                DriveCommonFileDialog(exportPath);

                Waits.Until(() => File.Exists(exportPath), DataChangeTimeout, "the exported CSV file to appear on disk");

                string exportedCsv = File.ReadAllText(exportPath);
                bool exportLooksReal = exportedCsv.Length > 0 && exportedCsv.Contains(RenamedDeviceName, StringComparison.Ordinal);

                if (exportLooksReal)
                {
                    steps.Add(StepResult.Pass("CSV export writes the approved devices to the fixture folder"));
                }
                else
                {
                    steps.Add(StepResult.Fail(
                        "CSV export writes the approved devices to the fixture folder",
                        $"a non-empty CSV at {exportPath} containing '{RenamedDeviceName}'",
                        $"{exportedCsv.Length} byte(s) written; expected text not found"));
                }

                // ApprovedDevicesPage.xaml.cs's ExportButtonClick calls ShellLauncher.Open(exportPath)
                // after writing, which launches the CSV's default handler (typically Excel or
                // Notepad) as a detached process on the real desktop. This phase does not try to
                // find or close it — an arbitrary external app is out of scope for FlaUI to
                // identify reliably, and closing the wrong window would be worse than leaving one open.
                AutomationElement importButton = Waits.UntilFound(
                    () => session.MainWindow.FindFirstDescendant("ImportCsvButton"),
                    ControlTimeout,
                    "the Import CSV button");

                importButton.Click();
                DriveCommonFileDialog(exportPath);

                AutomationElement resultDialog = Waits.UntilFound(
                    () => session.MainWindow.FindFirstDescendant(conditionFactory => conditionFactory.ByClassName("ContentDialog")),
                    DataChangeTimeout,
                    "the import result dialog");

                // The dialog's own Name is its Title ("Import Approved Devices" —
                // ApprovedDevicesPage.xaml.cs's ImportButtonClick), not its Content body, so the
                // "Added N / Approved-or-updated N" message has to be read from a descendant.
                string resultText = DescendantTextContaining(resultDialog, "existing device");
                bool reimportedExistingRows = resultText.Contains(
                    $"Approved/updated {approvedDeviceCount} existing device",
                    StringComparison.Ordinal);

                if (reimportedExistingRows)
                {
                    steps.Add(StepResult.Pass("Re-importing the exported CSV updates the same approved devices"));
                }
                else
                {
                    string actualText = resultText.Length > 0 ? resultText : "(no descendant text contained 'existing device')";

                    steps.Add(StepResult.Fail(
                        "Re-importing the exported CSV updates the same approved devices",
                        $"a result dialog reporting 'Approved/updated {approvedDeviceCount} existing device(s)'",
                        actualText));
                }

                AutomationElement closeButton = Waits.UntilFound(
                    () => resultDialog.FindFirstDescendant(conditionFactory => conditionFactory.ByName("OK")),
                    ControlTimeout,
                    "the import result dialog's OK button");

                closeButton.Click();
            }
            catch (Exception exception)
            {
                steps.Add(StepResult.Fail(
                    "CSV export to the fixture folder and re-import",
                    "the Export CSV / Import CSV round trip to complete without throwing",
                    exception.Message));

                // Fix round 1 (2026-08-20): a real run's Edit and Delete steps both failed
                // immediately after a failed CSV round trip, with UIA-level errors unrelated to
                // either step's own logic ("The requested pattern 'Grid [#10006]' is not
                // supported") — consistent with a native Save/Open dialog left open and modal to
                // the main window, blocking every step that runs after this one. Best-effort
                // recovery so this step's failure stays this step's failure.
                TryDismissStrayFileDialog();
            }

            return steps;
        }

        private static void TryDismissStrayFileDialog()
        {

            try
            {

                using (UIA3Automation automation = new UIA3Automation())
                {
                    AutomationElement desktop = automation.GetDesktop();
                    AutomationElement? strayDialog = desktop.FindFirstDescendant(
                        conditionFactory => conditionFactory.ByClassName(CommonFileDialogClassName));

                    if (strayDialog is not null)
                    {
                        AutomationElement? cancelButton = strayDialog.FindFirstDescendant(
                            conditionFactory => conditionFactory.ByName("Cancel"));

                        cancelButton?.Click();
                    }

                }

            }
            catch (Exception exception)
            {
                Console.WriteLine($"DevicesPhase: could not dismiss a stray file dialog: {exception.Message}");
            }

        }

        private static List<StepResult> RunEditDevice(AppSession session)
        {
            const string stepName = "Editing a device's friendly name updates the grid";
            List<StepResult> steps = new List<StepResult>();

            try
            {
                AutomationElement approvedGrid = WaitForGrid(session, "ApprovedDevicesGrid");
                int rowIndex = FindRowIndexByNameText(approvedGrid, ApprovedNameColumn, EditTargetHostname);

                if (rowIndex < 0)
                {
                    steps.Add(StepResult.Fail(stepName, $"a row whose Name column contains '{EditTargetHostname}'", "no matching row"));
                }
                else
                {
                    ClickRowButton(approvedGrid, rowIndex, ApprovedActionsColumn, "ApprovedEditDeviceButton");

                    AutomationElement dialog = Waits.UntilFound(
                        () => session.MainWindow.FindFirstDescendant(conditionFactory => conditionFactory.ByClassName("ContentDialog")),
                        ControlTimeout,
                        $"the Edit dialog for {EditTargetMac}");

                    // DeviceDialogs.ShowEditDeviceAsync builds this dialog's content by hand with no
                    // AutomationIds (Views/DeviceDialogs.cs:34-65); the friendly-name TextBox is added
                    // before the notes TextBox and the ComboBox in between is not ControlType.Edit, so
                    // it is the first Edit-type descendant in document order. UNVERIFIED for the same
                    // reason as the rest of this phase.
                    AutomationElement[] editBoxes = dialog.FindAllDescendants(conditionFactory => conditionFactory.ByControlType(ControlType.Edit));

                    if (editBoxes.Length == 0)
                    {
                        steps.Add(StepResult.Fail(stepName, "the Edit dialog's friendly name text box", "no Edit control found in the dialog"));
                    }
                    else
                    {
                        IValuePattern nameValuePattern = editBoxes[0].Patterns.Value.Pattern;

                        nameValuePattern.SetValue(EditedFriendlyName);

                        AutomationElement saveButton = Waits.UntilFound(
                            () => dialog.FindFirstDescendant(conditionFactory => conditionFactory.ByName("Save")),
                            ControlTimeout,
                            "the Edit dialog's Save button");

                        saveButton.Click();

                        bool renamed = false;

                        try
                        {
                            Waits.Until(
                                () => FindRowIndexByNameText(approvedGrid, ApprovedNameColumn, EditedFriendlyName) >= 0,
                                DataChangeTimeout,
                                "the Approved grid to show the edited friendly name after Save");

                            renamed = true;
                        }
                        catch (TimeoutException)
                        {
                            renamed = false;
                        }

                        if (renamed)
                        {
                            steps.Add(StepResult.Pass(stepName));
                        }
                        else
                        {
                            steps.Add(StepResult.Fail(
                                stepName,
                                $"a row whose Name column contains '{EditedFriendlyName}' after Save",
                                "no matching row within the timeout"));
                        }

                    }

                }

            }
            catch (Exception exception)
            {
                steps.Add(StepResult.Fail(stepName, "the edit dialog round trip to complete without throwing", exception.Message));
            }

            return steps;
        }

        private static StepResult RunDeleteDevice(AppSession session)
        {
            const string stepName = "Deleting a device drops the Approved count by one";
            StepResult result;

            try
            {
                AutomationElement approvedGrid = WaitForGrid(session, "ApprovedDevicesGrid");
                int countBeforeDelete = GridReader.RowCount(approvedGrid);
                int rowIndex = FindRowIndexByNameText(approvedGrid, ApprovedNameColumn, DeleteTargetHostname);

                if (rowIndex < 0)
                {
                    result = StepResult.Fail(stepName, $"a row whose Name column contains '{DeleteTargetHostname}'", "no matching row");
                }
                else
                {
                    ClickRowButton(approvedGrid, rowIndex, ApprovedActionsColumn, "ApprovedDeleteDeviceButton");

                    AutomationElement confirmDialog = Waits.UntilFound(
                        () => session.MainWindow.FindFirstDescendant(conditionFactory => conditionFactory.ByClassName("ContentDialog")),
                        ControlTimeout,
                        "the delete confirmation dialog");

                    AutomationElement deleteButton = Waits.UntilFound(
                        () => confirmDialog.FindFirstDescendant(conditionFactory => conditionFactory.ByName("Delete")),
                        ControlTimeout,
                        "the delete confirmation dialog's Delete button");

                    deleteButton.Click();

                    int expectedCountAfterDelete = countBeforeDelete - 1;
                    bool dropped = false;

                    try
                    {
                        Waits.Until(
                            () => GridReader.RowCount(approvedGrid) == expectedCountAfterDelete,
                            DataChangeTimeout,
                            "the Approved grid's row count to drop by one after Delete");

                        dropped = true;
                    }
                    catch (TimeoutException)
                    {
                        dropped = false;
                    }

                    if (dropped)
                    {
                        result = StepResult.Pass(stepName);
                    }
                    else
                    {
                        result = StepResult.Fail(
                            stepName,
                            $"{expectedCountAfterDelete} row(s)",
                            $"{GridReader.RowCount(approvedGrid)} row(s) after the timeout");
                    }

                }

            }
            catch (Exception exception)
            {
                result = StepResult.Fail(stepName, "the delete round trip to complete without throwing", exception.Message);
            }

            return result;
        }
    }
}
