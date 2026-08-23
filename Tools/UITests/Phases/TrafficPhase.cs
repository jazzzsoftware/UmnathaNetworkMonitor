using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Patterns;
using NetworkMonitor.Core.Charting;
using NetworkMonitor.UITests.Driving;
using NetworkMonitor.UITests.Fixtures;
using NetworkMonitor.UITests.Runner;

namespace NetworkMonitor.UITests.Phases
{
    // Phase 03: the Traffic page's Internet and Local tabs against the fixture's seeded WAN and LAN
    // traffic — the chart's own draw summary per range, the app/device rows and their chips, the
    // By-app/By-device lens, the drill-down, and clicking a chart bucket to pin the grid to it.
    // Not abortsRun: a failed assertion is recorded and the run continues, and only a missing tab,
    // grid or chart (the environment, not a value) is allowed to throw.
    //
    // Four things learned writing this, all of which shaped what is asserted below:
    //
    // 1. The chart controls' own AutomationIds ("InternetAreaChart", "LocalAreaChart", Task 7) are
    //    NOT in the UIA tree — a UserControl with no automation properties of its own is not a
    //    control-view element. What is reachable is the inner Grid the summary is published on,
    //    whose x:Name ("ChartRoot") WinUI surfaces as its AutomationId. Both traffic pages have
    //    exactly one; the Speed Test page has two, which is why SpeedTestPhase resolves them by the
    //    range token in the summary rather than by position.
    // 2. The peak is asserted as a floor, never an equality. The app under test captures real
    //    traffic into the fixture database for the whole time the suite drives it, so a bucket can
    //    only ever come out at or above what was seeded. An equality here would fail the moment the
    //    operator's machine did anything on the network.
    // 3. That floor only exists for windows a minute or wider. Those read TrafficRollups /
    //    LocalTrafficRollups, seeded across the trailing six hours, so the newest seeded minute is
    //    still inside the window however long the run takes to get here. The 5-minute window reads
    //    raw entries, and the fixture's raw rows only span the five minutes before seeding — by the
    //    time this phase runs they have usually aged out. That window is therefore asserted
    //    structurally (it redrew, at the right bucket count, with a peak inside its own axis) and
    //    its floor step is explicitly skipped with the reason, rather than asserted loosely.
    // 4. Bucket counts are exact and deliberately different per range: the 5-minute window buckets
    //    at Settings.TrafficIntervalSeconds (pinned to 1 by DataFolderFixture) and the wider two at
    //    a minute and six minutes, so 300/60/60 also proves the range button actually changed the
    //    window rather than only the label.
    public static class TrafficPhase
    {
        private const string InternetTabAutomationId = "InternetTab";
        private const string LocalTabAutomationId = "LocalTab";

        private const string InternetGridAutomationId = "InternetAppGrid";
        private const string LocalGridAutomationId = "LocalAppGrid";

        private const string ChartRootAutomationId = "ChartRoot";
        private const string ModeTextAutomationId = "ModeText";
        private const string ChartLabelAutomationId = "ChartLabel";

        private const string InternetRange5mButtonAutomationId = "InternetRange5mButton";
        private const string InternetRange1hButtonAutomationId = "InternetRange1hButton";
        private const string InternetRange6hButtonAutomationId = "InternetRange6hButton";
        private const string LocalRange5mButtonAutomationId = "LocalRange5mButton";
        private const string LocalRange1hButtonAutomationId = "LocalRange1hButton";
        private const string LocalRange6hButtonAutomationId = "LocalRange6hButton";

        private const string LensAppButtonAutomationId = "LensAppButton";
        private const string LensDeviceButtonAutomationId = "LensDeviceButton";

        private const string FiveMinuteRange = "5m";
        private const string HourRange = "1h";
        private const string SixHourRange = "6h";

        // 300 one-second buckets over five minutes, 60 one-minute buckets over an hour, 60
        // six-minute buckets over six hours (InternetViewModel.BucketSizeFor, with the fixture's
        // pinned TrafficIntervalSeconds of 1).
        private const int FiveMinuteBuckets = 300;
        private const int HourBuckets = 60;
        private const int SixHourBuckets = 60;

        private const int GroupNameColumn = 0;
        private const int InternetTotalColumn = 3;
        private const int LocalTotalColumn = 4;

