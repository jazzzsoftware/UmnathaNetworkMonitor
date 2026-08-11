using NetworkMonitor.Core.Widget;
using Xunit;

namespace NetworkMonitor.Tests
{
    // These pin the arithmetic behind C2-1, C2-3, C2-4 and C2-6 — four defects that previously lived
    // in the app project, where no test could reach them and the only check was a manual
    // multi-monitor walkthrough.
    public class PlacementMathTests
    {
        private static readonly FrameInsets StandardInsets = new FrameInsets(7, 0, 7, 7);

        [Fact]
        public void SizeFromDipsAddsTheFrameSoTheContentGetsWhatWasReserved()
        {
            PlacementRect size = PlacementMath.SizeFromDips(704.0, 40.0, 1.0, StandardInsets);

            Assert.Equal(704 + 14, size.Width);
            Assert.Equal(40 + 7, size.Height);
        }

        [Fact]
        public void SizeFromDipsScalesTheContentButNotTheAlreadyPhysicalInsets()
        {
            FrameInsets doubled = new FrameInsets(14, 0, 14, 14);

            PlacementRect size = PlacementMath.SizeFromDips(300.0, 100.0, 2.0, doubled);

            Assert.Equal(628, size.Width);
            Assert.Equal(214, size.Height);
        }

        [Fact]
        public void PanelHeightIsTheWindowHeightLessTheFrame()
        {
            double panelHeight = PlacementMath.PanelHeightInDips(47, 1.0, StandardInsets);

            Assert.Equal(40.0, panelHeight);
        }

        [Fact]
        public void PanelHeightAndSizeFromDipsAreInverses()
        {
            PlacementRect size = PlacementMath.SizeFromDips(704.0, 40.0, 1.5, StandardInsets);
            double panelHeight = PlacementMath.PanelHeightInDips(size.Height, 1.5, StandardInsets);

            Assert.Equal(40.0, panelHeight, 3);
        }

        [Fact]
        public void PanelHeightTreatsAZeroScaleAsOneRatherThanDividingByIt()
        {
            double panelHeight = PlacementMath.PanelHeightInDips(47, 0.0, StandardInsets);

            Assert.Equal(40.0, panelHeight);
        }

        [Fact]
        public void ExpandByInsetsGrowsTheAreaOnEverySideItHasAnInsetFor()
        {
            PlacementRect expanded = PlacementMath.ExpandByInsets(new PlacementRect(0, 0, 1920, 1080), StandardInsets);

            Assert.Equal(-7, expanded.X);
            Assert.Equal(0, expanded.Y);
            Assert.Equal(1934, expanded.Width);
            Assert.Equal(1087, expanded.Height);
        }

        [Fact]
        public void ClampPullsAWindowSavedPastTheRightEdgeBackOnScreen()
        {
            PlacementRect clamped = PlacementMath.ClampToArea(
                new PlacementRect(1900, 100, 320, 220),
                new PlacementRect(0, 0, 1920, 1080));

            Assert.Equal(1600, clamped.X);
            Assert.Equal(100, clamped.Y);
        }

        [Fact]
        public void ClampLeavesAWindowAlreadyInsideWhereTheUserPutIt()
        {
            PlacementRect clamped = PlacementMath.ClampToArea(
                new PlacementRect(400, 300, 320, 220),
                new PlacementRect(0, 0, 1920, 1080));

            Assert.Equal(400, clamped.X);
            Assert.Equal(300, clamped.Y);
        }

        [Fact]
        public void ClampNeverPushesAWindowWiderThanTheAreaOffTheLeftEdge()
        {
            PlacementRect clamped = PlacementMath.ClampToArea(
                new PlacementRect(50, 50, 2400, 1400),
                new PlacementRect(0, 0, 1920, 1080));

            Assert.Equal(0, clamped.X);
            Assert.Equal(0, clamped.Y);
        }

        [Fact]
        public void ResizeHoldsTheBottomEdgeSoTheStripStaysOnTheTaskbar()
        {
            // Top edge dragged up to y=900 with height 200; the clamp cuts the height back to 120.
            // Anchoring the origin would leave the bottom at 1020 — 80px above the taskbar it was
            // docked to. Holding the bottom keeps it at 1100.
            PlacementRect resized = PlacementMath.ResizeHoldingBottomRight(
                new PlacementRect(100, 900, 700, 200),
                700,
                120);

            Assert.Equal(1100, resized.Bottom);
            Assert.Equal(980, resized.Y);
        }

        [Fact]
        public void ResizeHoldsTheRightEdgeSoALeftEdgeDragDoesNotWalk()
        {
            // Left edge dragged 40px left: X becomes 60 and width 740. Restoring the derived width of
            // 700 anchored at X=60 would translate the strip 40px left, and again on every mouse step.
            PlacementRect resized = PlacementMath.ResizeHoldingBottomRight(
                new PlacementRect(60, 900, 740, 120),
                700,
                120);

            Assert.Equal(100, resized.X);
            Assert.Equal(800, resized.Right);
        }

        [Theory]
        [InlineData(1.0, 1.0, false)]
        [InlineData(1.0, 2.0, true)]
        [InlineData(2.0, 1.0, true)]
        [InlineData(1.5, 1.505, false)]
        public void ScaleReconcileTriggersOnlyOnARealDifference(double sizedAt, double live, bool expected)
        {
            bool needed = PlacementMath.NeedsScaleReconcile(sizedAt, live, 0.01);

            Assert.Equal(expected, needed);
        }
    }
}
