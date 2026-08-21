using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Patterns;
using FlaUI.UIA3;
using NetworkMonitor.UITests.Driving;
using NetworkMonitor.UITests.Evidence;
using NetworkMonitor.UITests.Runner;

namespace NetworkMonitor.UITests.Phases
{
    // Phase 02: All / Approved / Unapproved / History against the fixture SeedDatabase.BuildAsync
    // seeds (SeedCounts), then CSV export/re-import, an edit and a delete on the Approved tab.
    // Not abortsRun — a failed assertion here is recorded and the run continues; only a missing
    // nav item, tab or grid (the environment, not a value) is allowed to throw.
    //
    // Three corrections to what a literal reading of SeedCounts would suggest, all load-bearing
    // for the assertions below:
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
    // 3. ScanWorker scans immediately on startup, not after its first interval (CLAUDE.md's Notes
    //    section, corrected 2026-08-20) — see ScanFlippedOnlineDevicesToOffline below for what
    //    that does to the History count.
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
        private const string CsvExtension = ".csv";

        // The Windows common item dialog (IFileDialog and GetOpenFileNameW both render the same
        // Vista+ dialog on this OS — Win32FileSaveDialog.PickSavePath and OpenFileDialog.Show use
        // one each). "1148" (the file name combo's edit box) and "1" (its accept button) are the
        // well-known, stable AutomationIds for that system dialog — not ids this codebase defines.
        // A Windows message box (the illegal-character error fix round 2 exists because of) also
        // renders as class "#32770" — it is a second, separate top-level window of the same class,
        // not a descendant of the file dialog.
        private const string CommonFileDialogClassName = "#32770";
        private const string CommonFileDialogNameBoxId = "1148";
        private const string CommonFileDialogAcceptButtonId = "1";

        // The file dialog's actual input control carries this exact accessible name (its label),
        // regardless of which AutomationId the current Windows version gives it — fix round 2
        // (2026-08-20): the previous fallback ("the first Edit anywhere in the dialog") found the
        // dialog's search box instead, a real export path was typed into it, and Windows rejected
        // the `\`/`:` characters that box does not accept. Searching by this label is specific
        // rather than positional.
        private const string FileNameLabelText = "File name:";

        // The only seeded device that is both offline and outside AllDevicesViewModel's trailing
        // 24-hour window (see the class comment above) — dropped from All and Unapproved alike.
        private const int ExcludedByTwentyFourHourWindow = 1;

        // Fix round 2 (2026-08-20): this assertion was originally written against
        // SeedCounts.DeviceEvents (18) alone, on the wrong assumption that ScanWorker waits for
        // its first interval before scanning. CLAUDE.md's Notes section has since been corrected
        // (2026-08-20) to record that ScanWorker.RunScanLoopAsync scans immediately on startup,
        // before entering its interval loop. That startup scan runs against the fixture's
        // hardcoded Settings.SubnetBase ("192.168.50", AutoDetectSubnet off), which is not this
        // machine's real subnet, so DeviceTracker.MergeAsync finds none of the fixture's eight
        // seeded "online" devices still reachable and marks all eight newly offline — one
        // Disappeared DeviceEvent per device: 18 seeded + 8 scan-generated = 26.
        //
        // Fix round 3 (2026-08-20): asserting 26 unconditionally was itself a flake — the
        // reviewer's own report showed it landing 18 on 2 of 5 runs, because the History grid was
        // sometimes read before the startup scan had actually finished. The fix is not a wider
        // range (that would swallow a genuine regression exactly as easily as a genuine 18) and
        // not a fixed extra delay (the whole point of Waits is that no wait in this suite is a
        // guess at how long something takes) — it is waiting for the real condition this count
        // depends on: the app's own "a scan has completed" signal is polled below before this
        // value is ever compared against the grid.
        private const int ScanFlippedOnlineDevicesToOffline = 8;

