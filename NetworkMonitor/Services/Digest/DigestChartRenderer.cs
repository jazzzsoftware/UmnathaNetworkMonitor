using System.IO;
using System.Linq;
using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;
using Windows.UI;
using NetworkMonitor.Models;

namespace NetworkMonitor.Services.Digest
{
    public class DigestChartRenderer
    {
        private const float ChartWidth = 840f;
        private const float ChartHeight = 360f;
        private const float SpeedChartHeight = 180f;
        private const float RenderDpi = 288f;
        private static readonly Color DarkBackground = Color.FromArgb(255, 32, 32, 32);
        private static readonly Color DarkText = Color.FromArgb(255, 190, 190, 190);
        private static readonly Color LightBackground = Color.FromArgb(255, 0xEE, 0xEE, 0xEE);
        private static readonly Color LightText = Color.FromArgb(255, 80, 80, 80);
        private static readonly Color EmptyColour = Color.FromArgb(255, 150, 150, 150);
        private static readonly Color SliceTextColour = Color.FromArgb(255, 255, 255, 255);
        private static readonly Color DownloadColour = Color.FromArgb(255, 0x19, 0x76, 0xD2);
        private static readonly Color UploadColour = Color.FromArgb(255, 0xAB, 0x47, 0xBC);
        private static readonly Color LatencyColour = Color.FromArgb(255, 0xF5, 0x7C, 0x00);
        private static readonly Color JitterColour = Color.FromArgb(255, 0x2E, 0x7D, 0x32);

        public byte[] RenderInternetTrafficChart(DigestSummary summary, bool lightBackground, float dpi = RenderDpi)
        {
            List<string> categories = new();
            List<double[]> values = new();

            foreach (InternetTrafficAppSummary app in summary.InternetTopApps)
            {
                categories.Add(Truncate(app.ProcessName, 14));
                values.Add(new double[] { app.BytesDownloaded, app.BytesUploaded });
            }

            string[] seriesNames = new string[] { "Download", "Upload" };
            Color[] seriesColours = new Color[] { DownloadColour, UploadColour };
            Color background = lightBackground ? LightBackground : DarkBackground;
            Color textColour = lightBackground ? LightText : DarkText;
            byte[] png = RenderToPng(background, ChartHeight, dpi, (session, device) =>
                DrawGroupedBars(session, categories, seriesNames, seriesColours, values, FormatBytes, textColour));

            return png;
        }

        public byte[] RenderLocalTrafficSplitChart(DigestSummary summary, bool lightBackground, float dpi = RenderDpi)
        {
            List<string> categories = new();
            List<double[]> values = new();

            foreach (LocalTrafficAppSummary app in summary.TopLocalApps)
            {
                categories.Add(Truncate(app.ProcessName, 14));
                values.Add(new double[] { app.BytesDownloaded, app.BytesUploaded });
            }

            string[] seriesNames = new string[] { "Download", "Upload" };
            Color[] seriesColours = new Color[] { DownloadColour, UploadColour };
            Color background = lightBackground ? LightBackground : DarkBackground;
            Color textColour = lightBackground ? LightText : DarkText;
            byte[] png = RenderToPng(background, ChartHeight, dpi, (session, canvasDevice) =>
                DrawGroupedBars(session, categories, seriesNames, seriesColours, values, FormatBytes, textColour));

            return png;
        }

        public byte[] RenderInternetTrafficSplitChart(DigestSummary summary, bool lightBackground, float dpi = RenderDpi)
        {
            List<(string Label, double Value, Color Colour)> slices = new()
            {
                ("Download", summary.TotalBytesDownloaded, DownloadColour),
                ("Upload", summary.TotalBytesUploaded, UploadColour)
            };
            Color background = lightBackground ? LightBackground : DarkBackground;
            Color textColour = lightBackground ? LightText : DarkText;
            byte[] png = RenderToPng(background, ChartHeight, dpi, (session, device) =>
                DrawDonut(session, device, slices, background, textColour));

            return png;
        }

