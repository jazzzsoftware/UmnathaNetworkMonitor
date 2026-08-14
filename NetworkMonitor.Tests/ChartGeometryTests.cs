using System.Numerics;
using Xunit;
using NetworkMonitor.Core.Charting;

namespace NetworkMonitor.Tests
{
    public class ChartGeometryTests
    {
        private static (double[] TimeEpoch, double[] Values) Series(int count, double bucketSeconds, double value)
        {
            double[] timeEpoch = new double[count];
            double[] values = new double[count];

            for (int index = 0; index < count; index++)
            {
                timeEpoch[index] = index * bucketSeconds;
                values[index] = value;
            }

            (double[] TimeEpoch, double[] Values) series = (timeEpoch, values);

            return series;
        }

        // The full-page charts hold 60 buckets in roughly 600 pixels. Nothing shares a column there,
        // so the fold must be a no-op — it exists for the widget, not for them.
        [Fact]
        public void KeepsEveryPointWhenTheyAreFurtherApartThanAPixel()
        {
            (double[] timeEpoch, double[] values) = Series(60, 5.0, 1000.0);
            Vector2[] points = new Vector2[ChartGeometry.RequiredCapacity(60, false)];

            int written = ChartGeometry.BuildPoints(points, timeEpoch, values, 60, 0.0, 295.0, 600.0, 100.0, 90.0, 2000.0, false, 0.0);

            Assert.Equal(60, written);
        }

        // The widget's worst case: 300 one-second buckets into the 170 DIP strip cell.
        [Fact]
        public void FoldsToAboutOnePointPerPixelColumn()
        {
            (double[] timeEpoch, double[] values) = Series(300, 1.0, 1000.0);
            Vector2[] points = new Vector2[ChartGeometry.RequiredCapacity(300, false)];

            int written = ChartGeometry.BuildPoints(points, timeEpoch, values, 300, 0.0, 300.0, 170.0, 100.0, 90.0, 2000.0, false, 0.0);

            Assert.True(written <= 171, $"expected at most one point per column, got {written}");
            Assert.True(written >= 169, $"expected the trace to still span the width, got {written}");
        }

        // Averaging a column would flatten a one-second burst into the noise around it. The maximum
        // is what a traffic chart is read for, so it is the sample that survives.
        [Fact]
        public void KeepsTheColumnMaximumSoSpikesSurvive()
        {
            (double[] timeEpoch, double[] values) = Series(300, 1.0, 100.0);
            values[151] = 100000.0;
            Vector2[] points = new Vector2[ChartGeometry.RequiredCapacity(300, false)];

            int written = ChartGeometry.BuildPoints(points, timeEpoch, values, 300, 0.0, 300.0, 170.0, 100.0, 90.0, 100000.0, false, 0.0);

            float highest = float.MaxValue;
            float spikeX = 0f;

            for (int index = 0; index < written; index++)
            {

                if (points[index].Y < highest)
                {
                    highest = points[index].Y;
                    spikeX = points[index].X;
                }

            }

            Assert.Equal(10.0f, highest, 3);
            Assert.Equal(85.57f, spikeX, 1);
        }

        // A fold that reordered or duplicated x would put a kink in the bezier chain built from it.
        [Fact]
        public void LeavesTheFoldedPointsInAscendingX()
        {
            (double[] timeEpoch, double[] values) = Series(300, 1.0, 100.0);

            for (int index = 0; index < 300; index++)
            {
                values[index] = index % 7 * 500.0;
            }

            Vector2[] points = new Vector2[ChartGeometry.RequiredCapacity(300, true)];

            int written = ChartGeometry.BuildPoints(points, timeEpoch, values, 300, 0.0, 300.0, 170.0, 100.0, 90.0, 3000.0, true, 300.0);

            for (int index = 1; index < written; index++)
            {
                Assert.True(points[index].X > points[index - 1].X, $"x went backwards at {index}");
            }

        }

        // The live lead reads one bucket back, and it reads the SOURCE series rather than the folded
        // points — otherwise the rightmost value would be a column maximum rather than a bucket.
        [Fact]
        public void AppendsTheLiveLeadFromTheLastCompleteBucket()
        {
            (double[] timeEpoch, double[] values) = Series(300, 1.0, 100.0);
            values[298] = 1000.0;
            values[299] = 50.0;
            Vector2[] points = new Vector2[ChartGeometry.RequiredCapacity(300, true)];

            int written = ChartGeometry.BuildPoints(points, timeEpoch, values, 300, 0.0, 300.0, 170.0, 100.0, 90.0, 2000.0, true, 300.0);

            Vector2 lead = points[written - 1];

            Assert.Equal(170.0f, lead.X, 3);
            Assert.Equal(55.0f, lead.Y, 3);
        }

        [Fact]
        public void WritesNothingForAnEmptySeries()
        {
            Vector2[] points = new Vector2[ChartGeometry.RequiredCapacity(0, true)];

            int written = ChartGeometry.BuildPoints(points, [], [], 0, 0.0, 300.0, 170.0, 100.0, 90.0, 2000.0, true, 300.0);

            Assert.Equal(0, written);
        }

        [Fact]
        public void CapacityLeavesRoomForTheLiveLead()
        {
            int live = ChartGeometry.RequiredCapacity(300, true);
            int paused = ChartGeometry.RequiredCapacity(300, false);

            Assert.Equal(301, live);
            Assert.Equal(300, paused);
        }
    }
}
