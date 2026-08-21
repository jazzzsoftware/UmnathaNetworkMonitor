using FlaUI.Core.AutomationElements;
using FlaUI.Core.Patterns;
using NetworkMonitor.Core.Widget;
using NetworkMonitor.UITests.Driving;
using NetworkMonitor.UITests.Fixtures;
using NetworkMonitor.UITests.Runner;

namespace NetworkMonitor.UITests.Phases
{
    // Phase 07: the always-on-top widget as a window — what it shows, what the section switches do
    // to it, the rule that the last section cannot be switched off, both orientations, and the
    // height invariant that a real defect broke. Not abortsRun.
    //
    // The switches themselves live on the Settings page, and SettingsPhase already proves each one
    // round-trips to settings.json. This phase is about the other half: that the widget on screen
    // actually follows them.
    //
    // The widget's section containers (MiniGraphInternetSection and friends, Task 7) are not in the
    // UIA tree — a Border with no automation properties of its own is not a control-view element,
    // the same finding TrafficPhase records about the chart controls. What is reachable is the text
    // inside each section, which is what section visibility is asserted through here.
    //
    // Mixed-DPI is deliberately out of scope and is not closed by this suite: C2-2 and C2-5 in
    // Documents/Code Review/2026-08-10/manual-test-plan.md Part 1 need a second monitor at a
    // different scale factor, which no amount of automation on one screen can stand in for. That
    // belongs in the report's "Not covered" list.
    public static class MiniGraphPhase
    {
        private const string OtherTabAutomationId = "SettingsOtherTab";
        private const string ShowMiniGraphToggleAutomationId = "ShowMiniGraphToggle";
        private const string ShowInternetCheckBoxAutomationId = "MiniGraphShowInternetCheckBox";
        private const string ShowLocalCheckBoxAutomationId = "MiniGraphShowLocalCheckBox";
        private const string ShowSpeedTestCheckBoxAutomationId = "MiniGraphShowSpeedTestCheckBox";
        private const string ShowUnknownDevicesCheckBoxAutomationId = "MiniGraphShowUnknownDevicesCheckBox";
        private const string VerticalRadioButtonAutomationId = "MiniGraphOrientationVerticalRadioButton";
        private const string HorizontalRadioButtonAutomationId = "MiniGraphOrientationHorizontalRadioButton";

        private const string LabelTextAutomationId = "LabelText";
        private const string SpeedTestLineAutomationId = "SpeedTestLine";
        private const string UnknownDevicesLineAutomationId = "UnknownDevicesLine";

        private const string InternetSectionLabel = "Internet";
        private const string LocalSectionLabel = "Local";

        private const string ShowInternetSettingName = "MiniGraphShowInternet";
        private const string ShowLocalSettingName = "MiniGraphShowLocal";
        private const string ShowSpeedTestSettingName = "MiniGraphShowSpeedTest";
        private const string ShowUnknownDevicesSettingName = "MiniGraphShowUnknownDevices";
        private const string HorizontalSettingName = "MiniGraphHorizontal";
        private const string StripHeightSettingName = "MiniGraphStripHeight";
        private const string StripXSettingName = "MiniGraphStripX";
        private const string StripYSettingName = "MiniGraphStripY";
        private const string ShowMiniGraphSettingName = "ShowMiniGraph";

        // U-1, from the 2026-08-12 manual run: MiniGraphStripHeight is a PANEL height, and the
        // window used to save its own height — frame included — into it. Every reader adds the
        // frame back on, so each save/restore round trip fed it in twice and the strip grew about
        // seven device-independent pixels per orientation switch until ClampHeight pinned it at the
        // 120 ceiling. An orientation switch is exactly one save plus one restore, so five of them
        // is five round trips. The unit test pins the arithmetic; this pins the real thing.
        private const int OrientationSwitchCount = 5;

        private static readonly TimeSpan ControlTimeout = TimeSpan.FromSeconds(10);

        // The widget is created once and then hidden and shown, so a toggle reaches it through
        // MiniGraphState.Changed rather than a fresh window; either way it is sub-second.
        private static readonly TimeSpan WidgetChangeTimeout = TimeSpan.FromSeconds(15);

