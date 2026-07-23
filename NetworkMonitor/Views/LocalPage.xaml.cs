using CommunityToolkit.WinUI.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using NetworkMonitor.Data;
using NetworkMonitor.Models.Charting;
using NetworkMonitor.Models.Traffic;
using NetworkMonitor.ViewModels;
using NetworkMonitor.Services.Traffic;
using NetworkMonitor.Services.Platform;

namespace NetworkMonitor.Views
{
    public sealed partial class LocalPage : Page
    {
        private enum PauseReason
        {
            None,
            Badge,
            Scroll,
            Bucket
        }

        private const double FiveMinutes = 5.0 / 60.0;
        private bool _suppressSelection;
        private bool _userSelectionChanged;
        private string? _selectedRowKey;
        private PauseReason _pauseReason;
        private ScrollBar? _appScrollBar;
        private readonly TrafficTracker _trafficTracker;
        private readonly Settings _settings;
        private readonly SolidColorBrush _liveBackgroundBrush = new(Windows.UI.Color.FromArgb(0xCC, 0x2E, 0x7D, 0x32));
        private readonly SolidColorBrush _historyBackgroundBrush = new(Windows.UI.Color.FromArgb(0xCC, 0xF5, 0x7C, 0x00));

        public LocalPage()
        {
            ViewModel = App.AppHost.Services.GetRequiredService<LocalViewModel>();
            _trafficTracker = App.AppHost.Services.GetRequiredService<TrafficTracker>();
            _settings = App.AppHost.Services.GetRequiredService<Settings>();
            InitializeComponent();

            AppGrid.AddHandler(TappedEvent, new TappedEventHandler(AppGridTapped), true);

            AreaChart.BucketSelected += OnChartBucketSelected;
            Loaded += OnPageLoaded;
            Unloaded += OnPageUnloaded;

            if (MainWindow.Current is not null)
            {
                MainWindow.Current.Closed += OnMainWindowClosed;
            }

        }

        public LocalViewModel ViewModel
        {
            get;
        }

        public void ResetToLive()
        {
            _ = ResumeToLiveAsync();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs args)
        {
            base.OnNavigatedTo(args);
            AreaChart.SmoothScrolling = _settings.ChartSmoothScrolling;
            UpdateRangeButtonStyles(ButtonForRange(ViewModel.TimeRangeHours));
            UpdateLensButtonStyles(ViewModel.Lens == LocalLens.ByApp ? LensAppButton : LensDeviceButton);
            UpdateColumnHeaders();

            _pauseReason = PauseReason.None;
            _selectedRowKey = null;
            ViewModel.SelectedBucketStart = null;
            UpdateChartLabel();
            UpdateTimeLabels();

            await ViewModel.LoadAsync(true);
            SyncGridSelection();
        }

        private void OnPageLoaded(object sender, RoutedEventArgs args)
        {
            _trafficTracker.Flushed -= OnTrafficFlushed;
            _trafficTracker.Flushed += OnTrafficFlushed;
        }

        private void OnPageUnloaded(object sender, RoutedEventArgs args)
        {
            _trafficTracker.Flushed -= OnTrafficFlushed;
        }

        private void OnMainWindowClosed(object sender, WindowEventArgs args)
        {
            _trafficTracker.Flushed -= OnTrafficFlushed;
        }

        private void OnTrafficFlushed(object? sender, TrafficFlushedEventArgs args)
        {

            if (ViewModel.TimeRangeHours <= 6 && _pauseReason == PauseReason.None)
            {
                DispatcherQueue.TryEnqueue(async () =>
                {

                    if (_pauseReason == PauseReason.None)
                    {

                        try
                        {
                            AreaChart.MarkLiveUpdate();
                            await ViewModel.ApplyLiveFlushAsync(args.LocalDeltas);
                            SyncGridSelection();
                        }
                        catch (Exception exception)
                        {
                            AppLog.Error("LocalPage.OnTrafficFlushed", exception);
                        }

                    }

                });
            }

        }

        private async void OnChartBucketSelected(object? sender, ChartPoint point)
        {
            _pauseReason = PauseReason.Bucket;
            _selectedRowKey = null;
            ViewModel.SelectedGroupKey = null;
            ViewModel.SelectedBucketStart = point.BucketStart;
            UpdateChartLabel();
            await ViewModel.LoadAsync(true);
            SyncGridSelection();
        }

        private async void ModeBadgeTapped(object sender, TappedRoutedEventArgs args)
        {

            if (_pauseReason == PauseReason.None)
            {
                _pauseReason = PauseReason.Badge;
                UpdateChartLabel();
            }
            else
            {
                await ResumeToLiveAsync();
            }

        }

