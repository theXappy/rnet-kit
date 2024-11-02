using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using RemoteNET;

namespace RemoteNetSpy.Converters;
public class ComplexObjectVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null)
            return Visibility.Collapsed;

        if (value is RemoteObject || value is DynamicRemoteObject)
            return Visibility.Visible;
        if (value.GetType().IsArray)
            return Visibility.Visible;
        if (value is string)
            return Visibility.Visible;
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

