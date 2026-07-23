using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Dispatching;
using NetworkMonitor.Services.Data;
using NetworkMonitor.Models.Digest;
using NetworkMonitor.Services.Digest;
using NetworkMonitor.Services.Platform;
using NetworkMonitor.Core.Digest;

namespace NetworkMonitor.ViewModels
{
    public partial class ReportsViewModel : ObservableObject
    {
        private readonly DispatcherQueue _dispatcherQueue;
        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly DigestWorker _digestWorker;
        private readonly DigestPdfExporter _pdfExporter;
        private readonly InAppNotificationService _notificationService;

        public ReportsViewModel(
            IDbContextFactory<AppDbContext> dbFactory,
            DigestWorker digestWorker,
            DigestPdfExporter pdfExporter,
            InAppNotificationService notificationService)
        {
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            _dbFactory = dbFactory;
            _digestWorker = digestWorker;
            _pdfExporter = pdfExporter;
            _notificationService = notificationService;
        }

        private ObservableCollection<DigestReport> _reports = new();

        public ObservableCollection<DigestReport> Reports
        {
            get => _reports;
            set => SetProperty(ref _reports, value);
        }

        private DigestReport? _latestReport;

        public DigestReport? LatestReport
        {
            get => _latestReport;
            set
            {

                if (SetProperty(ref _latestReport, value))
                {
                    LatestSummary = Deserialize(value);
                }

            }
        }

        private DigestSummary? _latestSummary;

        public DigestSummary? LatestSummary
        {
            get => _latestSummary;
            set => SetProperty(ref _latestSummary, value);
        }

        private DigestReport? _selectedHistoryReport;

        public DigestReport? SelectedHistoryReport
        {
            get => _selectedHistoryReport;
            set
            {

                if (SetProperty(ref _selectedHistoryReport, value))
                {
                    SelectedHistorySummary = Deserialize(value);
                }

            }
        }

        private DigestSummary? _selectedHistorySummary;

        public DigestSummary? SelectedHistorySummary
        {
            get => _selectedHistorySummary;
            set => SetProperty(ref _selectedHistorySummary, value);
        }

        public async Task LoadAsync()
        {
            List<DigestReport> reports = await QueryReportsAsync();
            List<DigestReport> loaded = reports;

            _dispatcherQueue.TryEnqueue(() =>
            {
                Reports = new ObservableCollection<DigestReport>(loaded);
                DigestReport? newest = Reports.Count > 0 ? Reports[0] : null;
                LatestReport = newest;
                SelectedHistoryReport = newest;
            });
        }

        public byte[] BuildPdf(DigestReport? report)
        {
            DigestSummary? summary = Deserialize(report);
            byte[] pdf;

            if (report is null || summary is null)
            {
                pdf = Array.Empty<byte>();
            }
            else
            {
                pdf = _pdfExporter.BuildPdf(summary, report.PeriodStart, report.PeriodEnd, report.GeneratedAt);
            }

            return pdf;
        }

        public string BuildAllReportsCsv()
        {
            List<(DateTime PeriodStartUtc, DateTime PeriodEndUtc, DateTime GeneratedAtUtc, DigestSummary Summary)> entries = new();

            foreach (DigestReport report in Reports)
            {
                DigestSummary? summary = Deserialize(report);

                if (summary is not null)
                {
                    entries.Add((report.PeriodStart, report.PeriodEnd, report.GeneratedAt, summary));
                }

            }

            string csv = entries.Count == 0 ? string.Empty : DigestCsvExporter.BuildAllCsv(entries);

            return csv;
        }

        [RelayCommand]
        private async Task GenerateNowAsync()
        {
            await _digestWorker.GenerateNowAsync();
            await LoadAsync();
            _notificationService.Show($"Report generated for {LatestReport?.PeriodEndDisplay}");
        }

        [RelayCommand]
        private async Task DeleteAsync()
        {

            if (SelectedHistoryReport is not null)
            {
                string deletedPeriod = SelectedHistoryReport.PeriodEndDisplay;
                await using AppDbContext db = await _dbFactory.CreateDbContextAsync();
                await db.DigestReports
                    .Where(report => report.Id == SelectedHistoryReport.Id)
                    .ExecuteDeleteAsync();
                await LoadAsync();
                _notificationService.Show($"Report deleted: {deletedPeriod}");
            }

        }

        private async Task<List<DigestReport>> QueryReportsAsync()
        {
            await using AppDbContext db = await _dbFactory.CreateDbContextAsync();
            List<DigestReport> reports = await db.DigestReports
                .AsNoTracking()
                .OrderByDescending(report => report.PeriodEnd)
                .ToListAsync();

            return reports;
        }

        private static DigestSummary? Deserialize(DigestReport? report)
        {
            DigestSummary? summary = (report is null || string.IsNullOrEmpty(report.SummaryJson))
                ? null
                : JsonSerializer.Deserialize<DigestSummary>(report.SummaryJson);

            return summary;
        }

    }
}
