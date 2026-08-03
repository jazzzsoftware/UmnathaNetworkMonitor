using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using NetworkMonitor.Core.Charting;
using NetworkMonitor.Models.Charting;
using NetworkMonitor.Models.Formatting;
using Windows.Foundation;
using Windows.UI;

namespace NetworkMonitor.Views.Controls
{
    public sealed partial class TrafficAreaChart : UserControl
    {
        private static readonly Color DownloadStrokeColor = Color.FromArgb(0xFF, 0x19, 0x76, 0xD2);
        private static readonly Color DownloadFillTop = Color.FromArgb(0xCC, 0x19, 0x76, 0xD2);
        private static readonly Color DownloadFillBottom = Color.FromArgb(0x00, 0x19, 0x76, 0xD2);
        private static readonly Color UploadStrokeColor = Color.FromArgb(0xFF, 0xAB, 0x47, 0xBC);
        private static readonly Color UploadFillTop = Color.FromArgb(0xCC, 0xAB, 0x47, 0xBC);
        private static readonly Color UploadFillBottom = Color.FromArgb(0x00, 0xAB, 0x47, 0xBC);
        private static readonly Color SelectionLineColor = Color.FromArgb(0xCC, 0xF5, 0x7C, 0x00);

        private const double EaseTimeConstantSeconds = 2.5;

        private IReadOnlyList<ChartPoint>? _currentPoints;
        private double[]? _timeEpoch;
        private double[]? _download;
        private double[]? _upload;
        private double[]? _displayedDownload;
        private double[]? _displayedUpload;
        private int _count;
        private double _bucketSeconds = 5.0;
        private double _targetMax = 1.0;
        private double _displayMax;
        private double _lastFrameEpoch;
        private bool _isLive = true;
        private bool _smoothScrolling = true;
        private bool _frozen;
        private double _frozenNowEpoch;
        private bool _animateNext;
        private bool _renderingHooked;
        private double? _selectedBucketEpoch;
        private CanvasLinearGradientBrush? _downloadFill;
        private CanvasLinearGradientBrush? _uploadFill;
        private CanvasTextFormat? _axisTextFormat;
        private CanvasTextFormat? _compactTextFormat;
        private CanvasTextFormat? _compactCenterTextFormat;
        private CanvasTextFormat? _compactRightTextFormat;
        private Vector2[]? _downloadPointBuffer;
        private Vector2[]? _uploadPointBuffer;
        private long _labelTargetMax = -1;
        private double _labelBucketSeconds = -1;
        private string _labelTopBits = string.Empty;
        private string _labelTopBytes = string.Empty;
        private string _labelMidBits = string.Empty;
        private string _labelMidBytes = string.Empty;

        public static readonly DependencyProperty ChartPointsProperty =
            DependencyProperty.Register(
                nameof(ChartPoints),
                typeof(IReadOnlyList<ChartPoint>),
                typeof(TrafficAreaChart),
                new PropertyMetadata(null, OnChartPointsChanged));

        public static readonly DependencyProperty IsLiveProperty =
            DependencyProperty.Register(
                nameof(IsLive),
                typeof(bool),
                typeof(TrafficAreaChart),
                new PropertyMetadata(true, OnIsLiveChanged));

        public static readonly DependencyProperty SmoothScrollingProperty =
            DependencyProperty.Register(
                nameof(SmoothScrolling),
                typeof(bool),
                typeof(TrafficAreaChart),
                new PropertyMetadata(true, OnSmoothScrollingChanged));

        public static readonly DependencyProperty SelectedBucketStartProperty =
            DependencyProperty.Register(
                nameof(SelectedBucketStart),
                typeof(DateTime?),
                typeof(TrafficAreaChart),
                new PropertyMetadata(null, OnSelectedBucketStartChanged));

        public static readonly DependencyProperty CompactProperty =
            DependencyProperty.Register(
                nameof(Compact),
                typeof(bool),
                typeof(TrafficAreaChart),
                new PropertyMetadata(false, OnCompactChanged));

        public static readonly DependencyProperty CompactFontSizeProperty =
            DependencyProperty.Register(
                nameof(CompactFontSize),
                typeof(double),
                typeof(TrafficAreaChart),
                new PropertyMetadata(10.0, OnCompactFontSizeChanged));

