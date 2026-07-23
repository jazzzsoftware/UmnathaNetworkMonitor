using CommunityToolkit.WinUI.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System.IO;
using NetworkMonitor.Data;
using NetworkMonitor.Models.Devices;
using NetworkMonitor.ViewModels;
using NetworkMonitor.Services.Platform;
using NetworkMonitor.Core.Csv;

namespace NetworkMonitor.Views
{
    public sealed partial class ApprovedDevicesPage : Page
    {
        private readonly Dictionary<DataGridColumn, string> _sortPaths = [];
        private readonly InAppNotificationService _notificationService;

        public ApprovedDevicesPage()
        {
            ViewModel = App.AppHost.Services.GetRequiredService<AllDevicesViewModel>();
            _notificationService = App.AppHost.Services.GetRequiredService<InAppNotificationService>();
            InitializeComponent();
            DeviceGridSort.RegisterDeviceColumns(_sortPaths, DeviceGrid);
            Unloaded += OnPageUnloaded;
        }

        public AllDevicesViewModel ViewModel
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
            ViewModel.ShowApprovedOnly = true;
            SortPreference? pref = SortPreference.Load("approved");

            if (pref is not null)
            {
                ViewModel.Sort(pref.Property, pref.Ascending);
            }

            await ViewModel.LoadAsync();
            DeviceGridSort.ApplyIndicator(DeviceGrid, _sortPaths, ViewModel.SortProperty, ViewModel.SortAscending);
        }

        protected override void OnNavigatedFrom(NavigationEventArgs args)
        {
            base.OnNavigatedFrom(args);
            ViewModel.ShowApprovedOnly = false;
        }

        private void DataGridSorting(object sender, DataGridColumnEventArgs args)
        {
            DeviceGridSort.HandleSorting(args, DeviceGrid, _sortPaths, "approved", ViewModel.Sort);
        }

        private void DeviceGridSelectionChanged(object sender, SelectionChangedEventArgs args)
        {
        }

        private async void ImportButtonClick(object sender, RoutedEventArgs args)
        {
            IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(MainWindow.Current);
            string? path = OpenFileDialog.Show(hwnd, "Import Approved Devices", "CSV File", "csv");

            if (path is not null)
            {
                string title = "Import Approved Devices";
                string message;

                try
                {
                    string csvText = await File.ReadAllTextAsync(path);
                    IReadOnlyList<Device> candidates = DeviceCsvImporter.Parse(csvText);

                    if (candidates.Count == 0)
                    {
                        message = "No devices found in the file. Make sure it has a MAC Address column.";
                    }
                    else
                    {
                        (int added, int updated) = await ViewModel.ImportApprovedDevicesAsync(candidates);
                        await ViewModel.LoadAsync();
                        message = $"Added {added} new device{(added == 1 ? string.Empty : "s")}.\nApproved/updated {updated} existing device{(updated == 1 ? string.Empty : "s")}.";
                        ViewModel.StatusText = $"Import complete — {added} added, {updated} updated";
                        _notificationService.Show($"Imported {Path.GetFileName(path)}: {added} added, {updated} updated");
                    }

                }
                catch (Exception exception)
                {
                    AppLog.Error("ApprovedDevicesPage.ImportCsv", exception);
                    message = $"Import failed: {exception.Message}";
                }

                ContentDialog resultDialog = new()
                {
                    Title = title,
                    Content = message,
                    CloseButtonText = "OK",
                    XamlRoot = XamlRoot
                };

                await resultDialog.ShowAsync();
            }

        }

        private async void ExportButtonClick(object sender, RoutedEventArgs args)
        {
            List<Device> approved = await ViewModel.GetApprovedDevicesAsync();

            if (approved.Count == 0)
            {
                ContentDialog emptyDialog = new()
                {
                    Title = "Export Approved Devices",
                    Content = "There are no approved devices to export.",
                    CloseButtonText = "OK",
                    XamlRoot = XamlRoot
                };

                await emptyDialog.ShowAsync();
            }
            else
            {
                IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(MainWindow.Current);
                string suggestedFileName = $"Umnatha Network Monitor Approved Devices {DateTime.Now:yyyy-MM-dd HH-mm}";
                string? path = Win32FileSaveDialog.PickSavePath(hwnd, suggestedFileName, "CSV File", ".csv", "Export Approved Devices");

                if (path is not null)
                {
                    string csv = DeviceCsvExporter.ToCsv(approved);
                    await File.WriteAllTextAsync(path, csv);
                    ShellLauncher.Open(path);
                    ViewModel.StatusText = $"Exported {approved.Count} approved device{(approved.Count == 1 ? string.Empty : "s")} to {Path.GetFileName(path)}";
                    _notificationService.Show($"Exported {approved.Count} approved device{(approved.Count == 1 ? string.Empty : "s")} to {Path.GetFileName(path)}");
                }

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

        private async void EditButtonClick(object sender, RoutedEventArgs args)
        {

            if ((sender as FrameworkElement)?.Tag is Device device)
            {
                bool confirmed = await DeviceDialogs.ShowEditDeviceAsync(device, $"Edit — {device.MacAddress}", "Save", XamlRoot);

                if (confirmed)
                {
                    await ViewModel.SaveDeviceAsync(device);
                    await ViewModel.LoadAsync();
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
                    await ViewModel.DeleteDeviceAsync(device);
                }

            }

        }
    }
}