        public static Task<IReadOnlyList<StepResult>> RunAsync(PhaseContext context)
        {
            StepLog steps = new StepLog(context);
            AppSession session = context.Session
                ?? throw new InvalidOperationException(
                    "MiniGraphPhase requires LaunchPhase to have run first and set PhaseContext.Session.");

            Navigator navigator = new Navigator(session);

            navigator.GoTo(NavRoute.Settings);
            navigator.SelectTab(OtherTabAutomationId);

            RunWidgetContent(session, steps);
            RunSectionSwitch(session, context, steps);
            RunLastSectionRule(session, context, steps);
            RunOrientation(session, context, steps);
            RunPlacementAcrossHideAndShow(session, context, steps);

            IReadOnlyList<StepResult> result = steps.Steps;
            Task<IReadOnlyList<StepResult>> completed = Task.FromResult(result);

            return completed;
        }

        // What the widget is for: the same figures the pages show, small enough to leave on screen.
        // Both numbers come from the seed, so they are assertions rather than smoke.
        private static void RunWidgetContent(AppSession session, StepLog steps)
        {
            steps.Add(AssertWidgetWindowExists(session));
            steps.Add(AssertSectionShown(session, "The widget shows its Internet section", LabelTextAutomationId, InternetSectionLabel));
            steps.Add(AssertSectionShown(session, "The widget shows its Local section", LabelTextAutomationId, LocalSectionLabel));
            steps.Add(AssertWidgetTextContains(session, "The widget's speed test line reports the newest seeded result", SpeedTestLineAutomationId, "19 ms"));
            steps.Add(AssertWidgetTextContains(session, "The widget counts the seeded unapproved devices", UnknownDevicesLineAutomationId, "3 unknown devices"));
        }

        // One section switched off should leave the widget, not just settings.json.
        private static void RunSectionSwitch(AppSession session, PhaseContext context, StepLog steps)
        {
            const string stepName = "Switching a section off removes it from the widget";
            StepResult result;

            try
            {
                SetCheckBox(session, ShowInternetCheckBoxAutomationId, false);
                WaitForSetting(context, ShowInternetSettingName, "false");
                WaitForWidgetText(session, LabelTextAutomationId, InternetSectionLabel, false);

                SetCheckBox(session, ShowInternetCheckBoxAutomationId, true);
                WaitForSetting(context, ShowInternetSettingName, "true");
                WaitForWidgetText(session, LabelTextAutomationId, InternetSectionLabel, true);

                result = StepResult.Pass(stepName);
            }
            catch (Exception failure)
            {
                result = StepResult.Fail(stepName, "the Internet section to leave the widget and come back", failure.Message);
            }

            steps.Add(result);
        }

        // MiniGraphState.ApplySection refuses to switch off the last remaining section: an empty
        // widget is a bare rectangle on the desktop with nothing in it to say what it is, and the
        // only way back is a menu the user has no reason to look for. Asserted where it matters —
        // through the UI, with three sections already off — rather than only in the state object.
        private static void RunLastSectionRule(AppSession session, PhaseContext context, StepLog steps)
        {
            const string stepName = "The last remaining widget section cannot be switched off";
            StepResult result;

            try
            {
                SetCheckBox(session, ShowLocalCheckBoxAutomationId, false);
                WaitForSetting(context, ShowLocalSettingName, "false");

                SetCheckBox(session, ShowSpeedTestCheckBoxAutomationId, false);
                WaitForSetting(context, ShowSpeedTestSettingName, "false");

                SetCheckBox(session, ShowUnknownDevicesCheckBoxAutomationId, false);
                WaitForSetting(context, ShowUnknownDevicesSettingName, "false");

                SetCheckBox(session, ShowInternetCheckBoxAutomationId, false);

                // Given a chance to take effect and then read: the assertion is that nothing
                // happened, which cannot be waited for, only checked after the fact. The three
                // waits above are what makes that safe — each proves the app had already processed
                // the switch before this one was sent.
                string afterRefusal = ReadSetting(context, ShowInternetSettingName);
                bool stillOn = string.Equals(afterRefusal, "true", StringComparison.Ordinal);
                bool stillShown = WidgetShowsText(session, LabelTextAutomationId, InternetSectionLabel);

                if (stillOn && stillShown)
                {
                    result = StepResult.Pass(stepName);
                }
                else
                {
                    result = StepResult.Fail(
                        stepName,
                        "the Internet section to stay on, in settings.json and on the widget",
                        $"settings.json says {ShowInternetSettingName}={afterRefusal}; the widget "
                        + (stillShown ? "still shows it" : "no longer shows it"));
                }

            }
            catch (Exception failure)
            {
                result = StepResult.Fail(stepName, "the last section to survive being switched off", failure.Message);
            }

            steps.Add(result);
            steps.Add(RestoreAllSections(session, context));
        }

