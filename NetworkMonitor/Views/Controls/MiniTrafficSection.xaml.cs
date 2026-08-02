using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NetworkMonitor.Models.Charting;

namespace NetworkMonitor.Views.Controls
{
    public sealed partial class MiniTrafficSection : UserControl
    {
        public static readonly DependencyProperty LabelProperty =
            DependencyProperty.Register(
                nameof(Label),
                typeof(string),
                typeof(MiniTrafficSection),
                new PropertyMetadata(string.Empty, OnLabelChanged));

        public static readonly DependencyProperty RateTextProperty =
            DependencyProperty.Register(
                nameof(RateText),
                typeof(string),
                typeof(MiniTrafficSection),
                new PropertyMetadata(string.Empty, OnRateTextChanged));

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

        public MiniTrafficSection()
        {
            InitializeComponent();
        }

        public string Label
        {
            get => (string)GetValue(LabelProperty);
            set => SetValue(LabelProperty, value);
        }

        public string RateText
        {
            get => (string)GetValue(RateTextProperty);
            set => SetValue(RateTextProperty, value);
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

        private static void OnLabelChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        {
            MiniTrafficSection section = (MiniTrafficSection)sender;
            section.LabelText.Text = ((string?)args.NewValue ?? string.Empty).ToUpperInvariant();
        }

        private static void OnRateTextChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        {
            MiniTrafficSection section = (MiniTrafficSection)sender;
            section.RateLabel.Text = (string?)args.NewValue ?? string.Empty;
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
    }
}
