using System;
using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Data;
using RemoteNET.Internal.Reflection;
using RemoteNetSpy.Controls;

namespace RemoteNetSpy.Converters;

public class StrValueToInvokeButtonVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null)
            return Visibility.Collapsed;

        if (value is MembersGridItem mgi)
        {
            if (mgi.GetOriginalMemberInfo() is MethodInfo mi)
            {
                bool isDotNetInvokable = mi.GetParameters().Length == 0; // Only parameter-less
                bool isMsvcInvokable = mi is RemoteRttiMethodInfo; // All native functions
                if (isDotNetInvokable || isMsvcInvokable)
                    return Visibility.Visible;
            }
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}