        private const string AllAppsRowName = "All Apps";
        private const string AllDevicesRowName = "All Devices";
        private const string WanProcessOne = "chrome.exe";
        private const string WanProcessTwo = "OneDrive.exe";
        private const string LocalDataProcess = "System";
        private const string LocalFileProcess = "explorer.exe";
        private const string SmbServiceTag = "SMB";
        private const string DiscoveryChipText = "discovery only";
        private const string DiscoveryRowName = "1 device — discovery only";
        private const string NasDeviceName = "nas-media";
        private const string SecondPeerDeviceName = "echo-lounge";

        private const string LiveModeText = "Live";
        private const string HistoryModeText = "History";
        private const string BucketChartLabelPrefix = "Apps at";

        private const string ByAppGroupHeader = "App";
        private const string ByAppChildHeader = "Peers";
        private const string ByDeviceGroupHeader = "Device";
        private const string ByDeviceChildHeader = "Apps";

        // A tab, grid or chart either appears once the page's initial load (a handful of EF queries
        // against the small seeded database) has finished, or it is genuinely missing.
        private static readonly TimeSpan ControlTimeout = TimeSpan.FromSeconds(10);

        // A range button click runs a fresh query and then a Win2D redraw before the summary is
        // republished; twenty seconds is generous headroom on a machine also capturing live
        // traffic, not a wait this is expected to hit.
        private static readonly TimeSpan ChartRedrawTimeout = TimeSpan.FromSeconds(20);

        // Selecting a row, clicking a bucket or switching lens each reload the grid through
        // LoadAsync before the new state is on screen.
        private static readonly TimeSpan DataChangeTimeout = TimeSpan.FromSeconds(15);

        public static Task<IReadOnlyList<StepResult>> RunAsync(PhaseContext context)
        {
            StepLog steps = new StepLog(context);
            AppSession session = context.Session
                ?? throw new InvalidOperationException(
                    "TrafficPhase requires LaunchPhase to have run first and set PhaseContext.Session.");

            Navigator navigator = new Navigator(session);

            navigator.GoTo(NavRoute.Traffic);
            navigator.SelectTab(InternetTabAutomationId);

            RunInternetTab(session, steps);

            navigator.SelectTab(LocalTabAutomationId);

            RunLocalTab(session, steps);

            IReadOnlyList<StepResult> result = steps.Steps;
            Task<IReadOnlyList<StepResult>> completed = Task.FromResult(result);

            return completed;
        }

        private static void RunInternetTab(AppSession session, StepLog steps)
        {
            RunRange(session, "Internet", InternetRange5mButtonAutomationId, FiveMinuteRange, FiveMinuteBuckets, 0L, steps);
            RunRange(session, "Internet", InternetRange6hButtonAutomationId, SixHourRange, SixHourBuckets, SeedDatabase.WanNewestRollupBucketDownloadBytes, steps);
            RunRange(session, "Internet", InternetRange1hButtonAutomationId, HourRange, HourBuckets, SeedDatabase.WanNewestRollupBucketDownloadBytes, steps);

            AutomationElement internetGrid = WaitForGrid(session, InternetGridAutomationId);

            steps.Add(AssertRowPresent("The Internet grid leads with the All Apps row", internetGrid, GroupNameColumn, AllAppsRowName));
            steps.Add(AssertRowPresent("The Internet grid lists the first seeded WAN app", internetGrid, GroupNameColumn, WanProcessOne));
            steps.Add(AssertRowPresent("The Internet grid lists the second seeded WAN app", internetGrid, GroupNameColumn, WanProcessTwo));
            steps.Add(AssertAllRowHasTraffic("The Internet grid's All Apps row totals the window's traffic", internetGrid, AllAppsRowName, InternetTotalColumn));
            RunBucketSelection(session, "Internet", InternetGridAutomationId, AllAppsRowName, InternetTotalColumn, steps);
        }

