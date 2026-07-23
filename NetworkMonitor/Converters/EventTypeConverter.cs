using Microsoft.UI;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using NetworkMonitor.Models.Devices;

namespace NetworkMonitor.Converters
{
    public class EventTypeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            bool isAppeared = value is DeviceEventType eventType && eventType == DeviceEventType.Appeared;
            SolidColorBrush brush = new(isAppeared ? Colors.LimeGreen : Colors.OrangeRed);

            return brush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}