        public byte[] RenderSpeedThroughputChart(DigestSummary summary, bool lightBackground, float dpi = RenderDpi)
        {
            List<double[]> values = new();
            List<double> timestamps = new();

            foreach (SpeedTestRowSummary test in summary.SpeedTests)
            {
                values.Add(new double[] { test.DownloadMbps, test.UploadMbps });
                timestamps.Add((test.Timestamp - DateTime.UnixEpoch).TotalSeconds);
            }

            string[] seriesNames = new string[] { "Download", "Upload" };
            Color[] seriesColours = new Color[] { DownloadColour, UploadColour };
            Color background = lightBackground ? LightBackground : DarkBackground;
            Color textColour = lightBackground ? LightText : DarkText;
            byte[] png = RenderToPng(background, SpeedChartHeight, dpi, (session, device) =>
                DrawLineChart(session, device, SpeedChartHeight, seriesNames, seriesColours, values, timestamps, textColour, "Mbps", "MBps", 8.0));

            return png;
        }

        public byte[] RenderSpeedLatencyChart(DigestSummary summary, bool lightBackground, float dpi = RenderDpi)
        {
            List<double[]> values = new();
            List<double> timestamps = new();

            foreach (SpeedTestRowSummary test in summary.SpeedTests)
            {
                values.Add(new double[] { test.LatencyMs, test.JitterMs });
                timestamps.Add((test.Timestamp - DateTime.UnixEpoch).TotalSeconds);
            }

            string[] seriesNames = new string[] { "Latency", "Jitter" };
            Color[] seriesColours = new Color[] { LatencyColour, JitterColour };
            Color background = lightBackground ? LightBackground : DarkBackground;
            Color textColour = lightBackground ? LightText : DarkText;
            byte[] png = RenderToPng(background, SpeedChartHeight, dpi, (session, device) =>
                DrawLineChart(session, device, SpeedChartHeight, seriesNames, seriesColours, values, timestamps, textColour, "ms", string.Empty, 0.0));

            return png;
        }

        private static byte[] RenderToPng(Color background, float chartHeight, float dpi, Action<CanvasDrawingSession, CanvasDevice> draw)
        {
            CanvasDevice device = CanvasDevice.GetSharedDevice();
            byte[] result;

            using (CanvasRenderTarget target = new CanvasRenderTarget(device, ChartWidth, chartHeight, dpi))
            {

                using (CanvasDrawingSession session = target.CreateDrawingSession())
                {
                    session.Clear(background);
                    draw(session, device);
                }

                using (MemoryStream stream = new MemoryStream())
                {
                    target.SaveAsync(stream.AsRandomAccessStream(), CanvasBitmapFileFormat.Png).AsTask().GetAwaiter().GetResult();
                    result = stream.ToArray();
                }

            }

            return result;
        }

        private static void DrawGroupedBars(
            CanvasDrawingSession session,
            IReadOnlyList<string> categories,
            IReadOnlyList<string> seriesNames,
            IReadOnlyList<Color> seriesColours,
            IReadOnlyList<double[]> values,
            Func<double, string> valueFormatter,
            Color textColour)
        {
            DrawLegend(session, seriesNames, seriesColours, 8f, 10f, textColour);

            double maxValue = 0;

            foreach (double[] row in values)
            {

                foreach (double value in row)
                {

                    if (value > maxValue)
                    {
                        maxValue = value;
                    }

                }

            }

            if (categories.Count == 0 || maxValue <= 0)
            {
                using CanvasTextFormat emptyFormat = new CanvasTextFormat { FontSize = 14f };
                session.DrawText("No data", 16f, 60f, EmptyColour, emptyFormat);
            }
            else
            {
                float plotTop = 46f;
                float plotBottom = ChartHeight - 30f;
                float plotHeight = plotBottom - plotTop;
                float plotLeft = 16f;
                float plotRight = ChartWidth - 16f;
                float slotWidth = (plotRight - plotLeft) / categories.Count;
                int seriesCount = seriesNames.Count;
                float groupWidth = slotWidth * 0.72f;
                float barWidth = groupWidth / seriesCount;
                float groupOffset = (slotWidth - groupWidth) / 2f;

                using CanvasTextFormat valueFormat = new CanvasTextFormat { FontSize = 9f, HorizontalAlignment = CanvasHorizontalAlignment.Center };
                using CanvasTextFormat categoryFormat = new CanvasTextFormat { FontSize = 10f, HorizontalAlignment = CanvasHorizontalAlignment.Center, WordWrapping = CanvasWordWrapping.NoWrap };

                for (int categoryIndex = 0; categoryIndex < categories.Count; categoryIndex++)
                {
                    float slotLeft = plotLeft + categoryIndex * slotWidth;
                    double[] row = values[categoryIndex];
                    float tallestBarTop = plotBottom;

                    for (int seriesIndex = 0; seriesIndex < seriesCount; seriesIndex++)
                    {
                        double value = seriesIndex < row.Length ? row[seriesIndex] : 0;
                        float barHeight = (float)(value / maxValue) * plotHeight;
                        float barLeft = slotLeft + groupOffset + seriesIndex * barWidth;
                        float barTop = plotBottom - barHeight;
                        session.FillRectangle(barLeft, barTop, barWidth * 0.86f, barHeight, seriesColours[seriesIndex]);

                        if (barTop < tallestBarTop)
                        {
                            tallestBarTop = barTop;
                        }

                    }

                    float labelBlockTop = tallestBarTop - 2f - seriesCount * 12f;

                    for (int labelIndex = 0; labelIndex < seriesCount; labelIndex++)
                    {
                        double labelValue = labelIndex < row.Length ? row[labelIndex] : 0;

                        if (labelValue > 0)
                        {
                            float labelY = labelBlockTop + labelIndex * 12f;
                            session.DrawText(valueFormatter(labelValue), slotLeft, labelY, slotWidth, 12f, seriesColours[labelIndex], valueFormat);
                        }

                    }

                    session.DrawText(categories[categoryIndex], slotLeft, plotBottom + 4f, slotWidth, 18f, textColour, categoryFormat);
                }

            }

        }

