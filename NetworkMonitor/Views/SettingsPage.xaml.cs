using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using NetworkMonitor.Services.Data;
using NetworkMonitor.Services.Platform;
using NetworkMonitor.ViewModels;
using NetworkMonitor.Models.Devices;

namespace NetworkMonitor.Views
{
    public sealed partial class SettingsPage : Page
    {
        private readonly MiniGraphState _miniGraphState = App.AppHost.Services.GetRequiredService<MiniGraphState>();

        public SettingsPage()
        {
            ViewModel = App.AppHost.Services.GetRequiredService<SettingsViewModel>();
            UpdateViewModel = App.AppHost.Services.GetRequiredService<UpdateViewModel>();
            InitializeComponent();
            TabBar.SelectedItem = TabBar.Items[0];
            VersionText.Text = $"v{AppInfo.GetVersion()}";
            AboutLogo.Source = new BitmapImage(
                new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "splash-logo.png")));
            Loaded += OnPageLoaded;
            Unloaded += OnPageUnloaded;

            // handledEventsToo: a ScrollViewer marks PointerPressed handled for its own scrolling,
            // so the XAML attribute form of this would never fire.
            foreach (ScrollViewer panel in new[] { TrafficPanel, DevicePanel, ThemePanel, OtherPanel })
            {
                panel.AddHandler(PointerPressedEvent, new PointerEventHandler(SettingsPanelPointerPressed), true);
            }

        }

        public SettingsViewModel ViewModel
        {
            get;
        }

        public UpdateViewModel UpdateViewModel
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
                ThemePanel.Visibility = selectedTag == "Theme" ? Visibility.Visible : Visibility.Collapsed;
                OtherPanel.Visibility = selectedTag == "Other" ? Visibility.Visible : Visibility.Collapsed;
            }

        }

        private void ChartColourFlyoutClosed(object sender, object args)
        {
            ViewModel.SaveCustomColours();
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

            if (ViewModel.HistoryPurgeDays <= 0)
            {
                await ShowPurgeDisabledAsync("Purge History");

                return;
            }

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

        private async Task ShowPurgeDisabledAsync(string title)
        {
            ContentDialog dialog = new()
            {
                Title = title,
                Content = "Purging is disabled while the day count is 0. Set the number of days to keep first.",
                CloseButtonText = "OK",
                XamlRoot = XamlRoot
            };

            await dialog.ShowAsync();
        }

        private async void PurgeTrafficNowClick(object sender, RoutedEventArgs args)
        {

            if (ViewModel.TrafficPurgeDays <= 0)
            {
                await ShowPurgeDisabledAsync("Purge Traffic");

                return;
            }

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

        private void OnMiniGraphStateChanged(object? sender, EventArgs args)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                ViewModel.SyncMiniGraphFromState();
            });
        }

        private void OnPageLoaded(object sender, RoutedEventArgs args)
        {
            ViewModel.SyncMiniGraphFromState();

            // See TrafficHostPage.OnPageLoaded: a re-parent can raise Loaded without an intervening
            // Unloaded, and _miniGraphState is a singleton.
            _miniGraphState.Changed -= OnMiniGraphStateChanged;
            _miniGraphState.Changed += OnMiniGraphStateChanged;
        }

        private void OnPageUnloaded(object sender, RoutedEventArgs args)
        {
            _miniGraphState.Changed -= OnMiniGraphStateChanged;

            // A colour picker left open when the window closes never raises Flyout.Closed, so this
            // is the last chance to write a colour the user has already seen applied.
            ViewModel.SaveCustomColours();
        }

        // A NumberBox writes its typed text back to the binding on Enter, on losing focus, or through
        // the spin buttons — and on nothing else. Clicking inert space (a label, a panel, the page
        // background) moves focus nowhere, so the box carried on displaying a number that had never
        // reached the view model or settings.json, with no "Settings saved" toast and nothing else to
        // say so. That is how a retention value the user had visibly typed silently failed to save.
        // Moving focus off the box here turns that case into the focus-loss case that already worked.
        private void SettingsPanelPointerPressed(object sender, PointerRoutedEventArgs args)
        {

            if (XamlRoot is not null
                && FocusManager.GetFocusedElement(XamlRoot) is DependencyObject focused
                && FindAncestor<NumberBox>(focused) is NumberBox focusedBox
                && !IsWithin(args.OriginalSource as DependencyObject, focusedBox))
            {
                TabBar.Focus(FocusState.Programmatic);
            }

        }

        // Focus never lands on the NumberBox itself. It is a composite control and the caret sits in
        // the TextBox inside its template, so the focused element has to be walked up to find the box
        // it belongs to.
        private static T? FindAncestor<T>(DependencyObject? node) where T : class
        {
            T? found = null;
            DependencyObject? current = node;

            while (current is not null)
            {

                if (current is T match)
                {
                    found = match;

                    break;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return found;
        }

        private static bool IsWithin(DependencyObject? node, DependencyObject ancestor)
        {
            bool found = false;
            DependencyObject? current = node;

            while (current is not null)
            {

                if (ReferenceEquals(current, ancestor))
                {
                    found = true;

                    break;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return found;
        }
    }
}
