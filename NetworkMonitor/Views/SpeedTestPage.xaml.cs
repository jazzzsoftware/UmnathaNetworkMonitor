using System;
using System.Collections.Generic;
using System.IO;
using CommunityToolkit.WinUI.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using NetworkMonitor.Data;
using NetworkMonitor.Services.Csv;
using NetworkMonitor.Services.Platform;
using NetworkMonitor.ViewModels;
using NetworkMonitor.Models.Formatting;

namespace NetworkMonitor.Views
{
    public sealed partial class SpeedTestPage : Page
    {
        private readonly Dictionary<DataGridColumn, string> _sortPaths = [];

        public SpeedTestPage()
        {
            ViewModel = App.AppHost.Services.GetRequiredService<SpeedTestViewModel>();
            InitializeComponent();
            _sortPaths[HistoryGrid.Columns[0]] = "Timestamp";
            _sortPaths[HistoryGrid.Columns[1]] = "DownloadMbps";
            _sortPaths[HistoryGrid.Columns[2]] = "UploadMbps";
            _sortPaths[HistoryGrid.Columns[3]] = "DownloadMBps";
            _sortPaths[HistoryGrid.Columns[4]] = "UploadMBps";
            _sortPaths[HistoryGrid.Columns[5]] = "LatencyMs";
            _sortPaths[HistoryGrid.Columns[6]] = "JitterMs";
            _sortPaths[HistoryGrid.Columns[7]] = "Server";
        }

        public SpeedTestViewModel ViewModel
        {
            get;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs args)
        {
            base.OnNavigatedTo(args);

            ApplyRateUnitMode();
            UpdateRangeButtonStyles(Range24hButton);
            SortPreference? pref = SortPreference.Load("speedtest");

            if (pref is not null)
            {
                ViewModel.Sort(pref.Property, pref.Ascending);
            }

            await ViewModel.LoadAsync();
            DeviceGridSort.ApplyIndicator(HistoryGrid, _sortPaths, ViewModel.SortProperty, ViewModel.SortAscending);
        }

        private void ApplyRateUnitMode()
        {
            RateUnitMode mode = TrafficRateFormatter.Mode;
            Visibility bitsVisibility = mode == RateUnitMode.Bytes ? Visibility.Collapsed : Visibility.Visible;
            Visibility bytesVisibility = mode == RateUnitMode.Bits ? Visibility.Collapsed : Visibility.Visible;

            DownloadBitsText.Visibility = bitsVisibility;
            UploadBitsText.Visibility = bitsVisibility;
            DownloadBytesText.Visibility = bytesVisibility;
            UploadBytesText.Visibility = bytesVisibility;
            HistoryGrid.Columns[1].Visibility = bitsVisibility;
            HistoryGrid.Columns[2].Visibility = bitsVisibility;
            HistoryGrid.Columns[3].Visibility = bytesVisibility;
            HistoryGrid.Columns[4].Visibility = bytesVisibility;

            if (mode == RateUnitMode.Bits)
            {
                ThroughputTitle.Text = "Throughput (Mb/s)";
                ThroughputChart.Unit = "Mb/s";
                ThroughputChart.SecondaryUnit = string.Empty;
                ThroughputChart.SecondaryDivisor = 0.0;
                ThroughputChart.PrimaryDivisor = 1.0;
            }
            else if (mode == RateUnitMode.Bytes)
            {
                ThroughputTitle.Text = "Throughput (MB/s)";
                ThroughputChart.Unit = "MB/s";
                ThroughputChart.SecondaryUnit = string.Empty;
                ThroughputChart.SecondaryDivisor = 0.0;
                ThroughputChart.PrimaryDivisor = 8.0;
            }
            else
            {
                ThroughputTitle.Text = "Throughput (Mb/s · MB/s)";
                ThroughputChart.Unit = "Mb/s";
                ThroughputChart.SecondaryUnit = "MB/s";
                ThroughputChart.SecondaryDivisor = 8.0;
                ThroughputChart.PrimaryDivisor = 1.0;
            }

        }

        private void DataGridSorting(object sender, DataGridColumnEventArgs args)
        {
            DeviceGridSort.HandleSorting(args, HistoryGrid, _sortPaths, "speedtest", ViewModel.Sort);
        }

        private void RangeButtonClick(object sender, RoutedEventArgs args)
        {

            if (sender is Button button && button.Tag is string tag && int.TryParse(tag, out int hours))
            {
                ViewModel.ChartRangeHours = hours;
                UpdateRangeButtonStyles(button);
                _ = ViewModel.LoadAsync();
            }

        }

        private void UpdateRangeButtonStyles(Button activeButton)
        {
            Button[] allButtons = [Range24hButton, Range7dButton];

            foreach (Button button in allButtons)
            {
                button.Style = button == activeButton
                    ? (Style)Application.Current.Resources["AccentButtonStyle"]
                    : null;
            }

        }

        private async void ExportCsvClick(object sender, RoutedEventArgs args)
        {

            if (ViewModel.History.Count == 0)
            {
                StatusText.Text = "No speed test results to export.";
            }
            else
            {
                IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(MainWindow.Current);
                string suggestedFileName = $"Umnatha Network Monitor SpeedTests {DateTime.Now:yyyy-MM-dd HH-mm}";
                string? path = Win32FileSaveDialog.PickSavePath(hwnd, suggestedFileName, "CSV File", ".csv", "Export Speed Tests");

                if (path is not null)
                {
                    string csv = SpeedTestCsvExporter.ToCsv(ViewModel.History);
                    await File.WriteAllTextAsync(path, csv);
                    ShellLauncher.Open(path);
                    StatusText.Text = $"Exported {ViewModel.History.Count} result{(ViewModel.History.Count == 1 ? string.Empty : "s")} to {Path.GetFileName(path)}";
                }

            }

        }
    }
}