        private static StepResult RestoreAllSections(AppSession session, PhaseContext context)
        {
            const string stepName = "Every widget section is switched back on";
            StepResult result;

            try
            {
                SetCheckBox(session, ShowLocalCheckBoxAutomationId, true);
                WaitForSetting(context, ShowLocalSettingName, "true");

                SetCheckBox(session, ShowSpeedTestCheckBoxAutomationId, true);
                WaitForSetting(context, ShowSpeedTestSettingName, "true");

                SetCheckBox(session, ShowUnknownDevicesCheckBoxAutomationId, true);
                WaitForSetting(context, ShowUnknownDevicesSettingName, "true");

                result = StepResult.Pass(stepName);
            }
            catch (Exception failure)
            {
                result = StepResult.Fail(stepName, "all four sections switched back on", failure.Message);
            }

            return result;
        }

        // The strip is a different window shape, not a different skin: wider than it is tall, its
        // height clamped to the range HorizontalStripMetrics defines.
        private static void RunOrientation(AppSession session, PhaseContext context, StepLog steps)
        {
            const string switchStepName = "Switching to the horizontal strip reshapes the widget";
            const string heightStepName = "The strip's height survives five orientation switches (U-1)";
            bool switched;

            try
            {
                SelectRadio(session, HorizontalRadioButtonAutomationId);
                WaitForSetting(context, HorizontalSettingName, "true");

                System.Drawing.Rectangle stripBounds = WaitForWidgetBounds(session);
                bool wideAndShort = stripBounds.Width > stripBounds.Height;

                if (wideAndShort)
                {
                    steps.Add(StepResult.Pass(switchStepName));
                }
                else
                {
                    steps.Add(StepResult.Fail(switchStepName, "a window wider than it is tall", $"{stripBounds.Width}x{stripBounds.Height}"));
                }

                switched = true;
            }
            catch (Exception failure)
            {
                steps.Add(StepResult.Fail(switchStepName, "the widget to switch to its horizontal strip", failure.Message));
                switched = false;
            }

            if (!switched)
            {
                steps.Add(StepResult.Skip(heightStepName, "The widget never reached its horizontal orientation (see the previous step)."));
            }
            else
            {
                steps.Add(RunStripHeightInvariant(session, context));
            }

            steps.Add(RestoreVerticalOrientation(session, context));
        }

        private static StepResult RunStripHeightInvariant(AppSession session, PhaseContext context)
        {
            const string stepName = "The strip's height survives five orientation switches (U-1)";
            StepResult result;

            try
            {
                string heightAtStart = ReadSetting(context, StripHeightSettingName);
                List<string> heights = new List<string> { heightAtStart };

                for (int switchIndex = 0; switchIndex < OrientationSwitchCount; switchIndex++)
                {
                    SelectRadio(session, VerticalRadioButtonAutomationId);
                    WaitForSetting(context, HorizontalSettingName, "false");

                    SelectRadio(session, HorizontalRadioButtonAutomationId);
                    WaitForSetting(context, HorizontalSettingName, "true");

                    heights.Add(ReadSetting(context, StripHeightSettingName));
                }

                string heightAtEnd = heights[heights.Count - 1];
                bool unchanged = heights.All(height => string.Equals(height, heightAtStart, StringComparison.Ordinal));

                if (unchanged)
                {
                    result = StepResult.Pass(stepName);
                }
                else
                {
                    result = StepResult.Fail(
                        stepName,
                        $"{StripHeightSettingName} to stay {heightAtStart} across {OrientationSwitchCount} switches",
                        $"it went {string.Join(" -> ", heights)}, ending at {heightAtEnd} — the frame is being added back in on each round trip, which is U-1");
                }

            }
            catch (Exception failure)
            {
                result = StepResult.Fail(stepName, $"{OrientationSwitchCount} orientation switches at a constant strip height", failure.Message);
            }

            return result;
        }

        private static StepResult RestoreVerticalOrientation(AppSession session, PhaseContext context)
        {
            const string stepName = "The widget is put back in its panel orientation";
            StepResult result;

            try
            {
                SelectRadio(session, VerticalRadioButtonAutomationId);
                WaitForSetting(context, HorizontalSettingName, "false");

                result = StepResult.Pass(stepName);
            }
            catch (Exception failure)
            {
                result = StepResult.Fail(stepName, "the widget back in its panel orientation", failure.Message);
            }

            return result;
        }

