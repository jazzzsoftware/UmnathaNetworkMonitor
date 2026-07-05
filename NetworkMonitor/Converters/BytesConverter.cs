using Microsoft.UI.Xaml.Data;
using NetworkMonitor.ViewModels;

namespace NetworkMonitor.Converters
{
    public class BytesConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            long bytes = value is long longValue ? longValue : 0;
            string result = TrafficViewModel.FormatBytes(bytes);

            return result;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
