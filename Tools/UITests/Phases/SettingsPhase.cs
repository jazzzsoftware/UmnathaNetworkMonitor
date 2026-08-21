using System.Diagnostics;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.Patterns;
using FlaUI.Core.WindowsAPI;
using NetworkMonitor.Core.Charting;
using NetworkMonitor.UITests.Driving;
using NetworkMonitor.UITests.Fixtures;
using NetworkMonitor.UITests.Runner;

namespace NetworkMonitor.UITests.Phases
{
    // Phase 06: every setting the Settings page exposes, round-tripped through the fixture's
    // settings.json ON DISK — change it, wait for the file to show the new value, then restore it
    // and wait for the file to show the original. Not abortsRun.
    //
    // Reading the value back off the control would pass for a setting whose binding works and
    // whose persistence does not; the file is the only witness that matters. That is the class of
    // defect commit 3a822b8 fixed, and the reason this phase exists.
    //
    // Restoring is not politeness. Later phases read the same fixture: leaving the scan interval
    // at 7 seconds or the mini graph hidden would silently change what Task 11's mini graph phase
    // is looking at. Every setting therefore ends this phase where it started, and a setting that
    // changed but would not restore fails rather than passing quietly.
    //
    // Controls are driven through their UIA patterns (Toggle, Value, RangeValue, SelectionItem)
    // rather than by clicking. That is not only faster: a control scrolled out of view inside a
    // settings panel has no clickable point, and clicking it would either scroll the operator's
    // screen around or fail outright, while a pattern call reaches it either way.
    //
    // Two settings are deliberately not round-tripped, both recorded as skips with their reason
    // rather than silently absent — see RunLoggingToggle and RunStartupToggle.
    public static class SettingsPhase
    {
        private const string TrafficTabAutomationId = "SettingsTrafficTab";
        private const string DeviceTabAutomationId = "SettingsDeviceTab";
        private const string ThemeTabAutomationId = "SettingsThemeTab";
        private const string OtherTabAutomationId = "SettingsOtherTab";

        // The NumberBox's editable child; the Spinner wrapper itself carries no value pattern.
        private const string NumberBoxInputAutomationId = "InputBox";

        // Scheduled task name, from WindowsStartupService.TaskName — the Run at startup toggle
        // creates and deletes this rather than writing anything to settings.json.
        private const string StartupTaskName = "Umnatha Network Monitor";

        private static readonly TimeSpan ControlTimeout = TimeSpan.FromSeconds(10);

        // A change reaches settings.json through SettingsViewModel.PersistAll (or MiniGraphState /
        // ChartPaletteService for the widget and the palette), which serialises and writes the
        // whole file. Sub-second in practice; ten seconds is headroom, not an expected wait.
        private static readonly TimeSpan SaveTimeout = TimeSpan.FromSeconds(10);

        // schtasks.exe answers a query or a delete in well under a second.
        private static readonly TimeSpan SchTasksTimeout = TimeSpan.FromSeconds(15);

        public static Task<IReadOnlyList<StepResult>> RunAsync(PhaseContext context)
        {
            StepLog steps = new StepLog(context);
            AppSession session = context.Session
                ?? throw new InvalidOperationException(
                    "SettingsPhase requires LaunchPhase to have run first and set PhaseContext.Session.");

            Navigator navigator = new Navigator(session);

            navigator.GoTo(NavRoute.Settings);

            RunTrafficTab(session, context, navigator, steps);
            RunDeviceTab(session, context, navigator, steps);
            RunThemeTab(session, context, navigator, steps);
            RunOtherTab(session, context, navigator, steps);

            IReadOnlyList<StepResult> result = steps.Steps;
            Task<IReadOnlyList<StepResult>> completed = Task.FromResult(result);

            return completed;
        }

