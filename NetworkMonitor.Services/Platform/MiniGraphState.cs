using NetworkMonitor.Services.Data;

namespace NetworkMonitor.Services.Platform
{
    // The tray menu, the Traffic toolbar and the Settings page all drive the same booleans, so they
    // share one writer rather than each poking Settings and hoping the others notice.
    public sealed class MiniGraphState(Settings settings)
    {
        private const int MinimumOpacity = 50;
        private const int MaximumOpacity = 100;

        private readonly Settings _settings = settings;

        public event EventHandler? Changed;

        public bool IsVisible
        {
            get => _settings.ShowMiniGraph;
            set => Apply(_settings.ShowMiniGraph != value, () => _settings.ShowMiniGraph = value);
        }

        public bool ShowInternet
        {
            get => _settings.MiniGraphShowInternet;
            set => Apply(_settings.MiniGraphShowInternet != value, () => _settings.MiniGraphShowInternet = value);
        }

        public bool ShowLocal
        {
            get => _settings.MiniGraphShowLocal;
            set => Apply(_settings.MiniGraphShowLocal != value, () => _settings.MiniGraphShowLocal = value);
        }

        public bool ShowSpeedTest
        {
            get => _settings.MiniGraphShowSpeedTest;
            set => Apply(_settings.MiniGraphShowSpeedTest != value, () => _settings.MiniGraphShowSpeedTest = value);
        }

        public bool ShowUnknownDevices
        {
            get => _settings.MiniGraphShowUnknownDevices;
            set => Apply(_settings.MiniGraphShowUnknownDevices != value, () => _settings.MiniGraphShowUnknownDevices = value);
        }

        public int Opacity
        {
            get => Math.Clamp(_settings.MiniGraphOpacity, MinimumOpacity, MaximumOpacity);
            set
            {
                int clamped = Math.Clamp(value, MinimumOpacity, MaximumOpacity);

                Apply(_settings.MiniGraphOpacity != clamped, () => _settings.MiniGraphOpacity = clamped);
            }
        }

        public bool HasAnySection => ShowInternet || ShowLocal || ShowSpeedTest || ShowUnknownDevices;

        public void SavePlacement(int x, int y, int width, int height)
        {
            _settings.MiniGraphX = x;
            _settings.MiniGraphY = y;
            _settings.MiniGraphWidth = width;
            _settings.MiniGraphHeight = height;
            _settings.Save();
        }

        private void Apply(bool changed, Action assign)
        {

            if (changed)
            {
                assign();
                _settings.Save();
                Changed?.Invoke(this, EventArgs.Empty);
            }

        }
    }
}
