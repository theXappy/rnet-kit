using System;
using System.Globalization;
using System.Windows.Data;

namespace RemoteNetSpy.Converters;

public class BoolToFreezeButtonText : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool b = (bool)value;
        return b ? "Unfreeze" : " Freeze";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