        private static void RunTrafficTab(AppSession session, PhaseContext context, Navigator navigator, StepLog steps)
        {
            navigator.SelectTab(TrafficTabAutomationId);

            steps.Add(RunNumberSetting(session, context, "The traffic sampling interval", "TrafficIntervalSecondsBox", "TrafficIntervalSeconds", 4));
            steps.Add(RunToggleSetting(session, context, "Chart smooth scrolling", "ChartSmoothScrollingToggle", "ChartSmoothScrolling"));
            steps.Add(RunComboSetting(session, context, "The speed units", "SpeedUnitsComboBox", "RateUnitMode", 1));
            steps.Add(RunToggleSetting(session, context, "The periodic speed test", "SpeedTestEnabledToggle", "SpeedTestEnabled"));
            steps.Add(RunNumberSetting(session, context, "The traffic retention window", "TrafficPurgeDaysBox", "TrafficPurgeDays", 5));
        }

        private static void RunDeviceTab(AppSession session, PhaseContext context, Navigator navigator, StepLog steps)
        {
            navigator.SelectTab(DeviceTabAutomationId);

            // Before the auto-detect toggle, not after: SettingsViewModel.SubnetBaseEditable is
            // the inverse of AutoDetectSubnet, so driving them the other way round would leave the
            // text box disabled at the moment this tries to type into it.
            steps.Add(RunTextSetting(session, context, "The subnet base", "SubnetBaseTextBox", "SubnetBase", "192.168.77"));
            steps.Add(RunToggleSetting(session, context, "Subnet auto-detection", "AutoDetectSubnetToggle", "AutoDetectSubnet"));
            steps.Add(RunNumberSetting(session, context, "The scan range's first host", "StartHostBox", "StartHost", 5));
            steps.Add(RunNumberSetting(session, context, "The scan range's last host", "EndHostBox", "EndHost", 200));
            steps.Add(RunNumberSetting(session, context, "The device scan interval", "ScanIntervalMinutesBox", "IntervalMinutes", 9));
            steps.Add(RunNumberSetting(session, context, "The ping timeout", "PingTimeoutBox", "PingTimeoutMs", 400));
            steps.Add(RunNumberSetting(session, context, "The parallel ping count", "MaxParallelPingsBox", "MaxParallelPings", 32));
            steps.Add(RunNumberSetting(session, context, "The device history retention window", "HistoryPurgeDaysBox", "HistoryPurgeDays", 21));
        }

        private static void RunThemeTab(AppSession session, PhaseContext context, Navigator navigator, StepLog steps)
        {
            navigator.SelectTab(ThemeTabAutomationId);

            steps.Add(RunChartSchemeSetting(session, context));
        }

        private static void RunOtherTab(AppSession session, PhaseContext context, Navigator navigator, StepLog steps)
        {
            navigator.SelectTab(OtherTabAutomationId);

            steps.Add(RunStartupToggle(session));
            steps.Add(RunToggleSetting(session, context, "Toast notifications", "ShowToastsToggle", "ShowToasts"));
            steps.Add(RunToggleSetting(session, context, "Unapproved-only toasts", "UnapprovedOnlyToastsCheckBox", "UnapprovedOnlyToasts"));
            steps.Add(RunToggleSetting(session, context, "The automatic update check", "AutoCheckForUpdatesToggle", "AutoCheckForUpdates"));
            steps.Add(RunToggleSetting(session, context, "The mini graph's visibility", "ShowMiniGraphToggle", "ShowMiniGraph"));
            steps.Add(RunToggleSetting(session, context, "The mini graph's Internet section", "MiniGraphShowInternetCheckBox", "MiniGraphShowInternet"));
            steps.Add(RunToggleSetting(session, context, "The mini graph's Local section", "MiniGraphShowLocalCheckBox", "MiniGraphShowLocal"));
            steps.Add(RunToggleSetting(session, context, "The mini graph's speed test section", "MiniGraphShowSpeedTestCheckBox", "MiniGraphShowSpeedTest"));
            steps.Add(RunToggleSetting(session, context, "The mini graph's unknown devices section", "MiniGraphShowUnknownDevicesCheckBox", "MiniGraphShowUnknownDevices"));
            steps.Add(RunToggleSetting(session, context, "The mini graph's border", "MiniGraphShowBorderCheckBox", "MiniGraphShowBorder"));
            steps.Add(RunRadioSetting(session, context, "The mini graph's orientation", "MiniGraphOrientationHorizontalRadioButton", "MiniGraphOrientationVerticalRadioButton", "MiniGraphHorizontal"));
            steps.Add(RunSliderSetting(session, context, "The mini graph's opacity", "MiniGraphOpacitySlider", "MiniGraphOpacity", 65));
            steps.Add(RunNumberSetting(session, context, "The digest generation hour", "DigestGenerationHourBox", "DigestGenerationHour", 6));
            steps.Add(RunNumberSetting(session, context, "The digest retention window", "DigestPurgeDaysBox", "DigestPurgeDays", 45));
            steps.Add(RunToggleSetting(session, context, "The digest notification", "DigestNotifyToggle", "DigestNotify"));
            steps.Add(RunLoggingToggle(session));
        }

