using FlaUI.Core.AutomationElements;
using FlaUI.Core.Patterns;

namespace NetworkMonitor.UITests.Driving
{
    // Setting a NumberBox is not the same as it accepting what was set. WinUI silently coerces a
    // value outside the control's Minimum/Maximum back into range, and the caller then waits for
    // settings.json to change and times out with "expected TrafficPurgeDays to become 30" - true,
    // and useless, because nothing in it says the control refused the value.
    //
    // That is exactly what happened on 2026-08-23: TrafficPurgeDaysBox caps at 7 (SettingsPage.xaml
    // limits traffic retention to a week by design), a fixture asked for 30, and two steps failed
    // with timeouts that pointed at settings.json rather than at the control. It cost a debugging
    // round. Checking the readback turns that into a failure that names the cause.
    public static class NumberBoxDriver
    {
        private static readonly TimeSpan SettleTimeout = TimeSpan.FromSeconds(2);

        // Called after the value has been set AND committed - a NumberBox parses what was typed on
        // Enter or on losing focus, so reading it before the commit reports the old value.
        // Called after the value has been set AND committed - a NumberBox parses what was typed on
        // Enter or on losing focus, so reading it before the commit reports the old value.
        public static void VerifyAccepted(AutomationElement numberBox, AutomationElement input, string automationId, int requested)
        {
            string accepted = ReadSettledValue(input);
            bool matches = Matches(accepted, requested);

            if (!matches)
            {
                string range = DescribeRange(numberBox);

                throw new InvalidOperationException(
                    $"The '{automationId}' control did not accept {requested}; it reads '{accepted}'. "
                    + $"{range} A NumberBox coerces an out-of-range value silently, so this is the control "
                    + "refusing the value rather than the app failing to save it.");
            }

        }

        // The settled value, not the first one seen. A NumberBox goes on showing what was typed for
        // a moment before it coerces, so a poll that exited on the first match would pass on that
        // transient and report nothing - which is exactly what the first version of this check did
        // when asked for 30 against a control capped at 7. Reads until two consecutive reads agree.
        private static string ReadSettledValue(AutomationElement input)
        {
            string previous = ReadValue(input);
            string settled = previous;

            try
            {
                Waits.Until(
                    () =>
                    {
                        string current = ReadValue(input);
                        bool stable = string.Equals(current, previous, StringComparison.Ordinal);

                        previous = current;
                        settled = current;

                        return stable;
                    },
                    SettleTimeout,
                    "the number box to stop changing");
            }
            catch (TimeoutException)
            {
            }

            return settled;
        }

        private static string ReadValue(AutomationElement input)
        {
            string value = string.Empty;

            try
            {
                IValuePattern? valuePattern = input.Patterns.Value.PatternOrDefault;

                if (valuePattern is not null)
                {
                    value = valuePattern.Value.ValueOrDefault ?? string.Empty;
                }

            }
            catch (Exception)
            {
                value = string.Empty;
            }

            return value;
        }

        // Compared numerically where possible: a NumberBox may render 7 as "7" or "7.00" depending
        // on its formatter, and a string comparison would call that a rejection.
        private static bool Matches(string accepted, int requested)
        {
            bool matches;

            if (double.TryParse(accepted, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.CurrentCulture, out double parsed))
            {
                matches = Math.Abs(parsed - requested) < 0.0001;
            }
            else
            {
                matches = string.Equals(accepted, requested.ToString(), StringComparison.Ordinal);
            }

            return matches;
        }

        // The control's own limits, when it publishes them, so the failure says WHY rather than
        // leaving the reader to find the XAML.
        private static string DescribeRange(AutomationElement numberBox)
        {
            string description = "The control did not publish a range.";

            try
            {
                IRangeValuePattern? rangePattern = numberBox.Patterns.RangeValue.PatternOrDefault;

                if (rangePattern is not null)
                {
                    double minimum = rangePattern.Minimum.ValueOrDefault;
                    double maximum = rangePattern.Maximum.ValueOrDefault;

                    description = $"Its range is {minimum:0.##} to {maximum:0.##}.";
                }

            }
            catch (Exception)
            {
                description = "The control's range could not be read.";
            }

            return description;
        }
    }
}
