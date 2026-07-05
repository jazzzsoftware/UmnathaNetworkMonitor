using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace NetworkMonitor.Views
{
    public sealed partial class TrafficHostPage : Page
    {
        public TrafficHostPage()
        {
            InitializeComponent();
            TrafficFrame.Navigate(typeof(TrafficPage));
            TabBar.SelectedItem = TabBar.Items[0];
        }

        private void TabBarSelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
        {

            if (sender.SelectedItem is not null)
            {
                string selectedTag = (string)sender.SelectedItem.Tag;

                if (selectedTag == "SpeedTest" && SpeedTestFrame.Content is null)
                {
                    SpeedTestFrame.Navigate(typeof(SpeedTestPage));
                }

                if (selectedTag != "Traffic" && TrafficFrame.Content is TrafficPage trafficPage)
                {
                    trafficPage.ResetToLive();
                }

                TrafficFrame.Visibility = selectedTag == "Traffic" ? Visibility.Visible : Visibility.Collapsed;
                SpeedTestFrame.Visibility = selectedTag == "SpeedTest" ? Visibility.Visible : Visibility.Collapsed;
            }

        }
    }
}
