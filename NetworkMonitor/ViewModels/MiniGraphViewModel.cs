using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using NetworkMonitor.Models.Charting;
using NetworkMonitor.Models.Formatting;
using NetworkMonitor.Services.Data;
using NetworkMonitor.Services.Platform;
using NetworkMonitor.Services.Traffic;

namespace NetworkMonitor.ViewModels
{
    public sealed class MiniGraphViewModel : ObservableObject
    {
        private readonly LiveTrafficFeed _feed;
        private readonly MiniGraphState _state;
        private readonly Settings _settings;
        private readonly DispatcherQueue _dispatcherQueue;
        private bool _attached;

        public MiniGraphViewModel(LiveTrafficFeed feed, MiniGraphState state, Settings settings)
        {
            _feed = feed;
            _state = state;
            _settings = settings;
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            _state.Changed += OnStateChanged;
        }

        private IReadOnlyList<ChartPoint>? _internetPoints;

        public IReadOnlyList<ChartPoint>? InternetPoints
        {
            get => _internetPoints;
            private set => SetProperty(ref _internetPoints, value);
        }

        private IReadOnlyList<ChartPoint>? _localPoints;

        public IReadOnlyList<ChartPoint>? LocalPoints
        {
            get => _localPoints;
            private set => SetProperty(ref _localPoints, value);
        }

        private string _speedTestText = "No speed test yet";

        public string SpeedTestText
        {
            get => _speedTestText;
            private set => SetProperty(ref _speedTestText, value);
        }

        private string _speedTestShortText = "not run yet";

        public string SpeedTestShortText
        {
            get => _speedTestShortText;
            private set => SetProperty(ref _speedTestShortText, value);
        }

        private string _unknownDevicesText = "✓ no unknown devices";

        public string UnknownDevicesText
        {
            get => _unknownDevicesText;
            private set => SetProperty(ref _unknownDevicesText, value);
        }

        private bool _hasUnknownDevices;

        public bool HasUnknownDevices
        {
            get => _hasUnknownDevices;
            private set => SetProperty(ref _hasUnknownDevices, value);
        }

        public void Attach()
        {

            if (!_attached)
            {
                _attached = true;
                _feed.Updated += OnFeedUpdated;
                Refresh();
            }

        }

        public void Detach()
        {

            if (_attached)
            {
                _attached = false;
                _feed.Updated -= OnFeedUpdated;
            }

        }

        private void OnFeedUpdated(object? sender, EventArgs args)
        {
            _dispatcherQueue.TryEnqueue(Refresh);
        }

        private void OnStateChanged(object? sender, EventArgs args)
        {
            _dispatcherQueue.TryEnqueue(Refresh);
        }

        private void Refresh()
        {
            RateUnitMode mode = _settings.RateUnitMode;

            if (_state.ShowInternet)
            {
                InternetPoints = _feed.WanSnapshot();
            }

            if (_state.ShowLocal)
            {
                LocalPoints = _feed.LanSnapshot();
            }

            SpeedTestText = MiniGraphFormatter.SpeedTest(_feed.LatestSpeedTest, mode);
            SpeedTestShortText = MiniGraphFormatter.SpeedTestShort(_feed.LatestSpeedTest, mode);
            UnknownDevicesText = MiniGraphFormatter.UnknownDevices(_feed.UnapprovedDeviceCount);
            HasUnknownDevices = _feed.UnapprovedDeviceCount > 0;
        }
    }
}
