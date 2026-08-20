using FlaUI.Core.AutomationElements;
using FlaUI.Core.Patterns;

namespace NetworkMonitor.UITests.Driving
{
    // Selects a NavigationViewItem. Task 7 gave the shell stable AutomationIds (MainWindow.xaml
    // and the SettingsItem set in MainWindow.xaml.cs's NavViewLoaded), so GoTo looks up by
    // AutomationId first; the visible Content text ("Traffic", "Devices", "Reports", "Settings" —
    // MainWindow.xaml:22-61 plus the built-in Settings item) is kept as a fallback for whichever
    // of the two the running build does not yet carry. Selection uses SelectionItemPattern.Select()
    // rather than InvokePattern.Invoke(), because invoking a NavigationViewItem does not reliably
    // change which one is selected.
    public sealed class Navigator
    {
        private const string TrafficAutomationId = "TrafficNavItem";
        private const string DevicesAutomationId = "DevicesNavItem";
        private const string ReportsAutomationId = "ReportsNavItem";
        private const string SettingsAutomationId = "SettingsNavItem";

        private const string TrafficItemName = "Traffic";
        private const string DevicesItemName = "Devices";
        private const string ReportsItemName = "Reports";
        private const string SettingsItemName = "Settings";

        // NavigationView.SelectionChanged fires, then the destination page loads into
        // ContentFrame asynchronously; this bounds how long GoTo waits for the item to report
        // itself selected before handing control back to whatever the phase does next.
        private static readonly TimeSpan SelectionTimeout = TimeSpan.FromSeconds(10);

        private readonly AppSession _session;

        public Navigator(AppSession session)
        {
            _session = session;
        }

        public void GoTo(NavRoute route)
        {
            string automationId = AutomationIdFor(route);
            string itemName = NameFor(route);
            Window mainWindow = _session.MainWindow;

            AutomationElement navigationItem = Waits.UntilFound(
                () => mainWindow.FindFirstDescendant(automationId) ?? mainWindow.FindFirstDescendant(conditionFactory => conditionFactory.ByName(itemName)),
                SelectionTimeout,
                $"the '{itemName}' navigation item (AutomationId '{automationId}') to appear");

            ISelectionItemPattern selectionItemPattern = navigationItem.Patterns.SelectionItem.Pattern;

            selectionItemPattern.Select();

            Waits.Until(
                () => selectionItemPattern.IsSelected.Value,
                SelectionTimeout,
                $"the '{itemName}' navigation item to report itself selected after Select()");
        }

        private static string AutomationIdFor(NavRoute route)
        {
            string automationId = route switch
            {
                NavRoute.Traffic => TrafficAutomationId,
                NavRoute.Devices => DevicesAutomationId,
                NavRoute.Reports => ReportsAutomationId,
                NavRoute.Settings => SettingsAutomationId,
                _ => throw new ArgumentOutOfRangeException(nameof(route), route, "Navigator.GoTo does not know this NavRoute.")
            };

            return automationId;
        }

        private static string NameFor(NavRoute route)
        {
            string itemName = route switch
            {
                NavRoute.Traffic => TrafficItemName,
                NavRoute.Devices => DevicesItemName,
                NavRoute.Reports => ReportsItemName,
                NavRoute.Settings => SettingsItemName,
                _ => throw new ArgumentOutOfRangeException(nameof(route), route, "Navigator.GoTo does not know this NavRoute.")
            };

            return itemName;
        }
    }
}
