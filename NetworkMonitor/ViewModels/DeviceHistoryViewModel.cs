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
            IQueryable<DeviceEvent> query = db.DeviceEvents.AsNoTracking();

            if (_settings.HistoryPurgeDays > 0)
            {
                DateTime cutoff = DateTime.UtcNow.AddDays(-_settings.HistoryPurgeDays);
                query = query.Where(deviceEvent => deviceEvent.Timestamp >= cutoff);
            }

            // Deliberately NOT Include(deviceEvent => deviceEvent.Device). AsNoTracking keeps no
            // identity map, so an Include materialises a separate Device for every event row — a
            // thirty-day history of a household that flaps a few phones is thousands of rows against
            // perhaps fifty distinct devices, and each duplicate carries its own copy of every string
            // on the entity. Reading the referenced devices once into a dictionary and handing the
            // same instance to every event that points at it removes both the duplicate objects and
            // the duplicate strings, and it leaves the object graph the grid, the sort, the search and
            // the CSV exporter already read exactly as it was.
            IQueryable<int> referenced = query.Select(deviceEvent => deviceEvent.DeviceId).Distinct();
            Dictionary<int, Device> devices = await db.Devices
                .AsNoTracking()
                .Where(device => referenced.Contains(device.Id))
                .ToDictionaryAsync(device => device.Id);

            List<DeviceEvent> events = await query
                .OrderByDescending(deviceEvent => deviceEvent.Timestamp)
                .ToListAsync();

            foreach (DeviceEvent deviceEvent in events)
            {

                if (devices.TryGetValue(deviceEvent.DeviceId, out Device? device))
                {
                    deviceEvent.Device = device;
                }

            }

            _allEvents = events;
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
                // Ordinal-ignore-case rather than lowercasing both sides. The old form allocated a new
                // string per field per event on every pass — four for every row of a thirty-day history,
                // all discarded — and it also left IpAddress matched case-sensitively against a query
                // that had already been lowercased, which only went unnoticed because an IP has no
                // letters in it.
                string query = SearchText.Trim();
                filtered = filtered.Where(deviceEvent =>
                    (deviceEvent.Device?.DisplayName?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (deviceEvent.Device?.IpAddress?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (deviceEvent.Device?.MacAddress?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (deviceEvent.Device?.Vendor?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false));
            }

            List<DeviceEvent> target = ApplySorting(filtered).ToList();
            CollectionReconciler.SyncOrdered(Events, target, deviceEvent => deviceEvent.Id, static (existing, incoming) => { });
            StatusText = $"{Events.Count} events";
        }

        // The key selector used to be typed Func<DeviceEvent, object?>, which boxed a DateTime or an int
        // for every event and then compared those boxes through Comparer<object>.Default — an interface
        // dispatch and a type check per comparison, where a typed comparer inlines. This runs over the
        // whole thirty-day list on every scan, every column-header click and every debounced keystroke,
        // so the generic helper below keeps each key at its own type all the way to the comparer.
        private IEnumerable<DeviceEvent> ApplySorting(IEnumerable<DeviceEvent> source)
        {
            IEnumerable<DeviceEvent> sorted = _sortProperty switch
            {
                "EventType" => SortBy(source, deviceEvent => (int)deviceEvent.EventType),
                "Type" => SortBy(source, deviceEvent => (int)(deviceEvent.Device?.Type ?? DeviceType.Unknown)),
                "DisplayName" => SortBy(source, deviceEvent => deviceEvent.Device?.DisplayName),
                "IpAddress" => SortBy(source, deviceEvent => deviceEvent.Device?.IpAddress),
                "MacAddress" => SortBy(source, deviceEvent => deviceEvent.Device?.MacAddress),
                "Vendor" => SortBy(source, deviceEvent => deviceEvent.Device?.Vendor),
                _ => SortBy(source, deviceEvent => deviceEvent.Timestamp)
            };

            return sorted;
        }

        private IEnumerable<DeviceEvent> SortBy<TKey>(IEnumerable<DeviceEvent> source, Func<DeviceEvent, TKey> keySelector)
        {
            IEnumerable<DeviceEvent> sorted = _sortAscending
                ? source.OrderBy(keySelector)
                : source.OrderByDescending(keySelector);

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
