using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.Patterns;
using FlaUI.Core.WindowsAPI;
using NetworkMonitor.UITests.Driving;
using NetworkMonitor.UITests.Fixtures;
using NetworkMonitor.UITests.Runner;

namespace NetworkMonitor.UITests.Phases
{
    // Phase 08: the two Purge Now buttons — one-way doors, so both the door and its lock are
    // driven: Cancel must destroy nothing, Purge must destroy exactly what it says. Effects are
    // read out of the fixture database rather than off the status line, because a summary that says
    // "Purged 12 events" is the app's own account of itself. Not abortsRun.
    //
    // It runs last of the driving phases for the obvious reason: it deletes the seeded rows every
    // earlier phase asserts against.
    //
    // The history purge is the one with teeth. Its window is set to a day first, which puts the
    // seeded events — spread across the trailing 48 hours — on both sides of the line, so the
    // assertion is that the old ones went and the recent ones stayed, with both counts read from
    // the database before and after.
    //
    // The traffic purge cannot be given teeth, and that is a finding about the app rather than a
    // gap in this phase. SettingsViewModel.PurgeTrafficAsync deletes raw TrafficEntries older than
    // TrafficPurgeDays, a value measured in DAYS — but TrafficTracker.PurgeRawEntriesAsync already
    // deletes every raw entry older than ONE HOUR, every five minutes, unconditionally. So the
    // button's query can only ever match nothing, whatever the operator sets. ScanWorker's own
    // retention sweep documents exactly this ("deleting them here would only ever match nothing")
    // for the automatic path and purges the rollups instead; the manual button was not given the
    // same treatment and touches no rollup at all. What is asserted here is therefore what the
    // button really does: it runs, it reports, and it destroys nothing else — with the rollup
    // counts checked before and after to prove the last part.
    public static class PurgePhase
    {
        private const string TrafficTabAutomationId = "SettingsTrafficTab";
        private const string DeviceTabAutomationId = "SettingsDeviceTab";

        private const string PurgeTrafficButtonAutomationId = "PurgeTrafficNowButton";
        private const string PurgeHistoryButtonAutomationId = "PurgeHistoryNowButton";
        private const string HistoryPurgeDaysBoxAutomationId = "HistoryPurgeDaysBox";
        private const string NumberBoxInputAutomationId = "InputBox";

        private const string TrafficDialogTitle = "Purge Traffic";
        private const string HistoryDialogTitle = "Purge History";
        private const string ConfirmButtonText = "Purge";
        private const string CancelButtonText = "Cancel";

        private const string DeviceEventsTable = "DeviceEvents";
        private const string TrafficRollupsTable = "TrafficRollups";
        private const string LocalTrafficRollupsTable = "LocalTrafficRollups";

        // The fixture starts with a 7-day window so ScanWorker's startup sweep leaves the stale
        // rollups alone; this is what the window is narrowed to before Purge Now is pressed, and so
        // it is also the cutoff the button will use. Still wider than the six hours of ordinary
        // seeded rollups, which must survive.
        private static readonly TimeSpan PurgeCompletionTimeout = TimeSpan.FromSeconds(15);

        private const int TrafficNarrowedDays = 1;
        private const int TrafficPurgeDaysInFixture = 7;
        private const string TrafficPurgeDaysBoxAutomationId = "TrafficPurgeDaysBox";

        // One day, so the seeded events (nowUtc-47h .. nowUtc-3h) straddle the cutoff and the purge
        // has both something to delete and something to leave alone.
        private const int HistoryWindowDays = 1;
        private const int RestoredHistoryWindowDays = 30;

        private static readonly TimeSpan ControlTimeout = TimeSpan.FromSeconds(10);

        // A purge is one ExecuteDeleteAsync against a small database, but it is awaited through the
        // dialog's own continuation; generous rather than tight.
        private static readonly TimeSpan PurgeTimeout = TimeSpan.FromSeconds(20);

