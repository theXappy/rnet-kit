using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace RemoteNetSpy.Converters;

// IsProcessDeadToBackgroundColor impl
public class IsProcessDeadToBackgroundColor : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (!(value is bool isDead))
            return Brushes.Transparent;
        return isDead ? Brushes.Crimson: Brushes.Transparent;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