        // Toggle switches and check boxes are the same thing to UIA: both carry TogglePattern, and
        // both are flipped and flipped back here.
        private static StepResult RunToggleSetting(AppSession session, PhaseContext context, string description, string automationId, string settingName)
        {
            string stepName = $"{description} round-trips through settings.json";
            StepResult result;

            try
            {
                string originalValue = ReadSetting(context, settingName);
                AutomationElement control = FindControl(session, automationId);

                // A control the app has disabled cannot be driven, and pretending otherwise
                // produced a failure with an empty message: UnapprovedOnlyToasts is bound to
                // IsEnabled on ShowToasts, which the fixture turns off, so its check box is
                // greyed out and Toggle() throws with nothing useful to say. Reported as a skip
                // naming the control's own state instead.
                if (!control.Properties.IsEnabled.ValueOrDefault)
                {
                    result = StepResult.Skip(
                        stepName,
                        $"The '{automationId}' control is disabled in the app right now, so this setting cannot be driven. "
                        + "It depends on another setting the fixture leaves off.");
                }
                else
                {
                    ITogglePattern togglePattern = control.Patterns.Toggle.Pattern;

                    togglePattern.Toggle();
                    WaitForSettingToChangeFrom(context, settingName, originalValue);

                    togglePattern.Toggle();
                    WaitForSetting(context, settingName, originalValue);

                    result = StepResult.Pass(stepName);
                }
            }
            catch (Exception failure)
            {
                result = StepResult.Fail(stepName, $"'{settingName}' to change in settings.json and then restore", failure.Message);
            }

            return result;
        }

        // NumberBox: the value lives on its Edit child, and the box only commits what was typed on
        // Enter or focus loss — SetValue alone leaves the ViewModel binding untouched, so the file
        // would never change and the failure would read as "does not persist" when the real story
        // is "was never entered".
        private static StepResult RunNumberSetting(AppSession session, PhaseContext context, string description, string automationId, string settingName, int newValue)
        {
            string stepName = $"{description} round-trips through settings.json";
            StepResult result;

            try
            {
                string originalValue = ReadSetting(context, settingName);

                SetNumberBoxValue(session, automationId, newValue);
                WaitForSetting(context, settingName, newValue.ToString());

                SetNumberBoxValue(session, automationId, ParseInteger(originalValue));
                WaitForSetting(context, settingName, originalValue);

                result = StepResult.Pass(stepName);
            }
            catch (Exception failure)
            {
                result = StepResult.Fail(stepName, $"'{settingName}' to become {newValue} in settings.json and then restore", failure.Message);
            }

            return result;
        }

        private static StepResult RunTextSetting(AppSession session, PhaseContext context, string description, string automationId, string settingName, string newText)
        {
            string stepName = $"{description} round-trips through settings.json";
            StepResult result;

            try
            {
                string originalValue = ReadSetting(context, settingName);
                string originalText = originalValue.Trim('"');

                SetTextBoxValue(session, automationId, newText);
                WaitForSetting(context, settingName, "\"" + newText + "\"");

                SetTextBoxValue(session, automationId, originalText);
                WaitForSetting(context, settingName, originalValue);

                result = StepResult.Pass(stepName);
            }
            catch (Exception failure)
            {
                result = StepResult.Fail(stepName, $"'{settingName}' to become '{newText}' in settings.json and then restore", failure.Message);
            }

            return result;
        }

