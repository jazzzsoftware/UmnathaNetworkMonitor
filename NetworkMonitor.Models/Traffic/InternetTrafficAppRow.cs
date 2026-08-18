using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using NetworkMonitor.Models.Formatting;

namespace NetworkMonitor.Models.Traffic
{
    // An ObservableObject rather than a record so the Internet grid can be reconciled in place, the
    // way LocalTrafficGroupRow already allows the Local grid to be. As an immutable record the only
    // way new values could reach the grid was to replace the whole ObservableCollection, which made
    // the DataGrid drop its ItemsSource and re-realize every row once every flush.
    public class InternetTrafficAppRow : ObservableObject
    {
        private const double RateThresholdBytesPerSec = 62_500.0;

        public InternetTrafficAppRow(string? processName, long bytesUploaded, long bytesDownloaded, string? processPath, double rateBytesPerSec = 0.0)
        {
            ProcessName = processName;
            _bytesUploaded = bytesUploaded;
            _bytesDownloaded = bytesDownloaded;
            _processPath = processPath;
            _rateBytesPerSec = rateBytesPerSec;
        }

        // The row's identity, and the key the reconciler matches on, so it never changes for a row
        // that is already on screen. IsAllApps derives from it and is therefore fixed too, which is
        // why the grid can still bind that one OneTime.
        public string? ProcessName
        {
            get;
        }

        private long _bytesUploaded;

        public long BytesUploaded
        {
            get => _bytesUploaded;
            set
            {

                if (SetProperty(ref _bytesUploaded, value))
                {
                    OnPropertyChanged(nameof(TotalBytes));
                }

            }
        }

        private long _bytesDownloaded;

        public long BytesDownloaded
        {
            get => _bytesDownloaded;
            set
            {

                if (SetProperty(ref _bytesDownloaded, value))
                {
                    OnPropertyChanged(nameof(TotalBytes));
                }

            }
        }

        private string? _processPath;

        public string? ProcessPath
        {
            get => _processPath;
            set
            {

                if (SetProperty(ref _processPath, value))
                {
                    OnPropertyChanged(nameof(DisplayName));
                    OnPropertyChanged(nameof(CanOpen));
                }

            }
        }

        private double _rateBytesPerSec;

        public double RateBytesPerSec
        {
            get => _rateBytesPerSec;
            set
            {

                if (SetProperty(ref _rateBytesPerSec, value))
                {
                    OnPropertyChanged(nameof(HasRate));
                    OnPropertyChanged(nameof(RateText));
                }

            }
        }

        public long TotalBytes => BytesUploaded + BytesDownloaded;

        public bool IsAllApps => ProcessName is null;

        public string DisplayName => ProcessPath is not null ? Path.GetFileName(ProcessPath) : ProcessName ?? "All Apps";

        public bool CanOpen => !IsAllApps && ProcessPath is not null;

        public bool HasRate => _rateBytesPerSec >= RateThresholdBytesPerSec;

        public string RateText => TrafficRateFormatter.Composite((long)_rateBytesPerSec, 1.0);

        public void UpdateFrom(InternetTrafficAppRow source)
        {
            BytesUploaded = source.BytesUploaded;
            BytesDownloaded = source.BytesDownloaded;
            ProcessPath = source.ProcessPath;
            RateBytesPerSec = source.RateBytesPerSec;
        }
    }
}
