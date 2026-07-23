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

        private readonly DigestChartRenderer _chartRenderer;

        public DigestReportView()
        {
            _chartRenderer = App.AppHost.Services.GetRequiredService<DigestChartRenderer>();
            InitializeComponent();
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
                double rasterizationScale = XamlRoot?.RasterizationScale ?? 1.0;
                float screenDpi = (float)(96.0 * rasterizationScale);

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
