using Microsoft.UI;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace NetworkMonitor.Converters
{
    public class UnapprovedHighlightConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            bool highlight = value is bool highlightValue && highlightValue;
            Brush brush = highlight
                ? new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0x6B, 0x6B))
                : new SolidColorBrush(Colors.Transparent);

            return brush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
