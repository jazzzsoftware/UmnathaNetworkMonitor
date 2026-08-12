using Xunit;
using NetworkMonitor.Core.Widget;

namespace NetworkMonitor.Tests
{
    public class HorizontalStripMetricsTests
    {
        [Fact]
        public void FontScaleIsOneAtTheReferenceHeight()
        {
            double scale = HorizontalStripMetrics.FontScale(40.0);

            Assert.Equal(1.0, scale, 3);
        }

        // The vertical widget learned this the hard way: letting the scale fall below one made the
        // text illegible at small sizes rather than merely small. The strip keeps the same floor.
        [Fact]
        public void FontScaleNeverFallsBelowOne()
        {
            double scale = HorizontalStripMetrics.FontScale(28.0);

            Assert.Equal(1.0, scale, 3);
        }

        [Fact]
        public void FontScaleGrowsWithHeightAndCapsAtTwo()
        {
            double middle = HorizontalStripMetrics.FontScale(60.0);
            double capped = HorizontalStripMetrics.FontScale(400.0);

            Assert.Equal(1.5, middle, 3);
            Assert.Equal(2.0, capped, 3);
        }

        [Fact]
        public void ClampHeightHoldsTheStripBetweenItsBounds()
        {
            double low = HorizontalStripMetrics.ClampHeight(4.0);
            double high = HorizontalStripMetrics.ClampHeight(900.0);
            double inside = HorizontalStripMetrics.ClampHeight(55.0);

            Assert.Equal(HorizontalStripMetrics.MinimumHeight, low, 3);
            Assert.Equal(HorizontalStripMetrics.MaximumHeight, high, 3);
            Assert.Equal(55.0, inside, 3);
        }

        // 170 + 170 + 196 + 146 + 22 cells, four gaps of 4, padding of 4 either side.
        [Fact]
        public void WidthSumsEveryVisibleCellPlusGapsAndPadding()
        {
            double width = HorizontalStripMetrics.Width(true, true, true, true, 1.0);

            Assert.Equal(728.0, width, 3);
        }

        [Fact]
        public void TurningASectionOffNarrowsTheStrip()
        {
            double all = HorizontalStripMetrics.Width(true, true, true, true, 1.0);
            double withoutLocal = HorizontalStripMetrics.Width(true, false, true, true, 1.0);

            Assert.Equal(all - 170.0 - 4.0, withoutLocal, 3);
        }

        // The close column is not a section and cannot be switched off, so it is present even when
        // the state has been reduced to its single mandatory section.
        [Fact]
        public void TheCloseColumnIsAlwaysCounted()
        {
            double width = HorizontalStripMetrics.Width(true, false, false, false, 1.0);

            Assert.Equal(4.0 + 170.0 + 4.0 + 22.0 + 4.0, width, 3);
        }

        // Cells scale with the font but the gaps and padding do not, so width is not a flat multiple
        // of the scale. Getting this wrong leaves the text clipped at large heights.
        [Fact]
        public void CellsScaleWithTheFontButGapsAndPaddingDoNot()
        {
            double width = HorizontalStripMetrics.Width(true, false, false, false, 2.0);

            Assert.Equal(4.0 + 340.0 + 4.0 + 44.0 + 4.0, width, 3);
        }

        [Fact]
        public void ThePeakFigureIsDroppedOnlyBelowThirtyFour()
        {
            Assert.False(HorizontalStripMetrics.ShowsPeak(30.0));
            Assert.False(HorizontalStripMetrics.ShowsPeak(33.9));
            Assert.True(HorizontalStripMetrics.ShowsPeak(34.0));
            Assert.True(HorizontalStripMetrics.ShowsPeak(48.0));
        }

        // Pins the relationship that C2-7 got wrong in both directions. The clamp floors the PANEL
        // height, and every caller clamps before asking, so the drop threshold cannot be reached by
        // dragging — the peak is shown at every strip height the user can produce. Lower
        // MinimumHeight below 34 and this fails, which is the point: that would be a behaviour change.
        [Fact]
        public void ThePeakDropThresholdIsBelowTheHeightFloorAndSoCannotBeReached()
        {
            bool showsPeakAtTheFloor = HorizontalStripMetrics.ShowsPeak(HorizontalStripMetrics.ClampHeight(0.0));

            Assert.True(showsPeakAtTheFloor);
            Assert.True(HorizontalStripMetrics.ShowsPeak(HorizontalStripMetrics.MinimumHeight));
        }
    }
}
