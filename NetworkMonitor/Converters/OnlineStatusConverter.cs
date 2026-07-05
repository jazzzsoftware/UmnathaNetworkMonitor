using Microsoft.UI;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace NetworkMonitor.Converters
{
    public class OnlineStatusConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            bool isOnline = value is bool onlineValue && onlineValue;
            object result = parameter?.ToString() switch
            {
                "brush" => new SolidColorBrush(isOnline ? Colors.LimeGreen : Colors.OrangeRed),
                "text" => isOnline ? "Online" : "Offline",
                _ => new SolidColorBrush(isOnline ? Colors.LimeGreen : Colors.OrangeRed)
            };

            return result;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}