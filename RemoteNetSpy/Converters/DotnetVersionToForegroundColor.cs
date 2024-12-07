using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace RemoteNetSpy.Converters;

public class DotnetVersionToForegroundColor : IValueConverter
{
    private static Dictionary<string, Brush> _cache = new();

    static DotnetVersionToForegroundColor()
    {
        _cache["native"] = new SolidColorBrush(Color.FromArgb(0xFF, 0x3A, 0x9D , 0xC5));
        _cache["net451"] = new SolidColorBrush(Color.FromArgb(0xFF, 0x57, 0xC5, 0x3A));
        _cache["net6.0-windows"] = new SolidColorBrush(Color.FromArgb(0xFF, 0xA8, 0x3A, 0xC5));
        _cache["net7.0-windows"] = new SolidColorBrush(Color.FromArgb(0xFF, 0xC4, 0x3B, 0x9B));
        _cache["net8.0-windows"] = new SolidColorBrush(Color.FromArgb(0xFF, 0xA8, 0x3B, 0xC4));
        _cache["net9.0-windows"] = new SolidColorBrush(Color.FromArgb(0xFF, 0x64, 0x3B, 0xC4));
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (!(value is string str))
            return Brushes.White;

        if (_cache.TryGetValue(str, out var cached))
            return cached;

        int hash = str.GetHashCode();
        Brush manual = new SolidColorBrush(Color.FromRgb(
            (byte)(hash & 0xff),
            (byte)(hash >> 8 & 0xff),
            (byte)(hash >> 16 & 0xff))
        );

        _cache[str] = manual;
        return manual;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
