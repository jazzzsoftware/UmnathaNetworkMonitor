using System;

namespace NetworkMonitor.Core.Charting
{
    public static class AxisScale
    {
        // Charts draw a gridline at the maximum and another at half of it, so every step on this
        // ladder has to halve cleanly: 1 → 0.5, 2 → 1, 5 → 2.5, 10 → 5.
        private static readonly double[] Ladder = { 1.0, 2.0, 5.0, 10.0 };

        public static double NiceMax(double value)
        {
            double result;

            if (value <= 0.0 || double.IsNaN(value) || double.IsInfinity(value))
            {
                result = 0.0;
            }
            else
            {
                double magnitude = Math.Pow(10.0, Math.Floor(Math.Log10(value)));
                double normalized = value / magnitude;
                double niceNormalized = Ladder[Ladder.Length - 1];

                foreach (double step in Ladder)
                {

                    if (normalized <= step)
                    {
                        niceNormalized = step;

                        break;
                    }

                }

                result = niceNormalized * magnitude;
            }

            return result;
        }
    }
}