        private static void RunLocalTab(AppSession session, StepLog steps)
        {
            RunRange(session, "Local", LocalRange5mButtonAutomationId, FiveMinuteRange, FiveMinuteBuckets, 0L, steps);
            RunRange(session, "Local", LocalRange6hButtonAutomationId, SixHourRange, SixHourBuckets, SeedDatabase.LocalNewestRollupBucketDownloadBytes, steps);
            RunRange(session, "Local", LocalRange1hButtonAutomationId, HourRange, HourBuckets, SeedDatabase.LocalNewestRollupBucketDownloadBytes, steps);

            AutomationElement localGrid = WaitForGrid(session, LocalGridAutomationId);

            steps.Add(AssertColumnHeaders("The Local grid opens on the By-app lens", localGrid, ByAppGroupHeader, ByAppChildHeader));
            steps.Add(AssertRowPresent("The Local grid leads with the All Apps row", localGrid, GroupNameColumn, AllAppsRowName));
            steps.Add(AssertRowPresent("The Local grid lists the app behind the seeded SMB traffic", localGrid, GroupNameColumn, LocalDataProcess));
            steps.Add(AssertRowPresent("The Local grid lists the second app on the same device", localGrid, GroupNameColumn, LocalFileProcess));
            steps.Add(AssertChipOnRow("The SMB service tag is shown on the app that moved the bytes", localGrid, LocalDataProcess, SmbServiceTag));
            steps.Add(AssertRowPresent("Discovery-only chatter is folded into its own row", localGrid, GroupNameColumn, DiscoveryRowName));
            steps.Add(AssertChipOnRow("The folded row carries the discovery chip", localGrid, DiscoveryRowName, DiscoveryChipText));
            RunDrillDown(session, steps);
            RunLensToggle(session, steps);
            RunBucketSelection(session, "Local", LocalGridAutomationId, AllAppsRowName, LocalTotalColumn, steps);
        }

        // One range button, then everything that can be said about what the chart drew for it. The
        // wait for the summary is what proves the redraw happened at all, so a timeout here is
        // reported as this range's failure and its dependent assertions are skipped rather than
        // evaluated against whatever the previous range left on screen.
        private static void RunRange(
            AppSession session,
            string pageLabel,
            string rangeButtonAutomationId,
            string expectedRange,
            int expectedBuckets,
            long peakFloorBytes,
            StepLog steps)
        {
            string redrawStepName = $"The {pageLabel} chart redraws for the {expectedRange} range";

            try
            {
                ChartDrawValues values = SwitchRange(session, rangeButtonAutomationId, expectedRange);

                steps.Add(StepResult.Pass(redrawStepName));
                steps.Add(AssertBucketCount($"The {pageLabel} {expectedRange} chart buckets the window as expected", values, expectedBuckets));
                steps.Add(AssertPeakWithinScale($"The {pageLabel} {expectedRange} chart's axis contains its own peak", values));
                steps.Add(AssertPeakFloor($"The {pageLabel} {expectedRange} chart drew at least the seeded traffic", values, peakFloorBytes));
            }
            catch (TimeoutException redrawTimeout)
            {
                steps.Add(StepResult.Fail(redrawStepName, $"a chart summary reporting range={expectedRange}", redrawTimeout.Message));
                steps.Add(StepResult.Skip($"The {pageLabel} {expectedRange} chart buckets the window as expected", "The chart never reported drawing this range (see the previous step)."));
                steps.Add(StepResult.Skip($"The {pageLabel} {expectedRange} chart's axis contains its own peak", "The chart never reported drawing this range (see the previous step)."));
                steps.Add(StepResult.Skip($"The {pageLabel} {expectedRange} chart drew at least the seeded traffic", "The chart never reported drawing this range (see the previous step)."));
            }
        }

        // Clicks a range button and waits for the chart to report drawing that range. Throws
        // TimeoutException if it never does, which is the caller's cue that everything downstream
        // of the redraw would be reading the previous range.
        private static ChartDrawValues SwitchRange(AppSession session, string rangeButtonAutomationId, string expectedRange)
        {
            InvokeButton(session, rangeButtonAutomationId);

            ChartDrawValues values = WaitForChartSummary(session, expectedRange);

            return values;
        }