        public static readonly DependencyProperty ShowCompactLabelsProperty =
            DependencyProperty.Register(
                nameof(ShowCompactLabels),
                typeof(bool),
                typeof(TrafficAreaChart),
                new PropertyMetadata(true, OnShowCompactLabelsChanged));

        public static readonly DependencyProperty CompactTopInsetProperty =
            DependencyProperty.Register(
                nameof(CompactTopInset),
                typeof(double),
                typeof(TrafficAreaChart),
                new PropertyMetadata(0.0));

        public static readonly DependencyProperty PeakTextProperty =
            DependencyProperty.Register(
                nameof(PeakText),
                typeof(string),
                typeof(TrafficAreaChart),
                new PropertyMetadata(string.Empty));

        public event EventHandler<ChartPoint>? BucketSelected;

        public TrafficAreaChart()
        {
            InitializeComponent();

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        public IReadOnlyList<ChartPoint>? ChartPoints
        {
            get => (IReadOnlyList<ChartPoint>?)GetValue(ChartPointsProperty);
            set => SetValue(ChartPointsProperty, value);
        }

        public bool IsLive
        {
            get => (bool)GetValue(IsLiveProperty);
            set => SetValue(IsLiveProperty, value);
        }

        public bool SmoothScrolling
        {
            get => (bool)GetValue(SmoothScrollingProperty);
            set => SetValue(SmoothScrollingProperty, value);
        }

        public DateTime? SelectedBucketStart
        {
            get => (DateTime?)GetValue(SelectedBucketStartProperty);
            set => SetValue(SelectedBucketStartProperty, value);
        }

        public bool Compact
        {
            get => (bool)GetValue(CompactProperty);
            set => SetValue(CompactProperty, value);
        }

        public double CompactFontSize
        {
            get => (double)GetValue(CompactFontSizeProperty);
            set => SetValue(CompactFontSizeProperty, value);
        }

        // Set false when the section is too small to carry text at a legible size. The gridlines stay —
        // they still say where half and full scale are — but the values and the time row are dropped
        // rather than drawn at a size nobody can read.
        public bool ShowCompactLabels
        {
            get => (bool)GetValue(ShowCompactLabelsProperty);
            set => SetValue(ShowCompactLabelsProperty, value);
        }

        // How far down the compact labels must start to clear whatever the host draws over the top of
        // the chart. The mini graph puts its section header there, and the top gridline sits at a tenth
        // of the height — on a short section that is squarely behind the header.
        public double CompactTopInset
        {
            get => (double)GetValue(CompactTopInsetProperty);
            set => SetValue(CompactTopInsetProperty, value);
        }

        // The window's peak over the drawn range, already formatted for the current unit setting. The
        // mini graph shows it in place of the live rate, which moved too fast to read.
        public string PeakText
        {
            get => (string)GetValue(PeakTextProperty);
            private set => SetValue(PeakTextProperty, value);
        }

        public void MarkLiveUpdate()
        {
            _animateNext = true;
        }

        private static void OnChartPointsChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        {
            TrafficAreaChart chart = (TrafficAreaChart)sender;
            chart._currentPoints = args.NewValue as IReadOnlyList<ChartPoint>;
            chart.ApplyPoints();
        }

        private static void OnIsLiveChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        {
            TrafficAreaChart chart = (TrafficAreaChart)sender;
            chart._isLive = (bool)args.NewValue;
            chart.ChartCanvas.Invalidate();
        }

        private static void OnSmoothScrollingChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        {
            TrafficAreaChart chart = (TrafficAreaChart)sender;
            chart._smoothScrolling = (bool)args.NewValue;
            chart.ChartCanvas.Invalidate();
        }

        private static void OnSelectedBucketStartChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        {
            TrafficAreaChart chart = (TrafficAreaChart)sender;

            if (args.NewValue is DateTime bucketStart)
            {
                chart._selectedBucketEpoch = (bucketStart - DateTime.UnixEpoch).TotalSeconds;
            }
            else
            {
                chart._selectedBucketEpoch = null;
            }

            chart.ChartCanvas.Invalidate();
        }

        private static void OnShowCompactLabelsChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        {
            TrafficAreaChart chart = (TrafficAreaChart)sender;

            chart.ChartCanvas.Invalidate();
        }

        private static void OnCompactFontSizeChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        {
            TrafficAreaChart chart = (TrafficAreaChart)sender;

            // Rebuilt on the next draw at the new size. A text format is a DirectWrite object rather
            // than a device resource, so it does not have to wait for CreateResources.
            chart._compactTextFormat?.Dispose();
            chart._compactTextFormat = null;
            chart._compactCenterTextFormat?.Dispose();
            chart._compactCenterTextFormat = null;
            chart._compactRightTextFormat?.Dispose();
            chart._compactRightTextFormat = null;

            chart.ChartCanvas.Invalidate();
        }

        private static void OnCompactChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        {
            TrafficAreaChart chart = (TrafficAreaChart)sender;
            bool compact = (bool)args.NewValue;
            Visibility visibility = compact ? Visibility.Collapsed : Visibility.Visible;

            chart.AxisLabelPanel.Visibility = visibility;
            chart.InputLayer.Visibility = visibility;

            if (compact)
            {
                chart.CrosshairLine.Visibility = Visibility.Collapsed;
                chart.HoverPanel.Visibility = Visibility.Collapsed;
            }

        }

        private static double NowEpoch()
        {
            double epoch = (DateTime.UtcNow - DateTime.UnixEpoch).TotalSeconds;

            return epoch;
        }

        private static (double LeftEdge, double Span) Window(
            int count, double bucketSeconds, double firstEpoch, double lastEpoch, bool isLive, double nowEpoch)
        {
            double rightEdge;
            double span;

            if (isLive)
            {
                rightEdge = nowEpoch;
                span = count * bucketSeconds;
            }
            else
            {
                rightEdge = lastEpoch;
                span = (count - 1) * bucketSeconds;
            }

            if (span <= 0)
            {
                span = bucketSeconds;
            }

            double leftEdge = rightEdge - span;
            (double LeftEdge, double Span) result = (leftEdge, span);

            return result;
        }

        private static Vector2[] BuildPoints(
            Vector2[]? buffer,
            double[] timeEpoch,
            double[] values,
            int count,
            double leftEdge,
            double span,
            double width,
            double height,
            double usableHeight,
            double safeMax,
            bool isLive,
            double nowEpoch)
        {
            int total = isLive ? count + 1 : count;
            Vector2[] points;

            if (buffer != null && buffer.Length == total)
            {
                points = buffer;
            }
            else
            {
                points = new Vector2[total];
            }

            for (int index = 0; index < count; index++)
            {
                float xValue = (float)((timeEpoch[index] - leftEdge) / span * width);
                float yValue = (float)(height - values[index] / safeMax * usableHeight);
                points[index] = new Vector2(xValue, yValue);
            }

            if (isLive)
            {
                float leadX = (float)((nowEpoch - leftEdge) / span * width);
                float leadY = (float)(height - values[count - 1] / safeMax * usableHeight);
                points[count] = new Vector2(leadX, leadY);
            }

            return points;
        }

        private static void DrawArea(
            ICanvasResourceCreator creator,
            CanvasDrawingSession session,
            Vector2[] points,
            double height,
            CanvasLinearGradientBrush fill,
            Color stroke)
        {
            int count = points.Length;

            using CanvasPathBuilder areaBuilder = new(creator);
            areaBuilder.BeginFigure(points[0]);

            for (int index = 0; index < count - 1; index++)
            {
                float segmentWidth = points[index + 1].X - points[index].X;
                Vector2 control1 = new(points[index].X + segmentWidth / 3f, points[index].Y);
                Vector2 control2 = new(points[index + 1].X - segmentWidth / 3f, points[index + 1].Y);
                areaBuilder.AddCubicBezier(control1, control2, points[index + 1]);
            }

            areaBuilder.AddLine(new Vector2(points[count - 1].X, (float)height));
            areaBuilder.AddLine(new Vector2(points[0].X, (float)height));
            areaBuilder.EndFigure(CanvasFigureLoop.Closed);

            using CanvasGeometry areaGeometry = CanvasGeometry.CreatePath(areaBuilder);
            session.FillGeometry(areaGeometry, fill);

            using CanvasPathBuilder lineBuilder = new(creator);
            lineBuilder.BeginFigure(points[0]);

            for (int index = 0; index < count - 1; index++)
            {
                float segmentWidth = points[index + 1].X - points[index].X;
                Vector2 control1 = new(points[index].X + segmentWidth / 3f, points[index].Y);
                Vector2 control2 = new(points[index + 1].X - segmentWidth / 3f, points[index + 1].Y);
                lineBuilder.AddCubicBezier(control1, control2, points[index + 1]);
            }

            lineBuilder.EndFigure(CanvasFigureLoop.Open);

            using CanvasGeometry lineGeometry = CanvasGeometry.CreatePath(lineBuilder);
            session.DrawGeometry(lineGeometry, stroke, 1.5f);
        }

        private void OnLoaded(object sender, RoutedEventArgs args)
        {

            if (!_renderingHooked)
            {
                CompositionTarget.Rendering += OnRendering;
                _renderingHooked = true;
            }

        }

        private void OnUnloaded(object sender, RoutedEventArgs args)
        {

            if (_renderingHooked)
            {
                CompositionTarget.Rendering -= OnRendering;
                _renderingHooked = false;
            }

            _downloadFill?.Dispose();
            _downloadFill = null;
            _uploadFill?.Dispose();
            _uploadFill = null;
            _axisTextFormat?.Dispose();
            _axisTextFormat = null;
            _compactTextFormat?.Dispose();
            _compactTextFormat = null;
            _compactCenterTextFormat?.Dispose();
            _compactCenterTextFormat = null;
            _compactRightTextFormat?.Dispose();
            _compactRightTextFormat = null;

            ChartCanvas.RemoveFromVisualTree();
        }

        private void OnRendering(object? sender, object args)
        {

            if (_isLive && _smoothScrolling && !_frozen)
            {
                ChartCanvas.Invalidate();
            }

        }

        private void ChartCanvasCreateResources(CanvasControl sender, CanvasCreateResourcesEventArgs args)
        {
            _downloadFill = new CanvasLinearGradientBrush(sender, DownloadFillTop, DownloadFillBottom);
            _uploadFill = new CanvasLinearGradientBrush(sender, UploadFillTop, UploadFillBottom);

            _axisTextFormat?.Dispose();
            _axisTextFormat = new CanvasTextFormat
            {
                FontSize = 12f
            };

        }

        private void ChartCanvasDraw(CanvasControl sender, CanvasDrawEventArgs args)
        {

            if (_count >= 2
                && _timeEpoch != null
                && _download != null
                && _upload != null
                && _displayedDownload != null
                && _displayedUpload != null
                && _downloadFill != null
                && _uploadFill != null
                && _axisTextFormat != null)
            {
                double width = sender.Size.Width;
                double height = sender.Size.Height;

                EaseValues();

                if (_displayMax <= 0)
                {
                    _displayMax = _targetMax;
                }
                else
                {
                    _displayMax += (_targetMax - _displayMax) * 0.15;
                }

                double safeMax = Math.Max(_displayMax, 1.0);

                // Compact mode gives the header its own strip at the top and the time row its own at the
                // bottom, and the plot lives between them. Insetting the plot rather than the labels is
                // what keeps a gridline and its value on the same line.
                double plotTop = Compact ? Math.Min(CompactTopInset, height * 0.5) : 0.0;
                double bottomInset = ShowCompactLabels ? CompactFontSize + 4.0 : 0.0;
                double plotBottom = Compact ? Math.Max(plotTop + 1.0, height - bottomInset) : height;
                double usableHeight = (plotBottom - plotTop) * 0.9;
                bool scrolling = _isLive && _smoothScrolling;
                double nowEpoch = _frozen ? _frozenNowEpoch : NowEpoch();
                (double leftEdge, double span) = Window(_count, _bucketSeconds, _timeEpoch[0], _timeEpoch[_count - 1], scrolling, nowEpoch);

                _downloadFill.StartPoint = new Vector2(0f, (float)plotTop);
                _downloadFill.EndPoint = new Vector2(0f, (float)plotBottom);
                _uploadFill.StartPoint = new Vector2(0f, (float)plotTop);
                _uploadFill.EndPoint = new Vector2(0f, (float)plotBottom);

                _downloadPointBuffer = BuildPoints(_downloadPointBuffer, _timeEpoch, _displayedDownload, _count, leftEdge, span, width, plotBottom, usableHeight, safeMax, scrolling, nowEpoch);
                _uploadPointBuffer = BuildPoints(_uploadPointBuffer, _timeEpoch, _displayedUpload, _count, leftEdge, span, width, plotBottom, usableHeight, safeMax, scrolling, nowEpoch);

                DrawArea(sender, args.DrawingSession, _downloadPointBuffer, plotBottom, _downloadFill, DownloadStrokeColor);
                DrawArea(sender, args.DrawingSession, _uploadPointBuffer, plotBottom, _uploadFill, UploadStrokeColor);

                if (Compact)
                {
                    _compactTextFormat ??= new CanvasTextFormat
                    {
                        FontSize = (float)CompactFontSize
                    };

                    _compactCenterTextFormat ??= new CanvasTextFormat
                    {
                        FontSize = (float)CompactFontSize,
                        HorizontalAlignment = CanvasHorizontalAlignment.Center
                    };

                    _compactRightTextFormat ??= new CanvasTextFormat
                    {
                        FontSize = (float)CompactFontSize,
                        HorizontalAlignment = CanvasHorizontalAlignment.Right
                    };

                    DrawCompactAxis(args.DrawingSession, width, plotBottom, usableHeight, leftEdge, span);
                }
                else
                {
                    DrawAxisLabels(args.DrawingSession, width, height, _axisTextFormat);
                }

                if (_selectedBucketEpoch is double selectedEpoch)
                {
                    float selectionX = (float)((selectedEpoch - leftEdge) / span * width);

                    if (selectionX >= 0f && selectionX <= (float)width)
                    {
                        using CanvasStrokeStyle dashStyle = new CanvasStrokeStyle
                        {
                            DashStyle = CanvasDashStyle.Dash
                        };

                        args.DrawingSession.DrawLine(selectionX, 0f, selectionX, (float)height, SelectionLineColor, 1.5f, dashStyle);
                    }

                }

            }

        }

        // The mini graph has no room for a full axis, but a chart with no units at all cannot be read —
        // a spike could be a kilobit or a gigabit. It gets the same two gridlines as the full chart,
        // each labelled with its value, and honours the unit setting the same way.
        private void DrawCompactAxis(
            CanvasDrawingSession session, double width, double plotBottom, double usableHeight, double leftEdge, double span)
        {
            EnsureScaleLabels();

            Color baseColor = ((SolidColorBrush)Application.Current.Resources["TextFillColorPrimaryBrush"]).Color;
            Color labelColor = Color.FromArgb(0x99, baseColor.R, baseColor.G, baseColor.B);
            Color gridColor = Color.FromArgb(0x22, baseColor.R, baseColor.G, baseColor.B);
            float topLine = (float)(plotBottom - usableHeight);
            float midLine = (float)(plotBottom - usableHeight / 2.0);
            float lineHeight = (float)CompactFontSize + 2f;

            session.DrawLine(0f, topLine, (float)width, topLine, gridColor, 1f);
            session.DrawLine(0f, midLine, (float)width, midLine, gridColor, 1f);

            if (ShowCompactLabels)
            {
                RateUnitMode mode = TrafficRateFormatter.SingleUnit(TrafficRateFormatter.Mode);
                bool showBits = mode != RateUnitMode.Bytes;
                bool showBytes = mode != RateUnitMode.Bits;

                if (showBits)
                {
                    session.DrawText(_labelTopBits, 6f, topLine + 1f, labelColor, _compactTextFormat);
                    session.DrawText(_labelMidBits, 6f, midLine + 1f, labelColor, _compactTextFormat);
                }

                if (showBytes)
                {
                    float bytesOffset = showBits ? lineHeight : 0f;

                    session.DrawText(_labelTopBytes, 6f, topLine + 1f + bytesOffset, labelColor, _compactTextFormat);
                    session.DrawText(_labelMidBytes, 6f, midLine + 1f + bytesOffset, labelColor, _compactTextFormat);
                }

                DrawCompactTimeRow(session, width, plotBottom, leftEdge, span, labelColor);
            }
        }

        // The same row of ticks the full chart carries, thinned to what the width can hold: the oldest
        // time on the left, "now" on the right, and evenly spaced times between them. A chart that says
        // how much but never over how long is only half a chart.
        private void DrawCompactTimeRow(
            CanvasDrawingSession session, double width, double plotBottom, double leftEdge, double span, Color labelColor)
        {
            int ticks = (int)Math.Clamp(Math.Floor(width / 80.0), 2.0, 5.0);
            float timeRow = (float)(plotBottom + 2.0);
            double rowHeight = CompactFontSize + 3.0;

            for (int index = 0; index < ticks; index++)
            {
                double fraction = (double)index / (ticks - 1);
                DateTime tickTime = DateTime.UnixEpoch.AddSeconds(leftEdge + span * fraction).ToLocalTime();
                string text = index == ticks - 1 ? "now" : tickTime.ToString("HH:mm");

                if (index == 0)
                {
                    session.DrawText(text, 6f, timeRow, labelColor, _compactTextFormat);
                }
                else if (index == ticks - 1)
                {
                    session.DrawText(text, new Rect(0, timeRow, width - 6.0, rowHeight), labelColor, _compactRightTextFormat);
                }
                else
                {
                    double centre = width * fraction;

                    session.DrawText(text, new Rect(centre - 40.0, timeRow, 80.0, rowHeight), labelColor, _compactCenterTextFormat);
                }

            }

        }

        private void EnsureScaleLabels()
        {
            long targetMax = (long)_targetMax;

            if (targetMax != _labelTargetMax || _bucketSeconds != _labelBucketSeconds)
            {
                long midMax = (long)(_targetMax / 2.0);
                _labelTargetMax = targetMax;
                _labelBucketSeconds = _bucketSeconds;
                _labelTopBits = TrafficRateFormatter.BitsPerSecond(targetMax, _bucketSeconds);
                _labelTopBytes = TrafficRateFormatter.BytesPerSecond(targetMax, _bucketSeconds);
                _labelMidBits = TrafficRateFormatter.BitsPerSecond(midMax, _bucketSeconds);
                _labelMidBytes = TrafficRateFormatter.BytesPerSecond(midMax, _bucketSeconds);
            }

        }

        private void DrawAxisLabels(CanvasDrawingSession session, double width, double height, CanvasTextFormat format)
        {
            double usableHeight = height * 0.9;
            Color baseColor = ((SolidColorBrush)Application.Current.Resources["TextFillColorPrimaryBrush"]).Color;
            Color axisColor = Color.FromArgb(0x8C, baseColor.R, baseColor.G, baseColor.B);
            Color spineColor = Color.FromArgb(0x55, baseColor.R, baseColor.G, baseColor.B);
            Color gridColor = Color.FromArgb(0x22, baseColor.R, baseColor.G, baseColor.B);

            EnsureScaleLabels();

            session.DrawLine(1f, 0f, 1f, (float)height, spineColor, 1f);
            session.DrawLine(1f, (float)(height - usableHeight), (float)width, (float)(height - usableHeight), gridColor, 1f);
            session.DrawLine(1f, (float)(height - usableHeight / 2.0), (float)width, (float)(height - usableHeight / 2.0), gridColor, 1f);

            float topRow = (float)(height - usableHeight);
            float midRow = (float)(height - usableHeight / 2.0 - 14.0);
            RateUnitMode mode = TrafficRateFormatter.Mode;
            bool showBits = mode != RateUnitMode.Bytes;
            bool showBytes = mode != RateUnitMode.Bits;
            float bytesOffset = showBits ? 15f : 0f;

            if (showBits)
            {
                session.DrawText(_labelTopBits, 6f, topRow, axisColor, format);
                session.DrawText(_labelMidBits, 6f, midRow, axisColor, format);
            }

            if (showBytes)
            {
                session.DrawText(_labelTopBytes, 6f, topRow + bytesOffset, axisColor, format);
                session.DrawText(_labelMidBytes, 6f, midRow + bytesOffset, axisColor, format);
            }

        }

        private void EaseValues()
        {
            double nowEpoch = NowEpoch();
            double delta = _lastFrameEpoch > 0 ? nowEpoch - _lastFrameEpoch : 0.016;
            _lastFrameEpoch = nowEpoch;
            double timeConstant = EaseTimeConstantSeconds;
            double factor = 1.0 - Math.Exp(-delta / timeConstant);

            for (int index = 0; index < _count; index++)
            {
                _displayedDownload![index] += (_download![index] - _displayedDownload[index]) * factor;
                _displayedUpload![index] += (_upload![index] - _displayedUpload[index]) * factor;
            }

        }

        private void ApplyPoints()
        {
            IReadOnlyList<ChartPoint>? points = _currentPoints;
            int count = points?.Count ?? 0;

            if (count >= 2)
            {
                double[] timeEpoch = new double[count];
                double[] download = new double[count];
                double[] upload = new double[count];
                long maxValue = 1L;

                for (int index = 0; index < count; index++)
                {
                    ChartPoint point = points![index];
                    timeEpoch[index] = (point.BucketStart - DateTime.UnixEpoch).TotalSeconds;
                    download[index] = point.BytesDownloaded;
                    upload[index] = point.BytesUploaded;
                    maxValue = Math.Max(maxValue, Math.Max(point.BytesDownloaded, point.BytesUploaded));
                }

                double newBucketSeconds = TrafficRateFormatter.BucketSeconds(points!);
                bool animate = _animateNext
                    && _isLive
                    && _smoothScrolling
                    && _displayedDownload != null
                    && _displayedUpload != null
                    && _timeEpoch != null
                    && _count == count
                    && Math.Abs(newBucketSeconds - _bucketSeconds) < 0.001;

                double[] displayedDownload = new double[count];
                double[] displayedUpload = new double[count];

                if (animate)
                {
                    MigrateDisplayed(timeEpoch, download, upload, newBucketSeconds, displayedDownload, displayedUpload);
                }
                else
                {
                    Array.Copy(download, displayedDownload, count);
                    Array.Copy(upload, displayedUpload, count);
                }

                _timeEpoch = timeEpoch;
                _download = download;
                _upload = upload;
                _displayedDownload = displayedDownload;
                _displayedUpload = displayedUpload;
                _count = count;
                _bucketSeconds = newBucketSeconds;

                long axisMax = RoundAxisMax(maxValue, newBucketSeconds);
                _targetMax = axisMax;

                if (!animate)
                {
                    _displayMax = axisMax;
                }

                UpdatePeakLabels(points!, maxValue);
            }
            else
            {
                _count = 0;
                _timeEpoch = null;
                _download = null;
                _upload = null;
                _displayedDownload = null;
                _displayedUpload = null;
                PeakText = string.Empty;
                MaxScaleLabel.Text = string.Empty;
                MaxScaleMBpsLabel.Text = string.Empty;
            }

            _animateNext = false;
            ChartCanvas.Invalidate();
        }

        private void MigrateDisplayed(
            double[] newTime, double[] newDownload, double[] newUpload, double bucketSeconds, double[] displayedDownload, double[] displayedUpload)
        {
            double[] oldTime = _timeEpoch!;
            double[] oldDisplayedDownload = _displayedDownload!;
            double[] oldDisplayedUpload = _displayedUpload!;
            int oldCount = oldTime.Length;
            long shift = (long)Math.Round((newTime[0] - oldTime[0]) / bucketSeconds);

            for (int index = 0; index < newTime.Length; index++)
            {
                long oldIndex = index + shift;
                bool carried = false;

                if (oldIndex >= 0 && oldIndex < oldCount && Math.Abs(oldTime[(int)oldIndex] - newTime[index]) < bucketSeconds * 0.5)
                {
                    displayedDownload[index] = oldDisplayedDownload[(int)oldIndex];
                    displayedUpload[index] = oldDisplayedUpload[(int)oldIndex];
                    carried = true;
                }

                if (!carried)
                {

                    if (index > 0)
                    {
                        displayedDownload[index] = displayedDownload[index - 1];
                        displayedUpload[index] = displayedUpload[index - 1];
                    }
                    else
                    {
                        displayedDownload[index] = newDownload[index];
                        displayedUpload[index] = newUpload[index];
                    }

                }

            }

        }

        private static long RoundAxisMax(long peakBytes, double bucketSeconds)
        {
            double bitsPerSecond = peakBytes * 8.0 / bucketSeconds;
            double niceBitsPerSecond = AxisScale.NiceMax(bitsPerSecond);
            long result = (long)Math.Round(niceBitsPerSecond * bucketSeconds / 8.0);

            return result;
        }

        private void UpdatePeakLabels(IReadOnlyList<ChartPoint> points, long maxValue)
        {
            double bucketSeconds = TrafficRateFormatter.BucketSeconds(points);
            RateUnitMode mode = TrafficRateFormatter.Mode;
            Visibility bitsVisibility = mode == RateUnitMode.Bytes ? Visibility.Collapsed : Visibility.Visible;
            Visibility bytesVisibility = mode == RateUnitMode.Bits ? Visibility.Collapsed : Visibility.Visible;

            string peakBits = TrafficRateFormatter.BitsPerSecond(maxValue, bucketSeconds);
            string peakBytes = TrafficRateFormatter.BytesPerSecond(maxValue, bucketSeconds);

            // Only the mini graph reads this, and its header has room for one figure.
            PeakText = TrafficRateFormatter.SingleUnit(mode) == RateUnitMode.Bytes ? peakBytes : peakBits;

            MaxScaleLabel.Text = peakBits;
            MaxScaleMBpsLabel.Text = peakBytes;
            MaxScaleLabel.Visibility = bitsVisibility;
            MaxScaleCaption.Visibility = bitsVisibility;
            MaxScaleMBpsLabel.Visibility = bytesVisibility;
            MaxScaleMBpsCaption.Visibility = bytesVisibility;
            MaxScaleMBpsLabel.Margin = mode == RateUnitMode.Bytes ? new Thickness(0) : new Thickness(0, 15, 0, 0);
        }

        private int NearestIndex(double pointerX, double width)
        {
            bool scrolling = _isLive && _smoothScrolling;
            double nowEpoch = _frozen ? _frozenNowEpoch : NowEpoch();
            (double leftEdge, double span) = Window(_count, _bucketSeconds, _timeEpoch![0], _timeEpoch[_count - 1], scrolling, nowEpoch);
            double targetEpoch = leftEdge + pointerX / width * span;
            int nearest = 0;
            double bestDistance = double.MaxValue;

            for (int index = 0; index < _count; index++)
            {
                double distance = Math.Abs(_timeEpoch[index] - targetEpoch);

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    nearest = index;
                }

            }

            return nearest;
        }

