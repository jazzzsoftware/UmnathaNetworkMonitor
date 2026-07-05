using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Data;

namespace NetworkMonitor.Converters
{
    public class BoolToFontWeightConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            bool isBold = value is bool boolValue && boolValue;
            object result = isBold ? FontWeights.SemiBold : FontWeights.Normal;

            return result;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