        public static Task<IReadOnlyList<StepResult>> RunAsync(PhaseContext context)
        {
            StepLog steps = new StepLog(context);
            AppSession session = context.Session
                ?? throw new InvalidOperationException(
                    "PurgePhase requires LaunchPhase to have run first and set PhaseContext.Session.");

            Navigator navigator = new Navigator(session);

            navigator.GoTo(NavRoute.Settings);

            RunTrafficPurge(session, context, navigator, steps);
            RunHistoryPurge(session, context, navigator, steps);

            IReadOnlyList<StepResult> result = steps.Steps;
            Task<IReadOnlyList<StepResult>> completed = Task.FromResult(result);

            return completed;
        }

        private static void RunTrafficPurge(AppSession session, PhaseContext context, Navigator navigator, StepLog steps)
        {
            const string stepName = "Purging traffic leaves the rollups the charts read";
            StepResult result;
            StepResult staleResult = StepResult.Fail(StaleRollupStepName, "the purge to run", "the purge did not complete, so nothing could be counted");

            navigator.SelectTab(TrafficTabAutomationId);

            // Narrowed first, exactly as RunHistoryPurge does. Without this the button is a no-op by
            // construction: ScanWorker sweeps the same window at startup, so nothing older than it
            // is ever left for the manual press to find.
            steps.Add(RunTrafficWindowChange(session, context));

            try
            {
                long wanRollupsBefore = FixtureDatabase.CountRows(context.DataFolder, TrafficRollupsTable);
                long lanRollupsBefore = FixtureDatabase.CountRows(context.DataFolder, LocalTrafficRollupsTable);
                long staleCutoffEpoch = StaleCutoffEpoch();
                long staleWanBefore = FixtureDatabase.CountRollupsOlderThan(context.DataFolder, TrafficRollupsTable, staleCutoffEpoch);
                long staleLanBefore = FixtureDatabase.CountRollupsOlderThan(context.DataFolder, LocalTrafficRollupsTable, staleCutoffEpoch);

                ClickButton(session, PurgeTrafficButtonAutomationId, "the Purge Traffic button");
                ConfirmDialog(session, TrafficDialogTitle, ConfirmButtonText);

                // Nothing to wait for on screen — the status line is the app's own summary, and the
                // point of this step is what the database still holds. The dialog closing is the
                // signal that the purge has been asked for; the counts below are read after it.
                WaitForDialogToClose(session, TrafficDialogTitle);

                long wanRollupsAfter = FixtureDatabase.CountRows(context.DataFolder, TrafficRollupsTable);
                long lanRollupsAfter = FixtureDatabase.CountRows(context.DataFolder, LocalTrafficRollupsTable);
                // The dialog closing means the purge was ASKED FOR, not that it finished:
                // PurgeTrafficAsync is async and the deletion lands afterwards. The old assertion
                // here only checked that rollups survived, which passed either way and hid the race;
                // asserting a deletion needs an actual wait for it.
                WaitForStaleRollupsToClear(context, staleCutoffEpoch);

                long staleWanAfter = FixtureDatabase.CountRollupsOlderThan(context.DataFolder, TrafficRollupsTable, staleCutoffEpoch);
                long staleLanAfter = FixtureDatabase.CountRollupsOlderThan(context.DataFolder, LocalTrafficRollupsTable, staleCutoffEpoch);

                staleResult = BuildStaleRollupResult(staleWanBefore, staleLanBefore, staleWanAfter, staleLanAfter);
                bool rollupsIntact = wanRollupsAfter >= wanRollupsBefore && lanRollupsAfter >= lanRollupsBefore;

                if (rollupsIntact)
                {
                    result = StepResult.Pass(stepName);
                }
                else
                {
                    result = StepResult.Fail(
                        stepName,
                        $"the rollups to survive a raw-entry purge ({wanRollupsBefore} WAN, {lanRollupsBefore} LAN)",
                        $"{wanRollupsAfter} WAN and {lanRollupsAfter} LAN rollups remain");
                }

            }
            catch (Exception failure)
            {
                AppDialogs.DismissIfOpen(session);

                result = StepResult.Fail(stepName, "the traffic purge to run and leave the rollups alone", failure.Message);
            }

            steps.Add(result);
            steps.Add(staleResult);
            steps.Add(RunRestoreTrafficWindow(session, context));
        }

