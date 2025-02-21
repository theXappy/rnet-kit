using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace RemoteNetSpy.Converters;

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            bool isReversed = parameter?.ToString()?.Equals("Reversed", StringComparison.OrdinalIgnoreCase) == true;
            return boolValue
                ? (isReversed ? Visibility.Hidden : Visibility.Visible)
                : (isReversed ? Visibility.Visible : Visibility.Hidden);
        }
        return DependencyProperty.UnsetValue;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}