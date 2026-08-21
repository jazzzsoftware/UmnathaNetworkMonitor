using FlaUI.Core.AutomationElements;
using FlaUI.Core.Patterns;
using NetworkMonitor.Core.Charting;
using NetworkMonitor.UITests.Driving;
using NetworkMonitor.UITests.Runner;

namespace NetworkMonitor.UITests.Phases
{
    // Phase 04: the Speed Test tab against the fixture's thirty seeded results — the history grid,
    // the two trend charts' own draw summaries, and the range button that widens the charts from
    // the last day to the whole seeded run. Not abortsRun.
    //
    // No real speed test is run. It depends on the operator's internet at that moment, takes tens
    // of seconds and returns a different answer every time; the fixture's SpeedTestEnabled is off
    // for the same reason. That exclusion is recorded as a skipped step here so it appears in the
    // report rather than being silently absent, and belongs in Task 13's "Not covered" list.
    //
    // Unlike the traffic pages, this page has TWO chart roots in the tree, both with the
    // AutomationId "ChartRoot" (SpeedTrendChart's inner grid; the outer control's own
    // AutomationIds are not control-view elements). They are told apart by the range token each
    // publishes — "throughput" and "latency", set per instance in SpeedTestPage.xaml — rather than
    // by their order in the tree.
    //
    // Every figure below is derived from SeedDatabase.BuildSpeedTestResults, which walks thirty
    // hourly results with download rising 2.4 Mb/s an hour to 149.6 at the newest, upload to 24.5,
    // and latency falling to 19.3ms. Two consequences worth stating:
    //
    // 1. The charts open on their own 24-hour default (SpeedTestViewModel.ChartRangeHours = 24,
    //    not a persisted setting), which holds 24 of the 30 seeded results — the newest through the
    //    one seeded 23 hours back. 7d holds all 30.
    // 2. The throughput peak is the newest result either way, so it is the same 149 in both
    //    ranges. The latency peak is the OLDEST result, so it genuinely differs between the two —
    //    which is what makes it worth asserting that the range button changed what was drawn.
    public static class SpeedTestPhase
    {
        private const string SpeedTestTabAutomationId = "SpeedTestTab";
        private const string HistoryGridAutomationId = "SpeedTestHistoryGrid";
        private const string Range7dButtonAutomationId = "SpeedTestRange7dButton";
        private const string ChartRootAutomationId = "ChartRoot";
        private const string DownloadBitsTextAutomationId = "DownloadBitsText";
        private const string UploadBitsTextAutomationId = "UploadBitsText";

        private const string ThroughputRange = "throughput";
        private const string LatencyRange = "latency";
        private const string ThroughputSeries = "download,upload";
        private const string LatencySeries = "latency,jitter";

        private const int DownloadColumn = 1;

        // Thirty seeded results, of which the trailing 24 hours holds 24 (see finding 1 above).
        private const int SeededResultCount = 30;
        private const int DefaultRangeBuckets = 24;

        // Newest result: 80.0 + 29 x 2.4 = 149.6 Mb/s down, 10.0 + 29 x 0.5 = 24.5 up. The charts
        // publish whole numbers (the summary casts), the grid and the "Last Test" tiles render one
        // decimal place.
        private const string NewestDownloadText = "149.6";
        private const string OldestDownloadText = "80.0";
        private const string NewestDownloadRateText = "149.6 Mb/s";
        private const string NewestUploadRateText = "24.5 Mb/s";
        private const long ThroughputPeak = 149L;

        // Latency runs the other way — 28.0ms at the oldest result, 19.3 at the newest — so the
        // 24-hour window's peak is the result seeded 23 hours back (28.0 - 6 x 0.3 = 26.2, drawn as
        // 26) and the 7-day window's is the oldest of all thirty (28.0, drawn as 28).
        private const long LatencyPeakWithinDay = 26L;
        private const long LatencyPeakWithinWeek = 28L;

        private static readonly TimeSpan ControlTimeout = TimeSpan.FromSeconds(10);

        private static readonly TimeSpan ChartRedrawTimeout = TimeSpan.FromSeconds(20);

