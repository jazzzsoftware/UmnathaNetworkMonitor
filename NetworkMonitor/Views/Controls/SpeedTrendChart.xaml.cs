using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using NetworkMonitor.Core.Charting;
using NetworkMonitor.Models.Charting;
using Windows.Foundation;
using Windows.UI;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace NetworkMonitor.Views.Controls
{
    public sealed partial class SpeedTrendChart : UserControl
    {
        public static readonly DependencyProperty SeriesProperty =
            DependencyProperty.Register(
                nameof(Series),
                typeof(IReadOnlyList<ChartSeries>),
                typeof(SpeedTrendChart),
                new PropertyMetadata(null, OnSeriesChanged));

        public static readonly DependencyProperty UnitProperty =
            DependencyProperty.Register(
                nameof(Unit),
                typeof(string),
                typeof(SpeedTrendChart),
                new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty SecondaryUnitProperty =
            DependencyProperty.Register(
                nameof(SecondaryUnit),
                typeof(string),
                typeof(SpeedTrendChart),
                new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty SecondaryDivisorProperty =
            DependencyProperty.Register(
                nameof(SecondaryDivisor),
                typeof(double),
                typeof(SpeedTrendChart),
                new PropertyMetadata(0.0));

        public static readonly DependencyProperty PrimaryDivisorProperty =
            DependencyProperty.Register(
                nameof(PrimaryDivisor),
                typeof(double),
                typeof(SpeedTrendChart),
                new PropertyMetadata(1.0));

        public SpeedTrendChart()
        {
            InitializeComponent();
        }

        public IReadOnlyList<ChartSeries>? Series
        {
            get => (IReadOnlyList<ChartSeries>?)GetValue(SeriesProperty);
            set => SetValue(SeriesProperty, value);
        }

        public string Unit
        {
            get => (string)GetValue(UnitProperty);
            set => SetValue(UnitProperty, value);
        }

        public string SecondaryUnit
        {
            get => (string)GetValue(SecondaryUnitProperty);
            set => SetValue(SecondaryUnitProperty, value);
        }

        public double SecondaryDivisor
        {
            get => (double)GetValue(SecondaryDivisorProperty);
            set => SetValue(SecondaryDivisorProperty, value);
        }

        public double PrimaryDivisor
        {
            get => (double)GetValue(PrimaryDivisorProperty);
            set => SetValue(PrimaryDivisorProperty, value);
        }

        private static void OnSeriesChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        {
            SpeedTrendChart chart = (SpeedTrendChart)sender;

            chart.Redraw();
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs args)
        {
            Redraw();
        }

        private void Redraw()
        {
            PlotCanvas.Children.Clear();

            IReadOnlyList<ChartSeries>? series = NormalizeForRender(Series);
            double width = PlotCanvas.ActualWidth;
            double height = PlotCanvas.ActualHeight;

            if (series is not null && series.Count > 0 && width > 0 && height > 0)
            {
                DrawGraticule(width, height);

                double peakValue = 1.0;
                double minEpoch = double.MaxValue;
                double maxEpoch = double.MinValue;

                foreach (ChartSeries chartSeries in series)
                {

                    foreach (ChartValue point in chartSeries.Points)
                    {
                        double epoch = (point.Timestamp - DateTime.UnixEpoch).TotalSeconds;
                        peakValue = Math.Max(peakValue, point.Value);
                        minEpoch = Math.Min(minEpoch, epoch);
                        maxEpoch = Math.Max(maxEpoch, epoch);
                    }

                }

                double axisMax = Math.Ceiling(peakValue / 10.0) * 10.0;

                double span = maxEpoch - minEpoch;

                if (span <= 0)
                {
                    span = 1.0;
                }

                foreach (ChartSeries chartSeries in series)
                {

                    if (chartSeries.Points.Count >= 2)
                    {
                        DrawSeries(chartSeries, axisMax, minEpoch, span, width, height);
                    }

                }

                AddAxisLabels(axisMax, height);
                PublishDrawSummary(series[0].Points.Count, (long)peakValue, (long)axisMax);
            }

        }

        private void PublishDrawSummary(int pointCount, long peakBitsPerSecond, long axisMax)
        {
            string summary = ChartDrawSummary.Format(
                pointCount,
                "download,upload",
                peakBitsPerSecond,
                axisMax,
                "speed");

            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(ChartRoot, summary);
        }

        private static IReadOnlyList<ChartSeries>? NormalizeForRender(IReadOnlyList<ChartSeries>? series)
        {
            IReadOnlyList<ChartSeries>? normalized = series;

            if (series is not null)
            {
                List<ChartSeries> expanded = new List<ChartSeries>();

                foreach (ChartSeries chartSeries in series)
                {

                    if (chartSeries.Points.Count == 1)
                    {
                        ChartValue point = chartSeries.Points[0];
                        ChartValue synthetic = new ChartValue(point.Timestamp.AddMinutes(-1.0), point.Value);
                        List<ChartValue> points = new List<ChartValue> { synthetic, point };
                        expanded.Add(new ChartSeries(chartSeries.Name, chartSeries.ColorHex, points));
                    }
                    else
                    {
                        expanded.Add(chartSeries);
                    }

                }

                normalized = expanded;
            }

            return normalized;
        }

        private void DrawSeries(ChartSeries chartSeries, double maxValue, double minEpoch, double span, double width, double height)
        {
            double usableHeight = height * 0.9;
            Color color = ParseColor(chartSeries.ColorHex);
            List<Point> chartPoints = new List<Point>();

            foreach (ChartValue point in chartSeries.Points)
            {
                double epoch = (point.Timestamp - DateTime.UnixEpoch).TotalSeconds;
                double xValue = (epoch - minEpoch) / span * width;
                double yValue = height - point.Value / maxValue * usableHeight;
                chartPoints.Add(new Point(xValue, yValue));
            }

            Color fillColor = Color.FromArgb(0x33, color.R, color.G, color.B);

            PathFigure areaFigure = new PathFigure
            {
                StartPoint = chartPoints[0],
                IsClosed = true,
                IsFilled = true
            };

            AddBezierSegments(areaFigure.Segments, chartPoints);
            areaFigure.Segments.Add(new LineSegment { Point = new Point(chartPoints[chartPoints.Count - 1].X, height) });
            areaFigure.Segments.Add(new LineSegment { Point = new Point(chartPoints[0].X, height) });

            Path areaPath = new Path
            {
                Fill = new SolidColorBrush(fillColor),
                Data = new PathGeometry { Figures = { areaFigure } }
            };

            PathFigure lineFigure = new PathFigure
            {
                StartPoint = chartPoints[0],
                IsClosed = false,
                IsFilled = false
            };

            AddBezierSegments(lineFigure.Segments, chartPoints);

            Path linePath = new Path
            {
                Stroke = new SolidColorBrush(color),
                StrokeThickness = 1.5,
                Data = new PathGeometry { Figures = { lineFigure } }
            };

            PlotCanvas.Children.Add(areaPath);
            PlotCanvas.Children.Add(linePath);
        }

        private static void AddBezierSegments(PathSegmentCollection segments, List<Point> chartPoints)
        {

            for (int index = 0; index < chartPoints.Count - 1; index++)
            {
                double segmentWidth = chartPoints[index + 1].X - chartPoints[index].X;
                Point control1 = new Point(chartPoints[index].X + segmentWidth / 3.0, chartPoints[index].Y);
                Point control2 = new Point(chartPoints[index + 1].X - segmentWidth / 3.0, chartPoints[index + 1].Y);

                BezierSegment segment = new BezierSegment
                {
                    Point1 = control1,
                    Point2 = control2,
                    Point3 = chartPoints[index + 1]
                };

                segments.Add(segment);
            }

        }

        private void DrawGraticule(double width, double height)
        {
            double usableHeight = height * 0.9;
            Color baseColor = ((SolidColorBrush)Application.Current.Resources["TextFillColorPrimaryBrush"]).Color;
            Color axisLineColor = Color.FromArgb(0x55, baseColor.R, baseColor.G, baseColor.B);
            Color gridLineColor = Color.FromArgb(0x22, baseColor.R, baseColor.G, baseColor.B);

            Microsoft.UI.Xaml.Shapes.Line verticalAxis = new Microsoft.UI.Xaml.Shapes.Line
            {
                X1 = 1,
                Y1 = 0,
                X2 = 1,
                Y2 = height,
                Stroke = new SolidColorBrush(axisLineColor),
                StrokeThickness = 1
            };

            PlotCanvas.Children.Add(verticalAxis);

            AddHorizontalGridLine(width, height - usableHeight, gridLineColor);
            AddHorizontalGridLine(width, height - usableHeight / 2.0, gridLineColor);
        }

        private void AddHorizontalGridLine(double width, double yValue, Color color)
        {
            Microsoft.UI.Xaml.Shapes.Line line = new Microsoft.UI.Xaml.Shapes.Line
            {
                X1 = 1,
                Y1 = yValue,
                X2 = width,
                Y2 = yValue,
                Stroke = new SolidColorBrush(color),
                StrokeThickness = 1
            };

            PlotCanvas.Children.Add(line);
        }

        private void AddAxisLabels(double maxValue, double height)
        {
            double usableHeight = height * 0.9;

            AddAxisLabel(FormatAxisValue(maxValue), height - usableHeight);
            AddAxisLabel(FormatAxisValue(maxValue / 2.0), height - usableHeight / 2.0 - 6.0);
        }

        private string FormatAxisValue(double value)
        {
            double divisor = PrimaryDivisor > 0 ? PrimaryDivisor : 1.0;
            double primaryValue = value / divisor;
            string primaryFormat = divisor == 1.0 ? "0" : "0.0";
            string text = string.IsNullOrEmpty(Unit) ? primaryValue.ToString(primaryFormat) : $"{primaryValue.ToString(primaryFormat)} {Unit}";

            if (SecondaryDivisor > 0 && !string.IsNullOrEmpty(SecondaryUnit))
            {
                text += $"\n{value / SecondaryDivisor:0.0} {SecondaryUnit}";
            }

            return text;
        }

        private void AddAxisLabel(string text, double top)
        {
            TextBlock label = new TextBlock
            {
                FontSize = 12,
                Opacity = 0.55,
                Text = text
            };

            Canvas.SetLeft(label, 6);
            Canvas.SetTop(label, top);
            PlotCanvas.Children.Add(label);
        }

        private static Color ParseColor(string colorHex)
        {
            string value = colorHex.TrimStart('#');
            byte red = Convert.ToByte(value.Substring(0, 2), 16);
            byte green = Convert.ToByte(value.Substring(2, 2), 16);
            byte blue = Convert.ToByte(value.Substring(4, 2), 16);
            Color color = Color.FromArgb(0xFF, red, green, blue);

            return color;
        }

        private void OnPointerMoved(object sender, PointerRoutedEventArgs args)
        {
            IReadOnlyList<ChartSeries>? series = Series;

            if (series is not null && series.Count > 0 && series[0].Points.Count >= 2 && PlotCanvas.ActualWidth > 0)
            {
                ChartSeries reference = series[0];
                Point position = args.GetCurrentPoint(InputLayer).Position;
                double width = PlotCanvas.ActualWidth;
                double minEpoch = (reference.Points[0].Timestamp - DateTime.UnixEpoch).TotalSeconds;
                double maxEpoch = (reference.Points[reference.Points.Count - 1].Timestamp - DateTime.UnixEpoch).TotalSeconds;
                double span = maxEpoch - minEpoch;

                if (span <= 0)
                {
                    span = 1.0;
                }

                double targetEpoch = minEpoch + position.X / width * span;
                int nearest = 0;
                double bestDistance = double.MaxValue;

                for (int index = 0; index < reference.Points.Count; index++)
                {
                    double epoch = (reference.Points[index].Timestamp - DateTime.UnixEpoch).TotalSeconds;
                    double distance = Math.Abs(epoch - targetEpoch);

                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        nearest = index;
                    }

                }

                double crosshairX = ((reference.Points[nearest].Timestamp - DateTime.UnixEpoch).TotalSeconds - minEpoch) / span * width;
                CrosshairLine.X1 = crosshairX;
                CrosshairLine.X2 = crosshairX;
                CrosshairLine.Y1 = 0;
                CrosshairLine.Y2 = PlotCanvas.ActualHeight;
                CrosshairLine.Visibility = Visibility.Visible;

                HoverStack.Children.Clear();

                TextBlock timeLabel = new TextBlock
                {
                    FontSize = 11,
                    Opacity = 0.7,
                    Text = reference.Points[nearest].Timestamp.ToLocalTime().ToString("dd MMM HH:mm")
                };
                HoverStack.Children.Add(timeLabel);

                foreach (ChartSeries chartSeries in series)
                {

                    if (nearest < chartSeries.Points.Count)
                    {
                        double value = chartSeries.Points[nearest].Value;
                        double divisor = PrimaryDivisor > 0 ? PrimaryDivisor : 1.0;
                        string text = $"{chartSeries.Name}: {value / divisor:0.0} {Unit}";

                        if (SecondaryDivisor > 0 && !string.IsNullOrEmpty(SecondaryUnit))
                        {
                            text += $" / {value / SecondaryDivisor:0.0} {SecondaryUnit}";
                        }

                        TextBlock valueLabel = new TextBlock
                        {
                            FontSize = 12,
                            Foreground = new SolidColorBrush(ParseColor(chartSeries.ColorHex)),
                            Text = text
                        };
                        HoverStack.Children.Add(valueLabel);
                    }

                }

                HoverPanel.Visibility = Visibility.Visible;

                double panelLeft = position.X + 14;

                if (panelLeft + 150 > width)
                {
                    panelLeft = position.X - 164;
                }

                HoverPanel.Margin = new Thickness(Math.Max(0, panelLeft), 8, 0, 0);
            }

        }

        private void OnPointerExited(object sender, PointerRoutedEventArgs args)
        {
            CrosshairLine.Visibility = Visibility.Collapsed;
            HoverPanel.Visibility = Visibility.Collapsed;
        }
    }
}