        // Selecting a group row opens its details pane, which the row-details template renders only
        // for a row with more than one child (LocalTrafficGroupRow.HasChildren is Children.Count >
        // 1). "System" qualifies because the fixture gives it two peers; the peer that is NOT in the
        // collapsed Peers cell ("nas-media +1" names only the first) is what proves the pane is
        // actually open rather than the row merely being selected.
        //
        // "Staying open" is then checked across a real reload rather than a delay: switching the
        // range reloads every row from the database, and LocalPage.SyncGridSelection is supposed to
        // put the selection back by key afterwards. That is both observable (the chart reports the
        // new range) and the behaviour actually worth testing.
        private static void RunDrillDown(AppSession session, StepLog steps)
        {
            const string openStepName = "Selecting an app with several peers opens its drill-down";
            const string reloadStepName = "Changing the range reloads the Local page under the open drill-down";
            const string stayOpenStepName = "The drill-down stays open across that reload";
            AutomationElement localGrid = WaitForGrid(session, LocalGridAutomationId);
            int systemRowIndex = GridReader.FindRowIndex(localGrid, GroupNameColumn, LocalDataProcess);

            if (systemRowIndex < 0)
            {
                steps.Add(StepResult.Fail(openStepName, $"a row whose App column contains '{LocalDataProcess}'", "no matching row"));
                steps.Add(StepResult.Skip(stayOpenStepName, "The drill-down was never opened (see the previous step)."));
            }
            else
            {
                SelectRow(localGrid, systemRowIndex);

                bool opened = TryWaitForDrillDownPeer(session);

                if (!opened)
                {
                    steps.Add(StepResult.Fail(openStepName, $"the drill-down to list '{SecondPeerDeviceName}', the peer the collapsed row does not name", "no such row in the details pane"));
                    steps.Add(StepResult.Skip(stayOpenStepName, "The drill-down was never opened (see the previous step)."));
                }
                else
                {
                    steps.Add(StepResult.Pass(openStepName));

                    // Only that the reload happened is recorded here, not the four assertions
                    // RunRange makes about what was drawn: the 6h range has already been checked
                    // in full above, and repeating those step names mid-phase made the report read
                    // as though the range sweep had run twice.
                    try
                    {
                        SwitchRange(session, LocalRange6hButtonAutomationId, SixHourRange);

                        steps.Add(StepResult.Pass(reloadStepName));

                        bool stillOpen = TryWaitForDrillDownPeer(session);

                        if (stillOpen)
                        {
                            steps.Add(StepResult.Pass(stayOpenStepName));
                        }
                        else
                        {
                            steps.Add(StepResult.Fail(stayOpenStepName, $"the drill-down still listing '{SecondPeerDeviceName}' after the range reload", "the details pane closed"));
                        }

                        SwitchRange(session, LocalRange1hButtonAutomationId, HourRange);
                    }
                    catch (TimeoutException reloadTimeout)
                    {
                        steps.Add(StepResult.Fail(reloadStepName, "the chart to redraw for the newly selected range", reloadTimeout.Message));
                        steps.Add(StepResult.Skip(stayOpenStepName, "The reload the drill-down had to survive never happened (see the previous step)."));
                    }

                }

            }
        }

        // By device swaps what a group is and what its children are, which the two leading column
        // headers state outright ("App"/"Peers" becomes "Device"/"Apps"), and re-keys the rows onto
        // device names. Switched back at the end so the bucket-selection checks that follow run on
        // the lens the rest of this phase described.
        private static void RunLensToggle(AppSession session, StepLog steps)
        {
            InvokeButton(session, LensDeviceButtonAutomationId);

            AutomationElement deviceLensGrid = WaitForGrid(session, LocalGridAutomationId);

            steps.Add(WaitForColumnHeaders("The By-device lens relabels the grid's columns", deviceLensGrid, ByDeviceGroupHeader, ByDeviceChildHeader));
            steps.Add(WaitForRowPresent("The By-device lens leads with the All Devices row", deviceLensGrid, GroupNameColumn, AllDevicesRowName));
            steps.Add(WaitForRowPresent("The By-device lens groups the seeded LAN traffic by device", deviceLensGrid, GroupNameColumn, NasDeviceName));

            InvokeButton(session, LensAppButtonAutomationId);

            AutomationElement appLensGrid = WaitForGrid(session, LocalGridAutomationId);

            steps.Add(WaitForColumnHeaders("Switching back restores the By-app lens", appLensGrid, ByAppGroupHeader, ByAppChildHeader));
        }

