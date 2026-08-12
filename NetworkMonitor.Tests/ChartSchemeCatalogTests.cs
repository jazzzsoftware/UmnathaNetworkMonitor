using System;
using System.Collections.Generic;
using System.Linq;
using NetworkMonitor.Core.Charting;
using Xunit;

namespace NetworkMonitor.Tests
{
    public class ChartSchemeCatalogTests
    {
        // OKLab Euclidean distance x100. 15 is the floor below which two colours are hard to tell
        // apart even with full colour vision. Contrast against the surface is a separate gate and
        // does not imply this one: Ocean once passed contrast on both surfaces while its two blues
        // collapsed to 10.9 on dark, because the band ceiling pulled one down while the contrast
        // floor pushed the other up.
        private const double MinimumSeparation = 15.0;

        public static IEnumerable<object[]> EverySharedChartPairAndSurface()
        {

            foreach (ChartSchemePreset preset in ChartSchemeCatalog.Presets)
            {
                yield return new object[] { preset.Id, ChartRole.Download, ChartRole.Upload, ChartSurface.Dark };
                yield return new object[] { preset.Id, ChartRole.Download, ChartRole.Upload, ChartSurface.Light };
                yield return new object[] { preset.Id, ChartRole.Latency, ChartRole.Jitter, ChartSurface.Dark };
                yield return new object[] { preset.Id, ChartRole.Latency, ChartRole.Jitter, ChartSurface.Light };
            }

        }

        [Theory]
        [MemberData(nameof(EverySharedChartPairAndSurface))]
        public void SeriesThatShareAChartStayDistinguishable(string presetId, ChartRole firstRole, ChartRole secondRole, ChartSurface surface)
        {
            ChartSchemePreset preset = ChartSchemeCatalog.Resolve(presetId);
            string firstHex = PaletteVariant.Derive(preset.Palette.ForRole(firstRole), surface);
            string secondHex = PaletteVariant.Derive(preset.Palette.ForRole(secondRole), surface);
            double separation = Separation(firstHex, secondHex);

            Assert.True(
                separation >= MinimumSeparation,
                $"{presetId} {firstRole}/{secondRole} on {surface} derived to {firstHex} and {secondHex}, only {separation:F1} apart");
        }

        public static IEnumerable<object[]> EveryPresetRoleAndSurface()
        {

            foreach (ChartSchemePreset preset in ChartSchemeCatalog.Presets)
            {

                foreach (ChartRole role in Enum.GetValues<ChartRole>())
                {
                    yield return new object[] { preset.Id, role, ChartSurface.Dark };
                    yield return new object[] { preset.Id, role, ChartSurface.Light };
                }

            }

        }

        [Theory]
        [MemberData(nameof(EveryPresetRoleAndSurface))]
        public void EveryPresetColourIsReadableOnBothSurfaces(string presetId, ChartRole role, ChartSurface surface)
        {
            ChartSchemePreset preset = ChartSchemeCatalog.Resolve(presetId);
            string derived = PaletteVariant.Derive(preset.Palette.ForRole(role), surface);
            double contrast = OklchColour.Contrast(derived, PaletteVariant.SurfaceHex(surface));

            Assert.True(
                contrast >= PaletteVariant.MinimumContrast,
                $"{presetId}/{role} on {surface} derived to {derived} at only {contrast:F2}:1");
        }

        [Fact]
        public void ThereAreFivePresetsInTheDocumentedOrder()
        {
            string[] result = ChartSchemeCatalog.Presets.Select(preset => preset.Id).ToArray();

            Assert.Equal(new[] { "classic", "horizon", "aurora", "ember", "ocean" }, result);
        }

        [Fact]
        public void ClassicShipsTodaysPaletteUnchanged()
        {
            ChartPalette result = ChartSchemeCatalog.Resolve("classic").Palette;

            Assert.Equal("#1976D2", result.Download);
            Assert.Equal("#AB47BC", result.Upload);
            Assert.Equal("#F57C00", result.Latency);
            Assert.Equal("#2E7D32", result.Jitter);
            Assert.Equal("#F57C00", result.Selection);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("nonsense")]
        [InlineData("custom")]
        public void AnUnknownSchemeIdFallsBackToClassic(string? schemeId)
        {
            ChartSchemePreset result = ChartSchemeCatalog.Resolve(schemeId);

            Assert.Equal("classic", result.Id);
        }

        [Fact]
        public void SchemeIdLookupIsCaseInsensitive()
        {
            ChartSchemePreset result = ChartSchemeCatalog.Resolve("AURORA");

            Assert.Equal("aurora", result.Id);
        }

        [Fact]
        public void SelectionNeverReusesTheDownloadOrUploadHueInAnyPreset()
        {

            foreach (ChartSchemePreset preset in ChartSchemeCatalog.Presets)
            {
                Assert.NotEqual(preset.Palette.Download, preset.Palette.Selection);
                Assert.NotEqual(preset.Palette.Upload, preset.Palette.Selection);
            }

        }

        [Fact]
        public void ForRoleReturnsTheMatchingSlot()
        {
            ChartPalette palette = new ChartPalette("#111111", "#222222", "#333333", "#444444", "#555555");

            Assert.Equal("#111111", palette.ForRole(ChartRole.Download));
            Assert.Equal("#222222", palette.ForRole(ChartRole.Upload));
            Assert.Equal("#333333", palette.ForRole(ChartRole.Latency));
            Assert.Equal("#444444", palette.ForRole(ChartRole.Jitter));
            Assert.Equal("#555555", palette.ForRole(ChartRole.Selection));
        }

        [Fact]
        public void WithRoleReplacesOnlyTheNamedSlot()
        {
            ChartPalette palette = new ChartPalette("#111111", "#222222", "#333333", "#444444", "#555555");

            ChartPalette result = palette.WithRole(ChartRole.Latency, "#ABCDEF");

            Assert.Equal("#ABCDEF", result.Latency);
            Assert.Equal("#111111", result.Download);
            Assert.Equal("#222222", result.Upload);
            Assert.Equal("#444444", result.Jitter);
            Assert.Equal("#555555", result.Selection);
        }

        private static double Separation(string oneHex, string otherHex)
        {
            Oklch first = OklchColour.ToOklch(oneHex);
            Oklch second = OklchColour.ToOklch(otherHex);

            double lightnessGap = first.Lightness - second.Lightness;
            double aGap = Math.Cos(first.Hue) * first.Chroma - Math.Cos(second.Hue) * second.Chroma;
            double bGap = Math.Sin(first.Hue) * first.Chroma - Math.Sin(second.Hue) * second.Chroma;
            double result = Math.Sqrt(lightnessGap * lightnessGap + aGap * aGap + bGap * bGap) * 100.0;

            return result;
        }

    }
}