        private static void DrawLegend(
            CanvasDrawingSession session,
            IReadOnlyList<string> seriesNames,
            IReadOnlyList<Color> seriesColours,
            float topY,
            float fontSize,
            Color textColour)
        {
            using CanvasTextFormat legendFormat = new CanvasTextFormat { FontSize = fontSize };
            float swatchSize = 10f;
            float swatchGap = 5f;
            float rowHeight = 16f;
            float maxTextWidth = 0f;

            foreach (string name in seriesNames)
            {

                using (CanvasTextLayout layout = new CanvasTextLayout(session, name, legendFormat, 200f, 24f))
                {

                    if ((float)layout.LayoutBounds.Width > maxTextWidth)
                    {
                        maxTextWidth = (float)layout.LayoutBounds.Width;
                    }

                }

            }

            float blockWidth = swatchSize + swatchGap + maxTextWidth;
            float startX = ChartWidth - 16f - blockWidth;

            for (int seriesIndex = 0; seriesIndex < seriesNames.Count; seriesIndex++)
            {
                float rowY = topY + seriesIndex * rowHeight;
                session.FillRectangle(startX, rowY, swatchSize, swatchSize, seriesColours[seriesIndex]);
                session.DrawText(seriesNames[seriesIndex], startX + swatchSize + swatchGap, rowY - 2f, textColour, legendFormat);
            }

        }

