using System;
using System.Globalization;
using System.Windows.Data;

namespace RemoteNetSpy.Converters;

public class BoolToFreezeIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return (bool)value ? "/icons/Cancelled_Snowflake.png" : "/icons/Snowflake.png";
    }


    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