        // Clicking the chart pins both the chart and the grid to the one bucket that was clicked:
        // the badge stops reading "Live", the chart label names the bucket's timestamp, and the All
        // row's total falls to that bucket's traffic alone. The total is compared against itself
        // before and after rather than against a figure computed here, because which bucket the
        // click lands on depends on where the chart is on screen.
        private static void RunBucketSelection(
            AppSession session,
            string pageLabel,
            string gridAutomationId,
            string allRowName,
            int totalColumn,
            StepLog steps)
        {
            string pinStepName = $"Clicking a chart bucket pins the {pageLabel} page to it";
            string filterStepName = $"The pinned bucket filters the {pageLabel} grid";
            string resumeStepName = $"Dismissing the badge returns the {pageLabel} page to live";
            AutomationElement grid = WaitForGrid(session, gridAutomationId);
            int allRowIndex = GridReader.FindRowIndex(grid, GroupNameColumn, allRowName);
            string totalBefore = allRowIndex < 0 ? string.Empty : GridReader.CellText(grid, allRowIndex, totalColumn);

            ClickChart(session);

            bool pinned = TryWaitForModeText(session, HistoryModeText);

            if (!pinned)
            {
                steps.Add(StepResult.Fail(pinStepName, $"the mode badge to read '{HistoryModeText}'", $"it read '{ReadTextOrEmpty(session, ModeTextAutomationId)}'"));
                steps.Add(StepResult.Skip(filterStepName, "The page never pinned to a bucket (see the previous step)."));
                steps.Add(StepResult.Skip(resumeStepName, "The page never pinned to a bucket (see the previous step)."));
            }
            else
            {
                string chartLabel = ReadTextOrEmpty(session, ChartLabelAutomationId);

                if (chartLabel.StartsWith(BucketChartLabelPrefix, StringComparison.Ordinal))
                {
                    steps.Add(StepResult.Pass(pinStepName));
                }
                else
                {
                    steps.Add(StepResult.Fail(pinStepName, $"the chart label to start '{BucketChartLabelPrefix}'", $"it read '{chartLabel}'"));
                }

                steps.Add(AssertTotalChanged(filterStepName, session, gridAutomationId, allRowName, totalColumn, totalBefore));

                ClickText(session, ModeTextAutomationId);

                bool resumed = TryWaitForModeText(session, LiveModeText);

                if (resumed)
                {
                    steps.Add(StepResult.Pass(resumeStepName));
                }
                else
                {
                    steps.Add(StepResult.Fail(resumeStepName, $"the mode badge to read '{LiveModeText}' again", $"it read '{ReadTextOrEmpty(session, ModeTextAutomationId)}'"));
                }

            }
        }

        private static StepResult AssertBucketCount(string stepName, ChartDrawValues values, int expectedBuckets)
        {
            StepResult result;

            if (values.Buckets == expectedBuckets)
            {
                result = StepResult.Pass(stepName);
            }
            else
            {
                result = StepResult.Fail(stepName, $"buckets={expectedBuckets}", $"buckets={values.Buckets}");
            }

            return result;
        }

        private static StepResult AssertPeakWithinScale(string stepName, ChartDrawValues values)
        {
            StepResult result;

            if (values.Peak <= values.Scale)
            {
                result = StepResult.Pass(stepName);
            }
            else
            {
                result = StepResult.Fail(stepName, $"peak <= scale", $"peak={values.Peak} scale={values.Scale}");
            }

            return result;
        }

        // A floor of zero means "this window has no honest floor" rather than "any value passes" —
        // see finding 3 in this file's header for why the 5-minute window is in that position.
        private static StepResult AssertPeakFloor(string stepName, ChartDrawValues values, long peakFloorBytes)
        {
            StepResult result;

            if (peakFloorBytes <= 0L)
            {
                result = StepResult.Skip(
                    stepName,
                    "The 5-minute window reads raw traffic entries, and the fixture's raw rows only cover the five "
                    + "minutes before it was seeded — by the time this phase runs they have aged out of the window. "
                    + "The 1h and 6h windows read rollups spanning six hours and do carry a floor.");
            }
            else if (values.Peak >= peakFloorBytes)
            {
                result = StepResult.Pass(stepName);
            }
            else
            {
                result = StepResult.Fail(stepName, $"peak >= {peakFloorBytes} bytes (the newest seeded minute)", $"peak={values.Peak}");
            }

            return result;
        }

        // The Lens setter assigns GroupHeader/ChildHeader synchronously but rebuilds the rows from
        // a fire-and-forget LoadAsync, so a column-header check is satisfied before any row exists.
        // A row assertion taken straight after a lens switch has to wait for the reload, not for
        // the headers that raced ahead of it.
        private static StepResult WaitForRowPresent(string stepName, AutomationElement grid, int column, string expectedSubstring)
        {
            StepResult result;

            try
            {
                Waits.Until(
                    () =>
                    {
                        int rowIndex = GridReader.FindRowIndex(grid, column, expectedSubstring);

                        bool present = rowIndex >= 0;

                        return present;
                    },
                    DataChangeTimeout,
                    $"a row whose first column contains '{expectedSubstring}'");

                result = StepResult.Pass(stepName);
            }
            catch (TimeoutException)
            {
                result = StepResult.Fail(stepName, $"a row whose first column contains '{expectedSubstring}'", "no matching row");
            }

            return result;
        }

