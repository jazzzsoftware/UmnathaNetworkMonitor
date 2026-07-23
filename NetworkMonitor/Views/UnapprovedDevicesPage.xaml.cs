using CommunityToolkit.WinUI.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using NetworkMonitor.Services.Data;
using NetworkMonitor.Models.Devices;
using NetworkMonitor.ViewModels;
using NetworkMonitor.Services.Platform;

namespace NetworkMonitor.Views
{
    public sealed partial class UnapprovedDevicesPage : Page
    {
        private readonly Dictionary<DataGridColumn, string> _sortPaths = [];
        private readonly InAppNotificationService _notificationService;

        public UnapprovedDevicesPage()
        {
            ViewModel = App.AppHost.Services.GetRequiredService<UnapprovedDevicesViewModel>();
            _notificationService = App.AppHost.Services.GetRequiredService<InAppNotificationService>();
            InitializeComponent();
            DeviceGridSort.RegisterDeviceColumns(_sortPaths, DeviceGrid);
            Unloaded += OnPageUnloaded;
        }

        public UnapprovedDevicesViewModel ViewModel
        {
            get;
        }

        private void OnPageUnloaded(object sender, RoutedEventArgs args)
        {
            ViewModel.Detach();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs args)
        {
            base.OnNavigatedTo(args);
            SortPreference? pref = SortPreference.Load("unapproved");

            if (pref is not null)
            {
                ViewModel.Sort(pref.Property, pref.Ascending);
            }

            await ViewModel.LoadAsync();
            DeviceGridSort.ApplyIndicator(DeviceGrid, _sortPaths, ViewModel.SortProperty, ViewModel.SortAscending);
        }

        private void DataGridSorting(object sender, DataGridColumnEventArgs args)
        {
            DeviceGridSort.HandleSorting(args, DeviceGrid, _sortPaths, "unapproved", ViewModel.Sort);
        }

        private void CopyButtonClick(object sender, RoutedEventArgs args)
        {
            DeviceDialogs.CopyTagToClipboard(sender);
        }

        private void HistoryButtonClick(object sender, RoutedEventArgs args)
        {
            DeviceDialogs.NavigateToHistory(sender);
        }

        private async void ApproveButtonClick(object sender, RoutedEventArgs args)
        {

            if ((sender as FrameworkElement)?.Tag is Device device)
            {
                bool confirmed = await DeviceDialogs.ShowEditDeviceAsync(device, $"Approve — {device.MacAddress}", "Approve", XamlRoot);

                if (confirmed)
                {
                    await ViewModel.ApproveAsync(device);
                }

            }

        }

        private async void DeleteButtonClick(object sender, RoutedEventArgs args)
        {

            if ((sender as FrameworkElement)?.Tag is Device device)
            {
                bool confirmed = await DeviceDialogs.ShowDeleteConfirmAsync(device, XamlRoot);

                if (confirmed)
                {
                    await ViewModel.DeleteAsync(device);
                    _notificationService.Show($"Deleted {device.DisplayName} ({device.MacAddress})");
                }

            }

        }
    }
}
