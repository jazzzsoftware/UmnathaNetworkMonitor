using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using NetworkMonitor.Models.Update;
using NetworkMonitor.Services.Update;

namespace NetworkMonitor.ViewModels
{
    public sealed class UpdateViewModel : ObservableObject
    {
        private readonly IUpdateService _updateService;
        private readonly DispatcherQueue _dispatcher;
        private AvailableUpdate? _pendingUpdate;
        private bool _reportUpToDate;

        public UpdateViewModel(IUpdateService updateService)
        {
            _updateService = updateService;
            _dispatcher = DispatcherQueue.GetForCurrentThread();
            CheckNowCommand = new AsyncRelayCommand(CheckNowAsync);
            UpdateNowCommand = new AsyncRelayCommand(UpdateNowAsync);
            DismissCommand = new RelayCommand(Dismiss);

            _updateService.CheckCompleted += OnCheckCompleted;
        }

        private bool _isBannerOpen;

        public bool IsBannerOpen
        {
            get => _isBannerOpen;
            set
            {
                SetProperty(ref _isBannerOpen, value);
            }
        }

        private string _message = string.Empty;

        public string Message
        {
            get => _message;
            set
            {
                SetProperty(ref _message, value);
            }
        }

        private InfoBarSeverity _severity = InfoBarSeverity.Informational;

        public InfoBarSeverity Severity
        {
            get => _severity;
            set
            {
                SetProperty(ref _severity, value);
            }
        }

        private bool _isBusy;

        public bool IsBusy
        {
            get => _isBusy;
            set
            {

                if (SetProperty(ref _isBusy, value))
                {
                    OnPropertyChanged(nameof(IsNotBusy));
                }

            }
        }

        public bool IsNotBusy => !_isBusy;

        private bool _hasPendingUpdate;

        public bool HasPendingUpdate
        {
            get => _hasPendingUpdate;
            set
            {

                if (SetProperty(ref _hasPendingUpdate, value))
                {
                    OnPropertyChanged(nameof(HasNoPendingUpdate));
                }

            }
        }

        public bool HasNoPendingUpdate => !_hasPendingUpdate;

        private double _downloadProgress;

        public double DownloadProgress
        {
            get => _downloadProgress;
            set
            {

                if (SetProperty(ref _downloadProgress, value))
                {
                    OnPropertyChanged(nameof(DownloadProgressText));
                }

            }
        }

        public string DownloadProgressText
        {
            get
            {
                string text = _downloadProgress >= 100.0 ? "Verifying…" : $"{_downloadProgress:F0}%";

                return text;
            }
        }

        public IAsyncRelayCommand CheckNowCommand
        {
            get;
        }

        public IAsyncRelayCommand UpdateNowCommand
        {
            get;
        }

        public IRelayCommand DismissCommand
        {
            get;
        }

        private async Task CheckNowAsync()
        {
            _reportUpToDate = true;

            await _updateService.CheckAsync(CancellationToken.None);
        }

        private async Task UpdateNowAsync()
        {
            AvailableUpdate? update = _pendingUpdate;

            if (update is not null && !IsBusy)
            {
                IsBusy = true;
                DownloadProgress = 0;
                Message = $"Downloading version {update.NormalizedVersion}…";
                Severity = InfoBarSeverity.Informational;

                Progress<double> progress = new Progress<double>(fraction =>
                {
                    DownloadProgress = fraction * 100.0;
                });

                try
                {
                    string installerPath = await _updateService.DownloadAndVerifyAsync(update, progress, CancellationToken.None);
                    _updateService.LaunchInstaller(installerPath);
                }
                catch (Exception)
                {
                    IsBusy = false;
                    Severity = InfoBarSeverity.Error;
                    Message = "The update could not be downloaded or verified. Please try again later.";
                    IsBannerOpen = true;
                }

            }

        }

        private void Dismiss()
        {
            IsBannerOpen = false;
        }

        private void OnCheckCompleted(object? sender, UpdateCheckResult result)
        {
            _dispatcher.TryEnqueue(() =>
            {
                Apply(result);
            });
        }

        private void Apply(UpdateCheckResult result)
        {
            bool reportUpToDate = _reportUpToDate;
            _reportUpToDate = false;

            if (!IsBusy)
            {

                if (result.Availability == UpdateAvailability.UpdateAvailable && result.Update is not null)
                {
                    _pendingUpdate = result.Update;
                    HasPendingUpdate = true;
                    Severity = InfoBarSeverity.Informational;
                    Message = $"Version {result.Update.NormalizedVersion} is available.";
                    IsBannerOpen = true;
                }
                else if (result.Availability == UpdateAvailability.CheckFailed)
                {
                    _pendingUpdate = null;
                    HasPendingUpdate = false;
                    Severity = InfoBarSeverity.Error;
                    Message = result.ErrorMessage ?? "Couldn't check for updates.";
                    IsBannerOpen = true;
                }
                else
                {
                    _pendingUpdate = null;
                    HasPendingUpdate = false;
                    Severity = InfoBarSeverity.Success;
                    Message = "You're on the latest version.";
                    IsBannerOpen = reportUpToDate;
                }

            }

        }
    }
}
