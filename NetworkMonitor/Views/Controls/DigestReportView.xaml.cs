using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using NetworkMonitor.Models.Digest;
using NetworkMonitor.Models.Formatting;
using Windows.Storage.Streams;
using NetworkMonitor.Services.Digest;

namespace NetworkMonitor.Views.Controls
{
    public sealed partial class DigestReportView : UserControl
    {
        public static readonly DependencyProperty SummaryProperty = DependencyProperty.Register(
            nameof(Summary),
            typeof(DigestSummary),
            typeof(DigestReportView),
            new PropertyMetadata(null, OnSummaryChanged));

        public static readonly DependencyProperty ReportProperty = DependencyProperty.Register(
            nameof(Report),
            typeof(DigestReport),
            typeof(DigestReportView),
            new PropertyMetadata(null, OnReportChanged));

        private const float MinimumPreviewDpi = 96f;
        private const float MaximumPreviewDpi = 384f;
        private const float DpiChangeThreshold = 4f;

        private readonly DigestChartRenderer _chartRenderer;
        private readonly DispatcherTimer _resizeTimer;
        private float _renderedDpi;

        public DigestReportView()
        {
            _chartRenderer = App.AppHost.Services.GetRequiredService<DigestChartRenderer>();
            InitializeComponent();

            _resizeTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(250)
            };
            _resizeTimer.Tick += OnResizeSettled;

            Loaded += OnLoaded;
            SizeChanged += OnSizeChanged;
        }

        public DigestSummary? Summary
        {
            get => (DigestSummary?)GetValue(SummaryProperty);
            set => SetValue(SummaryProperty, value);
        }

        public DigestReport? Report
        {
            get => (DigestReport?)GetValue(ReportProperty);
            set => SetValue(ReportProperty, value);
        }

        private static void OnSummaryChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        {

            if (sender is DigestReportView view)
            {
                _ = view.RenderAsync();
            }

        }

        private static void OnReportChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        {

            if (sender is DigestReportView view)
            {
                view.UpdateSubheader();
            }

        }

        private void UpdateSubheader()
        {
            DigestReport? report = Report;

            if (report is null)
            {
                PeriodSubtitle.Text = string.Empty;
                GeneratedText.Text = string.Empty;
            }
            else
            {
                string periodStart = report.PeriodStart.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
                string periodEnd = report.PeriodEnd.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
                string generated = report.GeneratedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
                PeriodSubtitle.Text = $"Report: {periodStart} – {periodEnd}";
                GeneratedText.Text = $"Generated: {generated}";
            }

        }

        private async Task RenderAsync()
        {
            DigestSummary? summary = Summary;

            if (summary is null)
            {
                TrafficChartImage.Source = null;
                TrafficSplitImage.Source = null;
                LocalSplitImage.Source = null;
                ThroughputChartImage.Source = null;
                LatencyChartImage.Source = null;
                TopAppsTable.ItemsSource = null;
                TopLocalAppsTable.ItemsSource = null;
                AllTable.ItemsSource = null;
                UnapprovedTable.ItemsSource = null;
                SpeedTable.ItemsSource = null;
            }
            else
            {
                ApplySpeedColumnVisibility(SpeedHeaderGrid);
                TopAppsTable.ItemsSource = summary.InternetTopApps;
                TopLocalAppsTable.ItemsSource = summary.TopLocalApps;
                AllTable.ItemsSource = summary.AllDevices;
                UnapprovedTable.ItemsSource = summary.UnapprovedDevices;
                SpeedTable.ItemsSource = summary.SpeedTests.OrderByDescending(test => test.Timestamp).ToList();

                bool lightBackground = ActualTheme != ElementTheme.Dark;
                float screenDpi = PreviewDpi();
                _renderedDpi = screenDpi;

                byte[] trafficPng = await Task.Run(() => _chartRenderer.RenderInternetTrafficChart(summary, lightBackground, screenDpi));
                TrafficChartImage.Source = await ToBitmapAsync(trafficPng);

                byte[] trafficSplitPng = await Task.Run(() => _chartRenderer.RenderInternetTrafficSplitChart(summary, lightBackground, screenDpi));
                TrafficSplitImage.Source = await ToBitmapAsync(trafficSplitPng);

                byte[] localSplitPng = await Task.Run(() => _chartRenderer.RenderLocalTrafficSplitChart(summary, lightBackground, screenDpi));
                LocalSplitImage.Source = await ToBitmapAsync(localSplitPng);

                byte[] throughputPng = await Task.Run(() => _chartRenderer.RenderSpeedThroughputChart(summary, lightBackground, screenDpi));
                ThroughputChartImage.Source = await ToBitmapAsync(throughputPng);

                byte[] latencyPng = await Task.Run(() => _chartRenderer.RenderSpeedLatencyChart(summary, lightBackground, screenDpi));
                LatencyChartImage.Source = await ToBitmapAsync(latencyPng);
            }

        }

