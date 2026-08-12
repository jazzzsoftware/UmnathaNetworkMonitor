using System;
using NetworkMonitor.Core.Charting;
using Xunit;

namespace NetworkMonitor.Tests
{
    public class PaletteVariantTests
    {
        [Theory]
        [InlineData("#1976D2", ChartSurface.Dark)]
        [InlineData("#1976D2", ChartSurface.Light)]
        [InlineData("#EDA100", ChartSurface.Dark)]
        [InlineData("#EDA100", ChartSurface.Light)]
        [InlineData("#1C5FA8", ChartSurface.Dark)]
        [InlineData("#000000", ChartSurface.Dark)]
        [InlineData("#FFFFFF", ChartSurface.Light)]
        public void DerivedColourClearsMinimumContrastAgainstItsSurface(string baseHex, ChartSurface surface)
        {
            string derived = PaletteVariant.Derive(baseHex, surface);
            double contrast = OklchColour.Contrast(derived, PaletteVariant.SurfaceHex(surface));

            Assert.True(
                contrast >= PaletteVariant.MinimumContrast,
                $"{baseHex} on {surface} derived to {derived} at {contrast:F2}:1");
        }

        [Theory]
        [InlineData("#1976D2", ChartSurface.Dark, 0.48, 0.67)]
        [InlineData("#1976D2", ChartSurface.Light, 0.43, 0.77)]
        [InlineData("#EDA100", ChartSurface.Dark, 0.48, 0.67)]
        [InlineData("#EDA100", ChartSurface.Light, 0.43, 0.77)]
        public void DerivedLightnessLandsInsideTheSurfaceBand(string baseHex, ChartSurface surface, double minimum, double maximum)
        {
            string derived = PaletteVariant.Derive(baseHex, surface);
            double lightness = OklchColour.ToOklch(derived).Lightness;

            Assert.True(lightness >= minimum - 0.01, $"{derived} L={lightness:F3} below {minimum}");
            Assert.True(lightness <= maximum + 0.01, $"{derived} L={lightness:F3} above {maximum}");
        }

        [Theory]
        [InlineData("#1976D2")]
        [InlineData("#AB47BC")]
        [InlineData("#EB6834")]
        [InlineData("#1BAF7A")]
        public void DerivationHoldsTheHue(string baseHex)
        {
            double sourceHue = OklchColour.ToOklch(baseHex).Hue;
            double darkHue = OklchColour.ToOklch(PaletteVariant.Derive(baseHex, ChartSurface.Dark)).Hue;
            double lightHue = OklchColour.ToOklch(PaletteVariant.Derive(baseHex, ChartSurface.Light)).Hue;

            Assert.True(Math.Abs(sourceHue - darkHue) < 0.05, $"dark hue drifted from {sourceHue:F3} to {darkHue:F3}");
            Assert.True(Math.Abs(sourceHue - lightHue) < 0.05, $"light hue drifted from {sourceHue:F3} to {lightHue:F3}");
        }

        [Fact]
        public void AmberIsDarkenedForBothSurfaces()
        {
            double source = OklchColour.ToOklch("#EDA100").Lightness;
            double onLight = OklchColour.ToOklch(PaletteVariant.Derive("#EDA100", ChartSurface.Light)).Lightness;
            double onDark = OklchColour.ToOklch(PaletteVariant.Derive("#EDA100", ChartSurface.Dark)).Lightness;

            Assert.True(onLight < source);
            Assert.True(onDark < source);
        }

        [Fact]
        public void DerivationIsDeterministic()
        {
            string first = PaletteVariant.Derive("#7C5CDB", ChartSurface.Dark);
            string second = PaletteVariant.Derive("#7C5CDB", ChartSurface.Dark);

            Assert.Equal(first, second);
        }

        [Fact]
        public void DerivedValueIsAlwaysAParseableSixDigitHex()
        {
            string derived = PaletteVariant.Derive("#E34948", ChartSurface.Light);

            Assert.Equal(7, derived.Length);
            Assert.StartsWith("#", derived);
            Assert.Equal(derived, OklchColour.ToHex(OklchColour.ToOklch(derived)));
        }

        [Fact]
        public void SurfaceHexMatchesTheDocumentedCardColours()
        {
            Assert.Equal("#2D2D2D", PaletteVariant.SurfaceHex(ChartSurface.Dark));
            Assert.Equal("#FBFBFB", PaletteVariant.SurfaceHex(ChartSurface.Light));
        }
    }
}
