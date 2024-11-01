using System;
using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Data;
using RemoteNET;
using RemoteNET.Internal.Reflection;

namespace RemoteNetSpy;

public class StrValueToInvokeButtonVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null)
            return Visibility.Hidden;

        if (value is ObjectViewer.MembersGridItem mgi)
        {
            if (mgi.GetOriginalMemberInfo() is MethodInfo mi)
            {
                bool isDotNetInvokable = mi.GetParameters().Length == 0; // Only parameter-less
                bool isMsvcInvokable = mi is RemoteRttiMethodInfo; // All native functions
                if (isDotNetInvokable || isMsvcInvokable)
                    return Visibility.Visible;
            }
        }
        return Visibility.Hidden;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
public class ComplexObjectVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null)
            return Visibility.Collapsed;

        if(value is RemoteObject || value is DynamicRemoteObject)
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

public class RawViewItemToVisabilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null)
            return Visibility.Collapsed;

        if (value is string str)
            return str.StartsWith("Raw View[") ? Visibility.Visible : Visibility.Collapsed;
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class RawViewToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null)
            return Visibility.Collapsed;

        if (value is string str)
            return str == "Raw View" ? Visibility.Visible : Visibility.Collapsed;
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}



