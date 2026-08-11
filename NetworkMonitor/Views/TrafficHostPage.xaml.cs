using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NetworkMonitor.Services.Platform;

namespace NetworkMonitor.Views
{
    public sealed partial class TrafficHostPage : Page
    {
        private readonly MiniGraphState _miniGraphState = App.AppHost.Services.GetRequiredService<MiniGraphState>();

        public TrafficHostPage()
        {
            InitializeComponent();
            InternetFrame.Navigate(typeof(InternetPage));
            TabBar.SelectedItem = TabBar.Items[0];
            Loaded += OnPageLoaded;
            Unloaded += OnPageUnloaded;
        }

        internal void SelectTab(string tabTag)
        {

            foreach (object item in TabBar.Items)
            {

                if (item is SelectorBarItem barItem && barItem.Tag?.ToString() == tabTag)
                {
                    TabBar.SelectedItem = barItem;

                    break;
                }

            }

        }

        private void TabBarSelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
        {

            if (sender.SelectedItem is not null)
            {
                string selectedTag = (string)sender.SelectedItem.Tag;

                if (selectedTag == "Local" && LocalFrame.Content is null)
                {
                    LocalFrame.Navigate(typeof(LocalPage));
                }

                if (selectedTag == "SpeedTest" && SpeedTestFrame.Content is null)
                {
                    SpeedTestFrame.Navigate(typeof(SpeedTestPage));
                }

                if (selectedTag != "Internet" && InternetFrame.Content is InternetPage internetPage)
                {
                    internetPage.ResetToLive();
                }

                if (selectedTag != "Local" && LocalFrame.Content is LocalPage localPage)
                {
                    localPage.ResetToLive();
                }

                InternetFrame.Visibility = selectedTag == "Internet" ? Visibility.Visible : Visibility.Collapsed;
                LocalFrame.Visibility = selectedTag == "Local" ? Visibility.Visible : Visibility.Collapsed;
                SpeedTestFrame.Visibility = selectedTag == "SpeedTest" ? Visibility.Visible : Visibility.Collapsed;
            }

        }

        private void MiniGraphToggleClick(object sender, RoutedEventArgs args)
        {
            _miniGraphState.IsVisible = MiniGraphToggle.IsChecked == true;
        }

        private void OnMiniGraphStateChanged(object? sender, EventArgs args)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                MiniGraphToggle.IsChecked = _miniGraphState.IsVisible;
            });
        }

        private void OnPageLoaded(object sender, RoutedEventArgs args)
        {
            MiniGraphToggle.IsChecked = _miniGraphState.IsVisible;

            // WinUI can raise Loaded again without an intervening Unloaded on a re-parent, and the
            // pair is only balanced by the normal navigation flow. Removing first makes a double
            // Loaded harmless instead of leaving two subscriptions on a singleton.
            _miniGraphState.Changed -= OnMiniGraphStateChanged;
            _miniGraphState.Changed += OnMiniGraphStateChanged;
        }

        private void OnPageUnloaded(object sender, RoutedEventArgs args)
        {
            _miniGraphState.Changed -= OnMiniGraphStateChanged;
        }
    }
}
