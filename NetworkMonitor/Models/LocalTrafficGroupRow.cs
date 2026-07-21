using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using NetworkMonitor.Services.Common;
using NetworkMonitor.Services.Traffic;

namespace NetworkMonitor.Models
{
    public class LocalTrafficGroupRow : ObservableObject
    {
        private const double RateThresholdBytesPerSec = 65_536.0;

        public LocalTrafficGroupRow(string? key, string displayName, string? subLabel, long bytesUploaded, long bytesDownloaded, IReadOnlyList<LocalTrafficLeafRow> children, GroupKind kind, string? serviceTag)
        {
            Key = key;
            Kind = kind;
            _displayName = displayName;
            _subLabel = subLabel;
            _bytesUploaded = bytesUploaded;
            _bytesDownloaded = bytesDownloaded;
            _children = children;
            _serviceTag = serviceTag;
        }

        public string? Key
        {
            get;
        }

        public GroupKind Kind
        {
            get;
        }

        private string _displayName;

        public string DisplayName
        {
            get => _displayName;
            set => SetProperty(ref _displayName, value);
        }

        private string? _subLabel;

        public string? SubLabel
        {
            get => _subLabel;
            set
            {

                if (SetProperty(ref _subLabel, value))
                {
                    OnPropertyChanged(nameof(HasSubLabel));
                }

            }
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
                    OnPropertyChanged(nameof(UploadText));
                    OnPropertyChanged(nameof(TotalText));
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
                    OnPropertyChanged(nameof(DownloadText));
                    OnPropertyChanged(nameof(TotalText));
                }

            }
        }

        private IReadOnlyList<LocalTrafficLeafRow> _children;

        public IReadOnlyList<LocalTrafficLeafRow> Children
        {
            get => _children;
            set
            {

                if (SetProperty(ref _children, value))
                {
                    OnPropertyChanged(nameof(HasChildren));
                    OnPropertyChanged(nameof(ChildSummary));
                    OnPropertyChanged(nameof(ChildTooltip));
                }

            }
        }

        private string? _serviceTag;

        public string? ServiceTag
        {
            get => _serviceTag;
            set
            {

                if (SetProperty(ref _serviceTag, value))
                {
                    OnPropertyChanged(nameof(HasServiceTag));
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

        public bool IsAll => Kind == GroupKind.All;

        public bool IsBackground => Kind == GroupKind.Background;

        public bool HasChildren => Children.Count > 1;

        public bool HasServiceTag => ServiceTag is not null;

        public bool HasSubLabel => SubLabel is not null;

        public bool HasRate => _rateBytesPerSec >= RateThresholdBytesPerSec;

        public string RateText => $"{TrafficRateFormatter.BitsPerSecond((long)_rateBytesPerSec, 1.0)} · {TrafficRateFormatter.BytesPerSecond((long)_rateBytesPerSec, 1.0)}";

        public string DownloadText => ByteSizeFormatter.Format(BytesDownloaded);

        public string UploadText => ByteSizeFormatter.Format(BytesUploaded);

        public string TotalText => ByteSizeFormatter.Format(TotalBytes);

        public string ChildSummary => Children.Count switch
        {
            0 => string.Empty,
            1 => Children[0].DisplayName,
            _ => $"{Children[0].DisplayName} +{Children.Count - 1}"
        };

        public string ChildTooltip => string.Join(", ", Children.Select(child => child.DisplayName));

        public void UpdateFrom(LocalTrafficGroupRow source)
        {
            DisplayName = source.DisplayName;
            SubLabel = source.SubLabel;
            BytesUploaded = source.BytesUploaded;
            BytesDownloaded = source.BytesDownloaded;
            ServiceTag = source.ServiceTag;
            Children = source.Children;
        }
    }
}
