using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using RemoteNetSpy.Models;

namespace RemoteNetSpy.Converters;

public class DifferenceToForegroundColor : IValueConverter
{
    SolidColorBrush _green = new SolidColorBrush(Color.FromRgb(0x86, 0xFF, 0x7C));
    SolidColorBrush _red = new SolidColorBrush(Color.FromRgb(0xFF, 0x85, 0x85));
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is DumpedTypeModel dumpedType)
        {
            if (dumpedType.NumInstances == 0)
                return Brushes.Gray;
            int difference = dumpedType.NumInstances.GetValueOrDefault() - dumpedType.PreviousNumInstances.GetValueOrDefault();
            if (difference > 0)
                return _green;
            if (difference < 0)
                return _red;
            return Brushes.White;
        }
        return Brushes.White;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