        private const string LastScanTextAutomationId = "LastScanText";
        private const string NoScanYetText = "—";

        // A full 254-host sweep (Settings.StartHost..EndHost) at the fixture's default
        // PingTimeoutMs (150) and MaxParallelPings (50) needs roughly ceil(254/50) batches, well
        // under ten seconds in every real run this task observed; sixty seconds is generous
        // headroom for a slower host, not a wait this is expected to hit.
        private static readonly TimeSpan ScanCompletionTimeout = TimeSpan.FromSeconds(60);

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
            StepLog steps = new StepLog(context);
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

            // Fix round 3 (2026-08-20): the two assertions below both depend on the app's own
            // startup scan having actually finished — read too early, the grid shows the seeded
            // rows alone. Waited for as a real condition, not a fixed delay or a widened range;
            // if it times out, both dependent assertions are recorded as Skipped (with the reason
            // named) rather than evaluated against a state that is not what they describe.
            StepResult scanCompletionStep = AssertScanHasCompleted(session);

            steps.Add(scanCompletionStep);

            if (scanCompletionStep.Outcome == StepOutcome.Passed)
            {
                int expectedHistoryCount = context.Seed.DeviceEvents + ScanFlippedOnlineDevicesToOffline;

                steps.Add(AssertGridRowCount(
                    "The History grid shows the seeded events plus the startup scan's Disappeared events",
                    historyGrid,
                    expectedHistoryCount));
                steps.Add(AssertHistoryHasArrivalsAndDepartures(historyGrid));
            }
            else
            {
                steps.Add(StepResult.Skip(
                    "The History grid shows the seeded events plus the startup scan's Disappeared events",
                    "The startup scan never completed (see the previous step), so this count cannot be evaluated meaningfully."));
                steps.Add(StepResult.Skip(
                    "The History grid shows both arrivals and departures",
                    "The startup scan never completed (see the previous step), so this cannot be evaluated meaningfully."));
            }

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

            IReadOnlyList<StepResult> result = steps.Steps;
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

        // Task 9 moved the body of this to Navigator.SelectTab, now that TrafficPhase and
        // SpeedTestPhase drive tabs of their own and the dance should exist once.
        private static void SelectTab(AppSession session, string tabAutomationId)
        {
            Navigator navigator = new Navigator(session);

            navigator.SelectTab(tabAutomationId);
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
            int rowIndex = GridReader.FindRowIndex(grid, nameColumn, expectedSubstring);
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
            int rowIndex = GridReader.FindRowIndex(grid, nameColumn, hostname);
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

        // Fix round 3 (2026-08-20): the History count's flake traced to reading it before the
        // app's own startup scan (ScanWorker.RunScanLoopAsync — scans immediately, not after its
        // first interval) had actually finished. LastScanText (MainWindow.xaml:79 — x:Name alone
        // is enough for WinUI's default AutomationPeer to expose it as this AutomationId) starts
        // at "—" and MainWindow.xaml.cs's OnScanCompleted sets it to a real timestamp after every
        // scan, including the startup one — the app's own, real signal for "a scan has
        // completed", polled here instead of guessing at how long scanning takes.
        private static StepResult AssertScanHasCompleted(AppSession session)
        {
            const string stepName = "The app's own startup scan completes";
            StepResult result;

            try
            {
                Waits.Until(
                    () => ScanHasCompleted(session),
                    ScanCompletionTimeout,
                    $"'{LastScanTextAutomationId}' to show a real timestamp instead of '{NoScanYetText}' "
                    + "(MainWindow.xaml.cs's OnScanCompleted, fired after every scan including the startup one)");

                result = StepResult.Pass(stepName);
            }
            catch (TimeoutException timeoutException)
            {
                result = StepResult.Fail(
                    stepName,
                    $"'{LastScanTextAutomationId}' to show a real timestamp within {ScanCompletionTimeout.TotalSeconds:0}s",
                    timeoutException.Message);
            }

            return result;
        }

        private static bool ScanHasCompleted(AppSession session)
        {
            bool completed;

            try
            {
                AutomationElement? lastScanText = session.MainWindow.FindFirstDescendant(LastScanTextAutomationId);

                completed = lastScanText is not null && lastScanText.Name.Length > 0 && lastScanText.Name != NoScanYetText;
            }
            catch (Exception)
            {
                completed = false;
            }

            return completed;
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
                string candidateText = UiaText.NameOrEmpty(descendants[descendantIndex]);

                if (candidateText.Contains(marker, StringComparison.Ordinal))
                {
                    matchedText = candidateText;
                }

            }

            return matchedText;
        }

