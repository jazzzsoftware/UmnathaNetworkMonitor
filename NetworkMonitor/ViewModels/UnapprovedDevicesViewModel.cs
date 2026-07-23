using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Dispatching;
using NetworkMonitor.Data;
using NetworkMonitor.Models.Devices;
using NetworkMonitor.Services.Scanning;
using NetworkMonitor.Services.Common;
using NetworkMonitor.Services.Platform;

namespace NetworkMonitor.ViewModels
{
    public partial class UnapprovedDevicesViewModel : ObservableObject
    {
        private readonly DispatcherQueue _dispatcherQueue;
        private readonly DispatcherQueueTimer _searchDebounceTimer;
        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly ScanWorker _scanWorker;
        private List<Device> _allDevices = [];
        private bool _isActive;

        public UnapprovedDevicesViewModel(IDbContextFactory<AppDbContext> dbFactory, ScanWorker scanWorker)
        {
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            _dbFactory = dbFactory;
            _scanWorker = scanWorker;
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

        private string _statusText = string.Empty;

        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        private string _sortProperty = "IpAddress";

        public string SortProperty => _sortProperty;

        private bool _sortAscending = true;

        public bool SortAscending => _sortAscending;

        public async Task LoadAsync()
        {
            DateTime cutoff = DateTime.UtcNow.AddHours(-24);
            await using AppDbContext db = await _dbFactory.CreateDbContextAsync();
            List<Device> fresh = await db.Devices
                .AsNoTracking()
                .Where(device => !device.IsApproved && (device.IsOnline || device.LastSeen >= cutoff))
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

        public async Task ApproveAsync(Device device)
        {
            await using AppDbContext db = await _dbFactory.CreateDbContextAsync();
            Device? tracked = await db.Devices.FindAsync(device.Id);

            if (tracked is not null)
            {
                tracked.IsApproved = true;
                tracked.FriendlyName = device.FriendlyName;
                tracked.Type = device.Type;
                tracked.Notes = device.Notes;
                await db.SaveChangesAsync();
                _allDevices.Remove(_allDevices.First(existing => existing.Id == device.Id));
                ApplyFilter();
            }

        }

        public async Task DeleteAsync(Device device)
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
                    AppLog.Error("UnapprovedDevicesViewModel.ScanNow", exception);
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

            List<Device> sorted = ApplySorting(filtered).ToList();
            CollectionReconciler.SyncOrdered(Devices, sorted, device => device.Id, static (existing, incoming) => existing.CopyValuesFrom(incoming));
            int count = sorted.Count;
            StatusText = count == 1 ? "1 unapproved device" : $"{count} unapproved devices";
        }

        private IEnumerable<Device> ApplySorting(IEnumerable<Device> source)
        {
            Func<Device, object?> key = _sortProperty switch
            {
                "IsOnline" => device => device.IsOnline,
                "Type" => device => (int)device.Type,
                "DisplayName" => device => device.DisplayName,
                "IpAddress" => device => IpSortKey(device.IpAddress),
                "MacAddress" => device => device.MacAddress,
                "Vendor" => device => device.Vendor,
                _ => device => IpSortKey(device.IpAddress)
            };
            IEnumerable<Device> sorted = _sortAscending ? source.OrderBy(key) : source.OrderByDescending(key);

            return sorted;
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
            }
            catch (Exception exception)
            {
                AppLog.Error("UnapprovedDevicesViewModel.OnScanCompleted", exception);
            }

        }

    }
}
