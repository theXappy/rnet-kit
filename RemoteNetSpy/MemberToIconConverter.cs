using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using RemoteNET;

namespace RemoteNetSpy;

public class ModuleToIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        AssemblyModel assembly = value as AssemblyModel;
        if (assembly == null)
            return null;
        RuntimeType runtime = assembly.Runtime;
        if (runtime == RuntimeType.Unmanaged)
        {
            if (assembly.AnyTypes)
                return "/icons/CPPProjectNode.png";
            return "/icons/CPPProjectNode_grayed.png";
        }
        else
        {
            if (assembly.AnyTypes)
                return "/icons/ModulePublic.png";
            return "/icons/ModulePublic_grayed.png";
        }

    }


    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class BoolToWatchIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        bool IsMonitoringAllocation = (bool)value;
        if (IsMonitoringAllocation)
            return "/icons/Watch_on.png";
        return null;

    }


    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class MemberToIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
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


    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class BoolToFreezeIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        return (bool)value ? "/icons/Cancelled_Snowflake.png" : "/icons/Snowflake.png";
    }


    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}


public class BoolToFreezeButtonText : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        bool b = (bool)value;
        return b ? "Unfreeze" : " Freeze";
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

[ValueConversion(typeof(bool), typeof(bool))]
public class InverseBooleanConverter : IValueConverter
{
    #region IValueConverter Members

    public object Convert(object value, Type targetType, object parameter,
        System.Globalization.CultureInfo culture)
    {
        if (targetType != typeof(bool))
            throw new InvalidOperationException("The target must be a boolean");

        return !(bool)value;
    }

    public object ConvertBack(object value, Type targetType, object parameter,
        System.Globalization.CultureInfo culture)
    {
        throw new NotSupportedException();
    }

    #endregion
}

public class BoolToVisabilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return (bool)value ? Visibility.Visible : Visibility.Collapsed;

    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
public class InverseBoolToVisabilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return (bool)value ? Visibility.Collapsed : Visibility.Visible;

    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}


public class DumpedTypeToDescription : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        DumpedType a = value as DumpedType;
        if (a == null)
            return "Error: null";

        string desc = a.FullTypeName;
        if (a.HaveInstances)
        {
            int difference = a.NumInstances.GetValueOrDefault() - a.PreviousNumInstances.GetValueOrDefault();
            string differenceText = difference != 0 ? (difference > 0 ? $"+{difference}" : difference.ToString()) : string.Empty;
            if (string.IsNullOrEmpty(differenceText))
            {
                desc += $" (Count: {a.NumInstances})";
            }
            else
            {
                desc += $" (Count: {a.NumInstances}, Diff: {differenceText})";
            }
        }
        return desc;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class DotnetVersionToForegroundColor : IValueConverter
{
    private static Dictionary<string, Brush> _cache = new();

    static DotnetVersionToForegroundColor()
    {
        _cache["native"] = new SolidColorBrush(Color.FromArgb(0xFF, 0xD2, 0xAE, 0xD8));
        _cache["net451"] = new SolidColorBrush(Color.FromArgb(0xFF,0x6A,0x99,0xC9));
        _cache["net6.0-windows"] = new SolidColorBrush(Color.FromArgb(0xFF,0xB7,0x81,0x1F));
        _cache["net7.0-windows"] = new SolidColorBrush(Color.FromArgb(0xFF,0xEF,0x71,0xD3));
        _cache["net8.0-windows"] = new SolidColorBrush(Color.FromArgb(0xFF,0x2F,0x71,0xe3));
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
            (byte)((hash >> 8) & 0xff),
            (byte)((hash >> 16) & 0xff))
        );

        _cache[str] = manual;
        return manual;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
public class BoolToForegroundColor : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return (bool)value ? Brushes.White : Brushes.Gray;

    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class DifferenceToForegroundColor : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is DumpedType dumpedType)
        {
            if (dumpedType.NumInstances == 0)
                return Brushes.Gray;
            int difference = dumpedType.NumInstances.GetValueOrDefault() - dumpedType.PreviousNumInstances.GetValueOrDefault();
            if (difference > 0)
                return Brushes.Green;
            if (difference < 0)
                return Brushes.Red;
            return Brushes.White;
        }
        return Brushes.White;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