        private float PreviewDpi()
        {
            // The charts are authored at DigestChartRenderer.ChartWidth logical units but are shown
            // stretched to whatever width the page offers. WinUI maps one bitmap pixel to one DIP and
            // ignores PNG DPI metadata, so raising the DPI alone just makes the bitmap bigger in DIPs
            // — it never gets denser. Scaling the DPI by (displayed width / authored width) gives the
            // bitmap exactly as many pixels as the on-screen area has device pixels: a 1:1 mapping.
            // XamlRoot is null until the control is attached, in which case this render is provisional
            // and Loaded will redo it.
            double rasterizationScale = XamlRoot?.RasterizationScale ?? 1.0;
            double displayedWidth = TrafficSplitImage.ActualWidth;

            if (displayedWidth <= 0.0)
            {
                displayedWidth = ActualWidth;
            }

            if (displayedWidth <= 0.0)
            {
                displayedWidth = DigestChartRenderer.ChartWidth;
            }

            double dpi = 96.0 * (displayedWidth / DigestChartRenderer.ChartWidth) * rasterizationScale;
            float clamped = (float)Math.Clamp(dpi, MinimumPreviewDpi, MaximumPreviewDpi);

            return clamped;
        }

        private void OnLoaded(object sender, RoutedEventArgs args)
        {
            RerenderIfScaleChanged();
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs args)
        {
            // Resizing raises this continuously; coalesce so a drag re-renders once at the end.
            _resizeTimer.Stop();
            _resizeTimer.Start();
        }

        private void OnResizeSettled(object? sender, object args)
        {
            _resizeTimer.Stop();
            RerenderIfScaleChanged();
        }

        private void RerenderIfScaleChanged()
        {
            float dpi = PreviewDpi();

            // A redraw costs five Win2D renders, so ignore drift too small to be visible.
            if (Summary is not null && Math.Abs(dpi - _renderedDpi) > DpiChangeThreshold)
            {
                _ = RenderAsync();
            }

        }

        private void SpeedRowGridLoaded(object sender, RoutedEventArgs args)
        {

            if (sender is Grid rowGrid)
            {
                ApplySpeedColumnVisibility(rowGrid);
            }

        }

        private static void ApplySpeedColumnVisibility(Grid grid)
        {
            RateUnitMode mode = TrafficRateFormatter.Mode;
            bool showBits = mode != RateUnitMode.Bytes;
            bool showBytes = mode != RateUnitMode.Bits;

            SetSpeedColumn(grid, 1, showBits);
            SetSpeedColumn(grid, 2, showBits);
            SetSpeedColumn(grid, 3, showBytes);
            SetSpeedColumn(grid, 4, showBytes);
        }

        private static void SetSpeedColumn(Grid grid, int columnIndex, bool visible)
        {
            grid.ColumnDefinitions[columnIndex].Width = visible ? new GridLength(1.2, GridUnitType.Star) : new GridLength(0);

            foreach (UIElement child in grid.Children)
            {

                if (child is FrameworkElement element && Grid.GetColumn(element) == columnIndex)
                {
                    element.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                }

            }

        }

        private static async Task<BitmapImage> ToBitmapAsync(byte[] png)
        {
            BitmapImage image = new BitmapImage();

            using (InMemoryRandomAccessStream stream = new InMemoryRandomAccessStream())
            {
                await stream.WriteAsync(png.AsBuffer());
                stream.Seek(0);
                await image.SetSourceAsync(stream);
            }

            return image;
        }

    }
}
