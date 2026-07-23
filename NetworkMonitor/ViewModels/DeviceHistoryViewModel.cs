using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Dispatching;
using NetworkMonitor.Services.Data;
using NetworkMonitor.Models.Devices;
using NetworkMonitor.Services.Scanning;
using NetworkMonitor.Services.Platform;
using NetworkMonitor.Core.Common;

namespace NetworkMonitor.ViewModels
{
    public partial class DeviceHistoryViewModel : ObservableObject
    {
        private readonly DispatcherQueue _dispatcherQueue;
        private readonly DispatcherQueueTimer _searchDebounceTimer;
        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly ScanWorker _scanWorker;
        private readonly Settings _settings;
        private List<DeviceEvent> _allEvents = [];
        private bool _isActive;

        public DeviceHistoryViewModel(IDbContextFactory<AppDbContext> dbFactory, ScanWorker scanWorker, Settings settings)
        {
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            _dbFactory = dbFactory;
            _scanWorker = scanWorker;
            _settings = settings;
            _scanWorker.ScanCompleted += OnScanCompleted;

            _searchDebounceTimer = _dispatcherQueue.CreateTimer();
            _searchDebounceTimer.Interval = TimeSpan.FromMilliseconds(200);
            _searchDebounceTimer.IsRepeating = false;
            _searchDebounceTimer.Tick += OnSearchDebounceTick;
        }

        private ObservableCollection<DeviceEvent> _events = [];

        public ObservableCollection<DeviceEvent> Events
        {
            get => _events;
            set => SetProperty(ref _events, value);
        }

        private string _searchText = string.Empty;

        public string SearchText
        {
            get => _searchText;
            set
            {

                if (SetProperty(ref _searchText, value))
                {
                    _searchDebounceTimer.Stop();
                    _searchDebounceTimer.Start();
                }

            }
        }

        private string _statusText = string.Empty;

        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        private bool _isScanning;

        public bool IsScanning
        {
            get => _isScanning;
            set => SetProperty(ref _isScanning, value);
        }

        private string _sortProperty = "Timestamp";

        public string SortProperty => _sortProperty;

        private bool _sortAscending = false;

        public bool SortAscending => _sortAscending;

        public void Sort(string property, bool ascending)
        {
            _sortProperty = property;
            _sortAscending = ascending;
            _dispatcherQueue.TryEnqueue(ApplyFilter);
        }

        public async Task LoadAsync()
        {
            await using AppDbContext db = await _dbFactory.CreateDbContextAsync();
            IQueryable<DeviceEvent> query = db.DeviceEvents.AsNoTracking().Include(deviceEvent => deviceEvent.Device);

            if (_settings.HistoryPurgeDays > 0)
            {
                DateTime cutoff = DateTime.UtcNow.AddDays(-_settings.HistoryPurgeDays);
                query = query.Where(deviceEvent => deviceEvent.Timestamp >= cutoff);
            }

            _allEvents = await query
                .OrderByDescending(deviceEvent => deviceEvent.Timestamp)
                .ToListAsync();
            _dispatcherQueue.TryEnqueue(ApplyFilter);
        }

        public IReadOnlyList<DeviceEvent> GetEventsForExport()
        {
            List<DeviceEvent> snapshot = Events.ToList();

            return snapshot;
        }

        public void Detach()
        {
            _scanWorker.ScanCompleted -= OnScanCompleted;
        }

        public void Activate()
        {
            _isActive = true;

            _ = LoadAsync();
        }

        public void Deactivate()
        {
            _isActive = false;
        }

        private void OnSearchDebounceTick(DispatcherQueueTimer sender, object args)
        {
            _searchDebounceTimer.Stop();
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            IEnumerable<DeviceEvent> filtered = _allEvents;

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                string query = SearchText.Trim().ToLowerInvariant();
                filtered = filtered.Where(deviceEvent =>
                    (deviceEvent.Device?.DisplayName?.ToLowerInvariant().Contains(query) ?? false) ||
                    (deviceEvent.Device?.IpAddress?.Contains(query) ?? false) ||
                    (deviceEvent.Device?.MacAddress?.ToLowerInvariant().Contains(query) ?? false) ||
                    (deviceEvent.Device?.Vendor?.ToLowerInvariant().Contains(query) ?? false));
            }

            List<DeviceEvent> target = ApplySorting(filtered).ToList();
            CollectionReconciler.SyncOrdered(Events, target, deviceEvent => deviceEvent.Id, static (existing, incoming) => { });
            StatusText = $"{Events.Count} events";
        }

        private IEnumerable<DeviceEvent> ApplySorting(IEnumerable<DeviceEvent> source)
        {
            Func<DeviceEvent, object?> key = _sortProperty switch
            {
                "Timestamp" => deviceEvent => deviceEvent.Timestamp,
                "EventType" => deviceEvent => (int) deviceEvent.EventType,
                "Type" => deviceEvent => (int) (deviceEvent.Device?.Type ?? DeviceType.Unknown),
                "DisplayName" => deviceEvent => deviceEvent.Device?.DisplayName,
                "IpAddress" => deviceEvent => deviceEvent.Device?.IpAddress,
                "MacAddress" => deviceEvent => deviceEvent.Device?.MacAddress,
                "Vendor" => deviceEvent => deviceEvent.Device?.Vendor,
                _ => deviceEvent => deviceEvent.Timestamp
            };
            IEnumerable<DeviceEvent> sorted = _sortAscending ? source.OrderBy(key) : source.OrderByDescending(key);

            return sorted;
        }

        [RelayCommand]
        private async Task ScanNowAsync()
        {

            if (!IsScanning)
            {
                IsScanning = true;

                try
                {
                    await _scanWorker.ScanNowAsync();
                }
                catch (Exception exception)
                {
                    StatusText = $"Scan failed: {exception.Message}";
                    AppLog.Error("DeviceHistoryViewModel.ScanNow", exception);
                }
                finally
                {
                    IsScanning = false;
                }

            }

        }

        private async void OnScanCompleted(object? sender, ScanCompletedEventArgs args)
        {

            if (!_isActive)
            {
                return;
            }

            try
            {
                await LoadAsync();
            }
            catch (Exception exception)
            {
                AppLog.Error("DeviceHistoryViewModel.OnScanCompleted", exception);
            }

        }
    }
}
