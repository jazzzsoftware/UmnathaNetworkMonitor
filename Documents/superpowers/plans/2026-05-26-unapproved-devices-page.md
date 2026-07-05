# Unapproved Devices Page Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an Unapproved Devices page that lists devices seen in the last 24 hours that have not yet been approved (`IsKnown = false`), with Approve and History actions per row.

**Architecture:** A new `UnapprovedDevicesViewModel` handles its own DB query and in-memory filter/sort — no shared state with `DevicesViewModel`. `UnapprovedDevicesPage` mirrors the `ApprovedDevicesPage` layout with an Approve button (identical dialog to `DevicesPage`) and a History button in the Actions column. A new nav item is wired between Devices and Approved Devices in `MainWindow`.

**Tech Stack:** WinUI 3 (Windows App SDK), CommunityToolkit.Mvvm, CommunityToolkit.WinUI.UI.Controls.DataGrid, EF Core 10 + SQLite, `IDbContextFactory<AppDbContext>`, `SortPreference`.

---

## File Map

| File | Action |
|---|---|
| `NetworkMonitor/ViewModels/UnapprovedDevicesViewModel.cs` | Create |
| `NetworkMonitor/Views/UnapprovedDevicesPage.xaml` | Create |
| `NetworkMonitor/Views/UnapprovedDevicesPage.xaml.cs` | Create |
| `NetworkMonitor/MainWindow.xaml` | Modify — add nav item |
| `NetworkMonitor/MainWindow.xaml.cs` | Modify — add route |
| `NetworkMonitor/App.xaml.cs` | Modify — register ViewModel |

---

## Task 1: Create UnapprovedDevicesViewModel

**Files:**
- Create: `NetworkMonitor/ViewModels/UnapprovedDevicesViewModel.cs`

- [ ] **Step 1: Create the ViewModel file**

Create `NetworkMonitor/ViewModels/UnapprovedDevicesViewModel.cs` with this exact content:

```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Dispatching;
using NetworkMonitor.Data;
using NetworkMonitor.Models;

namespace NetworkMonitor.ViewModels
{
    public partial class UnapprovedDevicesViewModel : ObservableObject
    {
        private readonly DispatcherQueue _dispatcherQueue;
        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private List<Device> _allDevices = [];
        private string _sortProperty = "IpAddress";
        private bool _sortAscending = true;

        [ObservableProperty]
        private ObservableCollection<Device> _devices = [];

        [ObservableProperty]
        private string _searchText = string.Empty;

        [ObservableProperty]
        private string _statusText = string.Empty;

        public string SortProperty => _sortProperty;
        public bool SortAscending => _sortAscending;

        public UnapprovedDevicesViewModel(IDbContextFactory<AppDbContext> dbFactory)
        {
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            _dbFactory = dbFactory;
        }

        public async Task LoadAsync()
        {
            DateTime cutoff = DateTime.UtcNow.AddHours(-24);
            await using AppDbContext db = await _dbFactory.CreateDbContextAsync();
            _allDevices = await db.Devices
                .Where(d => !d.IsKnown && (d.IsOnline || d.LastSeen >= cutoff))
                .ToListAsync();
            _dispatcherQueue.TryEnqueue(ApplyFilter);
        }

        public void Sort(string property, bool ascending)
        {
            _sortProperty = property;
            _sortAscending = ascending;
            ApplyFilter();
        }

        public async Task ApproveAsync(Device device)
        {
            await using AppDbContext db = await _dbFactory.CreateDbContextAsync();
            Device? d = await db.Devices.FindAsync(device.Id);
            if (d is not null)
            {
                d.IsKnown = true;
                d.FriendlyName = device.FriendlyName;
                d.Type = device.Type;
                d.Notes = device.Notes;
                await db.SaveChangesAsync();
                _allDevices.Remove(_allDevices.First(x => x.Id == device.Id));
                ApplyFilter();
            }
        }

        partial void OnSearchTextChanged(string value)
        {
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            IEnumerable<Device> filtered = _allDevices;

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                string q = SearchText.Trim().ToLowerInvariant();
                filtered = filtered.Where(d =>
                    (d.FriendlyName?.ToLowerInvariant().Contains(q) ?? false) ||
                    (d.Hostname?.ToLowerInvariant().Contains(q) ?? false) ||
                    (d.Vendor?.ToLowerInvariant().Contains(q) ?? false) ||
                    d.IpAddress.Contains(q) ||
                    d.MacAddress.ToLowerInvariant().Contains(q));
            }

            List<Device> sorted = ApplySorting(filtered).ToList();
            Devices = new ObservableCollection<Device>(sorted);
            int count = sorted.Count;
            StatusText = count == 1 ? "1 unapproved device" : $"{count} unapproved devices";
        }

        private IEnumerable<Device> ApplySorting(IEnumerable<Device> source)
        {
            Func<Device, object?> key = _sortProperty switch
            {
                "IsOnline" => d => d.IsOnline,
                "Type" => d => (int)d.Type,
                "DisplayName" => d => d.DisplayName,
                "IpAddress" => d => IpSortKey(d.IpAddress),
                "MacAddress" => d => d.MacAddress,
                "Vendor" => d => d.Vendor,
                _ => d => IpSortKey(d.IpAddress)
            };
            return _sortAscending ? source.OrderBy(key) : source.OrderByDescending(key);
        }

        private static string IpSortKey(string ip)
        {
            return System.Net.IPAddress.TryParse(ip, out System.Net.IPAddress? addr)
                ? string.Join(".", addr.GetAddressBytes().Select(b => b.ToString("D3")))
                : ip;
        }
    }
}
```

