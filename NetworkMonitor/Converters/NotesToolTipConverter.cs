using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace NetworkMonitor.Converters
{
    public class NotesToolTipConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, string language)
        {
            object? result = null;

            if (value is string notes && !string.IsNullOrWhiteSpace(notes))
            {
                ToolTip toolTip = new ToolTip
                {
                    Content = notes,
                    FontSize = 14,
                    Padding = new Thickness(12, 6, 12, 6),
                    CornerRadius = new CornerRadius(8),
                    BorderThickness = new Thickness(1),
                    Background = (Brush) Application.Current.Resources["SolidBackgroundFillColorTertiaryBrush"],
                    BorderBrush = (Brush) Application.Current.Resources["AccentFillColorDefaultBrush"]
                };

                result = toolTip;
            }

            return result;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