        private void InputLayerPointerMoved(object sender, PointerRoutedEventArgs args)
        {
            IReadOnlyList<ChartPoint>? points = _currentPoints;

            if (points != null && _count >= 2 && _timeEpoch != null)
            {

                if (!_frozen)
                {
                    _frozen = true;
                    _frozenNowEpoch = NowEpoch();
                }

                Point position = args.GetCurrentPoint(InputLayer).Position;
                double width = InputLayer.ActualWidth;
                double height = InputLayer.ActualHeight;

                CrosshairLine.X1 = position.X;
                CrosshairLine.X2 = position.X;
                CrosshairLine.Y1 = 0;
                CrosshairLine.Y2 = height;
                CrosshairLine.Visibility = Visibility.Visible;

                int index = NearestIndex(position.X, width);
                ChartPoint hoveredPoint = points[index];

                HoverTimeLabel.Text = hoveredPoint.BucketStart.ToLocalTime().ToString("dd MMM yyyy  HH:mm:ss");
                HoverDownloadLabel.Text = HoverRateText(hoveredPoint.BytesDownloaded);
                HoverUploadLabel.Text = HoverRateText(hoveredPoint.BytesUploaded);
                HoverPanel.Visibility = Visibility.Visible;

                double panelWidth = HoverPanel.ActualWidth > 0 ? HoverPanel.ActualWidth : 130;
                double leftPos = position.X + 14;

                if (leftPos + panelWidth > width)
                {
                    leftPos = position.X - panelWidth - 14;
                }

                HoverPanel.Margin = new Thickness(Math.Max(0, leftPos), 8, 0, 0);
            }

        }

