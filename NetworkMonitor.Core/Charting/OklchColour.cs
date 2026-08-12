using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace NetworkMonitor.Core.Charting
{
    public static class OklchColour
    {
        private const double ChromaReductionStep = 0.005;
        private const double GamutTolerance = 0.0005;
        private const string HexDigitsPattern = "^[0-9A-Fa-f]{6}$";

        public static bool TryParseHex(string? hex, out string normalisedHex)
        {
            bool isValid = false;
            string candidate = string.Empty;

            if (!string.IsNullOrWhiteSpace(hex))
            {
                string trimmed = hex.Trim();
                string body = trimmed.StartsWith("#", StringComparison.Ordinal) ? trimmed.Substring(1) : trimmed;

                if (Regex.IsMatch(body, HexDigitsPattern))
                {
                    candidate = "#" + body.ToUpperInvariant();
                    isValid = true;
                }

            }

            normalisedHex = candidate;

            return isValid;
        }

        public static Oklch ToOklch(string hex)
        {
            (double red, double green, double blue) = ToLinearRgb(hex);

            double longCone = Math.Cbrt(0.4122214708 * red + 0.5363325363 * green + 0.0514459929 * blue);
            double mediumCone = Math.Cbrt(0.2119034982 * red + 0.6806995451 * green + 0.1073969566 * blue);
            double shortCone = Math.Cbrt(0.0883024619 * red + 0.2817188376 * green + 0.6299787005 * blue);

            double lightness = 0.2104542553 * longCone + 0.7936177850 * mediumCone - 0.0040720468 * shortCone;
            double aAxis = 1.9779984951 * longCone - 2.4285922050 * mediumCone + 0.4505937099 * shortCone;
            double bAxis = 0.0259040371 * longCone + 0.7827717662 * mediumCone - 0.8086757660 * shortCone;

            Oklch result = new Oklch(lightness, Math.Sqrt(aAxis * aAxis + bAxis * bAxis), Math.Atan2(bAxis, aAxis));

            return result;
        }

        public static string ToHex(Oklch value)
        {
            double chroma = Math.Max(0.0, value.Chroma);

            while (chroma > 0.0 && !IsInGamut(value.Lightness, chroma, value.Hue))
            {
                chroma = Math.Max(0.0, chroma - ChromaReductionStep);
            }

            (double red, double green, double blue) = ToLinearRgb(value.Lightness, chroma, value.Hue);
            string result = FromLinearRgb(red, green, blue);

            return result;
        }

        public static double Contrast(string oneHex, string otherHex)
        {
            double first = RelativeLuminance(oneHex);
            double second = RelativeLuminance(otherHex);
            double lighter = Math.Max(first, second);
            double darker = Math.Min(first, second);
            double result = (lighter + 0.05) / (darker + 0.05);

            return result;
        }

        private static (double Red, double Green, double Blue) ToLinearRgb(string hex)
        {
            string clean = hex.StartsWith("#", StringComparison.Ordinal) ? hex.Substring(1) : hex;

            double red = ToLinearChannel(byte.Parse(clean.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0);
            double green = ToLinearChannel(byte.Parse(clean.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0);
            double blue = ToLinearChannel(byte.Parse(clean.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0);

            return (red, green, blue);
        }

        private static (double Red, double Green, double Blue) ToLinearRgb(double lightness, double chroma, double hue)
        {
            double aAxis = Math.Cos(hue) * chroma;
            double bAxis = Math.Sin(hue) * chroma;

            double longCone = Math.Pow(lightness + 0.3963377774 * aAxis + 0.2158037573 * bAxis, 3.0);
            double mediumCone = Math.Pow(lightness - 0.1055613458 * aAxis - 0.0638541728 * bAxis, 3.0);
            double shortCone = Math.Pow(lightness - 0.0894841775 * aAxis - 1.2914855480 * bAxis, 3.0);

            double red = 4.0767416621 * longCone - 3.3077115913 * mediumCone + 0.2309699292 * shortCone;
            double green = -1.2684380046 * longCone + 2.6097574011 * mediumCone - 0.3413193965 * shortCone;
            double blue = -0.0041960863 * longCone - 0.7034186147 * mediumCone + 1.7076147010 * shortCone;

            return (red, green, blue);
        }

        private static bool IsInGamut(double lightness, double chroma, double hue)
        {
            (double red, double green, double blue) = ToLinearRgb(lightness, chroma, hue);
            bool result = IsChannelInGamut(red) && IsChannelInGamut(green) && IsChannelInGamut(blue);

            return result;
        }

        private static bool IsChannelInGamut(double channel)
        {
            bool result = channel >= -GamutTolerance && channel <= 1.0 + GamutTolerance;

            return result;
        }

        private static string FromLinearRgb(double red, double green, double blue)
        {
            int redByte = ToByte(red);
            int greenByte = ToByte(green);
            int blueByte = ToByte(blue);
            string result = string.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}", redByte, greenByte, blueByte);

            return result;
        }

        private static int ToByte(double linearChannel)
        {
            double gamma = ToGammaChannel(linearChannel);
            int result = (int)Math.Round(Math.Clamp(gamma, 0.0, 1.0) * 255.0);

            return result;
        }

        private static double ToLinearChannel(double channel)
        {
            double result = channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);

            return result;
        }

        private static double ToGammaChannel(double channel)
        {
            double clamped = Math.Clamp(channel, 0.0, 1.0);
            double result = clamped <= 0.0031308 ? clamped * 12.92 : 1.055 * Math.Pow(clamped, 1.0 / 2.4) - 0.055;

            return result;
        }

        private static double RelativeLuminance(string hex)
        {
            (double red, double green, double blue) = ToLinearRgb(hex);
            double result = 0.2126 * red + 0.7152 * green + 0.0722 * blue;

            return result;
        }
    }
}
