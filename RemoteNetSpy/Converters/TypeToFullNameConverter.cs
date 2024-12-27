using System;
using System.Globalization;
using System.Windows.Data;

namespace RemoteNetSpy.Converters;

public class TypeToFullNameConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Type type)
            return type.FullName;
        return "Unknown";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException("ConvertBack is not supported.");
    }
}
