using System;

namespace NetworkMonitor.Core.Charting
{
    public record ChartPalette(
        string Download,
        string Upload,
        string Latency,
        string Jitter,
        string Selection)
    {
        public string ForRole(ChartRole role)
        {
            string result = role switch
            {
                ChartRole.Download => Download,
                ChartRole.Upload => Upload,
                ChartRole.Latency => Latency,
                ChartRole.Jitter => Jitter,
                ChartRole.Selection => Selection,
                _ => Download
            };

            return result;
        }

        public ChartPalette WithRole(ChartRole role, string hex)
        {
            ChartPalette result = role switch
            {
                ChartRole.Download => this with { Download = hex },
                ChartRole.Upload => this with { Upload = hex },
                ChartRole.Latency => this with { Latency = hex },
                ChartRole.Jitter => this with { Jitter = hex },
                ChartRole.Selection => this with { Selection = hex },
                _ => this
            };

            return result;
        }
    }
}
