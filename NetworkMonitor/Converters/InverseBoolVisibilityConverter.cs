using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace NetworkMonitor.Converters
{
    public class InverseBoolVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            Visibility result = value is bool boolValue && boolValue ? Visibility.Collapsed : Visibility.Visible;

            return result;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
