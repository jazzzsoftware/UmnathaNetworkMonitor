using Microsoft.UI.Xaml.Data;

namespace NetworkMonitor.Converters
{
    public class DoubleFormatConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            string format = parameter is string stringParameter ? stringParameter : "0.0";
            double doubleValue = value is double typedValue ? typedValue : 0.0;
            string result = doubleValue.ToString(format);

            return result;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
