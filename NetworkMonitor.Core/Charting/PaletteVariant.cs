using System;

namespace NetworkMonitor.Core.Charting
{
    public static class PaletteVariant
    {
        public const string DarkSurfaceHex = "#2D2D2D";
        public const string LightSurfaceHex = "#FBFBFB";
        public const double MinimumContrast = 3.0;

        private const double DarkBandMinimum = 0.48;
        private const double DarkBandMaximum = 0.67;
        private const double LightBandMinimum = 0.43;
        private const double LightBandMaximum = 0.77;
        private const double LightnessStep = 0.02;

        public static string SurfaceHex(ChartSurface surface)
        {
            string result = surface == ChartSurface.Dark ? DarkSurfaceHex : LightSurfaceHex;

            return result;
        }

        public static string Derive(string baseHex, ChartSurface surface)
        {
            Oklch source = OklchColour.ToOklch(baseHex);
            double minimum = surface == ChartSurface.Dark ? DarkBandMinimum : LightBandMinimum;
            double maximum = surface == ChartSurface.Dark ? DarkBandMaximum : LightBandMaximum;
            double step = surface == ChartSurface.Dark ? LightnessStep : -LightnessStep;
            string surfaceHex = SurfaceHex(surface);

            double lightness = Math.Clamp(source.Lightness, minimum, maximum);
            string candidate = OklchColour.ToHex(new Oklch(lightness, source.Chroma, source.Hue));

            while (OklchColour.Contrast(candidate, surfaceHex) < MinimumContrast)
            {
                double next = lightness + step;

                if (next < minimum || next > maximum)
                {
                    break;
                }

                lightness = next;
                candidate = OklchColour.ToHex(new Oklch(lightness, source.Chroma, source.Hue));
            }

            return candidate;
        }
    }
}
