using FlaUI.Core.AutomationElements;

namespace NetworkMonitor.UITests.Driving
{
    // Defensive reads of the three properties this suite quotes in failure messages. Every one of
    // them can throw rather than return empty — GridReader's fix-round-1 comment records a real
    // run where reading .Name on a template cell's peer threw "The requested property 'Name
    // [#30005]' is not supported" and aborted the run on its first cell read. A message about a
    // control must never fail because of the control it is describing, so nothing here throws.
    public static class UiaText
    {
        public static string NameOrEmpty(AutomationElement element)
        {
            string name;

            try
            {
                name = element.Name ?? string.Empty;
            }
            catch (Exception)
            {
                name = string.Empty;
            }

            return name;
        }

        public static string ControlTypeOrUnknown(AutomationElement element)
        {
            string controlType;

            try
            {
                controlType = element.ControlType.ToString();
            }
            catch (Exception)
            {
                controlType = "?";
            }

            return controlType;
        }

        public static string AutomationIdOrUnknown(AutomationElement element)
        {
            string automationId;

            try
            {
                automationId = element.AutomationId;
            }
            catch (Exception)
            {
                automationId = "?";
            }

            return automationId;
        }

        // Names the control that was actually found, which is the whole point of a read-back guard
        // that has just discovered it drove the wrong one.
        public static string Describe(AutomationElement element)
        {
            string controlType = ControlTypeOrUnknown(element);
            string automationId = AutomationIdOrUnknown(element);
            string name = NameOrEmpty(element);
            string description = controlType + " (AutomationId='" + automationId + "', Name='" + name + "')";

            return description;
        }
    }
}
