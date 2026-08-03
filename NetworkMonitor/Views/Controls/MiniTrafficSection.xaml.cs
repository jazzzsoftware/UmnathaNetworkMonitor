using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NetworkMonitor.Models.Charting;

namespace NetworkMonitor.Views.Controls
{
    public sealed partial class MiniTrafficSection : UserControl
    {
        private const double BaseLabelFontSize = 12.0;
        private const double BasePeakFontSize = 7.35;
        private const double BaseHeaderHeight = 26.0;

        // The gridline values and the time markers share one text format, so this single size is what
        // keeps the time markers the size of the gridline values.
        private const double BaseChartFontSize = 7.0;

        // Below this the section has room for the header and a plot, but not for both plus two rows of
        // gridline values and a time row. Rather than shrink the text to fit, the labels are dropped.
        private const double MinimumLabelledHeight = 74.0;
        private const double MinimumLabelledWidth = 170.0;

        public static readonly DependencyProperty LabelProperty =
            DependencyProperty.Register(
                nameof(Label),
                typeof(string),
                typeof(MiniTrafficSection),
                new PropertyMetadata(string.Empty, OnLabelChanged));

        public static readonly DependencyProperty PointsProperty =
            DependencyProperty.Register(
                nameof(Points),
                typeof(IReadOnlyList<ChartPoint>),
                typeof(MiniTrafficSection),
                new PropertyMetadata(null, OnPointsChanged));

        public static readonly DependencyProperty IsLiveProperty =
            DependencyProperty.Register(
                nameof(IsLive),
                typeof(bool),
                typeof(MiniTrafficSection),
                new PropertyMetadata(true, OnIsLiveChanged));

        public static readonly DependencyProperty FontScaleProperty =
            DependencyProperty.Register(
                nameof(FontScale),
                typeof(double),
                typeof(MiniTrafficSection),
                new PropertyMetadata(1.0, OnFontScaleChanged));


        public MiniTrafficSection()
        {
            InitializeComponent();

            SectionChart.RegisterPropertyChangedCallback(TrafficAreaChart.PeakTextProperty, OnChartPeakTextChanged);
            SizeChanged += OnSectionSizeChanged;
            ApplyFontScale(FontScale);
        }

        public string Label
        {
            get => (string)GetValue(LabelProperty);
            set => SetValue(LabelProperty, value);
        }

        public IReadOnlyList<ChartPoint>? Points
        {
            get => (IReadOnlyList<ChartPoint>?)GetValue(PointsProperty);
            set => SetValue(PointsProperty, value);
        }

        public bool IsLive
        {
            get => (bool)GetValue(IsLiveProperty);
            set => SetValue(IsLiveProperty, value);
        }

        public double FontScale
        {
            get => (double)GetValue(FontScaleProperty);
            set => SetValue(FontScaleProperty, value);
        }


        private static void OnLabelChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        {
            MiniTrafficSection section = (MiniTrafficSection)sender;
            section.LabelText.Text = (string?)args.NewValue ?? string.Empty;
        }

        private static void OnPointsChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        {
            MiniTrafficSection section = (MiniTrafficSection)sender;
            section.SectionChart.ChartPoints = args.NewValue as IReadOnlyList<ChartPoint>;
            section.SectionChart.MarkLiveUpdate();
        }

        // Hiding the widget only hides the window; the chart stays loaded and keeps its per-frame
        // rendering hook, so the live flag is what actually stops the redraws.
        private static void OnIsLiveChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        {
            MiniTrafficSection section = (MiniTrafficSection)sender;
            section.SectionChart.IsLive = (bool)args.NewValue;
        }

        private static void OnFontScaleChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        {
            MiniTrafficSection section = (MiniTrafficSection)sender;

            section.ApplyFontScale((double)args.NewValue);
        }

        // Also called from the constructor: a widget that opens at the reference size never changes the
        // scale from its default, so the chart would otherwise never be told where the header ends.
        private void ApplyFontScale(double scale)
        {
            double headerHeight = BaseHeaderHeight * scale;

            LabelText.FontSize = BaseLabelFontSize * scale;
            PeakLabel.FontSize = BasePeakFontSize * scale;

            // The close glyph sits over the top-right corner of the first section, so the peak figure
            // has to stop short of it or the two overlap.
            HeaderRow.Margin = new Thickness(8, 4 * scale, 26 * scale, 0);
            HeaderScrim.Height = headerHeight;

            SectionChart.CompactFontSize = BaseChartFontSize * scale;
            SectionChart.CompactTopInset = headerHeight;
        }

        private void OnSectionSizeChanged(object sender, SizeChangedEventArgs args)
        {
            bool hasRoom = args.NewSize.Height >= MinimumLabelledHeight && args.NewSize.Width >= MinimumLabelledWidth;

            SectionChart.ShowCompactLabels = hasRoom;
        }

        private void OnChartPeakTextChanged(DependencyObject sender, DependencyProperty property)
        {
            string peak = SectionChart.PeakText;

            PeakLabel.Text = string.IsNullOrEmpty(peak) ? string.Empty : $"Peak {peak}";
        }
    }
}