        private async Task ResumeToLiveAsync()
        {
            _pauseReason = PauseReason.None;
            ViewModel.SelectedBucketStart = null;
            UpdateChartLabel();
            await ViewModel.LoadAsync(true);
            SyncGridSelection();
        }

        private void AppGridLoaded(object sender, RoutedEventArgs args)
        {
            HookScrollBar();

            if (_appScrollBar is null)
            {
                AppGrid.LayoutUpdated += AppGridLayoutUpdated;
            }

        }

        private void AppGridLayoutUpdated(object? sender, object args)
        {
            HookScrollBar();

            if (_appScrollBar is not null)
            {
                AppGrid.LayoutUpdated -= AppGridLayoutUpdated;
            }

        }

        private void HookScrollBar()
        {

            if (_appScrollBar is null)
            {
                ScrollBar? verticalScrollBar = FindVerticalScrollBar(AppGrid);

                if (verticalScrollBar is not null)
                {
                    _appScrollBar = verticalScrollBar;
                    _appScrollBar.ValueChanged += AppScrollBarValueChanged;
                }

            }

        }

        private void AppScrollBarValueChanged(object sender, RangeBaseValueChangedEventArgs args)
        {
            bool scrolledAway = args.NewValue > 1.0;

            if (scrolledAway && _pauseReason == PauseReason.None)
            {
                _pauseReason = PauseReason.Scroll;
                UpdateChartLabel();
            }

        }

        private static ScrollBar? FindVerticalScrollBar(DependencyObject root)
        {
            ScrollBar? result = null;
            int childCount = VisualTreeHelper.GetChildrenCount(root);

            for (int index = 0; index < childCount; index++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, index);

                if (child is ScrollBar scrollBar && (scrollBar.Name == "VerticalScrollbar" || scrollBar.Orientation == Orientation.Vertical))
                {
                    result = scrollBar;

                    break;
                }

                ScrollBar? nested = FindVerticalScrollBar(child);

                if (nested is not null)
                {
                    result = nested;

                    break;
                }

            }

            return result;
        }

        private void SyncGridSelection()
        {
            LocalTrafficGroupRow? target = _selectedRowKey is null
                ? ViewModel.Groups.FirstOrDefault(row => row.IsAll)
                : ViewModel.Groups.FirstOrDefault(row => row.Key == _selectedRowKey);

            _suppressSelection = true;

            try
            {
                AppGrid.SelectedItem = target;
            }
            finally
            {
                _suppressSelection = false;
            }

        }

        private void RangeButtonClick(object sender, RoutedEventArgs args)
        {

            if (sender is Button button)
            {
                string tag = button.Tag?.ToString() ?? string.Empty;
                double hours;
                bool parsed;

                if (tag == "5m")
                {
                    hours = FiveMinutes;
                    parsed = true;
                }
                else
                {
                    parsed = double.TryParse(tag, out hours);
                }

                if (parsed)
                {
                    _pauseReason = PauseReason.None;
                    ViewModel.SelectedBucketStart = null;
                    ViewModel.TimeRangeHours = hours;
                    UpdateRangeButtonStyles(button);
                    UpdateChartLabel();
                    UpdateTimeLabels();
                }

            }

        }

        private void UpdateRangeButtonStyles(Button activeButton)
        {
            Button[] allButtons = [Range7dButton, Range24hButton, Range6hButton, Range1hButton, Range5mButton];

            foreach (Button button in allButtons)
            {
                button.Style = button == activeButton
                    ? (Style)Application.Current.Resources["AccentButtonStyle"]
                    : null;
            }

        }

        private void LensButtonClick(object sender, RoutedEventArgs args)
        {

            if (sender is Button button)
            {
                string tag = button.Tag?.ToString() ?? string.Empty;

                if (Enum.TryParse(tag, out LocalLens lens))
                {
                    _pauseReason = PauseReason.None;
                    _selectedRowKey = null;
                    ViewModel.SelectedBucketStart = null;
                    ViewModel.Lens = lens;
                    UpdateLensButtonStyles(button);
                    UpdateColumnHeaders();
                    UpdateChartLabel();
                }

            }

        }

        private void UpdateLensButtonStyles(Button activeButton)
        {
            Button[] allButtons = [LensAppButton, LensDeviceButton];

            foreach (Button button in allButtons)
            {
                button.Style = button == activeButton
                    ? (Style)Application.Current.Resources["AccentButtonStyle"]
                    : null;
            }

        }

        private void UpdateColumnHeaders()
        {
            GroupColumn.Header = ViewModel.GroupHeader;
            ChildColumn.Header = ViewModel.ChildHeader;
        }