        public static Task<IReadOnlyList<StepResult>> RunAsync(PhaseContext context)
        {
            StepLog steps = new StepLog(context);
            AppSession session = context.Session
                ?? throw new InvalidOperationException(
                    "SpeedTestPhase requires LaunchPhase to have run first and set PhaseContext.Session.");

            Navigator navigator = new Navigator(session);

            navigator.GoTo(NavRoute.Traffic);
            navigator.SelectTab(SpeedTestTabAutomationId);

            AutomationElement historyGrid = WaitForGrid(session, HistoryGridAutomationId);

            steps.Add(AssertRowCount("The history grid lists every seeded speed test", historyGrid, context.Seed.SpeedTestResults));
            steps.Add(AssertCellText("The newest result leads the history grid", historyGrid, 0, DownloadColumn, NewestDownloadText));
            steps.Add(AssertCellText("The oldest result closes it, showing the seeded upward trend", historyGrid, context.Seed.SpeedTestResults - 1, DownloadColumn, OldestDownloadText));
            steps.Add(AssertText(session, "The Last Test tile reports the newest download", DownloadBitsTextAutomationId, NewestDownloadRateText));
            steps.Add(AssertText(session, "The Last Test tile reports the newest upload", UploadBitsTextAutomationId, NewestUploadRateText));

            steps.AddRange(RunChart(session, "throughput", ThroughputRange, ThroughputSeries, DefaultRangeBuckets, ThroughputPeak));
            steps.AddRange(RunChart(session, "latency", LatencyRange, LatencySeries, DefaultRangeBuckets, LatencyPeakWithinDay));

            InvokeButton(session, Range7dButtonAutomationId);

            steps.AddRange(RunChart(session, "throughput", ThroughputRange, ThroughputSeries, SeededResultCount, ThroughputPeak));
            steps.AddRange(RunChart(session, "latency", LatencyRange, LatencySeries, SeededResultCount, LatencyPeakWithinWeek));

            steps.Add(StepResult.Skip(
                "A real speed test runs and records a result",
                "Deliberately not driven: a real test depends on the operator's internet at that moment, is "
                + "non-deterministic and takes tens of seconds. The Run Speed Test button is therefore exercised "
                + "by nothing in this suite, and the fixture keeps SpeedTestEnabled off so the background worker "
                + "does not add results mid-run either."));

            IReadOnlyList<StepResult> result = steps.Steps;
            Task<IReadOnlyList<StepResult>> completed = Task.FromResult(result);

            return completed;
        }

        // Waits for the chart to publish a summary for its own range token, then reads everything
        // else off that one summary. A timeout is this chart's failure and skips the assertions
        // that depend on it, rather than letting them read whatever the previous range drew.
        private static List<StepResult> RunChart(
            AppSession session,
            string chartLabel,
            string rangeToken,
            string expectedSeries,
            int expectedBuckets,
            long expectedPeak)
        {
            List<StepResult> steps = new List<StepResult>();
            string drawStepName = $"The {chartLabel} chart reports drawing {expectedBuckets} results";

            try
            {
                ChartDrawValues values = WaitForChartSummary(session, rangeToken, expectedBuckets);

                steps.Add(StepResult.Pass(drawStepName));

                if (string.Equals(values.Series, expectedSeries, StringComparison.Ordinal))
                {
                    steps.Add(StepResult.Pass($"The {chartLabel} chart names the series it drew"));
                }
                else
                {
                    steps.Add(StepResult.Fail($"The {chartLabel} chart names the series it drew", $"series={expectedSeries}", $"series={values.Series}"));
                }

                if (values.Peak == expectedPeak)
                {
                    steps.Add(StepResult.Pass($"The {chartLabel} chart's peak matches the seeded results"));
                }
                else
                {
                    steps.Add(StepResult.Fail($"The {chartLabel} chart's peak matches the seeded results", $"peak={expectedPeak}", $"peak={values.Peak}"));
                }

                if (values.Peak <= values.Scale)
                {
                    steps.Add(StepResult.Pass($"The {chartLabel} chart's axis contains its own peak"));
                }
                else
                {
                    steps.Add(StepResult.Fail($"The {chartLabel} chart's axis contains its own peak", "peak <= scale", $"peak={values.Peak} scale={values.Scale}"));
                }

            }
            catch (TimeoutException redrawTimeout)
            {
                steps.Add(StepResult.Fail(drawStepName, $"a summary reporting range={rangeToken} with buckets={expectedBuckets}", redrawTimeout.Message));
                steps.Add(StepResult.Skip($"The {chartLabel} chart names the series it drew", "The chart never reported drawing this range (see the previous step)."));
                steps.Add(StepResult.Skip($"The {chartLabel} chart's peak matches the seeded results", "The chart never reported drawing this range (see the previous step)."));
                steps.Add(StepResult.Skip($"The {chartLabel} chart's axis contains its own peak", "The chart never reported drawing this range (see the previous step)."));
            }

            return steps;
        }

