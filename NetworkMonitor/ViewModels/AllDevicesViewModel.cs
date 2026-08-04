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
using NetworkMonitor.Core.Scanning;

namespace NetworkMonitor.ViewModels
{
    public partial class AllDevicesViewModel : ObservableObject
    {
        private readonly DispatcherQueue _dispatcherQueue;
        private readonly DispatcherQueueTimer _searchDebounceTimer;
        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly ScanWorker _scanWorker;
        private readonly Settings _settings;
        private List<Device> _allDevices = [];
        private bool _isActive;

        public AllDevicesViewModel(IDbContextFactory<AppDbContext> dbFactory, ScanWorker scanWorker, Settings settings)
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

        private ObservableCollection<Device> _devices = [];

        public ObservableCollection<Device> Devices
        {
            get => _devices;
            set => SetProperty(ref _devices, value);
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

        private bool _isScanning;

        public bool IsScanning
        {
            get => _isScanning;
            set => SetProperty(ref _isScanning, value);
        }

        private string _statusText = "Ready — click Scan Network to start.";

        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        private bool _showUnapprovedOnly;

        public bool ShowUnapprovedOnly
        {
            get => _showUnapprovedOnly;
            set
            {

                if (SetProperty(ref _showUnapprovedOnly, value))
                {
                    ApplyFilter();
                }

            }
        }

        private bool _showOnlineOnly;

        public bool ShowOnlineOnly
        {
            get => _showOnlineOnly;
            set
            {

                if (SetProperty(ref _showOnlineOnly, value))
                {
                    _settings.DevicesOnlineOnly = value;
                    _settings.Save();
                    ApplyFilter();
                }

            }
        }

        private string _sortProperty = "IpAddress";

        public string SortProperty => _sortProperty;

        private bool _sortAscending = true;

        public bool SortAscending => _sortAscending;

        public bool ShowApprovedOnly
        {
            get;
            set;
        }

        public async Task LoadAsync()
        {
            DateTime cutoff = DateTime.UtcNow.AddHours(-24);
            await using AppDbContext db = await _dbFactory.CreateDbContextAsync();
            List<Device> fresh = await db.Devices
                .AsNoTracking()
                .Where(device => device.IsOnline || device.LastSeen >= cutoff)
                .ToListAsync();

            _dispatcherQueue.TryEnqueue(() =>
            {
                CollectionReconciler.MergeUnordered(_allDevices, fresh, device => device.Id, static (existing, incoming) => existing.CopyValuesFrom(incoming));
                ApplyFilter();
            });
        }

        public void Sort(string property, bool ascending)
        {
            _sortProperty = property;
            _sortAscending = ascending;
            ApplyFilter();
        }

        public void RestoreOnlineOnlyFilter()
        {
            ShowOnlineOnly = _settings.DevicesOnlineOnly;
        }

        public async Task<List<Device>> GetApprovedDevicesAsync()
        {
            await using AppDbContext db = await _dbFactory.CreateDbContextAsync();
            List<Device> approved = await db.Devices
                .AsNoTracking()
                .Where(device => device.IsApproved)
                .ToListAsync();
            List<Device> sorted = approved.OrderBy(device => IpSortKey(device.IpAddress)).ToList();

            return sorted;
        }

        public async Task<(int Added, int Updated)> ImportApprovedDevicesAsync(IReadOnlyList<Device> candidates)
        {
            await using AppDbContext db = await _dbFactory.CreateDbContextAsync();
            List<Device> existingDevices = await db.Devices.ToListAsync();
            Dictionary<string, Device> devicesByMac = new();

            foreach (Device existing in existingDevices)
            {
                string existingKey = NormalizeMac(existing.MacAddress);

                if (!devicesByMac.ContainsKey(existingKey))
                {
                    devicesByMac[existingKey] = existing;
                }

            }

            int added = 0;
            int updated = 0;

            foreach (Device candidate in candidates)
            {
                string key = NormalizeMac(candidate.MacAddress);

                if (devicesByMac.TryGetValue(key, out Device? existing))
                {
                    existing.IsApproved = true;
                    existing.FriendlyName = candidate.FriendlyName;
                    existing.Type = candidate.Type;
                    existing.Notes = candidate.Notes;
                    updated++;
                }
                else
                {
                    db.Devices.Add(candidate);
                    devicesByMac[key] = candidate;
                    added++;
                }

            }

            await db.SaveChangesAsync();
            (int Added, int Updated) result = (added, updated);

            return result;
        }

        public async Task MarkApprovedAsync(Device device)
        {
            await using AppDbContext db = await _dbFactory.CreateDbContextAsync();
            Device? tracked = await db.Devices.FindAsync(device.Id);

            if (tracked is not null)
            {
                tracked.IsApproved = true;
                await db.SaveChangesAsync();
                device.IsApproved = true;
                ApplyFilter();
            }

        }

        public async Task SaveDeviceAsync(Device device)
        {
            await using AppDbContext db = await _dbFactory.CreateDbContextAsync();
            Device? tracked = await db.Devices.FindAsync(device.Id);

            if (tracked is not null)
            {
                tracked.FriendlyName = device.FriendlyName;
                tracked.Type = device.Type;
                tracked.Notes = device.Notes;
                tracked.IsApproved = device.IsApproved;
                await db.SaveChangesAsync();
            }

        }

        public async Task DeleteDeviceAsync(Device device)
        {
            await using AppDbContext db = await _dbFactory.CreateDbContextAsync();
            Device? tracked = await db.Devices.FindAsync(device.Id);

            if (tracked is not null)
            {
                db.Devices.Remove(tracked);
                await db.SaveChangesAsync();
                _allDevices.Remove(_allDevices.First(existing => existing.Id == device.Id));
                ApplyFilter();
            }

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

        [RelayCommand]
        private async Task ScanNowAsync()
        {

            if (!IsScanning)
            {
                IsScanning = true;
                StatusText = "Scanning network…";

                try
                {
                    await _scanWorker.ScanNowAsync();
                }
                catch (Exception exception)
                {
                    StatusText = $"Scan failed: {exception.Message}";
                    AppLog.Error("AllDevicesViewModel.ScanNow", exception);
                }
                finally
                {
                    IsScanning = false;
                }

            }

        }

        private void OnSearchDebounceTick(DispatcherQueueTimer sender, object args)
        {
            _searchDebounceTimer.Stop();
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            IEnumerable<Device> filtered = _allDevices;

            if (ShowApprovedOnly)
            {
                filtered = filtered.Where(device => device.IsApproved);
            }
            else if (ShowUnapprovedOnly)
            {
                filtered = filtered.Where(device => !device.IsApproved);
            }

            if (ShowOnlineOnly)
            {
                filtered = filtered.Where(device => device.IsOnline);
            }

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                string query = SearchText.Trim().ToLowerInvariant();
                filtered = filtered.Where(device =>
                    (device.FriendlyName?.ToLowerInvariant().Contains(query) ?? false) ||
                    (device.Hostname?.ToLowerInvariant().Contains(query) ?? false) ||
                    (device.Vendor?.ToLowerInvariant().Contains(query) ?? false) ||
                    device.IpAddress.Contains(query) ||
                    device.MacAddress.ToLowerInvariant().Contains(query));
            }

            List<Device> target = ApplySorting(filtered).ToList();
            CollectionReconciler.SyncOrdered(Devices, target, device => device.Id, static (existing, incoming) => existing.CopyValuesFrom(incoming));
        }

        private IEnumerable<Device> ApplySorting(IEnumerable<Device> source)
        {
            Func<Device, object?> key = _sortProperty switch
            {
                "IsOnline" => device => device.IsOnline,
                "Type" => device => (int) device.Type,
                "DisplayName" => device => device.DisplayName,
                "IpAddress" => device => IpSortKey(device.IpAddress),
                "MacAddress" => device => device.MacAddress,
                "Vendor" => device => device.Vendor,
                _ => device => IpSortKey(device.IpAddress)
            };
            IEnumerable<Device> sorted = _sortAscending ? source.OrderBy(key) : source.OrderByDescending(key);

            return sorted;
        }

        private static string NormalizeMac(string mac)
        {
            string normalized = MacNormalizer.Normalize(mac);

            return normalized;
        }

        private static long IpSortKey(string ip)
        {
            long key = 0;

            if (System.Net.IPAddress.TryParse(ip, out System.Net.IPAddress? parsed))
            {
                byte[] bytes = parsed.GetAddressBytes();

                if (bytes.Length == 4)
                {
                    key = ((long)bytes[0] << 24) | ((long)bytes[1] << 16) | ((long)bytes[2] << 8) | bytes[3];
                }

            }

            return key;
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

                _dispatcherQueue.TryEnqueue(() =>
                {
                    StatusText = $"Last Device scan: {DateTime.Now:HH:mm} — " +
                                 $"{args.Session.DevicesFound} devices, " +
                                 $"{args.Session.NewDevices} new, " +
                                 $"{args.Session.DevicesGone} gone offline";
                });
            }
            catch (Exception exception)
            {
                AppLog.Error("AllDevicesViewModel.OnScanCompleted", exception);
            }

        }
    }
}
