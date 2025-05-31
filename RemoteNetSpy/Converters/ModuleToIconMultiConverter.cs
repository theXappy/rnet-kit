using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using RemoteNET;

namespace RemoteNetSpy.Converters;

public class ModuleToIconMultiConverter : IMultiValueConverter
{
    private static readonly BitmapImage CppProjectNodeIcon;
    private static readonly BitmapImage CppProjectNodeGrayedIcon;
    private static readonly BitmapImage ModulePublicIcon;
    private static readonly BitmapImage ModulePublicGrayedIcon;

    static ModuleToIconMultiConverter()
    {
        CppProjectNodeIcon = new BitmapImage(new Uri("/icons/CPPProjectNode.png", UriKind.RelativeOrAbsolute));
        CppProjectNodeGrayedIcon = new BitmapImage(new Uri("/icons/CPPProjectNode_grayed.png", UriKind.RelativeOrAbsolute));
        ModulePublicIcon = new BitmapImage(new Uri("/icons/ModulePublic.png", UriKind.RelativeOrAbsolute));
        ModulePublicGrayedIcon = new BitmapImage(new Uri("/icons/ModulePublic_grayed.png", UriKind.RelativeOrAbsolute));
    }

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2) return null;

        RuntimeType runtime = (RuntimeType)values[0];
        bool anyTypes;
        if (values[1] is int typesCount)
        {
            anyTypes = typesCount > 0;
        }
        else
        {
            // Probably "UnsetValue"
            anyTypes = false;
        }

        if (runtime == RuntimeType.Unmanaged)
        {
            return anyTypes ? CppProjectNodeIcon : CppProjectNodeGrayedIcon;
        }
        else
        {
            return anyTypes ? ModulePublicIcon : ModulePublicGrayedIcon;
        }
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