        private static StepResult RunComboSetting(AppSession session, PhaseContext context, string description, string automationId, string settingName, int newIndex)
        {
            string stepName = $"{description} round-trips through settings.json";
            StepResult result;

            try
            {
                string originalValue = ReadSetting(context, settingName);
                int originalIndex = ParseInteger(originalValue);

                SelectComboBoxItem(session, automationId, newIndex);
                WaitForSetting(context, settingName, newIndex.ToString());

                SelectComboBoxItem(session, automationId, originalIndex);
                WaitForSetting(context, settingName, originalValue);

                result = StepResult.Pass(stepName);
            }
            catch (Exception failure)
            {
                result = StepResult.Fail(stepName, $"'{settingName}' to become {newIndex} in settings.json and then restore", failure.Message);
            }

            return result;
        }

        // The scheme combo is the v0.0.12 feature's front door, and what lands in the file is the
        // preset's id rather than the combo's index — so the expected value is taken from Core's
        // own catalogue rather than written out here, where it could drift from the list the combo
        // is actually populated from.
        private static StepResult RunChartSchemeSetting(AppSession session, PhaseContext context)
        {
            const string stepName = "The chart colour scheme round-trips through settings.json";
            StepResult result;

            try
            {
                string originalValue = ReadSetting(context, "ChartSchemeId");
                string originalId = originalValue.Trim('"');
                int originalIndex = IndexOfScheme(originalId);
                int newIndex = originalIndex == 0 ? 1 : 0;
                string newId = ChartSchemeCatalog.Presets[newIndex].Id;

                SelectComboBoxItem(session, "ChartSchemeComboBox", newIndex);
                WaitForSetting(context, "ChartSchemeId", "\"" + newId + "\"");

                SelectComboBoxItem(session, "ChartSchemeComboBox", originalIndex);
                WaitForSetting(context, "ChartSchemeId", originalValue);

                result = StepResult.Pass(stepName);
            }
            catch (Exception failure)
            {
                result = StepResult.Fail(stepName, "'ChartSchemeId' to change in settings.json and then restore", failure.Message);
            }

            return result;
        }

        private static StepResult RunSliderSetting(AppSession session, PhaseContext context, string description, string automationId, string settingName, int newValue)
        {
            string stepName = $"{description} round-trips through settings.json";
            StepResult result;

            try
            {
                string originalValue = ReadSetting(context, settingName);
                AutomationElement slider = FindControl(session, automationId);
                IRangeValuePattern rangeValuePattern = slider.Patterns.RangeValue.Pattern;

                rangeValuePattern.SetValue(newValue);
                WaitForSetting(context, settingName, newValue.ToString());

                rangeValuePattern.SetValue(ParseInteger(originalValue));
                WaitForSetting(context, settingName, originalValue);

                result = StepResult.Pass(stepName);
            }
            catch (Exception failure)
            {
                result = StepResult.Fail(stepName, $"'{settingName}' to become {newValue} in settings.json and then restore", failure.Message);
            }

            return result;
        }

        private static StepResult RunRadioSetting(AppSession session, PhaseContext context, string description, string otherOptionAutomationId, string originalOptionAutomationId, string settingName)
        {
            string stepName = $"{description} round-trips through settings.json";
            StepResult result;

            try
            {
                string originalValue = ReadSetting(context, settingName);
                bool startsOnOther = string.Equals(originalValue, "true", StringComparison.Ordinal);
                string toSelectFirst = startsOnOther ? originalOptionAutomationId : otherOptionAutomationId;
                string toRestore = startsOnOther ? otherOptionAutomationId : originalOptionAutomationId;

                SelectRadioButton(session, toSelectFirst);
                WaitForSettingToChangeFrom(context, settingName, originalValue);

                SelectRadioButton(session, toRestore);
                WaitForSetting(context, settingName, originalValue);

                result = StepResult.Pass(stepName);
            }
            catch (Exception failure)
            {
                result = StepResult.Fail(stepName, $"'{settingName}' to change in settings.json and then restore", failure.Message);
            }

            return result;
        }