        private const string StaleRollupStepName = "Purging traffic deletes rollups older than the retention window";

        // Best-effort: on timeout the counts are read anyway and the assertion reports what is
        // actually left, which is a better failure than a timeout message.
        private static void WaitForStaleRollupsToClear(PhaseContext context, long staleCutoffEpoch)
        {

            try
            {
                Waits.Until(
                    () =>
                    {
                        long wanLeft = FixtureDatabase.CountRollupsOlderThan(context.DataFolder, TrafficRollupsTable, staleCutoffEpoch);
                        long lanLeft = FixtureDatabase.CountRollupsOlderThan(context.DataFolder, LocalTrafficRollupsTable, staleCutoffEpoch);

                        bool cleared = wanLeft == 0L && lanLeft == 0L;

                        return cleared;
                    },
                    PurgeCompletionTimeout,
                    "the traffic purge to delete the rollups older than the retention window");
            }
            catch (TimeoutException)
            {
            }

        }

        private static StepResult RunTrafficWindowChange(AppSession session, PhaseContext context)
        {
            string stepName = $"The traffic retention window is narrowed to {TrafficNarrowedDays} days";
            StepResult result;

            try
            {
                SetNumberBoxValue(session, TrafficPurgeDaysBoxAutomationId, TrafficNarrowedDays);
                WaitForSetting(context, "TrafficPurgeDays", TrafficNarrowedDays.ToString());

                result = StepResult.Pass(stepName);
            }
            catch (Exception failure)
            {
                result = StepResult.Fail(stepName, $"TrafficPurgeDays to become {TrafficNarrowedDays}", failure.Message);
            }

            return result;
        }

        private static StepResult RunRestoreTrafficWindow(AppSession session, PhaseContext context)
        {
            string stepName = $"The traffic retention window is restored to {TrafficPurgeDaysInFixture} days";
            StepResult result;

            try
            {
                SetNumberBoxValue(session, TrafficPurgeDaysBoxAutomationId, TrafficPurgeDaysInFixture);
                WaitForSetting(context, "TrafficPurgeDays", TrafficPurgeDaysInFixture.ToString());

                result = StepResult.Pass(stepName);
            }
            catch (Exception failure)
            {
                result = StepResult.Fail(stepName, $"TrafficPurgeDays to return to {TrafficPurgeDaysInFixture}", failure.Message);
            }

            return result;
        }

        // The cutoff the app itself uses: TrafficPurgeDays before now, as seconds since the Unix
        // epoch, which is how the rollup tables are keyed.
        private static long StaleCutoffEpoch()
        {
            DateTime cutoffUtc = DateTime.UtcNow.AddDays(-TrafficNarrowedDays);
            long epochSeconds = (long)(cutoffUtc - DateTime.UnixEpoch).TotalSeconds;

            return epochSeconds;
        }

        // The fixture seeds a handful of rollups deliberately outside the retention window so this
        // button has something it is supposed to delete. Until 2026-08-23 it had nothing: it queried
        // raw TrafficEntries older than TrafficPurgeDays, and TrafficTracker had already deleted
        // every raw entry older than an hour, so the button reported "Purged 0 entries" and did
        // nothing. It now runs the same sweep ScanWorker does, and this asserts the result.
        private static StepResult BuildStaleRollupResult(long staleWanBefore, long staleLanBefore, long staleWanAfter, long staleLanAfter)
        {
            StepResult result;

            if (staleWanBefore <= 0L || staleLanBefore <= 0L)
            {
                result = StepResult.Fail(
                    StaleRollupStepName,
                    $"the fixture to hold rollups older than the retention window ({SeedDatabase.StaleRollupsPerTable} per table)",
                    $"{staleWanBefore} WAN and {staleLanBefore} LAN stale rollups were present before the purge");
            }
            else if (staleWanAfter == 0L && staleLanAfter == 0L)
            {
                result = StepResult.Pass(StaleRollupStepName);
            }
            else
            {
                result = StepResult.Fail(
                    StaleRollupStepName,
                    $"every rollup older than the window to be deleted (was {staleWanBefore} WAN, {staleLanBefore} LAN)",
                    $"{staleWanAfter} WAN and {staleLanAfter} LAN stale rollups remain");
            }

            return result;
        }

