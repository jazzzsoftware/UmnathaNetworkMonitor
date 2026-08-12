namespace NetworkMonitor.Core.Widget
{
    // The strip's width is not something the user drags: it is the sum of whatever sections are
    // switched on. Keeping that sum here rather than in the window is what makes it testable, and
    // the nominal cell widths below are the one place to tune if a string ever overflows.
    public static class HorizontalStripMetrics
    {
        public const double MinimumHeight = 40.0;
        public const double MaximumHeight = 120.0;
        public const double DefaultHeight = 40.0;

        private const double Padding = 4.0;
        private const double Gap = 4.0;
        public const double InternetCellWidth = 170.0;
        public const double LocalCellWidth = 170.0;
        public const double SpeedCellWidth = 196.0;
        public const double UnknownDevicesCellWidth = 146.0;
        private const double CloseCellWidth = 22.0;
        private const double MinimumFontScale = 1.0;
        private const double MaximumFontScale = 2.0;

        // The label and the peak share one baseline row and the chart needs whatever is left. Below
        // this the two collide, so the peak goes rather than being allowed to overlap the label.
        //
        // Unreachable in the shipped app, and deliberately kept: ClampHeight floors the PANEL height
        // at 40, and every caller clamps the panel before asking. Review finding C2-7 claimed the
        // opposite by reading MinimumHeight as a window height (40 window - 7 frame = 33, below the
        // threshold); the frame is added on top of the clamped panel, not taken out of it. Manual
        // testing on 2026-08-12 confirmed the peak is still shown at the smallest strip height.
        // Anything that lowers MinimumHeight below 34 turns this back into live behaviour.
        private const double PeakMinimumHeight = 34.0;

        public static double FontScale(double height)
        {
            double scale = Math.Clamp(height / DefaultHeight, MinimumFontScale, MaximumFontScale);

            return scale;
        }

        public static double ClampHeight(double height)
        {
            double clamped = Math.Clamp(height, MinimumHeight, MaximumHeight);

            return clamped;
        }

        public static bool ShowsPeak(double height)
        {
            bool showsPeak = height >= PeakMinimumHeight;

            return showsPeak;
        }

        public static double Width(bool showInternet, bool showLocal, bool showSpeedTest, bool showUnknownDevices, double fontScale)
        {
            double cells = CloseCellWidth;
            int cellCount = 1;

            if (showInternet)
            {
                cells += InternetCellWidth;
                cellCount++;
            }

            if (showLocal)
            {
                cells += LocalCellWidth;
                cellCount++;
            }

            if (showSpeedTest)
            {
                cells += SpeedCellWidth;
                cellCount++;
            }

            if (showUnknownDevices)
            {
                cells += UnknownDevicesCellWidth;
                cellCount++;
            }

            double width = (cells * fontScale) + ((cellCount - 1) * Gap) + (Padding * 2.0);

            return width;
        }
    }
}
