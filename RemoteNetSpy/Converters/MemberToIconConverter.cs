using System;
using System.Globalization;
using System.Windows.Data;

namespace RemoteNetSpy.Converters;

public class MemberToIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string str = value as string;
        if (str == null)
            return null;
        if (str.StartsWith("Event"))
            return "/icons/Event.png";
        if (str.StartsWith("Method") || str.StartsWith("Constructor"))
            return "/icons/Method.png";
        if (str.StartsWith("Property"))
            return "/icons/Property.png";
        if (str.StartsWith("MethodTable"))
            return "/icons/MethodTable.png";
        return "/icons/Field.png";
    }


    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
