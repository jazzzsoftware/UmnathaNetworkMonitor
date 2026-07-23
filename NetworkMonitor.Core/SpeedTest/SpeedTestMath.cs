using System;
using System.Collections.Generic;

namespace NetworkMonitor.Core.SpeedTest
{
    public static class SpeedTestMath
    {
        public static double ToMbps(long bytes, TimeSpan elapsed)
        {
            double seconds = elapsed.TotalSeconds;
            double mbps = 0.0;

            if (seconds > 0.0)
            {
                mbps = bytes * 8.0 / seconds / 1_000_000.0;
            }

            return mbps;
        }

        public static double Mean(IReadOnlyList<double> samples)
        {
            double mean = 0.0;

            if (samples.Count > 0)
            {
                double total = 0.0;

                foreach (double sample in samples)
                {
                    total += sample;
                }

                mean = total / samples.Count;
            }

            return mean;
        }

        public static double Min(IReadOnlyList<double> samples)
        {
            double min = 0.0;

            if (samples.Count > 0)
            {
                min = samples[0];

                foreach (double sample in samples)
                {

                    if (sample < min)
                    {
                        min = sample;
                    }

                }

            }

            return min;
        }

        public static double Jitter(IReadOnlyList<double> samples)
        {
            double jitter = 0.0;

            if (samples.Count > 1)
            {
                double total = 0.0;

                for (int index = 1; index < samples.Count; index++)
                {
                    total += Math.Abs(samples[index] - samples[index - 1]);
                }

                jitter = total / (samples.Count - 1);
            }

            return jitter;
        }

        public static string ColoFromCfRay(string cfRay)
        {
            string colo = string.Empty;

            if (!string.IsNullOrEmpty(cfRay))
            {
                int dashIndex = cfRay.LastIndexOf('-');

                if (dashIndex >= 0 && dashIndex < cfRay.Length - 1)
                {
                    colo = cfRay.Substring(dashIndex + 1);
                }

            }

            return colo;
        }
    }
}