        private static StepResult AssertRowPresent(string stepName, AutomationElement grid, int column, string expectedSubstring)
        {
            int rowIndex = GridReader.FindRowIndex(grid, column, expectedSubstring);
            StepResult result;

            if (rowIndex >= 0)
            {
                result = StepResult.Pass(stepName);
            }
            else
            {
                result = StepResult.Fail(stepName, $"a row whose first column contains '{expectedSubstring}'", "no matching row");
            }

            return result;
        }

        private static StepResult AssertChipOnRow(string stepName, AutomationElement grid, string rowSubstring, string chipText)
        {
            int rowIndex = GridReader.FindRowIndex(grid, GroupNameColumn, rowSubstring);
            StepResult result;

            if (rowIndex < 0)
            {
                result = StepResult.Fail(stepName, $"a row whose first column contains '{rowSubstring}'", "no matching row");
            }
            else
            {
                IReadOnlyList<string> cellTexts = GridReader.CellTexts(grid, rowIndex, GroupNameColumn);
                bool chipShown = cellTexts.Any(text => string.Equals(text, chipText, StringComparison.Ordinal));

                if (chipShown)
                {
                    result = StepResult.Pass(stepName);
                }
                else
                {
                    result = StepResult.Fail(stepName, $"a '{chipText}' chip on that row", $"the row read: {string.Join(" | ", cellTexts)}");
                }

            }

            return result;
        }

        private static StepResult AssertAllRowHasTraffic(string stepName, AutomationElement grid, string allRowName, int totalColumn)
        {
            int rowIndex = GridReader.FindRowIndex(grid, GroupNameColumn, allRowName);
            StepResult result;

            if (rowIndex < 0)
            {
                result = StepResult.Fail(stepName, $"a row whose first column contains '{allRowName}'", "no matching row");
            }
            else
            {
                string totalText = GridReader.CellText(grid, rowIndex, totalColumn);
                bool hasTraffic = totalText.Length > 0 && !string.Equals(totalText, "0 B", StringComparison.Ordinal);

                if (hasTraffic)
                {
                    result = StepResult.Pass(stepName);
                }
                else
                {
                    result = StepResult.Fail(stepName, "a non-zero total", $"'{totalText}'");
                }

            }

            return result;
        }

        private static StepResult AssertTotalChanged(
            string stepName,
            AppSession session,
            string gridAutomationId,
            string allRowName,
            int totalColumn,
            string totalBefore)
        {
            StepResult result;

            if (totalBefore.Length == 0)
            {
                result = StepResult.Skip(stepName, $"The '{allRowName}' row could not be read before the bucket was clicked, so there is nothing to compare against.");
            }
            else
            {
                string totalAfter = string.Empty;
                bool changed = false;

                try
                {
                    Waits.Until(
                        () =>
                        {
                            totalAfter = ReadAllRowTotal(session, gridAutomationId, allRowName, totalColumn);

                            bool differs = totalAfter.Length > 0 && !string.Equals(totalAfter, totalBefore, StringComparison.Ordinal);

                            return differs;
                        },
                        DataChangeTimeout,
                        $"the '{allRowName}' row's total to change from '{totalBefore}' after a bucket was pinned");

                    changed = true;
                }
                catch (TimeoutException)
                {
                    changed = false;
                }

                if (changed)
                {
                    result = StepResult.Pass(stepName);
                }
                else
                {
                    result = StepResult.Fail(stepName, $"a total other than the whole window's '{totalBefore}'", $"'{totalAfter}'");
                }

            }

            return result;
        }

        private static StepResult AssertColumnHeaders(string stepName, AutomationElement grid, string expectedGroupHeader, string expectedChildHeader)
        {
            IReadOnlyList<string> headers = ReadColumnHeaders(grid);
            bool matched = headers.Count >= 2
                && string.Equals(headers[0], expectedGroupHeader, StringComparison.Ordinal)
                && string.Equals(headers[1], expectedChildHeader, StringComparison.Ordinal);
            StepResult result;

            if (matched)
            {
                result = StepResult.Pass(stepName);
            }
            else
            {
                result = StepResult.Fail(stepName, $"columns '{expectedGroupHeader}' and '{expectedChildHeader}'", $"columns: {string.Join(" | ", headers)}");
            }

            return result;
        }

