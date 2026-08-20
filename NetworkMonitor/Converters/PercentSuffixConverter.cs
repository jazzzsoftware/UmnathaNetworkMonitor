using Microsoft.UI.Xaml.Data;

namespace NetworkMonitor.Converters
{
    public class PercentSuffixConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            double doubleValue = value is double typedValue ? typedValue : 0.0;
            string result = $"{doubleValue:0}%";

            return result;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
