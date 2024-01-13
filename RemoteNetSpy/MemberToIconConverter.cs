using System;
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
        AssemblyDesc assembly = (AssemblyDesc)value;
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

public class MemberToIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        string str = value as string;
        if (str == null)
            return null;
        if (str.StartsWith("[Event]"))
            return "/icons/Event.png";
        if (str.StartsWith("[Method]") || str.StartsWith("[Constructor]"))
            return "/icons/Method.png";
        if (str.StartsWith("[Property]"))
            return "/icons/Property.png";
        if (str.StartsWith("[MethodTable]"))
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
            desc += $" ({a.NumInstances})";
        return desc;
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