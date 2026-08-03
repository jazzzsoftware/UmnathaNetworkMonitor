using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace NetworkMonitor.Views
{
    public sealed partial class DevicesHostPage : Page
    {
        public DevicesHostPage()
        {
            InitializeComponent();
            DevicesFrame.Navigate(typeof(AllDevicesPage));
            TabBar.SelectedItem = TabBar.Items[0];
        }

        public void ShowDeviceHistory(string mac)
        {
            HistoryFrame.Navigate(typeof(DeviceHistoryPage), mac);

            foreach (object item in TabBar.Items)
            {

                if (item is SelectorBarItem barItem && (string)barItem.Tag == "History")
                {
                    TabBar.SelectedItem = barItem;

                    break;
                }

            }

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

                if (selectedTag == "Approved" && ApprovedFrame.Content is null)
                {
                    ApprovedFrame.Navigate(typeof(ApprovedDevicesPage));
                }

                if (selectedTag == "Unapproved" && UnapprovedFrame.Content is null)
                {
                    UnapprovedFrame.Navigate(typeof(UnapprovedDevicesPage));
                }

                if (selectedTag == "History" && HistoryFrame.Content is null)
                {
                    HistoryFrame.Navigate(typeof(DeviceHistoryPage));
                }

                if (DevicesFrame.Content is AllDevicesPage allDevicesPage)
                {

                    if (selectedTag == "Devices")
                    {
                        allDevicesPage.ViewModel.Activate();
                    }
                    else
                    {
                        allDevicesPage.ViewModel.Deactivate();
                    }

                }

                if (selectedTag == "Approved" && ApprovedFrame.Content is ApprovedDevicesPage approvedPage)
                {
                    _ = approvedPage.ViewModel.LoadAsync();
                }

                if (UnapprovedFrame.Content is UnapprovedDevicesPage unapprovedPage)
                {

                    if (selectedTag == "Unapproved")
                    {
                        unapprovedPage.ViewModel.Activate();
                    }
                    else
                    {
                        unapprovedPage.ViewModel.Deactivate();
                    }

                }

                if (HistoryFrame.Content is DeviceHistoryPage historyPage)
                {

                    if (selectedTag == "History")
                    {
                        historyPage.ViewModel.Activate();
                    }
                    else
                    {
                        historyPage.ViewModel.Deactivate();
                        historyPage.ViewModel.SearchText = string.Empty;
                    }

                }

                DevicesFrame.Visibility = selectedTag == "Devices" ? Visibility.Visible : Visibility.Collapsed;
                ApprovedFrame.Visibility = selectedTag == "Approved" ? Visibility.Visible : Visibility.Collapsed;
                UnapprovedFrame.Visibility = selectedTag == "Unapproved" ? Visibility.Visible : Visibility.Collapsed;
                HistoryFrame.Visibility = selectedTag == "History" ? Visibility.Visible : Visibility.Collapsed;
            }

        }
    }
}
