using CommunityToolkit.WinUI.UI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NetworkMonitor.Data;
using NetworkMonitor.Models;
using Windows.ApplicationModel.DataTransfer;

namespace NetworkMonitor.Views
{
    internal static class DeviceGridSort
    {
        public static void RegisterDeviceColumns(Dictionary<DataGridColumn, string> sortPaths, DataGrid grid)
        {
            sortPaths[grid.Columns[0]] = "IsOnline";
            sortPaths[grid.Columns[1]] = "Type";
            sortPaths[grid.Columns[2]] = "DisplayName";
            sortPaths[grid.Columns[3]] = "IpAddress";
            sortPaths[grid.Columns[4]] = "MacAddress";
            sortPaths[grid.Columns[5]] = "Vendor";
        }

        public static void ApplyIndicator(DataGrid grid, Dictionary<DataGridColumn, string> sortPaths, string sortProperty, bool sortAscending)
        {

            foreach (DataGridColumn column in grid.Columns)
            {
                bool isSort = sortPaths.TryGetValue(column, out string? path) && path == sortProperty;
                column.SortDirection = isSort
                    ? sortAscending ? DataGridSortDirection.Ascending : DataGridSortDirection.Descending
                    : null;
            }

        }

        public static void HandleSorting(DataGridColumnEventArgs args, DataGrid grid, Dictionary<DataGridColumn, string> sortPaths, string? pageKey, Action<string, bool> sort)
        {

            if (sortPaths.TryGetValue(args.Column, out string? property))
            {
                bool ascending = args.Column.SortDirection != DataGridSortDirection.Ascending;

                foreach (DataGridColumn column in grid.Columns)
                {
                    column.SortDirection = null;
                }

                args.Column.SortDirection = ascending ? DataGridSortDirection.Ascending : DataGridSortDirection.Descending;
                sort(property, ascending);

                if (pageKey is not null)
                {
                    new SortPreference(property, ascending).Save(pageKey);
                }

            }

        }
    }

    internal static class DeviceDialogs
    {
        public static void CopyTagToClipboard(object sender)
        {

            if ((sender as FrameworkElement)?.Tag is string text && !string.IsNullOrEmpty(text))
            {
                DataPackage package = new();
                package.SetText(text);
                Clipboard.SetContent(package);
            }

        }

        public static void NavigateToHistory(object sender)
        {

            if ((sender as FrameworkElement)?.Tag is string mac)
            {
                MainWindow.Current?.NavigateToHistory(mac);
            }

        }

        public static async Task<bool> ShowEditDeviceAsync(Device device, string title, string primaryButtonText, XamlRoot xamlRoot)
        {
            TextBox nameBox = new()
            {
                Text = device.FriendlyName ?? string.Empty, PlaceholderText = "Friendly name"
            };
            TextBox notesBox = new()
            {
                Text = device.Notes ?? string.Empty, PlaceholderText = "Notes", AcceptsReturn = true, Height = 80
            };
            ComboBox typeCombo = new()
            {
                ItemsSource = Enum.GetValues<DeviceType>(), SelectedItem = device.Type
            };

            StackPanel panel = new()
            {
                Spacing = 8
            };
            panel.Children.Add(new TextBlock
            {
                Text = "Friendly Name"
            });
            panel.Children.Add(nameBox);
            panel.Children.Add(new TextBlock
            {
                Text = "Type"
            });
            panel.Children.Add(typeCombo);
            panel.Children.Add(new TextBlock
            {
                Text = "Notes"
            });
            panel.Children.Add(notesBox);

            ContentDialog dialog = new()
            {
                Title = title,
                Content = panel,
                PrimaryButtonText = primaryButtonText,
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = xamlRoot
            };

            bool confirmed = await dialog.ShowAsync() == ContentDialogResult.Primary;

            if (confirmed)
            {
                device.FriendlyName = string.IsNullOrWhiteSpace(nameBox.Text) ? null : nameBox.Text.Trim();
                device.Type = typeCombo.SelectedItem is DeviceType selectedType ? selectedType : DeviceType.Unknown;
                device.Notes = string.IsNullOrWhiteSpace(notesBox.Text) ? null : notesBox.Text.Trim();
            }

            return confirmed;
        }

        public static async Task<bool> ShowDeleteConfirmAsync(Device device, XamlRoot xamlRoot)
        {
            ContentDialog dialog = new()
            {
                Title = "Delete device?",
                Content = $"Remove {device.DisplayName} ({device.MacAddress})?\nIt will re-appear as unapproved next time it is scanned.",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = xamlRoot
            };

            bool confirmed = await dialog.ShowAsync() == ContentDialogResult.Primary;

            return confirmed;
        }
    }
}