        private static void DrawDonut(
            CanvasDrawingSession session,
            CanvasDevice device,
            IReadOnlyList<(string Label, double Value, Color Colour)> slices,
            Color background,
            Color textColour)
        {
            double total = 0;

            foreach ((string Label, double Value, Color Colour) slice in slices)
            {
                total += slice.Value;
            }

            if (total <= 0)
            {
                using CanvasTextFormat emptyFormat = new CanvasTextFormat { FontSize = 14f };
                session.DrawText("No data", 16f, 60f, EmptyColour, emptyFormat);
            }
            else
            {
                string[] legendNames = slices.Select(slice => slice.Label).ToArray();
                Color[] legendColours = slices.Select(slice => slice.Colour).ToArray();
                DrawLegend(session, legendNames, legendColours, 8f, 10f, textColour);

                Vector2 centre = new Vector2(ChartWidth / 2f, 185f);
                float radius = 120f;
                float innerRadius = 66f;
                float midRadius = (radius + innerRadius) / 2f;
                float startAngle = -(float)(Math.PI / 2.0);

                using CanvasTextFormat statFormat = new CanvasTextFormat { FontSize = 13f, HorizontalAlignment = CanvasHorizontalAlignment.Center, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };

                foreach ((string Label, double Value, Color Colour) slice in slices)
                {

                    if (slice.Value > 0)
                    {
                        float sweep = -(float)(slice.Value / total * 2.0 * Math.PI);
                        float arcStartX = centre.X + radius * (float)Math.Cos(startAngle);
                        float arcStartY = centre.Y + radius * (float)Math.Sin(startAngle);
                        using CanvasPathBuilder pathBuilder = new CanvasPathBuilder(device);
                        pathBuilder.BeginFigure(centre);
                        pathBuilder.AddLine(new Vector2(arcStartX, arcStartY));
                        pathBuilder.AddArc(centre, radius, radius, startAngle, sweep);
                        pathBuilder.EndFigure(CanvasFigureLoop.Closed);

                        using (CanvasGeometry geometry = CanvasGeometry.CreatePath(pathBuilder))
                        {
                            session.FillGeometry(geometry, slice.Colour);
                        }

                        float midAngle = startAngle + sweep / 2f;
                        float statX = centre.X + midRadius * (float)Math.Cos(midAngle);
                        float statY = centre.Y + midRadius * (float)Math.Sin(midAngle);
                        double percent = slice.Value / total * 100.0;
                        session.DrawText(FormatBytes(slice.Value), statX - 55f, statY - 15f, 110f, 16f, SliceTextColour, statFormat);
                        session.DrawText($"{percent:F0}%", statX - 55f, statY + 1f, 110f, 16f, SliceTextColour, statFormat);

                        startAngle += sweep;
                    }

                }

                session.FillCircle(centre, innerRadius, background);
            }

        }