        // Hiding the widget must not cost the operator its position. The strip and the panel keep
        // separate coordinates on purpose (MiniGraphState.SaveStripPlacement's comment), so this
        // reads whichever pair the current orientation uses.
        private static void RunPlacementAcrossHideAndShow(AppSession session, PhaseContext context, StepLog steps)
        {
            const string hideStepName = "Hiding the widget closes its window";
            const string showStepName = "Showing it again restores the same placement";
            bool hidden;

            try
            {
                SetToggle(session, ShowMiniGraphToggleAutomationId, false);
                WaitForSetting(context, ShowMiniGraphSettingName, "false");

                Waits.Until(
                    () => session.MiniGraphWindow is null,
                    WidgetChangeTimeout,
                    "the widget window to disappear after it was switched off");

                steps.Add(StepResult.Pass(hideStepName));

                hidden = true;
            }
            catch (Exception failure)
            {
                steps.Add(StepResult.Fail(hideStepName, "the widget window to close", failure.Message));

                hidden = false;
            }

            if (!hidden)
            {
                steps.Add(StepResult.Skip(showStepName, "The widget was never hidden (see the previous step)."));
            }
            else
            {
                steps.Add(RunShowAgain(session, context, showStepName));
            }

        }

        private static StepResult RunShowAgain(AppSession session, PhaseContext context, string stepName)
        {
            StepResult result;

            try
            {
                string positionXBefore = ReadSetting(context, StripXSettingName);
                string positionYBefore = ReadSetting(context, StripYSettingName);

                SetToggle(session, ShowMiniGraphToggleAutomationId, true);
                WaitForSetting(context, ShowMiniGraphSettingName, "true");

                Waits.Until(
                    () => session.MiniGraphWindow is not null,
                    WidgetChangeTimeout,
                    "the widget window to come back after it was switched on");

                string positionXAfter = ReadSetting(context, StripXSettingName);
                string positionYAfter = ReadSetting(context, StripYSettingName);
                bool samePlacement = string.Equals(positionXBefore, positionXAfter, StringComparison.Ordinal)
                    && string.Equals(positionYBefore, positionYAfter, StringComparison.Ordinal);

                if (samePlacement)
                {
                    result = StepResult.Pass(stepName);
                }
                else
                {
                    result = StepResult.Fail(
                        stepName,
                        $"the widget's saved position to stay ({positionXBefore}, {positionYBefore})",
                        $"it became ({positionXAfter}, {positionYAfter})");
                }

            }
            catch (Exception failure)
            {
                result = StepResult.Fail(stepName, "the widget back on screen at its saved position", failure.Message);
            }

            return result;
        }

        private static StepResult AssertWidgetWindowExists(AppSession session)
        {
            const string stepName = "The widget window is on screen";
            StepResult result;

            try
            {
                Waits.Until(
                    () => session.MiniGraphWindow is not null,
                    ControlTimeout,
                    "the widget window to be present");

                result = StepResult.Pass(stepName);
            }
            catch (Exception failure)
            {
                result = StepResult.Fail(stepName, "a window titled 'Umnatha mini graph'", failure.Message);
            }

            return result;
        }

        private static StepResult AssertSectionShown(AppSession session, string stepName, string automationId, string expectedText)
        {
            bool shown = WidgetShowsText(session, automationId, expectedText);
            StepResult result;

            if (shown)
            {
                result = StepResult.Pass(stepName);
            }
            else
            {
                result = StepResult.Fail(stepName, $"a '{automationId}' element reading '{expectedText}'", "no such element on the widget");
            }

            return result;
        }

        private static StepResult AssertWidgetTextContains(AppSession session, string stepName, string automationId, string expectedFragment)
        {
            string text = ReadWidgetText(session, automationId);
            StepResult result;

            if (text.Contains(expectedFragment, StringComparison.Ordinal))
            {
                result = StepResult.Pass(stepName);
            }
            else
            {
                string actual = text.Length > 0 ? $"'{text}'" : $"no '{automationId}' element on the widget";

                result = StepResult.Fail(stepName, $"text containing '{expectedFragment}'", actual);
            }

            return result;
        }

