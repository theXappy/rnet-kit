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
        _cache["native"] = new SolidColorBrush(Color.FromArgb(0xFF, 0xD2, 0xAE, 0xD8));
        _cache["net451"] = new SolidColorBrush(Color.FromArgb(0xFF, 0x6A, 0x99, 0xC9));
        _cache["net6.0-windows"] = new SolidColorBrush(Color.FromArgb(0xFF, 0xB7, 0x81, 0x1F));
        _cache["net7.0-windows"] = new SolidColorBrush(Color.FromArgb(0xFF, 0xEF, 0x71, 0xD3));
        _cache["net8.0-windows"] = new SolidColorBrush(Color.FromArgb(0xFF, 0x2F, 0x71, 0xe3));
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