        private Button ButtonForRange(double hours)
        {
            Button result;

            if (hours <= FiveMinutes + 0.001)
            {
                result = Range5mButton;
            }
            else if (hours <= 1)
            {
                result = Range1hButton;
            }
            else if (hours <= 6)
            {
                result = Range6hButton;
            }
            else if (hours <= 24)
            {
                result = Range24hButton;
            }
            else
            {
                result = Range7dButton;
            }

            return result;
        }

        private void UpdateChartLabel()
        {
            string rangePart;

            if (ViewModel.TimeRangeHours <= FiveMinutes + 0.001)
            {
                rangePart = "last 5 minutes";
            }
            else if (ViewModel.TimeRangeHours <= 1)
            {
                rangePart = "last hour";
            }
            else if (ViewModel.TimeRangeHours <= 6)
            {
                rangePart = "last 6 hours";
            }
            else if (ViewModel.TimeRangeHours <= 24)
            {
                rangePart = "last 24 hours";
            }
            else
            {
                rangePart = "last 7 days";
            }

            string labelText;

            if (ViewModel.SelectedBucketStart is DateTime bucketStart)
            {
                string scopeNoun = ViewModel.Lens == LocalLens.ByApp ? "Apps" : "Devices";
                labelText = $"{scopeNoun} at {bucketStart.ToLocalTime():dd MMM HH:mm:ss}";
            }
            else
            {
                string fallback = ViewModel.Lens == LocalLens.ByApp ? "All Apps" : "All Devices";
                string appPart = (AppGrid.SelectedItem as LocalTrafficGroupRow)?.DisplayName ?? fallback;
                labelText = $"{appPart} — {rangePart}";
            }

            ChartLabel.Text = labelText;

            bool isLive = _pauseReason == PauseReason.None;
            AreaChart.IsLive = isLive;
            ViewModel.SetRatesActive(isLive && ViewModel.TimeRangeHours <= 6);

            if (isLive)
            {
                ModeText.Text = "Live";
                ModeBadge.Background = _liveBackgroundBrush;
                ModeClose.Visibility = Visibility.Collapsed;
            }
            else
            {
                ModeText.Text = _pauseReason == PauseReason.Bucket ? "History" : "Paused";
                ModeBadge.Background = _historyBackgroundBrush;
                ModeClose.Visibility = Visibility.Visible;
            }

        }

        private void UpdateTimeLabels()
        {
            DateTime now = DateTime.Now;
            double hours = ViewModel.TimeRangeHours;

            string format = hours >= 24 ? "dd MMM" : "HH:mm";

            TimeLabel0.Text = now.AddHours(-hours).ToString(format);
            TimeLabel1.Text = now.AddHours(-hours * 0.75).ToString(format);
            TimeLabel2.Text = now.AddHours(-hours * 0.5).ToString(format);
            TimeLabel3.Text = now.AddHours(-hours * 0.25).ToString(format);
        }

        private async void AppGridSelectionChanged(object sender, SelectionChangedEventArgs args)
        {

            if (!_suppressSelection)
            {
                _userSelectionChanged = true;

                if (AppGrid.SelectedItem is LocalTrafficGroupRow row)
                {
                    _selectedRowKey = row.IsAll ? null : row.Key;
                    string? newSelectedKey = (row.IsAll || row.IsBackground) ? null : row.Key;
                    bool appChanged = newSelectedKey != ViewModel.SelectedGroupKey;

                    if (appChanged)
                    {
                        ViewModel.SelectedGroupKey = newSelectedKey;
                        UpdateChartLabel();

                        if (_pauseReason == PauseReason.None)
                        {
                            await ViewModel.LoadAsync(true);
                            SyncGridSelection();
                        }
                        else
                        {
                            await ViewModel.LoadAsync(false, false);
                        }

                    }

                }

            }

        }

        private void AppGridTapped(object sender, TappedRoutedEventArgs args)
        {
            LocalTrafficGroupRow? tappedRow = FindTappedAppRow(args.OriginalSource as DependencyObject);

            if (!_userSelectionChanged && tappedRow is not null && !tappedRow.IsAll && ReferenceEquals(tappedRow, AppGrid.SelectedItem))
            {
                LocalTrafficGroupRow? allAppsRow = ViewModel.Groups.FirstOrDefault(row => row.IsAll);

                if (allAppsRow is not null)
                {
                    AppGrid.SelectedItem = allAppsRow;
                }

            }

            _userSelectionChanged = false;
        }

        private static LocalTrafficGroupRow? FindTappedAppRow(DependencyObject? source)
        {
            LocalTrafficGroupRow? result = null;
            DependencyObject? current = source;

            while (current is not null)
            {

                if (current is DataGridCell cell)
                {

                    if (cell.DataContext is LocalTrafficGroupRow row)
                    {
                        result = row;
                    }

                    break;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return result;
        }
    }
}
