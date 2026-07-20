using System.Linq;
using NetworkMonitor.Services.Common;

namespace NetworkMonitor.Models
{
    public record LocalTrafficGroupRow(string? Key, string DisplayName, string? SubLabel, long BytesUploaded, long BytesDownloaded, IReadOnlyList<LocalTrafficLeafRow> Children, GroupKind Kind, string? ServiceTag)
    {
        public long TotalBytes => BytesUploaded + BytesDownloaded;

        public bool IsAll => Kind == GroupKind.All;

        public bool IsBackground => Kind == GroupKind.Background;

        public bool HasChildren => Children.Count > 1;

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
    }
}