        // Matches on the bucket count as well as the range token, because both charts keep their
        // token across a range change — the count is what proves the redraw for the new range has
        // actually landed rather than the previous summary still being on the element.
        private static ChartDrawValues WaitForChartSummary(AppSession session, string rangeToken, int expectedBuckets)
        {
            ChartDrawValues found = default;

            Waits.Until(
                () =>
                {
                    bool matched = TryReadChartSummary(session, rangeToken, expectedBuckets, out ChartDrawValues candidate);

                    if (matched)
                    {
                        found = candidate;
                    }

                    return matched;
                },
                ChartRedrawTimeout,
                $"the '{rangeToken}' chart to publish a draw summary with buckets={expectedBuckets}");

            return found;
        }

        private static bool TryReadChartSummary(AppSession session, string rangeToken, int expectedBuckets, out ChartDrawValues values)
        {
            values = default;
            bool matched = false;

            try
            {
                AutomationElement[] chartRoots = session.MainWindow.FindAllDescendants(conditionFactory => conditionFactory.ByAutomationId(ChartRootAutomationId));

                foreach (AutomationElement chartRoot in chartRoots)
                {
                    bool parsed = ChartDrawSummary.TryParse(chartRoot.Name ?? string.Empty, out ChartDrawValues candidate);

                    if (parsed && string.Equals(candidate.Range, rangeToken, StringComparison.Ordinal) && candidate.Buckets == expectedBuckets)
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

        private static StepResult AssertRowCount(string stepName, AutomationElement grid, int expectedCount)
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

        private static StepResult AssertCellText(string stepName, AutomationElement grid, int row, int column, string expectedText)
        {
            StepResult result;

            if (row < 0 || row >= GridReader.RowCount(grid))
            {
                result = StepResult.Fail(stepName, $"a row at index {row}", $"the grid has {GridReader.RowCount(grid)} row(s)");
            }
            else
            {
                string cellText = GridReader.CellText(grid, row, column);

                if (string.Equals(cellText, expectedText, StringComparison.Ordinal))
                {
                    result = StepResult.Pass(stepName);
                }
                else
                {
                    result = StepResult.Fail(stepName, $"'{expectedText}'", $"'{cellText}'");
                }

            }

            return result;
        }

        private static StepResult AssertText(AppSession session, string stepName, string automationId, string expectedText)
        {
            AutomationElement? element = session.MainWindow.FindFirstDescendant(automationId);
            StepResult result;

            if (element is null)
            {
                result = StepResult.Fail(stepName, $"an element with AutomationId '{automationId}'", "no such element");
            }
            else
            {
                string actualText = element.Name ?? string.Empty;

                if (string.Equals(actualText, expectedText, StringComparison.Ordinal))
                {
                    result = StepResult.Pass(stepName);
                }
                else
                {
                    result = StepResult.Fail(stepName, $"'{expectedText}'", $"'{actualText}'");
                }

            }

            return result;
        }

        private static AutomationElement WaitForGrid(AppSession session, string gridAutomationId)
        {
            AutomationElement grid = Waits.UntilFound(
                () => session.MainWindow.FindFirstDescendant(gridAutomationId),
                ControlTimeout,
                $"the '{gridAutomationId}' grid to appear");

            return grid;
        }

        private static void InvokeButton(AppSession session, string buttonAutomationId)
        {
            AutomationElement button = Waits.UntilFound(
                () => session.MainWindow.FindFirstDescendant(buttonAutomationId),
                ControlTimeout,
                $"the '{buttonAutomationId}' button to appear");

            IInvokePattern invokePattern = button.Patterns.Invoke.Pattern;

            invokePattern.Invoke();
        }
    }
}