        // The lens change reloads the grid, so the headers are polled rather than read once.
        private static StepResult WaitForColumnHeaders(string stepName, AutomationElement grid, string expectedGroupHeader, string expectedChildHeader)
        {
            StepResult result;

            try
            {
                Waits.Until(
                    () =>
                    {
                        IReadOnlyList<string> headers = ReadColumnHeaders(grid);

                        bool matched = headers.Count >= 2
                            && string.Equals(headers[0], expectedGroupHeader, StringComparison.Ordinal)
                            && string.Equals(headers[1], expectedChildHeader, StringComparison.Ordinal);

                        return matched;
                    },
                    DataChangeTimeout,
                    $"the grid's leading columns to read '{expectedGroupHeader}' and '{expectedChildHeader}'");

                result = StepResult.Pass(stepName);
            }
            catch (TimeoutException)
            {
                result = StepResult.Fail(stepName, $"columns '{expectedGroupHeader}' and '{expectedChildHeader}'", $"columns: {string.Join(" | ", ReadColumnHeaders(grid))}");
            }

            return result;
        }

        private static IReadOnlyList<string> ReadColumnHeaders(AutomationElement grid)
        {
            List<string> headers = new List<string>();

            try
            {
                AutomationElement[] headerItems = grid.FindAllDescendants(conditionFactory => conditionFactory.ByControlType(ControlType.HeaderItem));

                foreach (AutomationElement headerItem in headerItems)
                {
                    headers.Add(headerItem.Name ?? string.Empty);
                }

            }
            catch (Exception)
            {
                headers.Clear();
            }

            IReadOnlyList<string> result = headers;

            return result;
        }

        private static ChartDrawValues WaitForChartSummary(AppSession session, string expectedRange)
        {
            ChartDrawValues found = default;

            Waits.Until(
                () =>
                {
                    bool matched = TryReadChartSummary(session, expectedRange, out ChartDrawValues candidate);

                    if (matched)
                    {
                        found = candidate;
                    }

                    return matched;
                },
                ChartRedrawTimeout,
                $"the chart to publish a draw summary reporting range={expectedRange}");

            return found;
        }

        // Defensive throughout: a redraw can recycle the element between the search and the read,
        // and the whole point of the caller's wait is that a transient failure means "not yet".
        private static bool TryReadChartSummary(AppSession session, string expectedRange, out ChartDrawValues values)
        {
            values = default;
            bool matched = false;

            try
            {
                AutomationElement[] chartRoots = session.MainWindow.FindAllDescendants(conditionFactory => conditionFactory.ByAutomationId(ChartRootAutomationId));

                foreach (AutomationElement chartRoot in chartRoots)
                {
                    bool parsed = ChartDrawSummary.TryParse(chartRoot.Name ?? string.Empty, out ChartDrawValues candidate);

                    if (parsed && string.Equals(candidate.Range, expectedRange, StringComparison.Ordinal))
                    {
                        values = candidate;
                        matched = true;

                        break;
                    }

                }

            }
            catch (Exception)
            {
                matched = false;
            }

            return matched;
        }

        private static bool TryWaitForDrillDownPeer(AppSession session)
        {
            bool found;

            try
            {
                Waits.Until(
                    () => DrillDownListsPeer(session),
                    DataChangeTimeout,
                    $"the drill-down to list '{SecondPeerDeviceName}', the peer only the open details pane names");

                found = true;
            }
            catch (TimeoutException)
            {
                found = false;
            }

            return found;
        }

