using System;
using System.Collections.Generic;
using System.Linq;

namespace NetworkMonitor.Core.Charting
{
    public static class ChartSchemeCatalog
    {
        public const string DefaultSchemeId = "classic";
        public const string CustomSchemeId = "custom";

        private static readonly IReadOnlyList<ChartSchemePreset> _presets = new List<ChartSchemePreset>
        {
            new ChartSchemePreset(
                "classic",
                "Classic",
                new ChartPalette("#1976D2", "#AB47BC", "#F57C00", "#2E7D32", "#F57C00")),
            new ChartSchemePreset(
                "horizon",
                "Horizon",
                new ChartPalette("#2A78D6", "#EB6834", "#EDA100", "#1BAF7A", "#E87BA4")),
            new ChartSchemePreset(
                "aurora",
                "Aurora",
                new ChartPalette("#1BAF7A", "#7C5CDB", "#EDA100", "#2A78D6", "#EB6834")),
            new ChartSchemePreset(
                "ember",
                "Ember",
                new ChartPalette("#E34948", "#EDA100", "#7C5CDB", "#1BAF7A", "#2A78D6")),
            new ChartSchemePreset(
                "ocean",
                "Ocean",
                new ChartPalette("#6EA8E8", "#1C5FA8", "#EDA100", "#1BAF7A", "#EB6834"))
        };

        public static IReadOnlyList<ChartSchemePreset> Presets => _presets;

        public static ChartSchemePreset Resolve(string? schemeId)
        {
            ChartSchemePreset? match = null;

            if (!string.IsNullOrWhiteSpace(schemeId))
            {
                match = _presets.FirstOrDefault(preset =>
                    string.Equals(preset.Id, schemeId.Trim(), StringComparison.OrdinalIgnoreCase));
            }

            ChartSchemePreset result = match ?? _presets[0];

            return result;
        }
    }
}
