﻿using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace RemoteNetSpy.Converters;

public class AddressesVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null)
            return Visibility.Collapsed;

        if (value is long || value is ulong || value is nint || value is nuint)
            return Visibility.Visible;
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}



