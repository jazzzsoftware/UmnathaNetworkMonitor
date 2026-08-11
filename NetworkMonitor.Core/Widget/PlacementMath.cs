namespace NetworkMonitor.Core.Widget
{
    // The DIP-to-physical conversions, the clamp arithmetic and the resize anchoring that decide
    // where the widget ends up. All of it used to live in MiniGraphWindow, in the app project, which
    // NetworkMonitor.Tests cannot reference — so every one of C2-1, C2-3, C2-4 and C2-6 was a
    // defect in code no test could reach, and the only way to check a fix was a manual
    // multi-monitor walkthrough.
    //
    // Nothing here touches AppWindow, DisplayArea or DWM. The window measures those and passes the
    // numbers in.
    public static class PlacementMath
    {
        // A DIP size becomes a WINDOW size: the stored size describes what the user can see, and the
        // invisible resize border sits outside it. Omitting the insets gave the strip's columns
        // ~14 DIP less than the metric reserved (C2-6).
        public static PlacementRect SizeFromDips(double widthInDips, double heightInDips, double scale, FrameInsets insets)
        {
            int width = (int)Math.Round(widthInDips * scale) + insets.Horizontal;
            int height = (int)Math.Round(heightInDips * scale) + insets.Vertical;
            PlacementRect size = new PlacementRect(0, 0, width, height);

            return size;
        }

        // The inverse: the panel height the layout actually gets, which is what the font scale must
        // be derived from. Feeding the outer window height instead made the derived width reserve
        // space at a font scale larger than the one rendered (C2-3).
        public static double PanelHeightInDips(int windowHeight, double scale, FrameInsets insets)
        {
            double safeScale = scale > 0.0 ? scale : 1.0;
            double panelHeight = (windowHeight - insets.Vertical) / safeScale;

            return panelHeight;
        }

        // The clamp is tested against what is visible, so the display area grows by the insets first.
        // Without this a position that put the visible strip flush with the bottom of the screen was
        // refused and the strip was dragged up by exactly the overhang.
        public static PlacementRect ExpandByInsets(PlacementRect area, FrameInsets insets)
        {
            PlacementRect expanded = new PlacementRect(
                area.X - insets.Left,
                area.Y - insets.Top,
                area.Width + insets.Left + insets.Right,
                area.Height + insets.Top + insets.Bottom);

            return expanded;
        }

        // Only the top-left corner used to be tested against a display, so a widget saved near a right
        // or bottom edge could return mostly off-screen — easier to hit once the size is scaled on
        // restore, because the widget can be wider than it was when the position was written.
        public static PlacementRect ClampToArea(PlacementRect window, PlacementRect area)
        {
            int maximumX = Math.Max(area.X, area.X + area.Width - window.Width);
            int maximumY = Math.Max(area.Y, area.Y + area.Height - window.Height);
            int positionX = Math.Clamp(window.X, area.X, maximumX);
            int positionY = Math.Clamp(window.Y, area.Y, maximumY);
            PlacementRect clamped = new PlacementRect(positionX, positionY, window.Width, window.Height);

            return clamped;
        }

        // Resizing the strip anchors the BOTTOM-RIGHT, not the origin. Anchoring the origin lifted the
        // strip off the taskbar on a top-edge over-drag, and made a left-edge drag walk the window
        // left for as long as the drag lasted (C2-4).
        public static PlacementRect ResizeHoldingBottomRight(PlacementRect current, int width, int height)
        {
            PlacementRect resized = new PlacementRect(
                current.Right - width,
                current.Bottom - height,
                width,
                height);

            return resized;
        }

        // Restore sized from the monitor under the window's corner while save divided by the monitor
        // holding its majority. When those disagree the widget shrank a step on every launch, so the
        // size is re-asserted from the live scale once the move has settled (C2-1).
        public static bool NeedsScaleReconcile(double sizedAtScale, double liveScale, double tolerance)
        {
            bool needed = Math.Abs(liveScale - sizedAtScale) > tolerance;

            return needed;
        }
    }
}
