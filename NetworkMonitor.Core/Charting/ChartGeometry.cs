using System.Numerics;

namespace NetworkMonitor.Core.Charting
{
    // Turns a traffic series into the screen-space points a chart draws, collapsing anything finer
    // than one horizontal pixel on the way through.
    //
    // The widget carries 300 one-second buckets and draws them into a cell as narrow as 170 DIP —
    // 1.8 buckets per pixel, every one of them costing a cubic bezier segment in two paths per
    // series, rebuilt every frame. Detail below a pixel cannot be seen, so it is not drawn.
    //
    // The fold keeps the LARGEST value in each pixel column rather than the first or an average: a
    // one-second spike is the whole point of a live traffic chart, and averaging is exactly what
    // would flatten it. The column's x comes from the sample that won it, so a spike stays where it
    // happened rather than snapping to the column edge.
    public static class ChartGeometry
    {
        public static int RequiredCapacity(int count, bool isLive)
        {
            int capacity = isLive ? count + 1 : count;

            return capacity;
        }

        public static int BuildPoints(
            Vector2[] points,
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
            int written = 0;
            int currentColumn = 0;

            for (int index = 0; index < count; index++)
            {
                float xValue = (float)((timeEpoch[index] - leftEdge) / span * width);
                float yValue = (float)(height - values[index] / safeMax * usableHeight);
                int column = (int)Math.Floor(xValue);

                if (written == 0 || column != currentColumn)
                {
                    points[written] = new Vector2(xValue, yValue);
                    written++;
                    currentColumn = column;
                }
                else if (yValue < points[written - 1].Y)
                {
                    points[written - 1] = new Vector2(xValue, yValue);
                }

            }

            if (isLive && count > 0)
            {

                // Extend from the last COMPLETE bucket, not the newest one. The newest only ever
                // holds the fraction of a second the most recent flush actually covered, so during a
                // sustained transfer the rightmost slice of the trace sat at roughly half the true
                // rate until the next flush topped it up — a permanent dip at the exact point the eye
                // goes to. Reading one bucket back costs nothing and the value is whole.
                //
                // This is taken from the source series, not from the folded points, so the lead is
                // one bucket's value either way rather than a column maximum.
                int leadSource = count > 1 ? count - 2 : count - 1;

                float leadX = (float)((nowEpoch - leftEdge) / span * width);
                float leadY = (float)(height - values[leadSource] / safeMax * usableHeight);
                points[written] = new Vector2(leadX, leadY);
                written++;
            }

            return written;
        }
    }
}