        // Not round-tripped, and the reason is verified rather than asserted: SettingsViewModel's
        // LoggingToggleEnabled is compiled to false in a Debug build (logging is forced on), and
        // the app under test is a Debug build by design — see AppUnderTest's header. Rather than
        // skipping on that reasoning alone, this reads the control's own enabled state, so the
        // report says "the app disabled it" as an observation rather than a claim.
        private static StepResult RunLoggingToggle(AppSession session)
        {
            const string stepName = "Logging can be turned off from Settings";
            StepResult result;

            try
            {
                AutomationElement control = FindControl(session, "EnableLoggingToggle");
                bool enabled = control.Properties.IsEnabled.ValueOrDefault;

                if (enabled)
                {
                    result = StepResult.Fail(
                        stepName,
                        "the logging toggle to be disabled in a Debug build, which forces logging on",
                        "it was enabled — the build under test is behaving like a Release build, so this assertion's premise no longer holds");
                }
                else
                {
                    result = StepResult.Skip(
                        stepName,
                        "The logging toggle is disabled in the build under test, which was confirmed by reading the control "
                        + "itself. SettingsViewModel.LoggingToggleEnabled compiles to false in Debug and logging is forced on; "
                        + "the suite drives a locally built Debug binary, so this setting cannot be exercised here. It is "
                        + "reachable only in a Release build.");
                }

            }
            catch (Exception failure)
            {
                result = StepResult.Fail(stepName, "the logging toggle to be readable", failure.Message);
            }

            return result;
        }

        // Run at startup writes nothing to settings.json: WindowsStartupService creates a logon
        // scheduled task, at highest privileges, pointing at whichever executable is running — here
        // the Debug build under test. That is a change to the operator's machine, outside the
        // fixture sandbox and outside anything RealDataGuard restores.
        //
        // So it is only driven when the task does not already exist, it is deleted in a finally
        // whatever happens, and the assertion is made against schtasks itself rather than any
        // UI text. If the operator already uses the feature, the toggle is left strictly alone.
        private static StepResult RunStartupToggle(AppSession session)
        {
            const string stepName = "Run at startup creates and removes its logon task";
            StepResult result;

            if (StartupTaskExists())
            {
                result = StepResult.Skip(
                    stepName,
                    $"A scheduled task named '{StartupTaskName}' already exists, which means the operator uses this feature. "
                    + "Driving the toggle would delete and recreate their task pointing at the Debug build under test, so it "
                    + "is left untouched.");
            }
            else
            {

                try
                {
                    AutomationElement control = FindControl(session, "RunAtStartupToggle");
                    ITogglePattern togglePattern = control.Patterns.Toggle.Pattern;

                    try
                    {
                        togglePattern.Toggle();

                        Waits.Until(
                            () => StartupTaskExists(),
                            SaveTimeout,
                            $"a scheduled task named '{StartupTaskName}' to exist after turning Run at startup on");

                        togglePattern.Toggle();

                        Waits.Until(
                            () => !StartupTaskExists(),
                            SaveTimeout,
                            $"the '{StartupTaskName}' scheduled task to be gone after turning Run at startup off again");

                        result = StepResult.Pass(stepName);
                    }
                    finally
                    {
                        DeleteStartupTaskIfPresent();
                    }

                }
                catch (Exception failure)
                {
                    result = StepResult.Fail(stepName, $"the '{StartupTaskName}' scheduled task to appear and then be removed", failure.Message);
                }

            }

            return result;
        }

        private static void SetNumberBoxValue(AppSession session, string automationId, int value)
        {
            AutomationElement numberBox = FindControl(session, automationId);
            AutomationElement input = numberBox.FindFirstDescendant(NumberBoxInputAutomationId) ?? numberBox;
            IValuePattern valuePattern = input.Patterns.Value.Pattern;

            valuePattern.SetValue(value.ToString());
            Commit(input);
        }

        private static void SetTextBoxValue(AppSession session, string automationId, string text)
        {
            AutomationElement textBox = FindControl(session, automationId);
            IValuePattern valuePattern = textBox.Patterns.Value.Pattern;

            valuePattern.SetValue(text);
            Commit(textBox);
        }

