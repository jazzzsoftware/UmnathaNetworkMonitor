using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Dispatching;
using NetworkMonitor.Data;
using NetworkMonitor.Models;
using NetworkMonitor.Services.SpeedTest;

namespace NetworkMonitor.ViewModels
{
    public partial class SpeedTestViewModel : ObservableObject
    {
        private readonly SpeedTestWorker _worker;
        private readonly Settings _settings;
        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly DispatcherQueue _dispatcherQueue;
        private List<SpeedTestResult> _allResults = new();

        public SpeedTestViewModel(SpeedTestWorker worker, Settings settings, IDbContextFactory<AppDbContext> dbFactory)
        {
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            _worker = worker;
            _settings = settings;
            _dbFactory = dbFactory;
            RunNowCommand = new AsyncRelayCommand(RunNowAsync);
            _worker.SpeedTestCompleted += OnSpeedTestCompleted;
        }

        public ObservableCollection<SpeedTestResult> History
        {
            get;
        } = new();

        public IAsyncRelayCommand RunNowCommand
        {
            get;
        }

        private SpeedTestResult? _latest;

        public SpeedTestResult? Latest
        {
            get => _latest;
            set => SetProperty(ref _latest, value);
        }

        private IReadOnlyList<ChartSeries> _throughputSeries = [];

        public IReadOnlyList<ChartSeries> ThroughputSeries
        {
            get => _throughputSeries;
            set => SetProperty(ref _throughputSeries, value);
        }

        private IReadOnlyList<ChartSeries> _latencySeries = [];

        public IReadOnlyList<ChartSeries> LatencySeries
        {
            get => _latencySeries;
            set => SetProperty(ref _latencySeries, value);
        }

        private bool _isRunning;

        public bool IsRunning
        {
            get => _isRunning;
            set => SetProperty(ref _isRunning, value);
        }

        private int _chartRangeHours = 24;

        public int ChartRangeHours
        {
            get => _chartRangeHours;
            set => SetProperty(ref _chartRangeHours, value);
        }

        private string _sortProperty = "Timestamp";

        public string SortProperty => _sortProperty;

        private bool _sortAscending;

        public bool SortAscending => _sortAscending;

        public async Task LoadAsync()
        {
            await using AppDbContext db = await _dbFactory.CreateDbContextAsync();
            DateTime cutoff = DateTime.UtcNow.AddDays(-Math.Max(1, _settings.TrafficPurgeDays));

            List<SpeedTestResult> rows = await db.SpeedTestResults
                .AsNoTracking()
                .Where(result => result.Timestamp >= cutoff)
                .OrderBy(result => result.Timestamp)
                .ToListAsync();

            _allResults = rows;

            DateTime chartCutoff = DateTime.UtcNow.AddHours(-ChartRangeHours);
            List<SpeedTestResult> chartRows = rows
                .Where(result => result.Timestamp >= chartCutoff)
                .ToList();

            List<ChartValue> download = chartRows.Select(result => new ChartValue(result.LocalTimestamp, result.DownloadMbps)).ToList();
            List<ChartValue> upload = chartRows.Select(result => new ChartValue(result.LocalTimestamp, result.UploadMbps)).ToList();
            List<ChartValue> latency = chartRows.Select(result => new ChartValue(result.LocalTimestamp, result.LatencyMs)).ToList();
            List<ChartValue> jitter = chartRows.Select(result => new ChartValue(result.LocalTimestamp, result.JitterMs)).ToList();

            ThroughputSeries = new List<ChartSeries>
            {
                new ChartSeries("Download", "#1976D2", download),
                new ChartSeries("Upload", "#AB47BC", upload)
            };

            LatencySeries = new List<ChartSeries>
            {
                new ChartSeries("Latency", "#F57C00", latency),
                new ChartSeries("Jitter", "#2E7D32", jitter)
            };

            Latest = rows.Count > 0 ? rows[^1] : null;
            ApplySort();
        }

        public void Sort(string property, bool ascending)
        {
            _sortProperty = property;
            _sortAscending = ascending;
            ApplySort();
        }

        private async Task RunNowAsync()
        {
            IsRunning = true;

            try
            {
                await _worker.RunNowAsync();
            }
            finally
            {
                IsRunning = false;
            }

        }

        private void OnSpeedTestCompleted(object? sender, SpeedTestCompletedEventArgs args)
        {
            _dispatcherQueue.TryEnqueue(() => _ = LoadAsync());
        }

        private void ApplySort()
        {
            List<SpeedTestResult> sorted = SortResults(_allResults).ToList();

            History.Clear();

            foreach (SpeedTestResult result in sorted)
            {
                History.Add(result);
            }

        }

        private IEnumerable<SpeedTestResult> SortResults(IEnumerable<SpeedTestResult> source)
        {
            Func<SpeedTestResult, object?> key = _sortProperty switch
            {
                "Timestamp" => result => result.Timestamp,
                "DownloadMbps" => result => result.DownloadMbps,
                "UploadMbps" => result => result.UploadMbps,
                "DownloadMBps" => result => result.DownloadMBps,
                "UploadMBps" => result => result.UploadMBps,
                "LatencyMs" => result => result.LatencyMs,
                "JitterMs" => result => result.JitterMs,
                "Server" => result => result.Server,
                _ => result => result.Timestamp
            };

            IEnumerable<SpeedTestResult> sorted = _sortAscending ? source.OrderBy(key) : source.OrderByDescending(key);

            return sorted;
        }
    }
}
