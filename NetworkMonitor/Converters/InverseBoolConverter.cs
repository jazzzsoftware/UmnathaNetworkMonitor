using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace NetworkMonitor.Converters
{
    public class InverseBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            object result = value is bool boolValue ? !boolValue : value;

            return result;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            object result = value is bool boolValue ? !boolValue : value;

            return result;
        }
    }

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

    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            Visibility result = value is bool boolValue && boolValue ? Visibility.Visible : Visibility.Collapsed;

            return result;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
