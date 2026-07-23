using System.IO;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NetworkMonitor.Models.Digest;
using NetworkMonitor.ViewModels;
using WinRT.Interop;
using NetworkMonitor.Services.Platform;

namespace NetworkMonitor.Views
{
    public sealed partial class ReportsPage : Page
    {
        private readonly InAppNotificationService _notificationService;

        public ReportsPage()
        {
            ViewModel = App.AppHost.Services.GetRequiredService<ReportsViewModel>();
            _notificationService = App.AppHost.Services.GetRequiredService<InAppNotificationService>();
            InitializeComponent();
            TabBar.SelectedItem = TabBar.Items[0];
            Loaded += ReportsPageLoaded;
        }

        public ReportsViewModel ViewModel
        {
            get;
        }

        private async void ReportsPageLoaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs args)
        {
            await ViewModel.LoadAsync();
        }

        public string GenerateButtonLabel(bool isRunning)
        {
            string label = isRunning ? "Generating…" : "Generate now";

            return label;
        }

        public Visibility RunningToVisibility(bool isRunning)
        {
            Visibility visibility = isRunning ? Visibility.Visible : Visibility.Collapsed;

            return visibility;
        }

        private void TabBarSelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
        {

            if (sender.SelectedItem is not null)
            {
                string selectedTag = (string)sender.SelectedItem.Tag;
                DigestPanel.Visibility = selectedTag == "Digest" ? Visibility.Visible : Visibility.Collapsed;
                HistoryPanel.Visibility = selectedTag == "History" ? Visibility.Visible : Visibility.Collapsed;
            }

        }

        private async void ExportDigestPdfClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs args)
        {
            DigestReport? report = ViewModel.LatestReport;

            if (report is not null)
            {
                string suggestedFileName = BuildReportFileName(report);

                await SaveBytesAsync(() => ViewModel.BuildPdf(report), ".pdf", "PDF document", suggestedFileName, "PDF exported");
            }

        }

        private async void ExportHistoryPdfClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs args)
        {
            DigestReport? report = ViewModel.SelectedHistoryReport;

            if (report is not null)
            {
                string suggestedFileName = BuildReportFileName(report);

                await SaveBytesAsync(() => ViewModel.BuildPdf(report), ".pdf", "PDF document", suggestedFileName, "PDF exported");
            }

        }

        private async void ExportAllReportsCsvClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs args)
        {
            string suggestedFileName = $"Umnatha Network Monitor Digest Reports {DateTime.Now:yyyy-MM-dd HH-mm}";

            await SaveBytesAsync(() => EncodeUtf8WithBom(ViewModel.BuildAllReportsCsv()), ".csv", "CSV file", suggestedFileName, "All reports exported to CSV");
        }

        private async void DeleteHistoryClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs args)
        {
            DigestReport? report = ViewModel.SelectedHistoryReport;

            if (report is not null)
            {
                ContentDialog dialog = new ContentDialog
                {
                    Title = "Delete report?",
                    Content = $"Delete the report for {report.PeriodEndDisplay}? This cannot be undone.",
                    PrimaryButtonText = "Delete",
                    CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = XamlRoot
                };

                if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                {
                    await ViewModel.DeleteCommand.ExecuteAsync(null);
                }

            }

        }

        private static byte[] EncodeUtf8WithBom(string text)
        {
            UTF8Encoding encoding = new UTF8Encoding(true);
            byte[] preamble = encoding.GetPreamble();
            byte[] body = encoding.GetBytes(text);
            byte[] result = new byte[preamble.Length + body.Length];

            Buffer.BlockCopy(preamble, 0, result, 0, preamble.Length);
            Buffer.BlockCopy(body, 0, result, preamble.Length, body.Length);

            return result;
        }

        private static string BuildReportFileName(DigestReport? report)
        {
            DateTime stamp = report is null ? DateTime.Now : report.PeriodEnd.ToLocalTime();
            string suggestedFileName = $"Umnatha Network Monitor Digest {stamp:yyyy-MM-dd HH-mm}";

            return suggestedFileName;
        }

        private async Task SaveBytesAsync(Func<byte[]> buildData, string extension, string description, string suggestedFileName, string savedMessage)
        {
            IntPtr ownerHandle = MainWindow.Current is null ? IntPtr.Zero : WindowNative.GetWindowHandle(MainWindow.Current);
            string? targetPath = Win32FileSaveDialog.PickSavePath(ownerHandle, suggestedFileName, description, extension);

            if (targetPath is not null)
            {
                byte[] data = await Task.Run(buildData);

                if (data.Length > 0)
                {
                    await File.WriteAllBytesAsync(targetPath, data);
                    ShellLauncher.Open(targetPath);
                    _notificationService.Show($"{savedMessage}: {Path.GetFileName(targetPath)}");
                }

            }

        }

    }
}