        // The collapsed row's Peers cell reads "nas-media +1", so the second peer's name appears on
        // screen only once the details pane is open. Offscreen elements are rejected: the details
        // presenter exists in the tree whether or not the row is expanded, and reports itself
        // offscreen while it is not.
        //
        // The name, not the address, on purpose: reading "echo-lounge" rather than "192.168.50.16"
        // also proves LocalTrafficNameResolver mapped the peer's address onto a known device. That
        // is what caught the first run's failure — the seed originally pointed this stream at
        // printer-office, which DevicesPhase deletes two phases earlier, so the peer arrived here
        // unnameable and rendered as a bare IP. Any device this assertion depends on must be one no
        // earlier phase edits or deletes.
        private static bool DrillDownListsPeer(AppSession session)
        {
            bool visible = false;

            try
            {
                AutomationElement? grid = session.MainWindow.FindFirstDescendant(LocalGridAutomationId);

                if (grid is not null)
                {
                    AutomationElement[] peerTexts = grid.FindAllDescendants(conditionFactory => conditionFactory.ByName(SecondPeerDeviceName));

                    foreach (AutomationElement peerText in peerTexts)
                    {

                        if (!peerText.Properties.IsOffscreen.ValueOrDefault)
                        {
                            visible = true;

                            break;
                        }

                    }

                }

            }
            catch (Exception)
            {
                visible = false;
            }

            return visible;
        }

        private static bool TryWaitForModeText(AppSession session, string expectedText)
        {
            bool matched;

            try
            {
                Waits.Until(
                    () => string.Equals(ReadTextOrEmpty(session, ModeTextAutomationId), expectedText, StringComparison.Ordinal),
                    DataChangeTimeout,
                    $"the mode badge to read '{expectedText}'");

                matched = true;
            }
            catch (TimeoutException)
            {
                matched = false;
            }

            return matched;
        }

        private static string ReadAllRowTotal(AppSession session, string gridAutomationId, string allRowName, int totalColumn)
        {
            string total = string.Empty;

            try
            {
                AutomationElement? grid = session.MainWindow.FindFirstDescendant(gridAutomationId);

                if (grid is not null)
                {
                    int rowIndex = GridReader.FindRowIndex(grid, GroupNameColumn, allRowName);

                    if (rowIndex >= 0)
                    {
                        total = GridReader.CellText(grid, rowIndex, totalColumn);
                    }

                }

            }
            catch (Exception)
            {
                total = string.Empty;
            }

            return total;
        }

        private static string ReadTextOrEmpty(AppSession session, string automationId)
        {
            string text = string.Empty;

            try
            {
                AutomationElement? element = session.MainWindow.FindFirstDescendant(automationId);

                if (element is not null)
                {
                    text = element.Name ?? string.Empty;
                }

            }
            catch (Exception)
            {
                text = string.Empty;
            }

            return text;
        }

        private static AutomationElement WaitForGrid(AppSession session, string gridAutomationId)
        {
            AutomationElement grid = Waits.UntilFound(
                () => session.MainWindow.FindFirstDescendant(gridAutomationId),
                ControlTimeout,
                $"the '{gridAutomationId}' grid to appear");

            return grid;
        }

        private static void SelectRow(AutomationElement grid, int rowIndex)
        {
            IGridPattern gridPattern = grid.Patterns.Grid.Pattern;

            GridReader.ScrollRowIntoView(grid, rowIndex);

            AutomationElement cell = gridPattern.GetItem(rowIndex, GroupNameColumn);

            cell.Click();
        }

        // Invoked rather than clicked: a range or lens button is a real Button with an
        // InvokePattern, and invoking it avoids moving the operator's mouse across their screen.
        private static void InvokeButton(AppSession session, string buttonAutomationId)
        {
            AutomationElement button = Waits.UntilFound(
                () => session.MainWindow.FindFirstDescendant(buttonAutomationId),
                ControlTimeout,
                $"the '{buttonAutomationId}' button to appear");

            IInvokePattern invokePattern = button.Patterns.Invoke.Pattern;

            invokePattern.Invoke();
        }

        // The mode badge is a Border with a Tapped handler, and a Border is not a control-view
        // element — the reachable part is the TextBlock inside it, whose click bubbles to the same
        // handler. There is no InvokePattern on either, so this is a real click.
        private static void ClickText(AppSession session, string automationId)
        {
            AutomationElement element = Waits.UntilFound(
                () => session.MainWindow.FindFirstDescendant(automationId),
                ControlTimeout,
                $"the '{automationId}' text to appear");

            element.Click();
        }

        // Clicks the middle of the chart, which TrafficAreaChart's input layer turns into whichever
        // bucket is nearest that x position. Which bucket that is depends on the window's width, so
        // nothing downstream assumes a particular one.
        private static void ClickChart(AppSession session)
        {
            AutomationElement chartRoot = Waits.UntilFound(
                () => session.MainWindow.FindFirstDescendant(ChartRootAutomationId),
                ControlTimeout,
                "the chart to appear");

            chartRoot.Click();
        }
    }
}