        private static void DrawLineChart(
            CanvasDrawingSession session,
            CanvasDevice device,
            float chartHeight,
            IReadOnlyList<string> seriesNames,
            IReadOnlyList<Color> seriesColours,
            IReadOnlyList<double[]> values,
            IReadOnlyList<double> timestamps,
            Color textColour,
            string unit,
            string secondaryUnit,
            double secondaryDivisor)
        {
            DrawLegend(session, seriesNames, seriesColours, 8f, 8f, textColour);

            double maxValue = 0;

            foreach (double[] row in values)
            {

                foreach (double value in row)
                {

                    if (value > maxValue)
                    {
                        maxValue = value;
                    }

                }

            }

            maxValue = Math.Ceiling(maxValue / 10.0) * 10.0;

            if (values.Count < 2 || maxValue <= 0)
            {
                using CanvasTextFormat emptyFormat = new CanvasTextFormat { FontSize = 14f };
                session.DrawText("No data", 16f, 60f, EmptyColour, emptyFormat);
            }
            else
            {
                float plotTop = 46f;
                float plotBottom = chartHeight - 30f;
                float plotHeight = plotBottom - plotTop;
                float plotLeft = 60f;
                float plotRight = ChartWidth - 16f;
                float plotWidth = plotRight - plotLeft;
                float usableHeight = plotHeight * 0.9f;
                int seriesCount = seriesNames.Count;
                float yMax = plotBottom - usableHeight;
                float yMid = plotBottom - usableHeight / 2f;

                Color axisLineColour = Color.FromArgb(0x55, textColour.R, textColour.G, textColour.B);
                Color gridColour = Color.FromArgb(0x22, textColour.R, textColour.G, textColour.B);

                session.DrawLine(plotLeft, plotTop, plotLeft, plotBottom, axisLineColour, 1f);
                session.DrawLine(plotLeft, yMax, plotRight, yMax, gridColour, 1f);
                session.DrawLine(plotLeft, yMid, plotRight, yMid, gridColour, 1f);

                using CanvasTextFormat axisFormat = new CanvasTextFormat { FontSize = 8f };
                DrawAxisValue(session, axisFormat, textColour, maxValue, unit, secondaryUnit, secondaryDivisor, yMax);
                DrawAxisValue(session, axisFormat, textColour, maxValue / 2.0, unit, secondaryUnit, secondaryDivisor, yMid);

                double minEpoch = double.MaxValue;
                double maxEpoch = double.MinValue;

                foreach (double epoch in timestamps)
                {
                    minEpoch = Math.Min(minEpoch, epoch);
                    maxEpoch = Math.Max(maxEpoch, epoch);
                }

                double epochSpan = maxEpoch - minEpoch;

                if (epochSpan <= 0)
                {
                    epochSpan = 1.0;
                }

                for (int seriesIndex = 0; seriesIndex < seriesCount; seriesIndex++)
                {
                    Vector2[] points = new Vector2[values.Count];

                    for (int pointIndex = 0; pointIndex < values.Count; pointIndex++)
                    {
                        double value = seriesIndex < values[pointIndex].Length ? values[pointIndex][seriesIndex] : 0;
                        float x = plotLeft + (float)((timestamps[pointIndex] - minEpoch) / epochSpan) * plotWidth;
                        float y = plotBottom - (float)(value / maxValue) * usableHeight;
                        points[pointIndex] = new Vector2(x, y);
                    }

                    Color colour = seriesColours[seriesIndex];
                    Color fillColour = Color.FromArgb(0x33, colour.R, colour.G, colour.B);

                    using (CanvasPathBuilder areaBuilder = new CanvasPathBuilder(device))
                    {
                        areaBuilder.BeginFigure(points[0]);

                        for (int index = 0; index < points.Length - 1; index++)
                        {
                            float segmentWidth = points[index + 1].X - points[index].X;
                            Vector2 control1 = new Vector2(points[index].X + segmentWidth / 3f, points[index].Y);
                            Vector2 control2 = new Vector2(points[index + 1].X - segmentWidth / 3f, points[index + 1].Y);
                            areaBuilder.AddCubicBezier(control1, control2, points[index + 1]);
                        }

                        areaBuilder.AddLine(new Vector2(points[points.Length - 1].X, plotBottom));
                        areaBuilder.AddLine(new Vector2(points[0].X, plotBottom));
                        areaBuilder.EndFigure(CanvasFigureLoop.Closed);

                        using (CanvasGeometry areaGeometry = CanvasGeometry.CreatePath(areaBuilder))
                        {
                            session.FillGeometry(areaGeometry, fillColour);
                        }

                    }

                    using (CanvasPathBuilder lineBuilder = new CanvasPathBuilder(device))
                    {
                        lineBuilder.BeginFigure(points[0]);

                        for (int index = 0; index < points.Length - 1; index++)
                        {
                            float segmentWidth = points[index + 1].X - points[index].X;
                            Vector2 control1 = new Vector2(points[index].X + segmentWidth / 3f, points[index].Y);
                            Vector2 control2 = new Vector2(points[index + 1].X - segmentWidth / 3f, points[index + 1].Y);
                            lineBuilder.AddCubicBezier(control1, control2, points[index + 1]);
                        }

                        lineBuilder.EndFigure(CanvasFigureLoop.Open);

                        using (CanvasGeometry lineGeometry = CanvasGeometry.CreatePath(lineBuilder))
                        {
                            session.DrawGeometry(lineGeometry, colour, 1.5f);
                        }

                    }

                }

            }

        }

        private static void DrawAxisValue(
            CanvasDrawingSession session,
            CanvasTextFormat format,
            Color colour,
            double value,
            string unit,
            string secondaryUnit,
            double secondaryDivisor,
            float top)
        {
            string primary = string.IsNullOrEmpty(unit) ? $"{value:0}" : $"{value:0} {unit}";

            session.DrawText(primary, 8f, top, colour, format);

            if (secondaryDivisor > 0 && !string.IsNullOrEmpty(secondaryUnit))
            {
                string secondary = $"{value / secondaryDivisor:0.0} {secondaryUnit}";

                session.DrawText(secondary, 8f, top + 11f, colour, format);
            }

        }

        private static string FormatBytes(double bytes)
        {
            string result;

            if (bytes >= 1_073_741_824.0)
            {
                result = $"{bytes / 1_073_741_824.0:F1} GB";
            }
            else if (bytes >= 1_048_576.0)
            {
                result = $"{bytes / 1_048_576.0:F1} MB";
            }
            else if (bytes >= 1_024.0)
            {
                result = $"{bytes / 1_024.0:F1} KB";
            }
            else
            {
                result = $"{bytes:F0} B";
            }

            return result;
        }

        private static string Truncate(string value, int maxLength)
        {
            string result = value.Length <= maxLength ? value : value.Substring(0, maxLength - 1) + "…";

            return result;
        }
    }
}