        private static void RunHistoryPurge(AppSession session, PhaseContext context, Navigator navigator, StepLog steps)
        {
            navigator.SelectTab(DeviceTabAutomationId);

            steps.Add(RunHistoryWindowChange(session, context));
            steps.Add(RunHistoryCancel(session, context));
            steps.Add(RunHistoryConfirm(session, context));
            steps.Add(RunRestoreHistoryWindow(session, context));
        }

        private static StepResult RunHistoryWindowChange(AppSession session, PhaseContext context)
        {
            string stepName = $"The history retention window is narrowed to {HistoryWindowDays} day";
            StepResult result;

            try
            {
                SetNumberBoxValue(session, HistoryPurgeDaysBoxAutomationId, HistoryWindowDays);
                WaitForSetting(context, "HistoryPurgeDays", HistoryWindowDays.ToString());

                result = StepResult.Pass(stepName);
            }
            catch (Exception failure)
            {
                result = StepResult.Fail(stepName, $"HistoryPurgeDays to become {HistoryWindowDays}", failure.Message);
            }

            return result;
        }

        // The lock before the door: a one-way action's Cancel has to be as trustworthy as its
        // confirmation, and nothing else in this suite proves that a confirmation dialog's Cancel
        // means cancel.
        private static StepResult RunHistoryCancel(AppSession session, PhaseContext context)
        {
            const string stepName = "Cancelling the history purge destroys nothing";
            StepResult result;

            try
            {
                long eventsBefore = FixtureDatabase.CountRows(context.DataFolder, DeviceEventsTable);

                ClickButton(session, PurgeHistoryButtonAutomationId, "the Purge History button");
                ConfirmDialog(session, HistoryDialogTitle, CancelButtonText);
                WaitForDialogToClose(session, HistoryDialogTitle);

                long eventsAfter = FixtureDatabase.CountRows(context.DataFolder, DeviceEventsTable);

                if (eventsAfter == eventsBefore)
                {
                    result = StepResult.Pass(stepName);
                }
                else
                {
                    result = StepResult.Fail(stepName, $"all {eventsBefore} events to survive Cancel", $"{eventsAfter} remain");
                }

            }
            catch (Exception failure)
            {
                AppDialogs.DismissIfOpen(session);

                result = StepResult.Fail(stepName, "Cancel to leave the history alone", failure.Message);
            }

            return result;
        }

        private static StepResult RunHistoryConfirm(AppSession session, PhaseContext context)
        {
            const string stepName = "Purging history deletes exactly the events outside the window";
            StepResult result;

            try
            {
                DateTime cutoffUtc = DateTime.UtcNow.AddDays(-HistoryWindowDays);
                long eventsBefore = FixtureDatabase.CountRows(context.DataFolder, DeviceEventsTable);
                long oldEventsBefore = FixtureDatabase.CountRowsOlderThan(context.DataFolder, DeviceEventsTable, cutoffUtc);

                ClickButton(session, PurgeHistoryButtonAutomationId, "the Purge History button");
                ConfirmDialog(session, HistoryDialogTitle, ConfirmButtonText);
                WaitForDialogToClose(session, HistoryDialogTitle);

                long expectedAfter = eventsBefore - oldEventsBefore;

                Waits.Until(
                    () => FixtureDatabase.CountRows(context.DataFolder, DeviceEventsTable) == expectedAfter,
                    PurgeTimeout,
                    $"the history to fall from {eventsBefore} events to {expectedAfter} after purging the {oldEventsBefore} outside the window");

                long oldEventsAfter = FixtureDatabase.CountRowsOlderThan(context.DataFolder, DeviceEventsTable, cutoffUtc);

                if (oldEventsAfter == 0 && oldEventsBefore > 0)
                {
                    result = StepResult.Pass(stepName);
                }
                else if (oldEventsBefore == 0)
                {
                    result = StepResult.Fail(
                        stepName,
                        "at least one seeded event older than the retention window, so the purge has something to delete",
                        "every event was inside the window — the fixture's 48-hour spread is not reaching this phase");
                }
                else
                {
                    result = StepResult.Fail(stepName, "no events left older than the cutoff", $"{oldEventsAfter} remain");
                }

            }
            catch (Exception failure)
            {
                AppDialogs.DismissIfOpen(session);

                result = StepResult.Fail(stepName, "the events outside the retention window to be deleted", failure.Message);
            }

            return result;
        }