- [ ] **Step 2: Build to verify the ViewModel compiles**

Open the solution in Visual Studio and build (Ctrl+Shift+B), targeting x64.

Expected: Build succeeds with no errors in `UnapprovedDevicesViewModel.cs`.

---

## Task 2: Create UnapprovedDevicesPage XAML

**Files:**
- Create: `NetworkMonitor/Views/UnapprovedDevicesPage.xaml`

- [ ] **Step 1: Create the XAML file**

Create `NetworkMonitor/Views/UnapprovedDevicesPage.xaml` with this exact content:

```xml
<?xml version="1.0" encoding="utf-8"?>

<Page
    x:Class="NetworkMonitor.Views.UnapprovedDevicesPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:controls="using:CommunityToolkit.WinUI.UI.Controls"
    xmlns:models="using:NetworkMonitor.Models"
    Background="{ThemeResource ApplicationPageBackgroundThemeBrush}">

    <Grid
        RowDefinitions="Auto,*,Auto"
        Padding="16,12,16,12">

        <!-- Toolbar -->
        <AutoSuggestBox
            Grid.Row="0"
            PlaceholderText="Search Type, Name, IP, MAC…"
            Text="{x:Bind ViewModel.SearchText, Mode=TwoWay}"
            MaxWidth="380"
            HorizontalAlignment="Left"
            Margin="0,0,0,10"
            QueryIcon="Find" />

        <!-- Device Grid -->
        <controls:DataGrid
            Grid.Row="1"
            x:Name="DeviceGrid"
            ItemsSource="{x:Bind ViewModel.Devices, Mode=OneWay}"
            AutoGenerateColumns="False"
            IsReadOnly="True"
            GridLinesVisibility="Horizontal"
            SelectionMode="Single"
            CanUserSortColumns="True"
            Sorting="DataGrid_Sorting"
            BorderThickness="1"
            BorderBrush="{ThemeResource CardStrokeColorDefaultBrush}">

            <controls:DataGrid.Columns>

                <controls:DataGridTemplateColumn
                    Header="Status"
                    Width="80">
                    <controls:DataGridTemplateColumn.CellTemplate>
                        <DataTemplate
                            x:DataType="models:Device">
                            <StackPanel
                                Orientation="Horizontal"
                                VerticalAlignment="Center"
                                Spacing="6"
                                Padding="8,0,0,0">
                                <Ellipse
                                    Width="10"
                                    Height="10"
                                    Fill="{x:Bind IsOnline,
                                               Converter={StaticResource OnlineStatusConverter},
                                               ConverterParameter=brush,
                                               Mode=OneWay}" />
                                <TextBlock
                                    Text="{x:Bind LastSeenLabel, Mode=OneWay}"
                                    VerticalAlignment="Center"
                                    FontSize="13" />
                            </StackPanel>
                        </DataTemplate>
                    </controls:DataGridTemplateColumn.CellTemplate>
                </controls:DataGridTemplateColumn>

                <controls:DataGridTemplateColumn
                    Header="Type"
                    Width="140">
                    <controls:DataGridTemplateColumn.CellTemplate>
                        <DataTemplate
                            x:DataType="models:Device">
                            <StackPanel
                                Orientation="Horizontal"
                                VerticalAlignment="Center"
                                Spacing="6"
                                Padding="8,0,0,0">
                                <TextBlock
                                    Text="{x:Bind TypeIcon, Mode=OneWay}"
                                    FontFamily="Segoe UI Emoji"
                                    FontSize="14"
                                    VerticalAlignment="Center" />
                                <TextBlock
                                    Text="{x:Bind Type, Mode=OneWay}"
                                    VerticalAlignment="Center" />
                            </StackPanel>
                        </DataTemplate>
                    </controls:DataGridTemplateColumn.CellTemplate>
                </controls:DataGridTemplateColumn>

                <controls:DataGridTemplateColumn
                    Header="Name"
                    Width="300">
                    <controls:DataGridTemplateColumn.CellTemplate>
                        <DataTemplate
                            x:DataType="models:Device">
                            <TextBlock
                                Text="{x:Bind DisplayName, Mode=OneWay}"
                                VerticalAlignment="Center"
                                Padding="8,0,0,0"
                                ToolTipService.ToolTip="{x:Bind Notes, Mode=OneWay}" />
                        </DataTemplate>
                    </controls:DataGridTemplateColumn.CellTemplate>
                </controls:DataGridTemplateColumn>

                <controls:DataGridTemplateColumn
                    Header="IP"
                    Width="155">
                    <controls:DataGridTemplateColumn.CellTemplate>
                        <DataTemplate
                            x:DataType="models:Device">
                            <StackPanel
                                Orientation="Horizontal"
                                VerticalAlignment="Center"
                                Spacing="4"
                                Padding="8,0,0,0">
                                <TextBlock
                                    Text="{x:Bind IpAddress, Mode=OneWay}"
                                    VerticalAlignment="Center" />
                                <Button
                                    Content="&#xE8C8;"
                                    FontFamily="Segoe MDL2 Assets"
                                    FontSize="11"
                                    Tag="{x:Bind IpAddress, Mode=OneWay}"
                                    Click="CopyButton_Click"
                                    Padding="3"
                                    Width="20"
                                    Height="20"
                                    BorderThickness="0"
                                    Background="Transparent"
                                    ToolTipService.ToolTip="Copy IP" />
                            </StackPanel>
                        </DataTemplate>
                    </controls:DataGridTemplateColumn.CellTemplate>
                </controls:DataGridTemplateColumn>

                <controls:DataGridTemplateColumn
                    Header="MAC"
                    Width="190">
                    <controls:DataGridTemplateColumn.CellTemplate>
                        <DataTemplate
                            x:DataType="models:Device">
                            <StackPanel
                                Orientation="Horizontal"
                                VerticalAlignment="Center"
                                Spacing="4"
                                Padding="8,0,0,0">
                                <TextBlock
                                    Text="{x:Bind MacAddress, Mode=OneWay}"
                                    VerticalAlignment="Center" />
                                <Button
                                    Content="&#xE8C8;"
                                    FontFamily="Segoe MDL2 Assets"
                                    FontSize="11"
                                    Tag="{x:Bind MacAddress, Mode=OneWay}"
                                    Click="CopyButton_Click"
                                    Padding="3"
                                    Width="20"
                                    Height="20"
                                    BorderThickness="0"
                                    Background="Transparent"
                                    ToolTipService.ToolTip="Copy MAC" />
                            </StackPanel>
                        </DataTemplate>
                    </controls:DataGridTemplateColumn.CellTemplate>
                </controls:DataGridTemplateColumn>

                <controls:DataGridTextColumn
                    Header="Vendor"
                    Binding="{Binding Vendor}"
                    Width="200" />

                <controls:DataGridTemplateColumn
                    Header="Actions"
                    Width="200">
                    <controls:DataGridTemplateColumn.CellTemplate>
                        <DataTemplate
                            x:DataType="models:Device">
                            <StackPanel
                                Orientation="Horizontal"
                                Spacing="4"
                                Padding="4,0,0,0">
                                <Button
                                    Content="Approve"
                                    Tag="{x:Bind}"
                                    Click="ApproveButton_Click"
                                    Padding="8,4"
                                    Style="{StaticResource AccentButtonStyle}" />
                                <Button
                                    Content="History"
                                    Tag="{x:Bind MacAddress, Mode=OneWay}"
                                    Click="HistoryButton_Click"
                                    Padding="8,4" />
                            </StackPanel>
                        </DataTemplate>
                    </controls:DataGridTemplateColumn.CellTemplate>
                </controls:DataGridTemplateColumn>

            </controls:DataGrid.Columns>
        </controls:DataGrid>

        <!-- Status bar -->
        <TextBlock
            Grid.Row="2"
            Text="{x:Bind ViewModel.StatusText, Mode=OneWay}"
            Margin="0,8,0,0"
            Opacity="0.65"
            FontSize="12" />

    </Grid>
</Page>
```

