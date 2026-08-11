namespace NetworkMonitor.Core.Widget
{
    // The widget's only real invariant: the last visible section can never be turned off. An empty
    // widget is a bare rectangle floating on the desktop with nothing in it to say what it is, and
    // the only way back is a right-click menu the user has no reason to look for.
    //
    // The decision is pure — a count and a refusal — but it lived in Services next to the settings
    // storage, where the test project cannot reach it, while being driven from three separate UIs
    // (the tray menu, the widget's own menu and the Settings checkboxes). All three must obey it.
    public static class SectionVisibility
    {
        public static int CountVisible(bool showInternet, bool showLocal, bool showSpeedTest, bool showUnknownDevices)
        {
            int count = 0;

            if (showInternet)
            {
                count++;
            }

            if (showLocal)
            {
                count++;
            }

            if (showSpeedTest)
            {
                count++;
            }

            if (showUnknownDevices)
            {
                count++;
            }

            return count;
        }

        public static bool CanApply(bool current, bool requested, int visibleCount)
        {
            bool changing = current != requested;
            bool wouldEmptyTheWidget = !requested && visibleCount <= 1;
            bool allowed = changing && !wouldEmptyTheWidget;

            return allowed;
        }
    }
}
