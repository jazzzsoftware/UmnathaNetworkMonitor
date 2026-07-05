using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using NetworkMonitor.Data;
using NetworkMonitor.Services.Platform;
using NetworkMonitor.ViewModels;

namespace NetworkMonitor.Views
{
    public sealed partial class SettingsPage : Page
    {
        public SettingsPage()
        {
            ViewModel = App.AppHost.Services.GetRequiredService<SettingsViewModel>();
            InitializeComponent();
            TabBar.SelectedItem = TabBar.Items[0];
            VersionText.Text = $"v{AppInfo.GetVersion()}";
            ReleaseNotesVersion.Text = $"Version {AppInfo.GetVersion()}";
            AboutLogo.Source = new BitmapImage(
                new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "splash-logo.png")));
        }

        public SettingsViewModel ViewModel
        {
            get;
        }

        private void TabBarSelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
        {

            if (sender.SelectedItem is not null)
            {
                string selectedTag = (string)sender.SelectedItem.Tag;
                TrafficPanel.Visibility = selectedTag == "Traffic" ? Visibility.Visible : Visibility.Collapsed;
                DevicePanel.Visibility = selectedTag == "Device" ? Visibility.Visible : Visibility.Collapsed;
                OtherPanel.Visibility = selectedTag == "Other" ? Visibility.Visible : Visibility.Collapsed;
            }

        }

        private void TrafficIntervalSecondsBoxValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {

            if (!double.IsNaN(args.NewValue))
            {
                ViewModel.TrafficIntervalSeconds = (int)args.NewValue;
            }

        }

        private async void PurgeNowClick(object sender, RoutedEventArgs args)
        {
            ContentDialog dialog = new()
            {
                Title = "Purge History",
                Content = $"Delete all history events older than {ViewModel.HistoryPurgeDays} days? This cannot be undone.",
                PrimaryButtonText = "Purge",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                await ViewModel.PurgeHistoryAsync();
            }

        }

        private async void PurgeTrafficNowClick(object sender, RoutedEventArgs args)
        {
            ContentDialog dialog = new()
            {
                Title = "Purge Traffic",
                Content = $"Delete all traffic entries older than {ViewModel.TrafficPurgeDays} days? This cannot be undone.",
                PrimaryButtonText = "Purge",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                await ViewModel.PurgeTrafficAsync();
            }

        }

        private async void AboutClick(object sender, RoutedEventArgs args)
        {
            AboutDialog.XamlRoot = XamlRoot;
            await AboutDialog.ShowAsync();
        }

        private async void ReleaseNotesClick(object sender, RoutedEventArgs args)
        {
            ReleaseNotesDialog.XamlRoot = XamlRoot;
            await ReleaseNotesDialog.ShowAsync();
        }

        private void OpenLogsFolderClick(object sender, RoutedEventArgs args)
        {

            try
            {
                Directory.CreateDirectory(AppLog.LogDirectory);

                Process.Start(new ProcessStartInfo
                {
                    FileName = AppLog.LogDirectory,
                    UseShellExecute = true
                });
            }
            catch (Exception exception)
            {
                AppLog.Error("SettingsPage.OpenLogsFolder", exception);
            }

        }

        private void OpenDataFolderClick(object sender, RoutedEventArgs args)
        {

            try
            {
                Directory.CreateDirectory(AppPaths.AppDataFolder);

                Process.Start(new ProcessStartInfo
                {
                    FileName = AppPaths.AppDataFolder,
                    UseShellExecute = true
                });
            }
            catch (Exception exception)
            {
                AppLog.Error("SettingsPage.OpenDataFolder", exception);
            }

        }
    }
}