        private string HoverRateText(long bytes)
        {
            string result;

            if (TrafficRateFormatter.Mode == RateUnitMode.Bytes)
            {
                result = TrafficRateFormatter.BytesPerSecond(bytes, _bucketSeconds);
            }
            else
            {
                result = TrafficRateFormatter.BitsPerSecond(bytes, _bucketSeconds);
            }

            return result;
        }

        private void InputLayerPointerExited(object sender, PointerRoutedEventArgs args)
        {
            _frozen = false;
            _lastFrameEpoch = 0;
            CrosshairLine.Visibility = Visibility.Collapsed;
            HoverPanel.Visibility = Visibility.Collapsed;
            ChartCanvas.Invalidate();
        }

        private void InputLayerPointerPressed(object sender, PointerRoutedEventArgs args)
        {

            if (args.GetCurrentPoint(InputLayer).Properties.IsLeftButtonPressed)
            {
                IReadOnlyList<ChartPoint>? points = _currentPoints;

                if (points != null && _count >= 2 && _timeEpoch != null)
                {
                    Point position = args.GetCurrentPoint(InputLayer).Position;
                    int index = NearestIndex(position.X, InputLayer.ActualWidth);
                    ChartPoint selectedPoint = points[index];

                    BucketSelected?.Invoke(this, selectedPoint);
                }

            }

        }
    }
}
