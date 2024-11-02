using System;
using System.Globalization;
using System.Windows.Data;
using RemoteNET;
using RemoteNetSpy.Models;

namespace RemoteNetSpy.Converters;

public class ModuleToIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
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


    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
