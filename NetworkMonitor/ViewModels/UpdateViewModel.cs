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
        private CancellationTokenSource? _downloadCancellation;
        private UpdateCheckResult? _manualResult;

        public UpdateViewModel(IUpdateService updateService)
        {
            _updateService = updateService;
            _dispatcher = DispatcherQueue.GetForCurrentThread();
            CheckNowCommand = new AsyncRelayCommand(CheckNowAsync);
            UpdateNowCommand = new AsyncRelayCommand(UpdateNowAsync);
            DismissCommand = new RelayCommand(Dismiss);
            CancelDownloadCommand = new RelayCommand(CancelDownload);

            _updateService.CheckCompleted += OnCheckCompleted;

            // The first check fires ten seconds after the host starts, which can be before this
            // view model exists (it is built with MainWindow). Replay whatever it found.
            UpdateCheckResult? missed = _updateService.LastResult;

            if (missed is not null)
            {
                Apply(missed, false);
            }

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

        public IRelayCommand CancelDownloadCommand
        {
            get;
        }

        public void CancelPendingWork()
        {
            _downloadCancellation?.Cancel();
        }

        private async Task CheckNowAsync()
        {
            // Applied from the returned result rather than the shared event, so a background check
            // completing at the same moment can't consume this check's "tell me either way" intent.
            UpdateCheckResult result = await _updateService.CheckAsync(CancellationToken.None);

            // The same check also broadcasts on CheckCompleted, and that handler is queued on the
            // dispatcher while this continuation runs inline — so without claiming the result first
            // the queued handler would re-apply it with reportUpToDate false and shut the banner.
            _manualResult = result;

            Apply(result, true);
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

                CancellationTokenSource cancellation = new CancellationTokenSource();
                _downloadCancellation = cancellation;

                try
                {
                    string installerPath = await _updateService.DownloadAndVerifyAsync(update, progress, cancellation.Token);
                    MainWindow? window = MainWindow.Current;
                    Action? beforeExit = null;

                    if (window is not null)
                    {
                        beforeExit = window.ShutdownForUpdate;
                    }

                    _updateService.LaunchInstaller(installerPath, beforeExit);
                }
                catch (OperationCanceledException)
                {
                    IsBusy = false;
                    DownloadProgress = 0;
                    Severity = InfoBarSeverity.Informational;
                    Message = $"Version {update.NormalizedVersion} is available.";
                    IsBannerOpen = true;
                }
                catch (Exception)
                {
                    IsBusy = false;
                    DownloadProgress = 0;
                    Severity = InfoBarSeverity.Error;
                    Message = "The update could not be downloaded or verified. Please try again later.";
                    IsBannerOpen = true;
                }
                finally
                {
                    _downloadCancellation = null;
                    cancellation.Dispose();
                }

            }

        }

        private void CancelDownload()
        {
            _downloadCancellation?.Cancel();
        }

        private void Dismiss()
        {
            IsBannerOpen = false;
        }

        private void OnCheckCompleted(object? sender, UpdateCheckResult result)
        {
            _dispatcher.TryEnqueue(() =>
            {

                if (!ReferenceEquals(result, _manualResult))
                {
                    Apply(result, false);
                }

            });
        }

        private void Apply(UpdateCheckResult result, bool reportUpToDate)
        {

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
