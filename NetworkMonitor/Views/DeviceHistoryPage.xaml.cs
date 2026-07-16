using System.IO;
using CommunityToolkit.WinUI.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using NetworkMonitor.Models;
using NetworkMonitor.Services.Csv;
using NetworkMonitor.Services.Platform;
using NetworkMonitor.ViewModels;

namespace NetworkMonitor.Views
{
    public sealed partial class DeviceHistoryPage : Page
    {
        private readonly Dictionary<DataGridColumn, string> _sortPaths = [];
        private readonly InAppNotificationService _notificationService;

        public DeviceHistoryPage()
        {
            ViewModel = App.AppHost.Services.GetRequiredService<DeviceHistoryViewModel>();
            _notificationService = App.AppHost.Services.GetRequiredService<InAppNotificationService>();
            InitializeComponent();
            _sortPaths[HistoryGrid.Columns[0]] = "Timestamp";
            _sortPaths[HistoryGrid.Columns[1]] = "EventType";
            _sortPaths[HistoryGrid.Columns[2]] = "Type";
            _sortPaths[HistoryGrid.Columns[3]] = "DisplayName";
            _sortPaths[HistoryGrid.Columns[4]] = "IpAddress";
            _sortPaths[HistoryGrid.Columns[5]] = "MacAddress";
            _sortPaths[HistoryGrid.Columns[6]] = "Vendor";
            Unloaded += OnPageUnloaded;
        }

        public DeviceHistoryViewModel ViewModel
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

            if (args.Parameter is string mac && !string.IsNullOrEmpty(mac))
            {
                ViewModel.SearchText = mac;
            }

            ViewModel.Sort("Timestamp", false);
            await ViewModel.LoadAsync();
            DeviceGridSort.ApplyIndicator(HistoryGrid, _sortPaths, ViewModel.SortProperty, ViewModel.SortAscending);
        }

        private void CopyButtonClick(object sender, RoutedEventArgs args)
        {
            DeviceDialogs.CopyTagToClipboard(sender);
        }

        private async void ExportButtonClick(object sender, RoutedEventArgs args)
        {
            IReadOnlyList<DeviceEvent> events = ViewModel.GetEventsForExport();

            if (events.Count == 0)
            {
                ContentDialog emptyDialog = new()
                {
                    Title = "Export Device History",
                    Content = "There are no history events to export.",
                    CloseButtonText = "OK",
                    XamlRoot = XamlRoot
                };

                await emptyDialog.ShowAsync();
            }
            else
            {
                IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(MainWindow.Current);
                string suggestedFileName = $"Umnatha Network Monitor Device History {DateTime.Now:yyyy-MM-dd HH-mm}";
                string? path = Win32FileSaveDialog.PickSavePath(hwnd, suggestedFileName, "CSV File", ".csv", "Export Device History");

                if (path is not null)
                {
                    string csv = DeviceEventCsvExporter.ToCsv(events);
                    await File.WriteAllTextAsync(path, csv);
                    ShellLauncher.Open(path);
                    ViewModel.StatusText = $"Exported {events.Count} event{(events.Count == 1 ? string.Empty : "s")} to {Path.GetFileName(path)}";
                    _notificationService.Show($"Exported {events.Count} event{(events.Count == 1 ? string.Empty : "s")} to {Path.GetFileName(path)}");
                }

            }

        }

        private void DataGridSorting(object sender, DataGridColumnEventArgs args)
        {
            DeviceGridSort.HandleSorting(args, HistoryGrid, _sortPaths, null, ViewModel.Sort);
        }
    }
}