        // Setting the text is not entering it. A WinUI NumberBox parses what was typed on Enter or
        // on losing focus, and a TextBox's two-way binding updates its source on losing focus — so
        // until one of those happens the view model, and therefore settings.json, still holds the
        // old value however convincing the control looks on screen.
        //
        // Both are done here, in that order. Enter is the commit a person would use; Tab then moves
        // focus off the control, which covers the case where Enter is not handled and is what
        // finally made the numeric settings pass.
        //
        // Keyboard.Type, NOT Keyboard.Press: in FlaUI, Press sends a key DOWN and nothing else —
        // Release sends the up, and Type sends both. Half a keystroke is what the first run of this
        // phase actually sent, which is why seven numeric settings each waited ten seconds for a
        // save that was never going to happen, while the two that passed were being committed by
        // the *next* setting's control stealing focus from them.
        private static void Commit(AutomationElement control)
        {
            control.Focus();
            Keyboard.Type(VirtualKeyShort.RETURN);
            Keyboard.Type(VirtualKeyShort.TAB);
        }

        private static void SelectComboBoxItem(AppSession session, string automationId, int itemIndex)
        {
            AutomationElement comboBox = FindControl(session, automationId);
            ComboBox typedComboBox = comboBox.AsComboBox();

            typedComboBox.Select(itemIndex);
        }

        private static void SelectRadioButton(AppSession session, string automationId)
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
                throw new InvalidOperationException(
                    $"settings.json in the fixture folder has no '{settingName}' property to round-trip.");
            }

            return value;
        }

        private static void WaitForSetting(PhaseContext context, string settingName, string expectedRawJson)
        {
            Waits.Until(
                () => string.Equals(SettingsFileReader.ReadValue(context.DataFolder, settingName), expectedRawJson, StringComparison.Ordinal),
                SaveTimeout,
                $"settings.json to report {settingName}={expectedRawJson}");
        }

        // For a toggle, what the new value will be is the app's business, not this phase's — all
        // that matters is that the file stopped saying what it said before, and then said it again.
        private static void WaitForSettingToChangeFrom(PhaseContext context, string settingName, string originalRawJson)
        {
            Waits.Until(
                () =>
                {
                    string current = SettingsFileReader.ReadValue(context.DataFolder, settingName);

                    bool changed = current.Length > 0 && !string.Equals(current, originalRawJson, StringComparison.Ordinal);

                    return changed;
                },
                SaveTimeout,
                $"settings.json to report a {settingName} other than {originalRawJson}");
        }

        private static int IndexOfScheme(string schemeId)
        {
            int index = 0;

            for (int candidate = 0; candidate < ChartSchemeCatalog.Presets.Count; candidate++)
            {

                if (string.Equals(ChartSchemeCatalog.Presets[candidate].Id, schemeId, StringComparison.Ordinal))
                {
                    index = candidate;

                    break;
                }

            }

            return index;
        }

        private static int ParseInteger(string rawJson)
        {
            int value = int.Parse(rawJson.Trim('"'), System.Globalization.CultureInfo.InvariantCulture);

            return value;
        }

        private static bool StartupTaskExists()
        {
            int exitCode = RunSchTasks($"/query /tn \"{StartupTaskName}\"");
            bool exists = exitCode == 0;

            return exists;
        }

        private static void DeleteStartupTaskIfPresent()
        {

            if (StartupTaskExists())
            {
                RunSchTasks($"/delete /tn \"{StartupTaskName}\" /f");
            }

        }

        // Mirrors WindowsStartupService's own use of schtasks.exe, so this asserts against the
        // same thing the app manipulates rather than a second opinion about where startup entries
        // live.
        private static int RunSchTasks(string arguments)
        {
            int exitCode = -1;

            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using (Process? process = Process.Start(startInfo))
                {

                    if (process is not null)
                    {
                        process.StandardOutput.ReadToEnd();
                        process.StandardError.ReadToEnd();

                        bool exited = WaitForProcessExit(process, SchTasksTimeout);

                        if (exited)
                        {
                            exitCode = process.ExitCode;
                        }

                    }

                }

            }
            catch (Exception failure)
            {
                Console.WriteLine($"SettingsPhase: schtasks.exe {arguments} failed: {failure.Message}");
            }

            return exitCode;
        }

        private static bool WaitForProcessExit(Process process, TimeSpan timeout)
        {
            bool exited;

            try
            {
                Waits.Until(() => process.HasExited, timeout, "schtasks.exe to exit");
                exited = true;
            }
            catch (TimeoutException)
            {
                exited = false;
            }

            return exited;
        }
    }
}
