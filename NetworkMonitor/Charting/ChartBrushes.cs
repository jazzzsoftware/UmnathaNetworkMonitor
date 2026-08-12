using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using NetworkMonitor.Core.Charting;
using NetworkMonitor.Services.Charting;

namespace NetworkMonitor.Charting
{
    public static class ChartBrushes
    {
        private static readonly Dictionary<ChartRole, string> _resourceKeys = new Dictionary<ChartRole, string>
        {
            { ChartRole.Download, "ChartDownloadBrush" },
            { ChartRole.Upload, "ChartUploadBrush" },
            { ChartRole.Latency, "ChartLatencyBrush" },
            { ChartRole.Jitter, "ChartJitterBrush" },
            { ChartRole.Selection, "ChartSelectionBrush" }
        };

        private static ChartPaletteService? _palette;

        public static void Attach(ChartPaletteService palette)
        {

            if (_palette is not null)
            {
                _palette.PaletteChanged -= OnPaletteChanged;
            }

            _palette = palette;
            _palette.PaletteChanged += OnPaletteChanged;
            Apply(palette);
        }

        private static void OnPaletteChanged(object? sender, EventArgs args)
        {

            if (_palette is not null)
            {
                Apply(_palette);
            }

        }

        private static void Apply(ChartPaletteService palette)
        {

            foreach (KeyValuePair<ChartRole, string> entry in _resourceKeys)
            {

                if (Application.Current.Resources.TryGetValue(entry.Value, out object? resource)
                    && resource is SolidColorBrush brush)
                {
                    brush.Color = palette.Resolve(entry.Key);
                }

            }

        }
    }
}
