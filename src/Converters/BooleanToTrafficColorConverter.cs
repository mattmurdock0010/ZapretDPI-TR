using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace ZapretDPI.Converters;

public class BooleanToTrafficColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isTrafficActive && isTrafficActive)
        {
            return new SolidColorBrush(Color.FromRgb(16, 185, 129));
        }
        return new SolidColorBrush(Color.FromRgb(107, 114, 128));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
