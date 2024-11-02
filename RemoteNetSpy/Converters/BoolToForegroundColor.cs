using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace RemoteNetSpy.Converters;

public class BoolToForegroundColor : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return (bool)value ? Brushes.White : Brushes.Gray;

    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