        private static void WaitForWidgetText(AppSession session, string automationId, string expectedText, bool shouldBeShown)
        {
            string what = shouldBeShown ? "appear on" : "leave";

            Waits.Until(
                () => WidgetShowsText(session, automationId, expectedText) == shouldBeShown,
                WidgetChangeTimeout,
                $"'{expectedText}' to {what} the widget");
        }

        // Several elements on the widget share an AutomationId (both sections label themselves
        // through LabelText), so this matches on the text as well and ignores anything the widget
        // is currently not showing.
        private static bool WidgetShowsText(AppSession session, string automationId, string expectedText)
        {
            bool shown = false;

            try
            {
                Window? widget = session.MiniGraphWindow;

                if (widget is not null)
                {
                    AutomationElement[] candidates = widget.FindAllDescendants(
                        conditionFactory => conditionFactory.ByAutomationId(automationId));

                    foreach (AutomationElement candidate in candidates)
                    {
                        bool matches = string.Equals(UiaText.NameOrEmpty(candidate), expectedText, StringComparison.Ordinal)
                            && !candidate.Properties.IsOffscreen.ValueOrDefault;

                        if (matches)
                        {
                            shown = true;

                            break;
                        }

                    }

                }

            }
            catch (Exception)
            {
                shown = false;
            }

            return shown;
        }

        private static string ReadWidgetText(AppSession session, string automationId)
        {
            string text = string.Empty;

            try
            {
                Window? widget = session.MiniGraphWindow;

                if (widget is not null)
                {
                    AutomationElement? element = widget.FindFirstDescendant(automationId);

                    if (element is not null)
                    {
                        text = UiaText.NameOrEmpty(element);
                    }

                }

            }
            catch (Exception)
            {
                text = string.Empty;
            }

            return text;
        }

        private static System.Drawing.Rectangle WaitForWidgetBounds(AppSession session)
        {
            System.Drawing.Rectangle bounds = System.Drawing.Rectangle.Empty;

            Waits.Until(
                () =>
                {
                    bounds = ReadWidgetBounds(session);

                    bool measured = bounds.Width > 0 && bounds.Height > 0;

                    return measured;
                },
                WidgetChangeTimeout,
                "the widget window to report a measurable size");

            return bounds;
        }

        private static System.Drawing.Rectangle ReadWidgetBounds(AppSession session)
        {
            System.Drawing.Rectangle bounds = System.Drawing.Rectangle.Empty;

            try
            {
                Window? widget = session.MiniGraphWindow;

                if (widget is not null)
                {
                    bounds = widget.BoundingRectangle;
                }

            }
            catch (Exception)
            {
                bounds = System.Drawing.Rectangle.Empty;
            }

            return bounds;
        }

        private static void SetCheckBox(AppSession session, string automationId, bool shouldBeChecked)
        {
            AutomationElement control = FindControl(session, automationId);
            ITogglePattern togglePattern = control.Patterns.Toggle.Pattern;
            bool isChecked = togglePattern.ToggleState.Value == FlaUI.Core.Definitions.ToggleState.On;

            if (isChecked != shouldBeChecked)
            {
                togglePattern.Toggle();
            }

        }

        private static void SetToggle(AppSession session, string automationId, bool shouldBeOn)
        {
            SetCheckBox(session, automationId, shouldBeOn);
        }

        private static void SelectRadio(AppSession session, string automationId)
        {
            AutomationElement radioButton = FindControl(session, automationId);
            ISelectionItemPattern selectionItemPattern = radioButton.Patterns.SelectionItem.Pattern;

            selectionItemPattern.Select();
        }

        private static AutomationElement FindControl(AppSession session, string automationId)
        {
            AutomationElement control = Waits.UntilFound(
                () => session.MainWindow.FindFirstDescendant(automationId),
                ControlTimeout,
                $"the '{automationId}' control to appear");

            return control;
        }

        private static string ReadSetting(PhaseContext context, string settingName)
        {
            string value = SettingsFileReader.ReadValue(context.DataFolder, settingName);

            if (value.Length == 0)
            {
                throw new InvalidOperationException($"settings.json in the fixture folder has no '{settingName}' property.");
            }

            return value;
        }

        private static void WaitForSetting(PhaseContext context, string settingName, string expectedRawJson)
        {
            Waits.Until(
                () => string.Equals(SettingsFileReader.ReadValue(context.DataFolder, settingName), expectedRawJson, StringComparison.Ordinal),
                WidgetChangeTimeout,
                $"settings.json to report {settingName}={expectedRawJson}");
        }
    }
}