        private static StepResult RunRestoreHistoryWindow(AppSession session, PhaseContext context)
        {
            string stepName = $"The history retention window is put back to {RestoredHistoryWindowDays} days";
            StepResult result;

            try
            {
                SetNumberBoxValue(session, HistoryPurgeDaysBoxAutomationId, RestoredHistoryWindowDays);
                WaitForSetting(context, "HistoryPurgeDays", RestoredHistoryWindowDays.ToString());

                result = StepResult.Pass(stepName);
            }
            catch (Exception failure)
            {
                result = StepResult.Fail(stepName, $"HistoryPurgeDays to become {RestoredHistoryWindowDays} again", failure.Message);
            }

            return result;
        }

        // Found by the Title the page sets, which is the string the app itself controls — searching
        // by class name for a ContentDialog failed repeatedly in real runs during Task 8.
        private static void ConfirmDialog(AppSession session, string dialogTitle, string buttonText)
        {
            AutomationElement dialog = Waits.UntilFound(
                () => session.MainWindow.FindFirstDescendant(conditionFactory => conditionFactory.ByName(dialogTitle)),
                ControlTimeout,
                $"the '{dialogTitle}' confirmation dialog");

            AutomationElement button = Waits.UntilFound(
                () => dialog.FindFirstDescendant(conditionFactory => conditionFactory.ByName(buttonText)),
                ControlTimeout,
                $"the '{dialogTitle}' dialog's '{buttonText}' button");

            button.Click();
        }

        private static void WaitForDialogToClose(AppSession session, string dialogTitle)
        {
            Waits.Until(
                () => session.MainWindow.FindFirstDescendant(conditionFactory => conditionFactory.ByName(dialogTitle)) is null,
                ControlTimeout,
                $"the '{dialogTitle}' dialog to close");
        }

        // A real click, not InvokePattern.Invoke(): these handlers open a modal ContentDialog, and
        // although ShowAsync is awaited (so the handler does return), clicking is what every other
        // dialog-opening button in this suite does after the deadlock TrafficPhase's sibling found.
        private static void ClickButton(AppSession session, string buttonAutomationId, string description)
        {
            AutomationElement button = Waits.UntilFound(
                () => session.MainWindow.FindFirstDescendant(buttonAutomationId),
                ControlTimeout,
                description);

            button.Click();
        }

        // Same commit dance SettingsPhase documents: setting the text is not entering it.
        private static void SetNumberBoxValue(AppSession session, string automationId, int value)
        {
            AutomationElement numberBox = Waits.UntilFound(
                () => session.MainWindow.FindFirstDescendant(automationId),
                ControlTimeout,
                $"the '{automationId}' control to appear");

            AutomationElement input = numberBox.FindFirstDescendant(NumberBoxInputAutomationId) ?? numberBox;
            IValuePattern valuePattern = input.Patterns.Value.Pattern;

            valuePattern.SetValue(value.ToString());
            input.Focus();
            Keyboard.Type(VirtualKeyShort.RETURN);
            Keyboard.Type(VirtualKeyShort.TAB);

            NumberBoxDriver.VerifyAccepted(numberBox, input, automationId, value);
        }

        private static void WaitForSetting(PhaseContext context, string settingName, string expectedRawJson)
        {
            Waits.Until(
                () => string.Equals(SettingsFileReader.ReadValue(context.DataFolder, settingName), expectedRawJson, StringComparison.Ordinal),
                ControlTimeout,
                $"settings.json to report {settingName}={expectedRawJson}");
        }
    }
}