---

## Task 3: Create UnapprovedDevicesPage code-behind

**Files:**
- Create: `NetworkMonitor/Views/UnapprovedDevicesPage.xaml.cs`

- [ ] **Step 1: Create the code-behind file**

Create `NetworkMonitor/Views/UnapprovedDevicesPage.xaml.cs` with this exact content:

```csharp
using CommunityToolkit.WinUI.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using NetworkMonitor.Data;
using NetworkMonitor.Models;
using NetworkMonitor.ViewModels;
using Windows.ApplicationModel.DataTransfer;

namespace NetworkMonitor.Views
{
    public sealed partial class UnapprovedDevicesPage : Page
    {
        private readonly Dictionary<DataGridColumn, string> _sortPaths = [];

        public UnapprovedDevicesPage()
        {
            ViewModel = App.AppHost.Services.GetRequiredService<UnapprovedDevicesViewModel>();
            InitializeComponent();
            _sortPaths[DeviceGrid.Columns[0]] = "IsOnline";
            _sortPaths[DeviceGrid.Columns[1]] = "Type";
            _sortPaths[DeviceGrid.Columns[2]] = "DisplayName";
            _sortPaths[DeviceGrid.Columns[3]] = "IpAddress";
            _sortPaths[DeviceGrid.Columns[4]] = "MacAddress";
            _sortPaths[DeviceGrid.Columns[5]] = "Vendor";
        }

        public UnapprovedDevicesViewModel ViewModel
        {
            get;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            SortPreference? pref = SortPreference.Load("unapproved");
            if (pref is not null)
            {
                ViewModel.Sort(pref.Property, pref.Ascending);
            }

            await ViewModel.LoadAsync();
            ApplySortIndicator();
        }

        private void ApplySortIndicator()
        {
            foreach (DataGridColumn col in DeviceGrid.Columns)
            {
                bool isSort = _sortPaths.TryGetValue(col, out string? path) && path == ViewModel.SortProperty;
                col.SortDirection = isSort
                    ? ViewModel.SortAscending ? DataGridSortDirection.Ascending : DataGridSortDirection.Descending
                    : null;
            }
        }

        private void DataGrid_Sorting(object sender, DataGridColumnEventArgs args)
        {
            if (!_sortPaths.TryGetValue(args.Column, out string? property))
            {
                return;
            }

            bool ascending = args.Column.SortDirection != DataGridSortDirection.Ascending;
            foreach (DataGridColumn col in DeviceGrid.Columns)
            {
                col.SortDirection = null;
            }

            args.Column.SortDirection = ascending ? DataGridSortDirection.Ascending : DataGridSortDirection.Descending;
            ViewModel.Sort(property, ascending);
            new SortPreference(property, ascending).Save("unapproved");
        }

        private void CopyButton_Click(object sender, RoutedEventArgs args)
        {
            if ((sender as FrameworkElement)?.Tag is string text && !string.IsNullOrEmpty(text))
            {
                DataPackage package = new();
                package.SetText(text);
                Clipboard.SetContent(package);
            }
        }

        private void HistoryButton_Click(object sender, RoutedEventArgs args)
        {
            if ((sender as FrameworkElement)?.Tag is string mac)
            {
                MainWindow.Current?.NavigateToHistory(mac);
            }
        }

        private async void ApproveButton_Click(object sender, RoutedEventArgs args)
        {
            if ((sender as FrameworkElement)?.Tag is not Device device)
            {
                return;
            }

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
                Title = $"Approve — {device.MacAddress}",
                Content = panel,
                PrimaryButtonText = "Approve",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                device.FriendlyName = string.IsNullOrWhiteSpace(nameBox.Text) ? null : nameBox.Text.Trim();
                device.Type = typeCombo.SelectedItem is DeviceType t ? t : DeviceType.Unknown;
                device.Notes = string.IsNullOrWhiteSpace(notesBox.Text) ? null : notesBox.Text.Trim();

                await ViewModel.ApproveAsync(device);
            }
        }
    }
}
```

