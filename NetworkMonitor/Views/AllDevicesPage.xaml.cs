using System.ComponentModel;
using CommunityToolkit.WinUI.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using NetworkMonitor.Services.Data;
using NetworkMonitor.Models.Devices;
using NetworkMonitor.ViewModels;

namespace NetworkMonitor.Views
{
    public sealed partial class AllDevicesPage : Page
    {
        private readonly Dictionary<DataGridColumn, string> _sortPaths = [];
        private readonly HashSet<Device> _subscribedDevices = [];
        private bool _repaintPending;

        public AllDevicesPage()
        {
            ViewModel = App.AppHost.Services.GetRequiredService<AllDevicesViewModel>();
            InitializeComponent();
            DeviceGridSort.RegisterDeviceColumns(_sortPaths, DeviceGrid);
            Unloaded += OnPageUnloaded;
        }

        public AllDevicesViewModel ViewModel
        {
            get;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs args)
        {
            base.OnNavigatedTo(args);
            ViewModel.RestoreOnlineOnlyFilter();
            SortPreference? pref = SortPreference.Load("devices");

            if (pref is not null)
            {
                ViewModel.Sort(pref.Property, pref.Ascending);
            }

            await ViewModel.LoadAsync();
            DeviceGridSort.ApplyIndicator(DeviceGrid, _sortPaths, ViewModel.SortProperty, ViewModel.SortAscending);
        }

        private void OnPageUnloaded(object sender, RoutedEventArgs args)
        {
            ViewModel.Detach();

            foreach (Device device in _subscribedDevices)
            {
                device.PropertyChanged -= OnDeviceApprovalChanged;
            }

            _subscribedDevices.Clear();
        }

        private void DataGridSorting(object sender, DataGridColumnEventArgs args)
        {
            DeviceGridSort.HandleSorting(args, DeviceGrid, _sortPaths, "devices", ViewModel.Sort);
        }

        private void DataGridLoadingRow(object sender, DataGridRowEventArgs args)
        {

            if (args.Row.DataContext is Device device)
            {

                if (_subscribedDevices.Add(device))
                {
                    device.PropertyChanged += OnDeviceApprovalChanged;
                }

                ApplyRowBackground(args.Row, device);
            }

        }

        private void DataGridUnloadingRow(object sender, DataGridRowEventArgs args)
        {

            if (args.Row.DataContext is Device device && _subscribedDevices.Remove(device))
            {
                device.PropertyChanged -= OnDeviceApprovalChanged;
            }

        }

        private void OnDeviceApprovalChanged(object? sender, PropertyChangedEventArgs args)
        {

            if (args.PropertyName == nameof(Device.IsApproved) && !_repaintPending)
            {
                _repaintPending = true;
                DispatcherQueue.TryEnqueue(RepaintRows);
            }

        }

        private void CopyButtonClick(object sender, RoutedEventArgs args)
        {
            DeviceDialogs.CopyTagToClipboard(sender);
        }

        private void HistoryButtonClick(object sender, RoutedEventArgs args)
        {
            DeviceDialogs.NavigateToHistory(sender);
        }

        private async void MarkApprovedButtonClick(object sender, RoutedEventArgs args)
        {

            if ((sender as FrameworkElement)?.Tag is Device device)
            {
                bool confirmed = await DeviceDialogs.ShowEditDeviceAsync(device, $"Approve — {device.MacAddress}", "Approve", XamlRoot);

                if (confirmed)
                {
                    await ViewModel.SaveDeviceAsync(device);
                    await ViewModel.MarkApprovedAsync(device);
                }

            }

        }

        private void RepaintRows()
        {
            _repaintPending = false;
            DeviceGrid.ItemsSource = null;
            DeviceGrid.ItemsSource = ViewModel.Devices;
        }

        private static void ApplyRowBackground(DataGridRow row, Device device)
        {
            row.Background = device.IsApproved
                ? null
                : new SolidColorBrush(Windows.UI.Color.FromArgb(0x40, 0xFF, 0x6B, 0x6B));
        }
    }
}
