using FlaUI.Core.AutomationElements;
using FlaUI.Core.Patterns;

namespace NetworkMonitor.UITests.Driving
{
    // Selects a NavigationViewItem by its visible Content text ("Traffic", "Devices", "Reports",
    // "Settings" — MainWindow.xaml:22-59 plus the built-in Settings item), because no
    // AutomationProperties.AutomationId exists on the shell yet; Task 7 adds those. Selection
    // uses SelectionItemPattern.Select() rather than InvokePattern.Invoke(), because invoking a
    // NavigationViewItem does not reliably change which one is selected.
    public sealed class Navigator
    {
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
            string itemName = NameFor(route);
            Window mainWindow = _session.MainWindow;

            AutomationElement navigationItem = Waits.UntilFound(
                () => mainWindow.FindFirstDescendant(conditionFactory => conditionFactory.ByName(itemName)),
                SelectionTimeout,
                $"the '{itemName}' navigation item to appear");

            ISelectionItemPattern selectionItemPattern = navigationItem.Patterns.SelectionItem.Pattern;

            selectionItemPattern.Select();

            Waits.Until(
                () => selectionItemPattern.IsSelected.Value,
                SelectionTimeout,
                $"the '{itemName}' navigation item to report itself selected after Select()");
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
