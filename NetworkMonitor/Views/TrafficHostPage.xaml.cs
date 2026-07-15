using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace NetworkMonitor.Views
{
    public sealed partial class TrafficHostPage : Page
    {
        public TrafficHostPage()
        {
            InitializeComponent();
            InternetFrame.Navigate(typeof(InternetPage));
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

                if (selectedTag != "Internet" && InternetFrame.Content is InternetPage internetPage)
                {
                    internetPage.ResetToLive();
                }

                InternetFrame.Visibility = selectedTag == "Internet" ? Visibility.Visible : Visibility.Collapsed;
                SpeedTestFrame.Visibility = selectedTag == "SpeedTest" ? Visibility.Visible : Visibility.Collapsed;
            }

        }
    }
}