        // Fix round 2 (2026-08-20): a real ContentDialog Save click threw NoClickablePointException
        // — FlaUI cannot resolve a screen point to click on an element that is offscreen or has a
        // zero-sized bounding rectangle, both plausible mid-way through a dialog's open animation.
        // A property read failing (the element going stale) is treated the same as "not ready".
        private static bool IsClickable(AutomationElement element)
        {
            bool clickable;

            try
            {
                System.Drawing.Rectangle boundingRectangle = element.BoundingRectangle;

                clickable = !element.IsOffscreen && boundingRectangle.Width > 0 && boundingRectangle.Height > 0;
            }
            catch (Exception)
            {
                clickable = false;
            }

            return clickable;
        }

        // Fix round 1 (2026-08-20): a real run's Edit/Delete steps both failed waiting for their
        // ContentDialog — "the Edit dialog for 02:00:00:00:00:07" and "the delete confirmation
        // dialog" both timed out even though the row was correctly located (GridReader.FindRowIndex
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

            Invoke(button);
        }

        // Fix round 3 (2026-08-20): investigated and reverted, recorded here so the next person
        // does not retry the same thing. A full stack trace captured from a real run in this
        // session showed Click()'s real OS-level mouse SendInput call throwing Win32Exception
        // (5): Access is denied (GetForegroundWindow() also returns NULL in this session, with
        // the session otherwise reporting itself Active — a genuine, if intermittent,
        // input-simulation restriction specific to this automation session, not a code defect).
        // Switching to InvokePattern (which does not use SendInput) was tried, both as the sole
        // mechanism and as a Click()-first-then-InvokePattern-on-Win32Exception fallback — in
        // both forms it then hung the very same Export button until the underlying UI Automation
        // COM call itself timed out (~45s, FlaUI.Core.Tools.Com.Call -> COMException 0x80131505),
        // consistently, which is worse than Click()'s intermittent failure, not better. By the
        // last of several real runs this round, Click() itself had also become consistently
        // access-denied rather than intermittently so. Both symptoms point to this specific,
        // long-running session's own UI Automation/input subsystem having degraded as the
        // session went on, not to a defect in either mechanism — Click() is kept as the plain,
        // previously-reliable default rather than adding unproven complexity on top of a
        // moving target.
        //
        // This is the same call site the Edit dialog's Save button deliberately still uses
        // Click() directly for — see that call site's own comment.
        private static void Invoke(AutomationElement element)
        {
            element.Click();
        }

        // Wrapped in one try/catch rather than letting a dialog-automation failure throw out of
        // RunAsync: this is the least certain part of the phase (a native OS dialog, plus
        // ShellLauncher.Open launching an uncontrolled external process — see the comment inline
        // below), and a failure here should not cost the Edit/Delete steps that follow it.
        private static List<StepResult> RunCsvExportImport(AppSession session, string artifactFolder, int approvedDeviceCount)
        {
            List<StepResult> steps = new List<StepResult>();
            string exportPath = Path.Combine(artifactFolder, ExportedCsvFileName);

            // Fix round 2 (2026-08-20), operator's ruling: keep driving the real path — Export
            // really does call ShellLauncher.Open, and this phase must keep exercising that, not
            // route around it. But close only what this step's own export opened, which is only
            // safe to promise if nothing of that kind was already running before the click: Excel
            // (and handlers like it) can open a new file as another window inside an *existing*
            // process rather than a new one, and a snapshot taken only afterward could then not
            // tell "the operator's own workbook" apart from "the one this step just wrote" at all.
            // Checked before Export is even clicked, so a real precondition failure is reported
            // by name instead of guessed at.
            string csvHandlerProcessName = ShellFileHandler.ResolveHandlerProcessName(CsvExtension);
            string preExistingHandlerBlocker = ShellFileHandler.FindPreExistingHandlerBlocker(csvHandlerProcessName, CsvExtension);

            if (preExistingHandlerBlocker.Length > 0)
            {
                steps.Add(StepResult.Fail(
                    "CSV export to the fixture folder and re-import",
                    "no instance of the .csv file handler already running before Export is clicked",
                    preExistingHandlerBlocker));
            }
            else
            {

                try
                {
                    AutomationElement exportButton = Waits.UntilFound(
                        () => session.MainWindow.FindFirstDescendant("ExportCsvButton"),
                        ControlTimeout,
                        "the Export CSV button");

                    Invoke(exportButton);
                    SaveFileDialog.SaveTo(exportPath, artifactFolder);

                    Waits.Until(() => File.Exists(exportPath), DataChangeTimeout, "the exported CSV file to appear on disk");

                    // Closed and confirmed BEFORE the file is read back, not after: a real run
                    // showed that ordering matters two ways. Reading first raced the handler's own
                    // startup for the file lock (IOException: "being used by another process") even
                    // though the earlier readability check had just reported it clear — a
                    // check-then-read gap, not a fluke. And a step that threw during the read used
                    // to skip this cleanup entirely, leaving the handler's window stealing focus
                    // into whichever step ran next. The precondition check above guarantees no
                    // instance of this process name was running before the export click, so any
                    // instance found now was started by it.
                    ShellFileHandler.CloseOpenedFile(csvHandlerProcessName, exportPath);
                    AssertForegroundWindowBelongsToAppUnderTest(session);

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

                    AutomationElement importButton = Waits.UntilFound(
                        () => session.MainWindow.FindFirstDescendant("ImportCsvButton"),
                        ControlTimeout,
                        "the Import CSV button");

                    Invoke(importButton);
                    SaveFileDialog.SaveTo(exportPath, artifactFolder);

                    // Fix round 2 (2026-08-20): searching by ByClassName("ContentDialog")
                    // consistently failed to find this dialog across several real runs (the Edit
                    // and Delete confirmation dialogs below hit the identical symptom), even once
                    // the CSV file dialog interaction leading up to it was independently confirmed
                    // working. ApprovedDevicesPage.xaml.cs's ImportButtonClick sets this
                    // ContentDialog's own Title to a fixed, known string, so searching by that
                    // exact Name is a specific, confirmed-available signal rather than a guessed
                    // class name.
                    AutomationElement resultDialog = Waits.UntilFound(
                        () => session.MainWindow.FindFirstDescendant(conditionFactory => conditionFactory.ByName("Import Approved Devices")),
                        DataChangeTimeout,
                        "the import result dialog");

                    // The dialog's own Name is its Title ("Import Approved Devices" —
                    // ApprovedDevicesPage.xaml.cs's ImportButtonClick), not its Content body, so
                    // the "Added N / Approved-or-updated N" message has to be read from a
                    // descendant.
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

                    Invoke(closeButton);
                }
                catch (Exception exception)
                {
                    Console.WriteLine($"DevicesPhase: CSV export/import threw (diagnostic, full detail): {exception}");
                    steps.Add(StepResult.Fail(
                        "CSV export to the fixture folder and re-import",
                        "the Export CSV / Import CSV round trip to complete without throwing",
                        exception.Message));

                    // Fix round 1 (2026-08-20): a real run's Edit and Delete steps both failed
                    // immediately after a failed CSV round trip, with UIA-level errors unrelated
                    // to either step's own logic ("The requested pattern 'Grid [#10006]' is not
                    // supported") — consistent with a native Save/Open dialog left open and modal
                    // to the main window, blocking every step that runs after this one.
                    // Best-effort recovery so this step's failure stays this step's failure.
                    //
                    // Task 10 added the second half. The native dialog is not the only thing that
                    // can be left holding the front: a run threw *after* the import succeeded and
                    // left the app's own "Import Approved Devices" result dialog open, which is
                    // modal to the window and cost the Edit and Delete steps 11 seconds each
                    // waiting for dialogs that could never appear behind it.
                    TryDismissStrayFileDialog();
                    AppDialogs.DismissIfOpen(session);
                }

            }

            return steps;
        }

        // Fix round 2 (2026-08-20): ShellLauncher.Open's handler becoming the foreground window
        // is what actually broke the file-name-box SetValue/click sequence in real runs, not
        // automation flakiness — every subsequent dialog interaction checks this explicitly so a
        // stolen-focus failure is reported by name instead of read as an unexplained timeout.
        // Fix round 3 (2026-08-20): confirmed directly against this run's own session
        // (GetForegroundWindow() queried by hand, immediately, mid-run) that it can legitimately
        // return NULL — no window at all holding focus — even though the session itself is
        // genuinely Active, not locked, not disconnected. That reads as "inconclusive", not "an
        // intruder has focus": a null handle (and the pid 0 GetWindowThreadProcessId reports for
        // one) is not a process this method — or anyone — can meaningfully name or blame, and
        // treating it as a failure produced two real runs' worth of "the foreground window
        // belongs to 'Idle' (pid 0)" that had nothing to do with ShellLauncher.Open. Only a real,
        // different, identifiable process holding focus is a failure.
        private static void AssertForegroundWindowBelongsToAppUnderTest(AppSession session)
        {
            IntPtr foregroundWindowHandle = GetForegroundWindow();

            if (foregroundWindowHandle != IntPtr.Zero)
            {
                GetWindowThreadProcessId(foregroundWindowHandle, out uint foregroundProcessId);

                int appProcessId = session.Application.ProcessId;

                if (foregroundProcessId != 0 && foregroundProcessId != (uint)appProcessId)
                {
                    string intruderTitle = ReadWindowText(foregroundWindowHandle);
                    string intruderProcessName = TryReadProcessName((int)foregroundProcessId);

                    throw new InvalidOperationException(
                        $"The foreground window belongs to '{intruderProcessName}' (pid {foregroundProcessId}, titled "
                        + $"'{intruderTitle}'), not the app under test (pid {appProcessId}). An intruding window — most "
                        + "likely ShellLauncher.Open's CSV handler — has stolen focus and would silently break the next "
                        + "click or SetValue.");
                }

            }

        }

        private static string ReadWindowText(IntPtr windowHandle)
        {
            StringBuilder buffer = new StringBuilder(256);

            GetWindowText(windowHandle, buffer, buffer.Capacity);

            string text = buffer.ToString();

            return text;
        }

        private static string TryReadProcessName(int processId)
        {
            string name;

            try
            {

                using (Process process = Process.GetProcessById(processId))
                {
                    name = process.ProcessName;
                }

            }
            catch (Exception)
            {
                name = $"pid {processId}";
            }

            return name;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr windowHandle, StringBuilder text, int count);

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

                        if (cancelButton is not null)
                        {
                            Invoke(cancelButton);
                        }

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
                // The CSV step immediately before this one can leave ShellLauncher.Open's handler
                // fighting for focus (fix round 2's second finding); checking here too, not only
                // right after that step, catches it even if it was still settling.
                AssertForegroundWindowBelongsToAppUnderTest(session);

                AutomationElement approvedGrid = WaitForGrid(session, "ApprovedDevicesGrid");
                int rowIndex = GridReader.FindRowIndex(approvedGrid, ApprovedNameColumn, EditTargetHostname);

                if (rowIndex < 0)
                {
                    steps.Add(StepResult.Fail(stepName, $"a row whose Name column contains '{EditTargetHostname}'", "no matching row"));
                }
                else
                {
                    ClickRowButton(approvedGrid, rowIndex, ApprovedActionsColumn, "ApprovedEditDeviceButton");

                    // Fix round 2 (2026-08-20): ByClassName("ContentDialog") consistently failed
                    // to find this dialog across several real runs. ApprovedDevicesPage.xaml.cs's
                    // EditButtonClick sets this dialog's Title to "Edit — {MacAddress}" exactly, so
                    // searching by that known Name is specific and confirmed-available rather than
                    // a guessed class name.
                    string editDialogTitle = "Edit — " + EditTargetMac;
                    AutomationElement dialog = Waits.UntilFound(
                        () => session.MainWindow.FindFirstDescendant(conditionFactory => conditionFactory.ByName(editDialogTitle)),
                        ControlTimeout,
                        $"the Edit dialog titled '{editDialogTitle}'");

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

                        // Fix round 2 (2026-08-20): a real run threw NoClickablePointException
                        // here — ContentDialog's own open animation can still be settling into its
                        // final position immediately after the dialog is first found by title.
                        // Waiting for a real, non-degenerate on-screen rectangle before clicking is
                        // a condition, not a blind delay.
                        Waits.Until(
                            () => IsClickable(saveButton),
                            ControlTimeout,
                            "the Edit dialog's Save button to finish animating into a clickable position");

                        // Fix round 3 (2026-08-20): deliberately still Click(), not Invoke() —
                        // this specific NoClickablePointException is one of the two failures the
                        // operator named explicitly to leave alone, not route around by changing
                        // the mechanism that reaches it, even though Invoke() (used everywhere
                        // else in this file as of this round) would very plausibly avoid it too.
                        saveButton.Click();

                        bool renamed = false;

                        try
                        {
                            Waits.Until(
                                () => GridReader.FindRowIndex(approvedGrid, ApprovedNameColumn, EditedFriendlyName) >= 0,
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
                int rowIndex = GridReader.FindRowIndex(approvedGrid, ApprovedNameColumn, DeleteTargetHostname);

                if (rowIndex < 0)
                {
                    result = StepResult.Fail(stepName, $"a row whose Name column contains '{DeleteTargetHostname}'", "no matching row");
                }
                else
                {
                    ClickRowButton(approvedGrid, rowIndex, ApprovedActionsColumn, "ApprovedDeleteDeviceButton");

                    // Fix round 2 (2026-08-20): ByClassName("ContentDialog") consistently failed
                    // to find this dialog across several real runs. DeviceDialogs.ShowDeleteConfirmAsync
                    // sets this dialog's Title to "Delete device?" exactly, so searching by that
                    // known Name is specific and confirmed-available rather than a guessed class name.
                    AutomationElement confirmDialog = Waits.UntilFound(
                        () => session.MainWindow.FindFirstDescendant(conditionFactory => conditionFactory.ByName("Delete device?")),
                        ControlTimeout,
                        "the delete confirmation dialog titled 'Delete device?'");

                    AutomationElement deleteButton = Waits.UntilFound(
                        () => confirmDialog.FindFirstDescendant(conditionFactory => conditionFactory.ByName("Delete")),
                        ControlTimeout,
                        "the delete confirmation dialog's Delete button");

                    Invoke(deleteButton);

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
                Console.WriteLine($"DevicesPhase: delete round trip threw (diagnostic, full detail): {exception}");
                result = StepResult.Fail(stepName, "the delete round trip to complete without throwing", exception.Message);
            }

            return result;
        }
    }
}
