using System.ComponentModel.DataAnnotations.Schema;
using CommunityToolkit.Mvvm.ComponentModel;

namespace NetworkMonitor.Models
{
    public class Device : ObservableObject
    {
        private int _id;

        public int Id
        {
            get => _id;
            set => SetProperty(ref _id, value);
        }

        private string _macAddress = string.Empty;

        public string MacAddress
        {
            get => _macAddress;
            set => SetProperty(ref _macAddress, value);
        }

        private string _ipAddress = string.Empty;

        public string IpAddress
        {
            get => _ipAddress;
            set
            {

                if (SetProperty(ref _ipAddress, value))
                {
                    OnPropertyChanged(nameof(DisplayName));
                }

            }
        }

        private string? _hostname;

        public string? Hostname
        {
            get => _hostname;
            set
            {

                if (SetProperty(ref _hostname, value))
                {
                    OnPropertyChanged(nameof(DisplayName));
                }

            }
        }

        private string? _friendlyName;

        public string? FriendlyName
        {
            get => _friendlyName;
            set
            {

                if (SetProperty(ref _friendlyName, value))
                {
                    OnPropertyChanged(nameof(DisplayName));
                }

            }
        }

        private string? _mdnsName;

        public string? MdnsName
        {
            get => _mdnsName;
            set
            {

                if (SetProperty(ref _mdnsName, value))
                {
                    OnPropertyChanged(nameof(DisplayName));
                }

            }
        }

        private string? _vendor;

        public string? Vendor
        {
            get => _vendor;
            set => SetProperty(ref _vendor, value);
        }

        private string? _model;

        public string? Model
        {
            get => _model;
            set => SetProperty(ref _model, value);
        }

        private DeviceType _type = DeviceType.Unknown;

        public DeviceType Type
        {
            get => _type;
            set
            {

                if (SetProperty(ref _type, value))
                {
                    OnPropertyChanged(nameof(TypeIcon));
                }

            }
        }

        private bool _isApproved;

        public bool IsApproved
        {
            get => _isApproved;
            set => SetProperty(ref _isApproved, value);
        }

        private bool _isHost;

        public bool IsHost
        {
            get => _isHost;
            set => SetProperty(ref _isHost, value);
        }

        private bool _isOnline;

        public bool IsOnline
        {
            get => _isOnline;
            set
            {

                if (SetProperty(ref _isOnline, value))
                {
                    OnPropertyChanged(nameof(LastSeenLabel));
                }

            }
        }

        private DateTime _firstSeen;

        public DateTime FirstSeen
        {
            get => _firstSeen;
            set => SetProperty(ref _firstSeen, value);
        }

        private DateTime _lastSeen;

        public DateTime LastSeen
        {
            get => _lastSeen;
            set
            {

                if (SetProperty(ref _lastSeen, value))
                {
                    OnPropertyChanged(nameof(LastSeenLabel));
                }

            }
        }

        private string? _notes;

        public string? Notes
        {
            get => _notes;
            set => SetProperty(ref _notes, value);
        }

        [NotMapped]
        public string DisplayName => FriendlyName ?? MdnsName ?? Hostname ?? IpAddress;

        [NotMapped]
        public string LastSeenLabel
        {
            get
            {
                string label;

                if (IsOnline)
                {
                    label = "Online";
                }
                else
                {
                    TimeSpan diff = DateTime.UtcNow - LastSeen;

                    if (diff.TotalMinutes < 60)
                    {
                        label = $"{(int) diff.TotalMinutes}m ago";
                    }
                    else if (diff.TotalHours < 24)
                    {
                        label = $"{(int) diff.TotalHours}h ago";
                    }
                    else
                    {
                        label = $"{(int) diff.TotalDays}d ago";
                    }

                }

                return label;
            }
        }

        [NotMapped]
        public string TypeIcon => Type switch
        {
            DeviceType.Router => "🌐",
            DeviceType.Switch => "🔀",
            DeviceType.WiFi => "📶",
            DeviceType.PC => "💻",
            DeviceType.Server => "🖥️",
            DeviceType.Mobile => "📱",
            DeviceType.Camera => "📷",
            DeviceType.SmartDevice => "💡",
            DeviceType.Energy => "⚡",
            _ => "❓"
        };

        [NotMapped]
        public bool IsRandomizedMac
        {
            get
            {
                bool randomized = false;

                if (MacAddress.Length >= 2
                    && byte.TryParse(MacAddress.AsSpan(0, 2), System.Globalization.NumberStyles.HexNumber, null, out byte firstOctet))
                {
                    randomized = (firstOctet & 0x02) != 0 && (firstOctet & 0x01) == 0;
                }

                return randomized;
            }
        }

        public void CopyValuesFrom(Device other)
        {
            MacAddress = other.MacAddress;
            IpAddress = other.IpAddress;
            Hostname = other.Hostname;
            MdnsName = other.MdnsName;
            FriendlyName = other.FriendlyName;
            Vendor = other.Vendor;
            Model = other.Model;
            Type = other.Type;
            IsApproved = other.IsApproved;
            IsHost = other.IsHost;
            IsOnline = other.IsOnline;
            FirstSeen = other.FirstSeen;
            LastSeen = other.LastSeen;
            Notes = other.Notes;
        }
    }
}
