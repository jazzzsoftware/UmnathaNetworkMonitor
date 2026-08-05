using NetworkMonitor.Core.Widget;
using NetworkMonitor.Models.Widget;
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
            set => ApplySection(_settings.MiniGraphShowInternet, value, () => _settings.MiniGraphShowInternet = value);
        }

        public bool ShowLocal
        {
            get => _settings.MiniGraphShowLocal;
            set => ApplySection(_settings.MiniGraphShowLocal, value, () => _settings.MiniGraphShowLocal = value);
        }

        public bool ShowSpeedTest
        {
            get => _settings.MiniGraphShowSpeedTest;
            set => ApplySection(_settings.MiniGraphShowSpeedTest, value, () => _settings.MiniGraphShowSpeedTest = value);
        }

        public bool ShowUnknownDevices
        {
            get => _settings.MiniGraphShowUnknownDevices;
            set => ApplySection(_settings.MiniGraphShowUnknownDevices, value, () => _settings.MiniGraphShowUnknownDevices = value);
        }

        public int VisibleSectionCount
        {
            get
            {
                int count = 0;

                if (ShowInternet)
                {
                    count++;
                }

                if (ShowLocal)
                {
                    count++;
                }

                if (ShowSpeedTest)
                {
                    count++;
                }

                if (ShowUnknownDevices)
                {
                    count++;
                }

                return count;
            }
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

        public MiniGraphOrientation Orientation
        {
            get => _settings.MiniGraphHorizontal ? MiniGraphOrientation.Horizontal : MiniGraphOrientation.Vertical;
            set
            {
                bool horizontal = value == MiniGraphOrientation.Horizontal;

                Apply(_settings.MiniGraphHorizontal != horizontal, () => _settings.MiniGraphHorizontal = horizontal);
            }
        }

        public bool IsHorizontal => _settings.MiniGraphHorizontal;

        public bool ShowBorder
        {
            get => _settings.MiniGraphShowBorder;
            set => Apply(_settings.MiniGraphShowBorder != value, () => _settings.MiniGraphShowBorder = value);
        }

        public bool HasAnySection => ShowInternet || ShowLocal || ShowSpeedTest || ShowUnknownDevices;

        public void SavePlacement(int positionX, int positionY, int width, int height)
        {
            _settings.MiniGraphX = positionX;
            _settings.MiniGraphY = positionY;
            _settings.MiniGraphWidth = width;
            _settings.MiniGraphHeight = height;
            _settings.Save();
        }

        // The strip and the floating widget keep separate positions. Sharing one would drop a 700-wide
        // strip at the floating widget's coordinates on every orientation change, and the user would
        // have to reposition it each time.
        public void SaveStripPlacement(int positionX, int positionY, int height)
        {
            _settings.MiniGraphStripX = positionX;
            _settings.MiniGraphStripY = positionY;
            _settings.MiniGraphStripHeight = (int)Math.Round(HorizontalStripMetrics.ClampHeight(height));
            _settings.Save();
        }

        // The last section cannot be turned off. An empty widget is a bare rectangle floating on the
        // desktop with nothing in it to say what it is, and the only way back is a right-click menu the
        // user has no reason to look for. This sits in the state rather than in the menus so the tray,
        // the widget's own menu and the Settings checkboxes all obey the same rule.
        private void ApplySection(bool current, bool value, Action assign)
        {
            bool wouldEmptyTheWidget = !value && VisibleSectionCount <= 1;

            Apply(current != value && !wouldEmptyTheWidget, assign);
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