- [ ] **Step 2: Build to verify both new files compile together**

Build in Visual Studio (Ctrl+Shift+B, x64).

Expected: Build succeeds. The XAML designer may show a partial error until the nav is wired up — that is fine, only the build output matters.

---

## Task 4: Wire up navigation and DI registration

**Files:**
- Modify: `NetworkMonitor/MainWindow.xaml`
- Modify: `NetworkMonitor/MainWindow.xaml.cs`
- Modify: `NetworkMonitor/App.xaml.cs`

- [ ] **Step 1: Add the nav item to MainWindow.xaml**

In `MainWindow.xaml`, insert a new `NavigationViewItem` between the Devices item and the Approved Devices item. The existing block to find:

```xml
            <NavigationViewItem
                Content="Devices"
                Tag="devices">
                <NavigationViewItem.Icon>
                    <FontIcon
                        Glyph="&#xE839;" />
                </NavigationViewItem.Icon>
            </NavigationViewItem>
            <NavigationViewItem
                Content="Approved Devices"
                Tag="approved">
```

Replace with:

```xml
            <NavigationViewItem
                Content="Devices"
                Tag="devices">
                <NavigationViewItem.Icon>
                    <FontIcon
                        Glyph="&#xE839;" />
                </NavigationViewItem.Icon>
            </NavigationViewItem>
            <NavigationViewItem
                Content="Unapproved Devices"
                Tag="unapproved-devices">
                <NavigationViewItem.Icon>
                    <FontIcon
                        Glyph="&#xE946;" />
                </NavigationViewItem.Icon>
            </NavigationViewItem>
            <NavigationViewItem
                Content="Approved Devices"
                Tag="approved">
```

