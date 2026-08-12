using System;
using System.Collections.Generic;
using System.Globalization;
using NetworkMonitor.Core.Charting;
using NetworkMonitor.Services.Data;
using Windows.UI;

namespace NetworkMonitor.Services.Charting
{
    public class ChartPaletteService
    {
        private readonly Settings _settings;
        private readonly Dictionary<ChartRole, Color> _colours = new Dictionary<ChartRole, Color>();
        private readonly Dictionary<ChartRole, string> _hexes = new Dictionary<ChartRole, string>();
        private ChartSurface _surface = ChartSurface.Dark;

        public event EventHandler? PaletteChanged;

        public ChartPaletteService(Settings settings)
        {
            _settings = settings;
            Recompute();
        }

        public string SchemeId => _settings.ChartSchemeId;

        public bool IsCustom => string.Equals(
            _settings.ChartSchemeId,
            ChartSchemeCatalog.CustomSchemeId,
            StringComparison.OrdinalIgnoreCase);

        public Color Resolve(ChartRole role)
        {
            Color result = _colours[role];

            return result;
        }

        public string ResolveHex(ChartRole role)
        {
            string result = _hexes[role];

            return result;
        }

        public ChartPalette CurrentBasePalette()
        {
            ChartPalette result;

            if (IsCustom)
            {
                result = new ChartPalette(
                    _settings.ChartCustomDownload,
                    _settings.ChartCustomUpload,
                    _settings.ChartCustomLatency,
                    _settings.ChartCustomJitter,
                    _settings.ChartCustomSelection);
            }
            else
            {
                result = ChartSchemeCatalog.Resolve(_settings.ChartSchemeId).Palette;
            }

            return result;
        }

        public void SetSurface(ChartSurface surface)
        {

            if (_surface != surface)
            {
                _surface = surface;
                Recompute();
                PaletteChanged?.Invoke(this, EventArgs.Empty);
            }

        }

        public void ApplyScheme(string schemeId)
        {
            _settings.ChartSchemeId = schemeId;
            _settings.Save();
            Recompute();
            PaletteChanged?.Invoke(this, EventArgs.Empty);
        }

        public void ApplyCustomColour(ChartRole role, string baseHex)
        {

            switch (role)
            {
                case ChartRole.Download:
                    _settings.ChartCustomDownload = baseHex;
                    break;
                case ChartRole.Upload:
                    _settings.ChartCustomUpload = baseHex;
                    break;
                case ChartRole.Latency:
                    _settings.ChartCustomLatency = baseHex;
                    break;
                case ChartRole.Jitter:
                    _settings.ChartCustomJitter = baseHex;
                    break;
                case ChartRole.Selection:
                    _settings.ChartCustomSelection = baseHex;
                    break;
            }

            _settings.Save();
            Recompute();
            PaletteChanged?.Invoke(this, EventArgs.Empty);
        }

        public void ResetToDefault()
        {
            ApplyScheme(ChartSchemeCatalog.DefaultSchemeId);
        }

        private void Recompute()
        {
            ChartPalette basePalette = CurrentBasePalette();

            foreach (ChartRole role in Enum.GetValues<ChartRole>())
            {
                string derived = PaletteVariant.Derive(basePalette.ForRole(role), _surface);
                _hexes[role] = derived;
                _colours[role] = ToColor(derived);
            }

        }

        private static Color ToColor(string hex)
        {
            string clean = hex.StartsWith("#", StringComparison.Ordinal) ? hex.Substring(1) : hex;
            byte red = byte.Parse(clean.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            byte green = byte.Parse(clean.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            byte blue = byte.Parse(clean.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            Color result = Color.FromArgb(0xFF, red, green, blue);

            return result;
        }
    }
}