- [ ] **Step 2: Add the route to MainWindow.xaml.cs**

In `MainWindow.xaml.cs`, find the `switch` in `NavView_SelectionChanged`:

```csharp
                pageType = item.Tag?.ToString() switch
                {
                    "devices" => typeof(DevicesPage),
                    "approved" => typeof(ApprovedDevicesPage),
                    "history" => typeof(HistoryPage),
                    _ => null
                };
```

Replace with:

```csharp
                pageType = item.Tag?.ToString() switch
                {
                    "devices" => typeof(DevicesPage),
                    "unapproved-devices" => typeof(UnapprovedDevicesPage),
                    "approved" => typeof(ApprovedDevicesPage),
                    "history" => typeof(HistoryPage),
                    _ => null
                };
```

- [ ] **Step 3: Register UnapprovedDevicesViewModel in App.xaml.cs**

In `App.xaml.cs`, find:

```csharp
                    services.AddTransient<DevicesViewModel>();
```

Add the new registration directly after it:

```csharp
                    services.AddTransient<DevicesViewModel>();
                    services.AddTransient<UnapprovedDevicesViewModel>();
```

- [ ] **Step 4: Build to verify all wiring compiles**

Build in Visual Studio (Ctrl+Shift+B, x64).

Expected: Build succeeds with 0 errors.

---

## Task 5: Run, verify, and commit

- [ ] **Step 1: Run the app**

Launch the app from Visual Studio (F5, x64).

Verify:
- "Unapproved Devices" nav item appears between Devices and Approved Devices in the left pane.
- Clicking it loads the page with the DataGrid (Status, Type, Name, IP, MAC, Vendor, Actions columns).
- The status bar shows "N unapproved devices".
- Devices with `IsKnown = false` seen in the last 24 hours appear in the list.
- Clicking **Approve** opens the dialog (FriendlyName, Type, Notes fields, Approve/Cancel buttons). Confirming removes the row from the list and the device now appears on Approved Devices.
- Clicking **History** navigates to the History page for that device.
- Search box filters the list correctly.
- Column header clicks sort the grid.

- [ ] **Step 2: Commit**

Ask the user for the commit message and use it verbatim.
